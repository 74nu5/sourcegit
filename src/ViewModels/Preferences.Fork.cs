using Avalonia.Collections;

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

        /// <summary>
        ///     Credentials for the forges this fork talks to. Empty by default, and while it
        ///     is empty nothing here ever reaches the network.
        /// </summary>
        public AvaloniaList<Models.ForgeAccount> ForgeAccounts
        {
            get;
            set;
        } = [];

        /// <summary>
        ///     The account to use for a repository, or null when none covers it.
        ///
        ///     The most specific one wins, so a token issued for a single Azure DevOps project
        ///     can sit beside the organisation-wide one without either shadowing the other.
        /// </summary>
        public Models.ForgeAccount FindForgeAccount(Models.ForgeRepository repo)
        {
            Models.ForgeAccount best = null;
            var bestScore = -1;

            foreach (var account in ForgeAccounts)
            {
                var score = account.Match(repo);
                if (score > bestScore)
                {
                    best = account;
                    bestScore = score;
                }
            }

            return best;
        }

        /// <summary>
        ///     Whether a branch carrying an open pull request shows it. Off by default like
        ///     everything else this fork adds — and while it is off, nothing here ever
        ///     reaches the network.
        /// </summary>
        public bool ShowPullRequestIndicator
        {
            get => _showPullRequestIndicator;
            set => SetProperty(ref _showPullRequestIndicator, value);
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
        private bool _showPullRequestIndicator = false;
        private Models.BranchColumnMode _branchColumnMode = Models.BranchColumnMode.RefsOnly;
    }
}
