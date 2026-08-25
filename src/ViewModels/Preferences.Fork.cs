
using System;

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
        ///     The account to use for a host, or null when none is configured. Matching is on
        ///     the host alone: one set of credentials serves every repository on it.
        /// </summary>
        public Models.ForgeAccount FindForgeAccount(string host)
        {
            if (string.IsNullOrEmpty(host))
                return null;

            foreach (var account in ForgeAccounts)
            {
                if (host.Equals(account.Host, StringComparison.OrdinalIgnoreCase))
                    return account;
            }

            return null;
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
