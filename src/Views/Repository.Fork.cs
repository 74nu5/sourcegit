using Avalonia.Controls;

namespace SourceGit.Views
{
    /// <summary>
    ///     History options this fork adds to the repository menu. Gathered here so that
    ///     OnOpenAdvancedHistoriesOption, which upstream keeps extending, only carries a single
    ///     call to us.
    /// </summary>
    public partial class Repository
    {
        /// <summary>
        ///     Room the pull request section needs, taken off the height the panel has to hand
        ///     out to the sections upstream owns.
        ///
        ///     This is the single line this fork adds to UpdateLeftSidebarLayout, which is a
        ///     hundred and twenty lines of hand-rolled cascade that upstream keeps editing.
        ///     Zero when no forge covers the repository, in which case the section is not
        ///     there at all and the panel behaves exactly as it always did.
        /// </summary>
        internal double ReserveForkSidebarSpace(double available)
        {
            var section = PullRequestsSection;
            if (section is not { IsVisible: true })
                return 0;

            // A third of what is left, at most: the branches are what the panel is mostly for.
            return section.Measure(available / 3);
        }

        /// <summary>
        ///     Called by the section when its contents change, since the panel hands out
        ///     heights by hand and cannot notice on its own.
        /// </summary>
        internal void UpdateForkSidebarLayout()
        {
            UpdateLeftSidebarLayout();
        }

        private void AppendForkHistoryOptions(ContextMenu menu, ViewModels.Repository repo, ViewModels.Histories histories, ViewModels.Preferences pref)
        {
            var branchPlacement = new MenuItem();
            branchPlacement.Header = App.Text("Repository.BranchPlacement");
            branchPlacement.IsEnabled = false;

            var compactLanes = new MenuItem();
            compactLanes.Header = App.Text("Repository.BranchPlacement.Compact");
            if (pref.GraphLaneMode == Models.GraphLaneMode.Compact)
                compactLanes.Icon = this.CreateMenuIcon("Icons.Check");
            compactLanes.Click += (_, ev) =>
            {
                pref.GraphLaneMode = Models.GraphLaneMode.Compact;
                repo.RefreshCommits();
                ev.Handled = true;
            };

            var stableLanes = new MenuItem();
            stableLanes.Header = App.Text("Repository.BranchPlacement.Stable");
            if (pref.GraphLaneMode == Models.GraphLaneMode.Stable)
                stableLanes.Icon = this.CreateMenuIcon("Icons.Check");
            stableLanes.Click += (_, ev) =>
            {
                pref.GraphLaneMode = Models.GraphLaneMode.Stable;
                repo.RefreshCommits();
                ev.Handled = true;
            };

            var columns = new MenuItem();
            columns.Header = App.Text("Repository.HistoriesColumns");
            columns.IsEnabled = false;

            var showBranchColumn = new MenuItem();
            showBranchColumn.Header = App.Text("Repository.HistoriesColumns.ShowBranch");
            if (pref.ShowBranchColumnInHistories)
                showBranchColumn.Icon = this.CreateMenuIcon("Icons.Check");
            showBranchColumn.Click += (_, ev) =>
            {
                pref.ShowBranchColumnInHistories = !pref.ShowBranchColumnInHistories;
                ev.Handled = true;
            };

            var splitGraph = new MenuItem();
            splitGraph.Header = App.Text("Repository.HistoriesColumns.SplitGraph");
            if (pref.SplitGraphColumnInHistories)
                splitGraph.Icon = this.CreateMenuIcon("Icons.Check");
            splitGraph.Click += (_, ev) =>
            {
                pref.SplitGraphColumnInHistories = !pref.SplitGraphColumnInHistories;
                ev.Handled = true;
            };

            var refsOnly = new MenuItem();
            refsOnly.Header = App.Text("Repository.HistoriesColumns.RefsOnly");
            if (pref.BranchColumnMode == Models.BranchColumnMode.RefsOnly)
                refsOnly.Icon = this.CreateMenuIcon("Icons.Check");
            refsOnly.Click += (_, ev) =>
            {
                pref.BranchColumnMode = Models.BranchColumnMode.RefsOnly;
                pref.ShowBranchColumnInHistories = true;
                histories.ResolveBranchOwnership();
                ev.Handled = true;
            };

            var allRows = new MenuItem();
            allRows.Header = App.Text("Repository.HistoriesColumns.AllRows");
            if (pref.BranchColumnMode == Models.BranchColumnMode.AllRows)
                allRows.Icon = this.CreateMenuIcon("Icons.Check");
            allRows.Click += (_, ev) =>
            {
                pref.BranchColumnMode = Models.BranchColumnMode.AllRows;
                pref.ShowBranchColumnInHistories = true;
                histories.ResolveBranchOwnership();
                ev.Handled = true;
            };

            var colorizeRows = new MenuItem();
            colorizeRows.Header = App.Text("Repository.HistoriesColumns.ColorizeRows");
            if (pref.ColorizeRowsByBranch)
                colorizeRows.Icon = this.CreateMenuIcon("Icons.Check");
            colorizeRows.Click += (_, ev) =>
            {
                pref.ColorizeRowsByBranch = !pref.ColorizeRowsByBranch;
                ev.Handled = true;
            };

            var remoteIcon = new MenuItem();
            remoteIcon.Header = App.Text("Repository.HistoriesColumns.RemoteIcon");
            if (pref.ShowRemoteIconInsteadOfName)
                remoteIcon.Icon = this.CreateMenuIcon("Icons.Check");
            remoteIcon.Click += (_, ev) =>
            {
                pref.ShowRemoteIconInsteadOfName = !pref.ShowRemoteIconInsteadOfName;

                // The chips are measured once and kept; only a reload builds them again.
                repo.RefreshCommits();
                ev.Handled = true;
            };

            menu.Items.Add(new MenuItem() { Header = "-" });
            menu.Items.Add(branchPlacement);
            menu.Items.Add(compactLanes);
            menu.Items.Add(stableLanes);
            menu.Items.Add(new MenuItem() { Header = "-" });
            menu.Items.Add(columns);
            menu.Items.Add(showBranchColumn);
            menu.Items.Add(refsOnly);
            menu.Items.Add(allRows);
            menu.Items.Add(splitGraph);
            menu.Items.Add(colorizeRows);
            menu.Items.Add(remoteIcon);
        }

        /// <summary>
        ///     Opens the window that offers to remove the local branches that no longer
        ///     stand for anything.
        /// </summary>
        private void OnPruneLocalBranches(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            e.Handled = true;

            if (DataContext is ViewModels.Repository repo)
                repo.PruneLocalBranches();
        }
    }
}
