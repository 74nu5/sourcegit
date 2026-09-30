using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;

using Avalonia.Collections;

namespace SourceGit.ViewModels
{
    /// <summary>
    ///     Settings this fork adds. Preferences.cs is a long file upstream keeps appending to;
    ///     staying out of it removes a whole class of rebase conflicts.
    /// </summary>
    public partial class Preferences
    {
        [JsonIgnore]
        public bool SplitGraphColumnInHistories
        {
            get => _splitGraphColumnInHistories;
            set => SetProperty(ref _splitGraphColumnInHistories, value);
        }

        [JsonIgnore]
        public bool ShowBranchColumnInHistories
        {
            get => _showBranchColumnInHistories;
            set => SetProperty(ref _showBranchColumnInHistories, value);
        }

        [JsonIgnore]
        public Models.GraphLaneMode GraphLaneMode
        {
            get => _graphLaneMode;
            set => SetProperty(ref _graphLaneMode, value);
        }

        [JsonIgnore]
        public bool ColorizeRowsByBranch
        {
            get => _colorizeRowsByBranch;
            set => SetProperty(ref _colorizeRowsByBranch, value);
        }

        /// <summary>
        ///     Whether the stashes list leads with the message rather than with stash@{N}.
        ///
        ///     Off, so the page looks exactly as upstream draws it until somebody asks. The
        ///     index does not disappear when this is on -- it moves to the second line, because
        ///     it is what the confirmation dialogs name and what `git stash list` prints, so a
        ///     list that dropped it could not be matched against either.
        /// </summary>
        [JsonIgnore]
        public bool ShowStashMessageAsLabel
        {
            get => _showStashMessageAsLabel;
            set => SetProperty(ref _showStashMessageAsLabel, value);
        }

        /// <summary>
        ///     Whether a repository's worktrees live as a row under its tab rather than as tabs
        ///     of their own. Off, so the tab bar is exactly upstream's until somebody asks.
        /// </summary>
        [JsonIgnore]
        public bool GroupWorktreesInTabs
        {
            get => _groupWorktreesInTabs;
            set => SetProperty(ref _groupWorktreesInTabs, value);
        }

        /// <summary>
        ///     Credentials for the forges this fork talks to. Empty by default, and while it
        ///     is empty nothing here ever reaches the network.
        /// </summary>
        [JsonIgnore]
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
        [JsonIgnore]
        public bool ShowPullRequestIndicator
        {
            get => _showPullRequestIndicator;
            set => SetProperty(ref _showPullRequestIndicator, value);
        }

        /// <summary>
        ///     Whether the "+origin" suffix on a chip becomes the remote's icon, tinted with
        ///     the colours of the forge it lives on. Off by default, like everything else
        ///     this fork adds.
        /// </summary>
        [JsonIgnore]
        public bool ShowRemoteIconInsteadOfName
        {
            get => _showRemoteIconInsteadOfName;
            set => SetProperty(ref _showRemoteIconInsteadOfName, value);
        }

        [JsonIgnore]
        public Models.BranchColumnMode BranchColumnMode
        {
            get => _branchColumnMode;
            set => SetProperty(ref _branchColumnMode, value);
        }

        /// <summary>
        ///     Whether to look for a new release as the application starts.
        ///
        ///     Upstream asks once a day and remembers when it last asked, which suits an
        ///     application people open when they need something from it. This one is left
        ///     running for days, and a daily throttle turns into almost never against that
        ///     habit: the check fires on the first start after midnight, then not again for
        ///     as long as the window stays open.
        ///
        ///     Asking on every start is affordable for the same reason the throttle was
        ///     useless -- an application nobody closes is not started often -- and one
        ///     unauthenticated request sits well inside the sixty an hour GitHub allows per
        ///     address. The timestamp is still written, so going back to upstream's throttle
        ///     would not find a stale value and fire on it.
        /// </summary>
        public bool ShouldCheck4UpdateOnEveryStartup()
        {
            if (!_check4UpdatesOnStartup)
                return false;

            LastCheckUpdateTime = DateTime.Now.Subtract(DateTime.UnixEpoch.ToLocalTime()).TotalSeconds;
            return true;
        }

