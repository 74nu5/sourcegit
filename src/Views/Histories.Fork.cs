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

        private ColumnResizer _columnResizer = null;
    }
}
