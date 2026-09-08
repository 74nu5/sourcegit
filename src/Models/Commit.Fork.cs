namespace SourceGit.Models
{
    public partial class Commit
    {
        /// <summary>
        ///     The stash this row stands for, such as "stash@{1}", or empty.
        ///
        ///     Only the newest stash carries a ref git will decorate: the others live in the
        ///     reflog of refs/stash and come back from `git log` bare, indistinguishable from
        ///     an ordinary commit. So the name is put here from the stash list rather than
        ///     read off the decorations.
        /// </summary>
        public string StashName { get; set; } = string.Empty;

        public bool IsStash => StashName.Length > 0;
    }
}
