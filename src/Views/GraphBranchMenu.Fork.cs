using Avalonia.Controls;

namespace SourceGit.Views
{
    /// <summary>
    ///     What this fork adds to the menu of a branch, a folder of branches and a tag:
    ///     what the graph shows, and which column it shows it in.
    ///
    ///     Both were reachable before, and neither was findable. Filtering hid behind an icon
    ///     that only appears on hover, and meant ticking one branch at a time; holding the
    ///     leftmost lane was not offered at all, it simply followed whatever was checked out.
    /// </summary>
    public static class GraphBranchMenu
    {
        public static void Append(ContextMenu menu, Control owner, ViewModels.Repository repo, Models.Branch branch)
        {
            if (menu == null || repo == null || branch == null)
                return;

            menu.Items.Add(new MenuItem() { Header = "-" });
            AppendPin(menu, owner, repo, branch);
            menu.Items.Add(BuildOnly(owner, () => repo.ShowOnlyInHistory(branch)));
            AppendShowAll(menu, owner, repo);
        }

        public static void Append(ContextMenu menu, Control owner, ViewModels.Repository repo, ViewModels.BranchTreeNode node)
        {
            if (menu == null || repo == null || node == null)
                return;

            menu.Items.Add(new MenuItem() { Header = "-" });
            menu.Items.Add(BuildOnly(owner, () => repo.ShowOnlyInHistory(node)));
            AppendShowAll(menu, owner, repo);
        }

        public static void Append(ContextMenu menu, Control owner, ViewModels.Repository repo, Models.Tag tag)
        {
            if (menu == null || repo == null || tag == null)
                return;

            menu.Items.Add(new MenuItem() { Header = "-" });
            menu.Items.Add(BuildOnly(owner, () => repo.ShowOnlyInHistory(tag)));
            AppendShowAll(menu, owner, repo);
        }

        /// <summary>
        ///     Hold the leftmost lane for this branch, whatever is checked out.
        ///
        ///     Offered only where lanes are handed out at all: the compact placement puts a
        ///     path at whatever rank it happens to hold among the live ones, so there is no
        ///     column there to pin anything to.
        /// </summary>
        private static void AppendPin(ContextMenu menu, Control owner, ViewModels.Repository repo, Models.Branch branch)
        {
            if (ViewModels.Preferences.Instance.GraphLaneMode != Models.GraphLaneMode.Stable)
                return;

            var pinned = repo.IsLanePinnedTo(branch);

            var pin = new MenuItem();
            pin.Header = App.Text(pinned ? "GraphLane.Unpin" : "GraphLane.Pin");
            pin.Icon = owner.CreateMenuIcon(pinned ? "Icons.Unlock" : "Icons.Lock");
            pin.Click += (_, e) =>
            {
                repo.SetPinnedLaneBranch(branch);
                e.Handled = true;
            };

            menu.Items.Add(pin);
        }

        /// <summary>
        ///     Added to the same menus rather than only to the filter bar, because that bar
        ///     appears only once something is filtered -- which is precisely the moment the
        ///     way back stops being obvious.
        /// </summary>
        private static void AppendShowAll(ContextMenu menu, Control owner, ViewModels.Repository repo)
        {
            if (!repo.HasHistoryFilters)
                return;

            var all = new MenuItem();
            all.Header = App.Text("GraphVisibility.All");
            all.Icon = owner.CreateMenuIcon("Icons.Eye");
            all.Click += (_, e) =>
            {
                repo.ClearHistoryFilters();
                e.Handled = true;
            };

            menu.Items.Add(all);
        }

        private static MenuItem BuildOnly(Control owner, System.Action apply)
        {
            var only = new MenuItem();
            only.Header = App.Text("GraphVisibility.Only");
            only.Icon = owner.CreateMenuIcon("Icons.Filter");
            only.Click += (_, e) =>
            {
                apply();
                e.Handled = true;
            };

            return only;
        }
    }
}
