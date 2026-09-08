using Avalonia.Controls;

namespace SourceGit.Views
{
    /// <summary>
    ///     "Show only this one" in the graph, and the way back.
    ///
    ///     Filtering the graph down to a few branches has always been possible: upstream ships
    ///     an include mode that means exactly "only these". What it never shipped is the
    ///     gesture. Reaching it takes hovering a branch row until a small icon appears, then
    ///     understanding that ticking it a second time on another branch adds to a set rather
    ///     than replacing it -- so isolating one branch means first clearing whatever is
    ///     already there. Hence upstream #1687, and twenty comments under it.
    ///
    ///     One entry, in the menus people actually open, doing the whole thing at once.
    /// </summary>
    public static class HistoryVisibility
    {
        public static void Append(ContextMenu menu, Control owner, ViewModels.Repository repo, Models.Branch branch)
        {
            if (menu == null || repo == null || branch == null)
                return;

            menu.Items.Add(new MenuItem() { Header = "-" });
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
