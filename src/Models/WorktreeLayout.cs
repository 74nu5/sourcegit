using System;
using System.IO;

namespace SourceGit.Models
{
    /// <summary>
    ///     Where a working copy sits among its worktrees, read off the disk the way git lays
    ///     it out -- before any Repository exists, since the decision of which tab to open it
    ///     in has to be taken first.
    /// </summary>
    public static class WorktreeLayout
    {
        public static string Normalize(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/').TrimEnd('/');
        }

        /// <summary>
        ///     Whether two paths name the same folder. Case-blind on Windows, where git may write
        ///     a drive letter in a different case than the one the user typed -- and two groups
        ///     for one repository would be worse than none.
        /// </summary>
        public static bool SamePath(string a, string b)
        {
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return Normalize(a).Equals(Normalize(b), comparison);
        }

        /// <summary>
        ///     The git directory shared by every worktree of a repository.
        ///
        ///     The same reading Repository's constructor does, deliberately: a linked worktree's
        ///     own git directory lives under &lt;common&gt;/worktrees/&lt;name&gt; and holds a commondir file
        ///     naming the shared one. Anything else is its own common directory.
        /// </summary>
        public static string CommonDirOf(string gitDir)
        {
            var dir = Normalize(gitDir);
            if (dir.Length == 0)
                return dir;

            var commonDirFile = Path.Combine(dir, "commondir");
            var isWorktree = dir.IndexOf("/worktrees/", StringComparison.Ordinal) > 0 && File.Exists(commonDirFile);
            if (!isWorktree)
                return dir;

            var commonDir = File.ReadAllText(commonDirFile).Trim();
            commonDir = Path.IsPathRooted(commonDir)
                ? new DirectoryInfo(commonDir).FullName
                : new DirectoryInfo(Path.Combine(dir, commonDir)).FullName;

            return Normalize(commonDir);
        }

        /// <summary>
        ///     The working copy of the main worktree when <paramref name="gitDir"/> belongs to a
        ///     linked one, or null when it is a main worktree itself or cannot be placed.
        ///
        ///     Git keeps the shared directory inside the main worktree as ".git", or makes it
        ///     the repository itself when the main is bare. A repository created with
        ///     --separate-git-dir fits neither: its git directory is somewhere else entirely and
        ///     nothing on disk says where its working copy is. Guessing there would group a
        ///     worktree under a path that is not a repository, so it is not grouped at all and
        ///     opens on its own, as it always has.
        /// </summary>
        public static string MainPathOf(string gitDir)
        {
            var own = Normalize(gitDir);
            var common = CommonDirOf(own);
            if (common.Length == 0 || SamePath(common, own))
                return null;

            if (Path.GetFileName(common).Equals(".git", StringComparison.Ordinal))
                return Normalize(Path.GetDirectoryName(common));

            return IsBareDir(common) ? common : null;
        }

        /// <summary>
        ///     What a worktree is called in the row under its repository's tab: its branch.
        ///
        ///     The branch is what tells worktrees apart -- they are usually created to hold one
        ///     each. A detached one shows the start of its commit, and a bare main, which has no
        ///     working branch to name, shows its folder.
        /// </summary>
        public static string LabelOf(Worktree worktree)
        {
            if (worktree == null)
                return string.Empty;

            if (worktree.IsDetached)
                return worktree.Head.Length > 10 ? worktree.Head[..10] : worktree.Head;

            var branch = worktree.Branch ?? string.Empty;
            if (branch.StartsWith("refs/heads/", StringComparison.Ordinal))
                return branch[11..];
            if (branch.StartsWith("refs/remotes/", StringComparison.Ordinal))
                return branch[13..];
            if (branch.Length > 0)
                return branch;

            return Path.GetFileName(Normalize(worktree.FullPath));
        }

        private static bool IsBareDir(string dir)
        {
            var config = Path.Combine(dir, "config");
            if (!File.Exists(config))
                return false;

            foreach (var line in File.ReadAllLines(config))
            {
                var trimmed = line.Replace(" ", string.Empty).Replace("\t", string.Empty);
                if (trimmed.Equals("bare=true", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
