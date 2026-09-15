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

        /// <summary>
        ///     The row standing for work that is not committed yet.
        ///
        ///     It answers to no object in the repository, which is what makes it delicate:
        ///     everything downstream of a Commit assumes it can go and ask git about it. The
        ///     hash below is deliberately not a hash -- forty characters so that every
        ///     Substring in the code still works, and letters that cannot appear in one so
        ///     that it can never be mistaken for a real revision.
        /// </summary>
        public const string UNCOMMITTED_SHA = "WORKINGCOPYWORKINGCOPYWORKINGCOPYWORKING";

        public bool IsUncommitted => SHA.Equals(UNCOMMITTED_SHA, System.StringComparison.Ordinal);
    }
}
