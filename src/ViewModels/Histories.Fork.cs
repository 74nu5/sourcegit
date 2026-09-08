using System.Collections.Generic;

namespace SourceGit.ViewModels
{
    /// <summary>
    ///     State this fork adds to the history view-model.
    /// </summary>
    public partial class Histories
    {
        /// <summary>
        ///     Number of branches that had to share the last lane, and whether to say so.
        /// </summary>
        public int HiddenLanes => _graph?.HiddenLanes ?? 0;
        public bool HasHiddenLanes => HiddenLanes > 0;

        /// <summary>
        ///     Manual width of the branch column, or 0 to size it to its contents.
        /// </summary>
        public double BranchColumnWidth
        {
            get => _repo.UIStates.BranchColumnWidth;
            set
            {
                if (_repo.UIStates.BranchColumnWidth != value)
                {
                    _repo.UIStates.BranchColumnWidth = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        ///     Commits and branches are loaded by two independent tasks, so ownership is
        ///     resolved again whenever either of them lands.
        /// </summary>
        public void ResolveBranchOwnership()
        {
            ResolveBranchOwnership(_commits);
        }

        private void ResolveBranchOwnership(List<Models.Commit> commits)
        {
            if (Preferences.Instance.BranchColumnMode == Models.BranchColumnMode.AllRows)
                Models.BranchOwnership.Resolve(commits, _repo.Branches);
            else
                Models.BranchOwnership.Clear(commits);
        }

        /// <summary>
        ///     Leave the graph for the working copy, where the uncommitted work actually is.
        /// </summary>
        public void OpenWorkingCopy()
        {
            _repo.SelectedViewIndex = 1;
        }

        /// <summary>
        ///     Selecting the work in progress shows nothing rather than asking git about a
        ///     commit that does not exist.
        ///
        ///     Everything downstream of a selected commit goes and reads it: the detail
        ///     panel, the file tree, the diff. None of that has an answer here, and letting
        ///     them try would put a git error on screen for a row that is behaving normally.
        ///     A double click on the row is the way in, and it opens the working copy.
        /// </summary>
        private bool ShowUncommittedDetail()
        {
            var touched = false;
            foreach (var c in _selectedCommits)
            {
                if (c.IsUncommitted)
                {
                    touched = true;
                    break;
                }
            }

            if (!touched)
                return false;

            _repo.SearchCommitContext.Selected = null;
            DetailContext = new Models.Null();

            if (_repo.UIStates.GraphHighlighting >= Models.CommitGraphHighlighting.SelectedCommitsOnly)
                GenerateGraph(_commits);

            return true;
        }

        /// <summary>
        ///     The commit the leftmost lane is held for, or null when nothing is pinned.
        ///
        ///     Resolved at draw time rather than stored: a pinned branch moves with every
        ///     commit, and what the graph needs is where it points now.
        /// </summary>
        private string ResolvePinnedLaneHead()
        {
            var full = _repo?.PinnedLaneBranch;
            if (string.IsNullOrEmpty(full))
                return null;

            var branch = _repo.Branches.Find(x => x.FullName.Equals(full, System.StringComparison.Ordinal));
            return branch?.Head;
        }

        private const double MIN_GRAPH_COLUMN_WIDTH = 24;
        private const double MAX_GRAPH_COLUMN_WIDTH = 240;
    }
}
