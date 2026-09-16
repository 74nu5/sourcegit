using System;
using System.Collections.Generic;
using System.IO;

namespace SourceGit.Models
{
    /// <summary>
    ///     Puts a staged copy of the new version over the installed one, and knows how to
    ///     take it back.
    ///
    ///     The whole design rests on one asymmetry: a running executable cannot be deleted or
    ///     overwritten, but it can be renamed -- on Windows and on Unix alike. So no file is
    ///     ever written over. Each one is renamed aside first, and the replacement is copied
    ///     into the name that just came free. If anything fails partway, the renames are
    ///     undone in reverse and the installation is exactly as it was.
    ///
    ///     Copy, not move: the staging folder is under the cache directory, which may well be
    ///     on another volume, and a half-finished cross-volume move would leave neither file
    ///     whole. Copying also leaves the staged tree intact for a second attempt.
    /// </summary>
    public static class SelfUpdateSwap
    {
        /// <summary>
        ///     Replaces, under <paramref name="installRoot"/>, every file that the staged tree
        ///     carries. Files the installation has and the new version no longer ships are
        ///     left alone: nothing here can tell a dropped file from one somebody put there.
        ///
        ///     Throws on failure, having first put back what it had moved.
        /// </summary>
        public static void Apply(string stageRoot, string installRoot)
        {
            var renamed = new List<(string Target, string Aside)>();
            var written = new List<string>();

            try
            {
                foreach (var source in Directory.EnumerateFiles(stageRoot, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(stageRoot, source);
                    var target = Path.Combine(installRoot, relative);

                    var folder = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(folder))
                        Directory.CreateDirectory(folder);

                    if (File.Exists(target))
                    {
                        var aside = target + SelfUpdatePackage.ASIDE_SUFFIX;
                        DeleteIfPossible(aside);
                        File.Move(target, aside);
                        renamed.Add((target, aside));
                    }

                    File.Copy(source, target);
                    written.Add(target);

                    CopyExecutableBit(source, target);
                }
            }
            catch
            {
                Undo(renamed, written);
                throw;
            }
        }

        /// <summary>
        ///     Clears the files an earlier swap left behind. Called before the next one rather
        ///     than at startup, because the one that matters most -- the executable that was
        ///     running when it was renamed -- cannot be deleted until something restarts, and
        ///     a sweep that runs only when an update is about to happen needs no hook in the
        ///     application's startup path.
        /// </summary>
        public static void SweepAside(string installRoot)
        {
            if (string.IsNullOrEmpty(installRoot) || !Directory.Exists(installRoot))
                return;

            try
            {
                var pattern = "*" + SelfUpdatePackage.ASIDE_SUFFIX;
                foreach (var leftover in Directory.EnumerateFiles(installRoot, pattern, SearchOption.AllDirectories))
                    DeleteIfPossible(leftover);
            }
            catch
            {
                // A sweep is a courtesy. Failing it must never stop an update.
            }
        }

        /// <summary>
        ///     Whether files can be created where the installation lives.
        ///
        ///     Answered by writing, not by inspecting paths: a list of folders known to be
        ///     protected would miss the copy installed by another account, and the answer that
        ///     counts is whether this process can write here, now.
        /// </summary>
        public static bool CanWriteTo(string installRoot)
        {
            if (string.IsNullOrEmpty(installRoot) || !Directory.Exists(installRoot))
                return false;

            var probe = Path.Combine(installRoot, $".sourcegit-write-probe-{Guid.NewGuid():N}");

            try
            {
                using (var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    stream.WriteByte(0);

                File.Delete(probe);
                return true;
            }
            catch
            {
                DeleteIfPossible(probe);
                return false;
            }
        }

        private static void Undo(List<(string Target, string Aside)> renamed, List<string> written)
        {
            for (var i = written.Count - 1; i >= 0; i--)
                DeleteIfPossible(written[i]);

            for (var i = renamed.Count - 1; i >= 0; i--)
            {
                var (target, aside) = renamed[i];

                try
                {
                    if (File.Exists(aside) && !File.Exists(target))
                        File.Move(aside, target);
                }
                catch
                {
                    // Nothing better is available here. The file is still on disk under its
                    // aside name, which is why the aside name is stable and documented.
                }
            }
        }

        /// <summary>
        ///     File.Copy creates the target with default permissions, so on Unix the copied
        ///     binary would arrive without its executable bit and the application would simply
        ///     refuse to start. The mode is carried over explicitly.
        /// </summary>
        private static void CopyExecutableBit(string source, string target)
        {
            if (OperatingSystem.IsWindows())
                return;

            try
            {
                File.SetUnixFileMode(target, File.GetUnixFileMode(source));
            }
            catch
            {
                // Left as a best effort: a mode that cannot be read is not a reason to undo
                // a swap that otherwise succeeded.
            }
        }

        private static void DeleteIfPossible(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // The running executable's aside copy is locked until the process exits.
            }
        }
    }
}
