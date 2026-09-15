using System;
using System.Collections.Generic;

using Avalonia.Controls;
using Avalonia.Input;

namespace SourceGit.Views
{
    /// <summary>
    ///     Everything this fork adds to the history view. Kept out of Histories.axaml.cs, which
    ///     upstream touches about fifty times a year, so that a rebase never has to merge our
    ///     additions with theirs. Handlers wired from the XAML work from here just as well:
    ///     they are members of the same partial class.
    /// </summary>
    public partial class Histories
    {
        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            ApplyStoredColumnWidths();
        }

        /// <summary>
        ///     Puts the stored widths back on the columns that carry one. A binding on
        ///     DataGridColumn.Width does not survive the grid's own sizing pass, which is why
        ///     the author column has always been sized from code here.
        /// </summary>
        private void ApplyStoredColumnWidths()
        {
            if (DataContext is not ViewModels.Histories vm)
                return;

            foreach (var column in CommitListContainer.Columns)
            {
                if (column.Tag is "author")
                    column.Width = new(vm.AuthorColumnWidth, DataGridLengthUnitType.Pixel);
                else if (column.Tag is "branch" && vm.BranchColumnWidth > 0)
                    column.Width = new(vm.BranchColumnWidth, DataGridLengthUnitType.Pixel);
            }
        }

        /// <summary>
        ///     A drag handle sitting on the right edge of a column, and the column it resizes.
        /// </summary>
        private sealed class ColumnResizer
        {
            public DataGridColumn Target { get; init; }
            public double Origin { get; init; }
            public double StartWidth { get; init; }
            public bool Inverted { get; init; }
            public double Min { get; init; }
            public double Max { get; init; }
        }

        /// <summary>
        ///     Finds the handle under the cursor, if any. Column indices are never assumed:
        ///     which columns are visible depends on the user's display options.
        /// </summary>
        private ColumnResizer FindColumnResizer(double x)
        {
            var visible = new List<DataGridColumn>();
            foreach (var column in CommitListContainer.Columns)
            {
                if (column.IsVisible)
                    visible.Add(column);
            }

            var edge = 0.0;
            for (var i = 0; i < visible.Count - 1; i++)
            {
                var column = visible[i];
                edge += column.ActualWidth;

                if (Math.Abs(edge - 4 - x) > 4)
                    continue;

                // A fixed-width column is resized directly.
                if (!column.Width.IsStar && column.Tag is "branch" or "graph")
                {
                    return new ColumnResizer
                    {
                        Target = column,
                        Origin = edge,
                        StartWidth = column.ActualWidth,
                        Inverted = false,
                        Min = 24,
                        Max = 400,
                    };
                }

                // A star column cannot take a pixel width, so the handle on its right edge
                // resizes the column that follows it instead, in the opposite direction.
                var next = visible[i + 1];
                if (column.Width.IsStar && next.Tag is "author")
                {
                    return new ColumnResizer
                    {
                        Target = next,
                        Origin = edge,
                        StartWidth = next.ActualWidth,
                        Inverted = true,
                        Min = 80,
                        Max = Math.Max(80, column.ActualWidth + next.ActualWidth - 100),
                    };
                }
            }

            return null;
        }

        private void OnCommitListHeaderPointerMoved(object sender, PointerEventArgs e)
        {
            if (sender is not Border border)
                return;

            if (DataContext is not ViewModels.Histories vm)
                return;

            var pos = e.GetPosition(border);
            if (_columnResizer != null)
            {
                var delta = _columnResizer.Inverted ? _columnResizer.Origin - pos.X : pos.X - _columnResizer.Origin;
                var w = Math.Clamp(_columnResizer.StartWidth + delta, _columnResizer.Min, _columnResizer.Max);
                _columnResizer.Target.Width = new(w, DataGridLengthUnitType.Pixel);

                if (_columnResizer.Target.Tag is "author")
                    vm.AuthorColumnWidth = w;
                else if (_columnResizer.Target.Tag is "branch")
                    vm.BranchColumnWidth = w;
                else if (_columnResizer.Target.Tag is "graph")
                    vm.GraphColumnWidth = w;

                return;
            }

            var cursor = FindColumnResizer(pos.X) != null ? _resizingCursor : Cursor.Default;
            if (border.Cursor != cursor)
                border.Cursor = cursor;
        }

        private void OnCommitListHeaderPointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (sender is not Border border)
                return;

            if (!e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
                return;

            _columnResizer = FindColumnResizer(e.GetPosition(border).X);
            if (_columnResizer != null)
                e.Handled = true;
        }

        private void OnCommitListHeaderPointerReleased(object sender, PointerReleasedEventArgs e)
        {
            _columnResizer = null;
        }

        /// <summary>
        ///     Locates the column the commit graph is drawn over and returns its horizontal
        ///     offset within the grid along with the width available to the graph.
        /// </summary>
        private static (double StartX, double ClipWidth) MeasureGraphViewport(DataGrid dataGrid)
        {
            var startX = 0.0;
            foreach (var column in dataGrid.Columns)
            {
                if (!column.IsVisible)
                    continue;

                // The graph column is flagged with Tag="graph" in Histories.axaml.
                if (column.Tag is "graph")
                    return (startX, column.ActualWidth - 4);

                startX += column.ActualWidth;
            }

            return (0, dataGrid.Columns[0].ActualWidth - 4);
        }

