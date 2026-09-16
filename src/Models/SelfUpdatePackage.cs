using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace SourceGit.Models
{
    public enum OSFamily
    {
        Windows,
        MacOS,
        Linux,
    }

    /// <summary>
    ///     Whether this installation is one we may replace files in.
    /// </summary>
    public enum SelfUpdateSupport
    {
        /// <summary>The files can be swapped where they are.</summary>
        InPlace,

        /// <summary>Something else owns these files -- a package manager. Do not touch them.</summary>
        ManagedExternally,

        /// <summary>The layout is not one this knows how to read. Fall back to downloading.</summary>
        Unknown,
    }

    /// <summary>
    ///     What an update would have to replace on this machine, decided before anything is
    ///     downloaded. Every field comes from the process itself, never from the release.
    /// </summary>
    public sealed class SelfUpdateTarget
    {
        public SelfUpdateSupport Support { get; init; } = SelfUpdateSupport.Unknown;

        /// <summary>Why not, when <see cref="Support"/> is not <see cref="SelfUpdateSupport.InPlace"/>.</summary>
        public string Reason { get; init; } = string.Empty;

        /// <summary>The tail of the asset name that belongs to this platform, e.g. ".win-x64.zip".</summary>
        public string AssetSuffix { get; init; } = string.Empty;

        /// <summary>
        ///     The single folder every archive we publish holds -- "SourceGit" in the Windows
        ///     zip, "SourceGit.app" in the macOS one. Empty when the asset is the executable
        ///     itself, which is the AppImage case.
        /// </summary>
        public string ArchiveRootEntry { get; init; } = string.Empty;

        /// <summary>The folder whose files get replaced.</summary>
        public string InstallRoot { get; init; } = string.Empty;

        /// <summary>What to launch once the files are in place.</summary>
        public string ExecutablePath { get; init; } = string.Empty;

        public bool IsArchive => ArchiveRootEntry.Length > 0;
    }

    /// <summary>
    ///     Which package belongs to this machine, and whether its files are ours to replace.
    ///
    ///     Kept free of any ambient state -- the running OS, the process path and the AppImage
    ///     variable are all passed in -- because this is the decision that sends an installer
    ///     at somebody else's files, and it has to be checkable without being on that platform.
    /// </summary>
    public static class SelfUpdatePackage
    {
        /// <summary>
        ///     Replaced files are renamed aside rather than deleted, so a swap that fails
        ///     halfway can be undone. The running executable cannot be deleted at all while it
        ///     runs, which is the other reason the old files outlive the swap.
        /// </summary>
        public const string ASIDE_SUFFIX = ".sourcegit-old";

        public static SelfUpdateTarget Resolve(OSFamily os, Architecture arch, string processPath, string appImagePath)
        {
            if (string.IsNullOrEmpty(processPath))
                return Unknown("the running executable cannot be located");

            return os switch
            {
                OSFamily.Windows => ResolveWindows(arch, processPath),
                OSFamily.MacOS => ResolveMacOS(arch, processPath),
                OSFamily.Linux => ResolveLinux(arch, appImagePath),
                _ => Unknown("unrecognized platform"),
            };
        }

        public static SelfUpdateTarget ResolveForThisProcess()
        {
            var os = OperatingSystem.IsWindows() ? OSFamily.Windows :
                     OperatingSystem.IsMacOS() ? OSFamily.MacOS :
                     OSFamily.Linux;

            return Resolve(os,
                           RuntimeInformation.ProcessArchitecture,
                           Environment.ProcessPath,
                           Environment.GetEnvironmentVariable("APPIMAGE") ?? string.Empty);
        }

        /// <summary>
        ///     Finds the one asset whose name ends with this platform's suffix.
        ///
        ///     Refuses a tie rather than taking the first: two candidates would mean the
        ///     naming changed, and guessing there hands the installer the wrong package.
        /// </summary>
        public static ReleaseAsset Select(IReadOnlyList<ReleaseAsset> assets, string suffix)
        {
            if (assets == null || string.IsNullOrEmpty(suffix))
                return null;

            ReleaseAsset found = null;
            foreach (var asset in assets)
            {
                if (asset?.Name == null || !asset.Name.EndsWith(suffix, StringComparison.Ordinal))
                    continue;

                if (found != null)
                    return null;

                found = asset;
            }

            return found;
        }

        private static SelfUpdateTarget ResolveWindows(Architecture arch, string processPath)
        {
            var suffix = arch switch
            {
                Architecture.X64 => ".win-x64.zip",
                Architecture.Arm64 => ".win-arm64.zip",
                _ => string.Empty,
            };

            if (suffix.Length == 0)
                return Unknown("no package is published for this processor");

            var root = Path.GetDirectoryName(processPath);
            if (string.IsNullOrEmpty(root))
                return Unknown("the running executable cannot be located");

            return new SelfUpdateTarget
            {
                Support = SelfUpdateSupport.InPlace,
                AssetSuffix = suffix,
                ArchiveRootEntry = "SourceGit",
                InstallRoot = root,
                ExecutablePath = processPath,
            };
        }

        /// <summary>
        ///     The macOS package is a bundle, and the executable sits three levels inside it:
        ///     SourceGit.app/Contents/MacOS/SourceGit. What gets replaced is the bundle, so the
        ///     bundle is what has to be found -- by walking up to the .app rather than counting
        ///     directories, because a build run straight from its output folder has no bundle
        ///     at all and has to be recognized as such instead of mistaking a parent for one.
        /// </summary>
        private static SelfUpdateTarget ResolveMacOS(Architecture arch, string processPath)
        {
            var suffix = arch switch
            {
                Architecture.X64 => ".osx-x64.zip",
                Architecture.Arm64 => ".osx-arm64.zip",
                _ => string.Empty,
            };

            if (suffix.Length == 0)
                return Unknown("no package is published for this processor");

            var bundle = Path.GetDirectoryName(processPath);
            while (!string.IsNullOrEmpty(bundle))
            {
                if (Path.GetFileName(bundle).EndsWith(".app", StringComparison.Ordinal))
                {
                    return new SelfUpdateTarget
                    {
                        Support = SelfUpdateSupport.InPlace,
                        AssetSuffix = suffix,
                        ArchiveRootEntry = Path.GetFileName(bundle),
                        InstallRoot = bundle,
                        ExecutablePath = processPath,
                    };
                }

                bundle = Path.GetDirectoryName(bundle);
            }

            return new SelfUpdateTarget
            {
                Support = SelfUpdateSupport.Unknown,
                Reason = "this build is not running from an application bundle",
                AssetSuffix = suffix,
            };
        }

        /// <summary>
        ///     On Linux only the AppImage is ours. A .deb or .rpm puts its files under /opt or
        ///     /usr and they belong to the package manager: replacing them behind its back
        ///     breaks its bookkeeping, and the next system upgrade overwrites the lot anyway.
        ///
        ///     An AppImage announces itself through APPIMAGE. The process path is no help --
        ///     it points into the temporary mount, not at the file to replace -- so that
        ///     variable is the only thing that can name it.
        /// </summary>
        private static SelfUpdateTarget ResolveLinux(Architecture arch, string appImagePath)
        {
            var suffix = arch switch
            {
                Architecture.X64 => ".linux.amd64.AppImage",
                Architecture.Arm64 => ".linux.arm64.AppImage",
                _ => string.Empty,
            };

            if (string.IsNullOrEmpty(appImagePath))
            {
                // No suffix on purpose. Nothing published here belongs to this install, and
                // handing a .deb user the AppImage would answer a question they did not ask.
                return new SelfUpdateTarget
                {
                    Support = SelfUpdateSupport.ManagedExternally,
                    Reason = "this copy was installed by a package manager, which owns its files",
                };
            }

            if (suffix.Length == 0)
                return Unknown("no package is published for this processor");

            var root = Path.GetDirectoryName(appImagePath);
            if (string.IsNullOrEmpty(root))
                return Unknown("the running AppImage cannot be located");

            return new SelfUpdateTarget
            {
                Support = SelfUpdateSupport.InPlace,
                AssetSuffix = suffix,
                ArchiveRootEntry = string.Empty,
                InstallRoot = root,
                ExecutablePath = appImagePath,
            };
        }

        private static SelfUpdateTarget Unknown(string reason)
        {
            return new SelfUpdateTarget { Support = SelfUpdateSupport.Unknown, Reason = reason };
        }
    }
}
