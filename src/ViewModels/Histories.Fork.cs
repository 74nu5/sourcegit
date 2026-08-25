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

        private const double MIN_GRAPH_COLUMN_WIDTH = 24;
        private const double MAX_GRAPH_COLUMN_WIDTH = 240;
    }
}
