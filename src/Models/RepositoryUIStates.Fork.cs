namespace SourceGit.Models
{
    /// <summary>
    ///     Which sections of the left panel this repository shows at all.
    ///
    ///     Not the same thing as expanded. A collapsed section still says what it is and how
    ///     many it holds; a hidden one is gone, because on this repository it never had
    ///     anything to say -- no worktrees, no submodules, no tags, ever.
    ///
    ///     It lives here rather than in the preferences because the answer is a property of
    ///     the repository, not of the person: the same person wants worktrees on one clone
    ///     and not on the next. The file is already per repository and already serialized,
    ///     so these six cost nothing to store.
    /// </summary>
    public partial class RepositoryUIStates
    {
        public bool IsLocalBranchesVisibleInSideBar { get; set; } = true;
        public bool IsRemotesVisibleInSideBar { get; set; } = true;
        public bool IsTagsVisibleInSideBar { get; set; } = true;
        public bool IsSubmodulesVisibleInSideBar { get; set; } = true;
        public bool IsWorktreesVisibleInSideBar { get; set; } = true;
        public bool IsPullRequestsVisibleInSideBar { get; set; } = true;
    }
}