        /// <summary>
        ///     Loads this fork's settings from preference.fork.json. Called right after
        ///     upstream's own load, before anything reads them.
        ///
        ///     Every one of them is marked [JsonIgnore], so upstream's file neither provides nor
        ///     receives them any more. When the side file does not exist yet, they are read from
        ///     preference.json, where earlier versions of this fork kept them -- and that has to
        ///     happen before the first save, which would otherwise write preference.json without
        ///     them and lose them for good.
        ///
        ///     Fields are assigned directly: nothing is bound yet, and raising changes here would
        ///     only wake handlers that expect a running application.
        /// </summary>
        public void LoadForkSettings(string configDir)
        {
            var settings = Models.ForkSettingsFile.Read(
                Path.Combine(configDir ?? string.Empty, Models.ForkSettingsFile.PREFERENCES),
                Path.Combine(configDir ?? string.Empty, "preference.json"),
                ForkJsonCodeGen.Default.ForkPreferences);

            _splitGraphColumnInHistories = settings.SplitGraphColumnInHistories;
            _showBranchColumnInHistories = settings.ShowBranchColumnInHistories;
            _graphLaneMode = settings.GraphLaneMode;
            _colorizeRowsByBranch = settings.ColorizeRowsByBranch;
            _showStashMessageAsLabel = settings.ShowStashMessageAsLabel;
            _groupWorktreesInTabs = settings.GroupWorktreesInTabs;
            _showPullRequestIndicator = settings.ShowPullRequestIndicator;
            _showRemoteIconInsteadOfName = settings.ShowRemoteIconInsteadOfName;
            _branchColumnMode = settings.BranchColumnMode;

            ForgeAccounts.Clear();
            ForgeAccounts.AddRange(settings.ForgeAccounts ?? []);
        }

        /// <summary>
        ///     Writes this fork's settings to preference.fork.json. Called from upstream's
        ///     Save, before it writes preference.json, so a save that gets no further than the
        ///     side file has still kept everything.
        ///
        ///     A setting added to this fork later has to be added here and to ForkPreferences
        ///     as well as marked [JsonIgnore]. Left out, it would still be kept -- in
        ///     preference.json, where upstream can erase it again.
        /// </summary>
        public void SaveForkSettings(string configDir)
        {
            var settings = new Models.ForkPreferences
            {
                SplitGraphColumnInHistories = _splitGraphColumnInHistories,
                ShowBranchColumnInHistories = _showBranchColumnInHistories,
                GraphLaneMode = _graphLaneMode,
                ColorizeRowsByBranch = _colorizeRowsByBranch,
                ShowStashMessageAsLabel = _showStashMessageAsLabel,
                GroupWorktreesInTabs = _groupWorktreesInTabs,
                ForgeAccounts = new List<Models.ForgeAccount>(ForgeAccounts),
                ShowPullRequestIndicator = _showPullRequestIndicator,
                ShowRemoteIconInsteadOfName = _showRemoteIconInsteadOfName,
                BranchColumnMode = _branchColumnMode,
            };

            Models.ForkSettingsFile.Write(
                Path.Combine(configDir ?? string.Empty, Models.ForkSettingsFile.PREFERENCES),
                settings,
                ForkJsonCodeGen.Default.ForkPreferences);
        }

        private bool _splitGraphColumnInHistories = false;
        private bool _showBranchColumnInHistories = false;
        private Models.GraphLaneMode _graphLaneMode = Models.GraphLaneMode.Compact;
        private bool _colorizeRowsByBranch = false;
        private bool _showStashMessageAsLabel = false;
        private bool _groupWorktreesInTabs = false;
        private bool _showPullRequestIndicator = false;
        private bool _showRemoteIconInsteadOfName = false;
        private Models.BranchColumnMode _branchColumnMode = Models.BranchColumnMode.RefsOnly;
    }
}
