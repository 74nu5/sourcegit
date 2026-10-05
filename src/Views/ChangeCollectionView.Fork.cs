using System.Collections.Generic;

using Avalonia.Controls;

namespace SourceGit.Views
{
    public partial class ChangeCollectionView
    {
        /// <summary>
        ///     Adds "Expand All Folders" and "Collapse All Folders" to a menu opened on this list,
        ///     when it shows a tree with folders in it. Nothing in list or grid mode, nothing for
        ///     a tree of files at the root only.
        /// </summary>
        internal void AppendFolderOptions(ContextMenu menu)
        {
            if (menu == null || Content is not ViewModels.ChangeCollectionAsTree tree)
                return;

            var anyExpanded = false;
            var anyCollapsed = false;
            ScanFolders(tree.Tree, ref anyExpanded, ref anyCollapsed);
            if (!anyExpanded && !anyCollapsed)
                return;

            var expandAll = new MenuItem();
            expandAll.Header = App.Text("ChangeTree.ExpandAll");
            expandAll.Icon = this.CreateMenuIcon("Icons.Folder.Open");
            expandAll.IsEnabled = anyCollapsed;
            expandAll.Click += (_, e) =>
            {
                SetAllFoldersExpanded(true);
                e.Handled = true;
            };

            var collapseAll = new MenuItem();
            collapseAll.Header = App.Text("ChangeTree.CollapseAll");
            collapseAll.Icon = this.CreateMenuIcon("Icons.Folder");
            collapseAll.IsEnabled = anyExpanded;
            collapseAll.Click += (_, e) =>
            {
                SetAllFoldersExpanded(false);
                e.Handled = true;
            };

            if (menu.Items.Count > 0)
                menu.Items.Add(new MenuItem { Header = "-" });
            menu.Items.Add(expandAll);
            menu.Items.Add(collapseAll);
        }

        /// <summary>
        ///     Opens a menu holding only the folder entries, for a right-click on a list where
        ///     nothing is selected -- upstream opens no menu then, since all its entries act on
        ///     the selection, but expanding or collapsing the tree needs none.
        /// </summary>
        internal bool OpenFolderMenu(object sender, ContextRequestedEventArgs e)
        {
            var menu = new ContextMenu();
            AppendFolderOptions(menu);
            if (menu.Items.Count == 0)
                return false;

            menu.Open(sender as Control ?? this);
            e.Handled = true;
            return true;
        }

        /// <summary>
        ///     Expands or collapses every folder of the tree, nested ones included.
        ///
        ///     Rows are inserted and removed through ToggleNodeIsExpanded, one visible folder at
        ///     a time, rather than rebuilt from scratch: clearing the rows would clear the
        ///     selection with them, and with it the diff on the right, even when expanding --
        ///     which hides nothing. Collapsing still drops the selected files it hides, exactly
        ///     as collapsing a folder by hand does.
        /// </summary>
        public void SetAllFoldersExpanded(bool expanded)
        {
            if (Content is not ViewModels.ChangeCollectionAsTree tree)
                return;

            if (expanded)
                ExpandAll(tree.Tree);
            else
                CollapseAll(tree.Tree);
        }

        /// <summary>
        ///     Adds every collapsed folder of the tree to <paramref name="folded"/>, hidden ones
        ///     included.
        ///
        ///     Upstream remembers which folders were collapsed, across the refresh that follows
        ///     every change on disk, by reading the visible rows -- so a folder collapsed inside
        ///     another collapsed folder was forgotten, and came back expanded. Once "Collapse
        ///     All" exists, that is every nested folder, at the first file saved.
        /// </summary>
        private static void CollectFolded(List<ViewModels.ChangeTreeNode> nodes, HashSet<string> folded)
        {
            foreach (var node in nodes)
            {
                if (!node.IsFolder)
                    continue;

                if (!node.IsExpanded)
                    folded.Add(node.FullPath);

                CollectFolded(node.Children, folded);
            }
        }

        // Every node passed here is visible: the roots always are, and a folder's children
        // are only walked once the folder is expanded.
        private void ExpandAll(List<ViewModels.ChangeTreeNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (!node.IsFolder)
                    continue;

                if (node.IsExpanded)
                {
                    ExpandAll(node.Children);
                }
                else
                {
                    MarkExpanded(node.Children, true);
                    ToggleNodeIsExpanded(node);
                }
            }
        }

        private void CollapseAll(List<ViewModels.ChangeTreeNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (!node.IsFolder)
                    continue;

                MarkExpanded(node.Children, false);
                if (node.IsExpanded)
                    ToggleNodeIsExpanded(node);
            }
        }

        private static void MarkExpanded(List<ViewModels.ChangeTreeNode> nodes, bool expanded)
        {
            foreach (var node in nodes)
            {
                if (!node.IsFolder)
                    continue;

                node.IsExpanded = expanded;
                MarkExpanded(node.Children, expanded);
            }
        }

        // Visible folders only: an expanded folder inside a collapsed one shows nothing, and
        // offering to collapse it would offer a click that changes nothing on screen. Every
        // hidden folder has a collapsed ancestor in view, so "expand" loses nothing by this.
        private static void ScanFolders(List<ViewModels.ChangeTreeNode> nodes, ref bool anyExpanded, ref bool anyCollapsed)
        {
            foreach (var node in nodes)
            {
                if (!node.IsFolder)
                    continue;

                if (node.IsExpanded)
                {
                    anyExpanded = true;
                    ScanFolders(node.Children, ref anyExpanded, ref anyCollapsed);
                }
                else
                {
                    anyCollapsed = true;
                }
            }
        }
    }
}
