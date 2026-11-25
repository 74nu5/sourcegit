using System.Collections.Generic;
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
        ///     The order in which branches hold the leftmost columns of the graph, for this
        ///     repository; empty to follow the default order of the preferences.
        ///
        ///     Per repository for the same reason as the six above: the answer is a property
        ///     of the history being read. It is main then dev on one clone, master then
        ///     integration on the next, and nobody wants to re-say it on every switch.
        /// </summary>
        [JsonIgnore]
        public List<string> LaneOrder { get; set; } = [];

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
            LaneOrder = states.LaneOrder ?? [];
            ShowStashesInGraph = states.ShowStashesInGraph;
            ShowUncommittedInGraph = states.ShowUncommittedInGraph;
            BranchColumnWidth = states.BranchColumnWidth;
            GraphColumnWidth = states.GraphColumnWidth;

            // The single branch this fork used to keep in the leftmost lane becomes the
            // first, and only, entry of the order -- under the name the sidebar shows, which
            // is what an entry matches.
            var pinned = states.PinnedLaneBranch ?? string.Empty;
            if (LaneOrder.Count == 0 && pinned.Length > 0)
            {
                if (pinned.StartsWith("refs/heads/", System.StringComparison.Ordinal))
                    LaneOrder = [pinned.Substring(11)];
                else if (pinned.StartsWith("refs/remotes/", System.StringComparison.Ordinal))
                    LaneOrder = [pinned.Substring(13)];
            }
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
                LaneOrder = LaneOrder,
                ShowStashesInGraph = ShowStashesInGraph,
                ShowUncommittedInGraph = ShowUncommittedInGraph,
                BranchColumnWidth = BranchColumnWidth,
                GraphColumnWidth = GraphColumnWidth,
            };

            ForkSettingsFile.Write(Path.Combine(Path.GetDirectoryName(_file) ?? string.Empty, ForkSettingsFile.UI_STATES), states, ForkJsonCodeGen.Default.ForkRepositoryStates);
        }
    }
}
