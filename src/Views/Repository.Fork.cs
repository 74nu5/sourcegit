using System;
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
            // Applied on every pass rather than from a hook of its own: upstream already
            // calls this one each time it hands out heights, and a hidden section has to
            // give its row back before those heights are computed, not after.
            ApplySectionRowHeights();

            // The chips that bring a section back are built here too, for the same reason:
            // a repository opened with sections already hidden has to show them without
            // waiting for somebody to hide a sixth one.
            if (DataContext is ViewModels.Repository owner)
                owner.RefreshHiddenSections();

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

            if (DataContext is not ViewModels.Repository repo)
                return;

            var window = new PruneBranches() { DataContext = repo.PrepareBranchPruning() };

            // ShowDialog throws on a null owner, and GetTopLevel returns one only while the
            // control is in a window -- which it is, but a handler that crashes the process
            // to prove it is not worth the line saved.
            if (TopLevel.GetTopLevel(this) is Avalonia.Controls.Window owner)
                window.ShowDialog(owner);
            else
                window.Show();
        }

        /// <summary>
        ///     Right-clicking a section header offers to hide it. The gesture is the one
        ///     already used on everything else in this panel, and costs no pixels for
        ///     something done once per repository.
        /// </summary>
        private void OnSidebarSectionContextRequested(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            e.Handled = true;

            if (sender is not Control { Tag: string key } anchor || DataContext is not ViewModels.Repository repo)
                return;

            var hide = new MenuItem() { Header = App.Text("Sidebar.Hide") };
            hide.Icon = this.CreateMenuIcon("Icons.EyeClose");
            hide.Click += (_, ev) =>
            {
                switch (key)
                {
                    case "local":
                        repo.IsLocalBranchSectionVisible = false;
                        break;
                    case "remote":
                        repo.IsRemoteSectionVisible = false;
                        break;
                    case "tag":
                        repo.IsTagSectionVisible = false;
                        break;
                    case "submodule":
                        repo.IsSubmoduleSectionVisible = false;
                        break;
                    case "worktree":
                        repo.IsWorktreeSectionVisible = false;
                        break;
                }

                UpdateLeftSidebarLayoutFromFork();
                ev.Handled = true;
            };

            var menu = new ContextMenu();
            menu.Items.Add(hide);
            menu.Open(anchor);
        }

        /// <summary>
        ///     Applies a section's disappearance to the panel: its header row gives back its
        ///     28 pixels, and the heights are handed out again.
        ///
        ///     The row has to be zeroed from code because its height is a literal in the
        ///     grid's definition string -- hiding the button alone leaves the gap behind.
        /// </summary>
        internal void UpdateLeftSidebarLayoutFromFork()
        {
            if (DataContext is not ViewModels.Repository repo)
                return;

            repo.RefreshHiddenSections();
            ApplySectionRowHeights();

            // Posted rather than called: the row heights above only take effect at the next
            // measure, and handing out list heights before that computes them against the
            // grid as it was -- a restored section came back thirteen pixels short.
            LeftSidebarGroups.InvalidateMeasure();
            Avalonia.Threading.Dispatcher.UIThread.Post(UpdateLeftSidebarLayout,
                Avalonia.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        ///     Gives a hidden section's header row its 28 pixels back.
        ///
        ///     It has to happen from code because that height is a literal inside the grid's
        ///     definition string: hiding the button alone leaves the gap exactly where it was.
        /// </summary>
        private void ApplySectionRowHeights()
        {
            if (DataContext is not ViewModels.Repository repo)
                return;

            var rows = LeftSidebarGroups.RowDefinitions;
            if (rows.Count < 10)
                return;

            void Row(int index, bool visible)
            {
                var wanted = visible ? 28.0 : 0.0;
                if (Math.Abs(rows[index].Height.Value - wanted) > 0.01)
                    rows[index].Height = new GridLength(wanted);
            }

            Row(0, repo.IsLocalBranchSectionVisible);
            Row(2, repo.IsRemoteSectionVisible);
            Row(4, repo.IsTagSectionVisible);
            Row(6, repo.IsSubmoduleSectionVisible);
            Row(8, repo.IsWorktreeSectionVisible);
        }
    }
}
