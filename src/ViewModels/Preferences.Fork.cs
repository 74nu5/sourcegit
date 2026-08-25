
namespace SourceGit.ViewModels
{
    /// <summary>
    ///     Settings this fork adds. Preferences.cs is a long file upstream keeps appending to;
    ///     staying out of it removes a whole class of rebase conflicts.
    /// </summary>
    public partial class Preferences
    {
        public bool SplitGraphColumnInHistories
        {
            get => _splitGraphColumnInHistories;
            set => SetProperty(ref _splitGraphColumnInHistories, value);
        }

        public bool ShowBranchColumnInHistories
        {
            get => _showBranchColumnInHistories;
            set => SetProperty(ref _showBranchColumnInHistories, value);
        }

        public Models.GraphLaneMode GraphLaneMode
        {
            get => _graphLaneMode;
            set => SetProperty(ref _graphLaneMode, value);
        }

        public bool ColorizeRowsByBranch
        {
            get => _colorizeRowsByBranch;
            set => SetProperty(ref _colorizeRowsByBranch, value);
        }

        public Models.BranchColumnMode BranchColumnMode
        {
            get => _branchColumnMode;
            set => SetProperty(ref _branchColumnMode, value);
        }

        private bool _splitGraphColumnInHistories = false;
        private bool _showBranchColumnInHistories = false;
        private Models.GraphLaneMode _graphLaneMode = Models.GraphLaneMode.Compact;
        private bool _colorizeRowsByBranch = false;
        private Models.BranchColumnMode _branchColumnMode = Models.BranchColumnMode.RefsOnly;
    }
}
