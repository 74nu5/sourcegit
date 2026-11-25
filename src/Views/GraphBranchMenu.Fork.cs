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
        ///     Where this branch stands in the lane order: first, one column left or right,
        ///     last, or out of it.
        ///
        ///     Offered in both layouts. The compact placement gives a path no lane of its
        ///     own -- it sits at whatever rank it holds among the live paths -- and drifting
        ///     is exactly what it does, so that is where holding a column is worth the most.
        /// </summary>
        private static void AppendPin(ContextMenu menu, Control owner, ViewModels.Repository repo, Models.Branch branch)
        {
            if (branch.IsDetachedHead)
                return;

            var index = repo.LaneOrderIndexOf(branch);
            var count = repo.LaneOrder.Count;

            var root = new MenuItem();
            root.Header = App.Text("LaneOrder.Menu");
            root.Icon = owner.CreateMenuIcon(index >= 0 ? "Icons.Lock" : "Icons.Unlock");

            if (index != 0)
                root.Items.Add(LaneOrderItem(owner, "LaneOrder.First", "Icons.Lock", () => repo.PlaceInLaneOrder(branch, 0)));

            if (index > 0)
                root.Items.Add(LaneOrderItem(owner, "LaneOrder.Left", null, () => repo.PlaceInLaneOrder(branch, index - 1)));

            if (index >= 0 && index < count - 1)
                root.Items.Add(LaneOrderItem(owner, "LaneOrder.Right", null, () => repo.PlaceInLaneOrder(branch, index + 1)));

            if (index < 0)
                root.Items.Add(LaneOrderItem(owner, "LaneOrder.Last", null, () => repo.PlaceInLaneOrder(branch, -1)));
            else
                root.Items.Add(LaneOrderItem(owner, "LaneOrder.Remove", "Icons.Unlock", () => repo.RemoveFromLaneOrder(branch)));

            root.Items.Add(new MenuItem() { Header = "-" });
            root.Items.Add(LaneOrderItem(owner, "LaneOrder.Edit", null, () =>
            {
                if (repo.CanCreatePopup())
                    repo.ShowPopup(new ViewModels.ConfigureLaneOrder(repo));
            }));

            menu.Items.Add(root);
        }

        private static MenuItem LaneOrderItem(Control owner, string key, string icon, System.Action apply)
        {
            var item = new MenuItem();
            item.Header = App.Text(key);
            if (icon != null)
                item.Icon = owner.CreateMenuIcon(icon);
            item.Click += (_, e) =>
            {
                apply();
                e.Handled = true;
            };

            return item;
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