        /// <summary>
        ///     One submenu per reference, so a commit carrying several of them gives access to
        ///     each of them without having to unfold anything.
        /// </summary>
        private ContextMenu CreateContextMenuForDecorators(ViewModels.Repository repo, Models.Commit commit, List<Models.Decorator> decorators)
        {
            if (decorators.Count == 1)
                return CreateContextMenuForDecorator(repo, commit, decorators[0]);

            var menu = new ContextMenu();
            foreach (var decorator in decorators)
            {
                var submenu = BuildDecoratorSubmenu(repo, commit, decorator);
                if (submenu != null)
                    menu.Items.Add(submenu);
            }

            return menu.Items.Count > 0 ? menu : null;
        }

        /// <summary>
        ///     Actions of a single reference, listed directly: there is nothing to disambiguate,
        ///     so nesting them under the reference name would only add a level to walk through.
        /// </summary>
        private ContextMenu CreateContextMenuForDecorator(ViewModels.Repository repo, Models.Commit commit, Models.Decorator decorator)
        {
            var submenu = BuildDecoratorSubmenu(repo, commit, decorator);
            if (submenu == null)
                return null;

            // An item cannot belong to two parents, so detach before re-parenting.
            var items = new List<object>();
            foreach (var item in submenu.Items)
                items.Add(item);
            submenu.Items.Clear();

            var menu = new ContextMenu();
            foreach (var item in items)
                menu.Items.Add(item);

            return menu;
        }

        /// <summary>
        ///     The FillXxxMenu helpers already wrap their actions in a submenu named after the
        ///     reference, icon included. This hands that submenu back.
        /// </summary>
        private MenuItem BuildDecoratorSubmenu(ViewModels.Repository repo, Models.Commit commit, Models.Decorator decorator)
        {
            var current = repo.CurrentBranch;
            if (current == null)
                return null;

            var host = new ContextMenu();
            switch (decorator.Type)
            {
                case Models.DecoratorType.CurrentBranchHead:
                    FillCurrentBranchMenu(host, repo, current);
                    break;
                case Models.DecoratorType.LocalBranchHead:
                    var local = repo.Branches.Find(x => x.IsLocal && decorator.Name.Equals(x.Name, StringComparison.Ordinal));
                    if (local == null)
                        return null;
                    FillOtherLocalBranchMenu(host, repo, local, current, commit.IsMerged);
                    break;
                case Models.DecoratorType.RemoteBranchHead:
                    var remote = repo.Branches.Find(x => !x.IsLocal && decorator.Name.Equals(x.FriendlyName, StringComparison.Ordinal));
                    if (remote == null)
                        return null;
                    FillRemoteBranchMenu(host, repo, remote, current, commit.IsMerged);
                    break;
                case Models.DecoratorType.Tag:
                    var tag = repo.Tags.Find(x => decorator.Name.Equals(x.Name, StringComparison.Ordinal));
                    if (tag == null)
                        return null;
                    FillTagMenu(host, repo, tag, current);
                    break;
                default:
                    return null;
            }

            if (host.Items.Count != 1 || host.Items[0] is not MenuItem submenu)
                return null;

            host.Items.Clear();
            return submenu;
        }

        /// <summary>
        ///     The work in progress has no menu.
        ///
        ///     Every entry upstream builds acts on a revision -- reset here, cherry-pick,
        ///     create a branch, copy the hash. There is no revision, so an empty menu is the
        ///     honest answer, and a menu that offered any of them would be a trap.
        /// </summary>
        private bool SuppressMenuForUncommitted(List<Models.Commit> commits, ContextRequestedEventArgs e)
        {
            foreach (var c in commits)
            {
                if (!c.IsUncommitted)
                    continue;

                e.Handled = true;
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Double-clicking the work in progress opens it, rather than trying to check out
        ///     a branch at a revision that does not exist.
        /// </summary>
        private bool OpenUncommitted(ViewModels.Histories histories, Models.Commit commit)
        {
            if (histories == null || !commit.IsUncommitted)
                return false;

            histories.OpenWorkingCopy();
            return true;
        }

        /// <summary>
        ///     The history options this fork adds. Gathered in one method so that the
        ///     upstream builder, which keeps being extended, carries a single call to us.
        ///
        ///     It sat in the repository page until 2026.20 split that page in two and
        ///     moved this menu here.
        /// </summary>
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

            // Stashes sit under the placement options rather than under upstream's show
            // flags: those all map to a git argument, and this one does not -- it names
            // revisions git would otherwise never walk to.
            var stashesInGraph = new MenuItem();
            stashesInGraph.Header = App.Text("Repository.ShowStashesInGraph");
            if (repo.ShowStashesInGraph)
                stashesInGraph.Icon = this.CreateMenuIcon("Icons.Check");
            stashesInGraph.Click += (_, ev) =>
            {
                repo.ToggleStashesInGraph();
                ev.Handled = true;
            };

            menu.Items.Add(new MenuItem() { Header = "-" });
            menu.Items.Add(branchPlacement);
            menu.Items.Add(compactLanes);
            menu.Items.Add(stableLanes);
            var uncommittedInGraph = new MenuItem();
            uncommittedInGraph.Header = App.Text("Repository.ShowUncommittedInGraph");
            if (repo.ShowUncommittedInGraph)
                uncommittedInGraph.Icon = this.CreateMenuIcon("Icons.Check");
            uncommittedInGraph.Click += (_, ev) =>
            {
                repo.ToggleUncommittedInGraph();
                ev.Handled = true;
            };

            menu.Items.Add(stashesInGraph);
            menu.Items.Add(uncommittedInGraph);
            menu.Items.Add(new MenuItem() { Header = "-" });
            menu.Items.Add(columns);
            menu.Items.Add(showBranchColumn);
            menu.Items.Add(refsOnly);
            menu.Items.Add(allRows);
            menu.Items.Add(splitGraph);
            menu.Items.Add(colorizeRows);
            menu.Items.Add(remoteIcon);
        }

        private ColumnResizer _columnResizer = null;
    }
}
