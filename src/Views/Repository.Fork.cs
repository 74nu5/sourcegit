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
    }
}
