using System.IO;
using System.Text.Json.Serialization;

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
        [JsonIgnore]
        public bool IsLocalBranchesVisibleInSideBar { get; set; } = true;
        [JsonIgnore]
        public bool IsRemotesVisibleInSideBar { get; set; } = true;
        [JsonIgnore]
        public bool IsTagsVisibleInSideBar { get; set; } = true;
        [JsonIgnore]
        public bool IsSubmodulesVisibleInSideBar { get; set; } = true;
        [JsonIgnore]
        public bool IsWorktreesVisibleInSideBar { get; set; } = true;
        [JsonIgnore]
        public bool IsPullRequestsVisibleInSideBar { get; set; } = true;

        /// <summary>
        ///     Full name of the branch that holds the leftmost lane of the graph, or empty.
        ///
        ///     Per repository rather than per person for the same reason as the six above:
        ///     the answer is a property of the history being read. It is `main` on one clone
        ///     and `develop` on the next, and nobody wants to re-say it on every switch.
        /// </summary>
        [JsonIgnore]
        public string PinnedLaneBranch { get; set; } = string.Empty;

        /// <summary>
        ///     Whether stashes are drawn in the graph, hanging off the commit they were
        ///     taken from. Off, like everything else this fork adds.
        /// </summary>
        [JsonIgnore]
        public bool ShowStashesInGraph { get; set; } = false;

        /// <summary>
        ///     Whether a row for the work in progress sits at the head of the graph.
        /// </summary>
        [JsonIgnore]
        public bool ShowUncommittedInGraph { get; set; } = false;

        /// <summary>
        ///     Manual width of the branch column, or 0 to size it to its contents.
        /// </summary>
        [JsonIgnore]
        public double BranchColumnWidth { get; set; } = 0;

        /// <summary>
        ///     Manual width of the graph column, or 0 to size it to the graph.
        /// </summary>
        [JsonIgnore]
        public double GraphColumnWidth { get; set; } = 0;

        /// <summary>
        ///     Loads this fork's state from sourcegit.fork.uistates, beside the file upstream
        ///     reads. Every property of this fork is [JsonIgnore], because upstream rewrites
        ///     sourcegit.uistates with only its own fields whenever it closes the repository.
        ///
        ///     A repository opened for the first time since the move still has them in the
        ///     shared file; they are read from there, and the next save moves them across.
        /// </summary>
        private void LoadForkStates()
        {
            var dir = Path.GetDirectoryName(_file) ?? string.Empty;
            var states = ForkSettingsFile.Read(Path.Combine(dir, ForkSettingsFile.UI_STATES), _file, ForkJsonCodeGen.Default.ForkRepositoryStates);

            IsLocalBranchesVisibleInSideBar = states.IsLocalBranchesVisibleInSideBar;
            IsRemotesVisibleInSideBar = states.IsRemotesVisibleInSideBar;
            IsTagsVisibleInSideBar = states.IsTagsVisibleInSideBar;
            IsSubmodulesVisibleInSideBar = states.IsSubmodulesVisibleInSideBar;
            IsWorktreesVisibleInSideBar = states.IsWorktreesVisibleInSideBar;
            IsPullRequestsVisibleInSideBar = states.IsPullRequestsVisibleInSideBar;
            PinnedLaneBranch = states.PinnedLaneBranch ?? string.Empty;
            ShowStashesInGraph = states.ShowStashesInGraph;
            ShowUncommittedInGraph = states.ShowUncommittedInGraph;
            BranchColumnWidth = states.BranchColumnWidth;
            GraphColumnWidth = states.GraphColumnWidth;
        }

        /// <summary>
        ///     Writes this fork's state before upstream writes its own. A property added here
        ///     later has to be added to ForkRepositoryStates too, or it would be kept in the
        ///     shared file, where upstream can erase it.
        /// </summary>
        private void SaveForkStates()
        {
            if (string.IsNullOrEmpty(_file))
                return;

            var states = new ForkRepositoryStates
            {
                IsLocalBranchesVisibleInSideBar = IsLocalBranchesVisibleInSideBar,
                IsRemotesVisibleInSideBar = IsRemotesVisibleInSideBar,
                IsTagsVisibleInSideBar = IsTagsVisibleInSideBar,
                IsSubmodulesVisibleInSideBar = IsSubmodulesVisibleInSideBar,
                IsWorktreesVisibleInSideBar = IsWorktreesVisibleInSideBar,
                IsPullRequestsVisibleInSideBar = IsPullRequestsVisibleInSideBar,
                PinnedLaneBranch = PinnedLaneBranch,
                ShowStashesInGraph = ShowStashesInGraph,
                ShowUncommittedInGraph = ShowUncommittedInGraph,
                BranchColumnWidth = BranchColumnWidth,
                GraphColumnWidth = GraphColumnWidth,
            };

            ForkSettingsFile.Write(Path.Combine(Path.GetDirectoryName(_file) ?? string.Empty, ForkSettingsFile.UI_STATES), states, ForkJsonCodeGen.Default.ForkRepositoryStates);
        }
    }
}
