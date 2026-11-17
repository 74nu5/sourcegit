using Avalonia.Controls;

namespace SourceGit.Views
{
    /// <summary>
    ///     The "Branch Filters" submenu of the history's advanced options: how the filters
    ///     behave, the wildcard rules, and the saved views.
    /// </summary>
    public partial class Histories
    {
        private MenuItem BuildBranchFiltersMenu(ViewModels.Repository repo, ViewModels.Preferences pref)
        {
            var root = new MenuItem();
            root.Header = App.Text("HistoryRules.Menu");
            root.Icon = this.CreateMenuIcon("Icons.Filter");

            root.Items.Add(Toggle("HistoryRules.Combine", pref.CombineHistoryFilters, () =>
            {
                pref.CombineHistoryFilters = !pref.CombineHistoryFilters;
                repo.RefreshHistoryFiltersFromMenu();
            }));

            root.Items.Add(Toggle("HistoryRules.AlwaysShowCurrent", pref.AlwaysShowCurrentBranch, () =>
            {
                pref.AlwaysShowCurrentBranch = !pref.AlwaysShowCurrentBranch;
                repo.RefreshHistoryFiltersFromMenu();
            }));

            root.Items.Add(Toggle("HistoryRules.KeepDuringFocus", pref.KeepFiltersDuringFocus, () =>
            {
                pref.KeepFiltersDuringFocus = !pref.KeepFiltersDuringFocus;
            }));

            root.Items.Add(new MenuItem() { Header = "-" });

            var showMatching = new MenuItem();
            showMatching.Header = App.Text("HistoryRules.ShowMatching");
            showMatching.Icon = this.CreateMenuIcon("Icons.Filter");
            showMatching.Click += (_, e) =>
            {
                if (repo.CanCreatePopup())
                    repo.ShowPopup(new ViewModels.AddHistoryRule(repo, Models.FilterMode.Included));
                e.Handled = true;
            };
            root.Items.Add(showMatching);

            var hideMatching = new MenuItem();
            hideMatching.Header = App.Text("HistoryRules.HideMatching");
            hideMatching.Icon = this.CreateMenuIcon("Icons.EyeClose");
            hideMatching.Click += (_, e) =>
            {
                if (repo.CanCreatePopup())
                    repo.ShowPopup(new ViewModels.AddHistoryRule(repo, Models.FilterMode.Excluded));
                e.Handled = true;
            };
            root.Items.Add(hideMatching);

            root.Items.Add(new MenuItem() { Header = "-" });

            var views = repo.HistoryFilterViews;
            foreach (var view in views)
            {
                var dup = view;
                var apply = new MenuItem();
                apply.Header = dup.Name;
                apply.Icon = this.CreateMenuIcon("Icons.Eye");
                apply.Click += (_, e) =>
                {
                    repo.ApplyHistoryFilterView(dup);
                    e.Handled = true;
                };
                root.Items.Add(apply);
            }

            var save = new MenuItem();
            save.Header = App.Text("HistoryRules.SaveView");
            save.IsEnabled = repo.HasHistoryFilters;
            save.Click += (_, e) =>
            {
                if (repo.CanCreatePopup())
                    repo.ShowPopup(new ViewModels.SaveHistoryFilterView(repo));
                e.Handled = true;
            };
            root.Items.Add(save);

            if (views.Count > 0)
            {
                var delete = new MenuItem();
                delete.Header = App.Text("HistoryRules.DeleteView");
                foreach (var view in views)
                {
                    var dup = view;
                    var item = new MenuItem();
                    item.Header = dup.Name;
                    item.Click += (_, e) =>
                    {
                        repo.DeleteHistoryFilterView(dup);
                        e.Handled = true;
                    };
                    delete.Items.Add(item);
                }

                root.Items.Add(delete);
            }

            return root;
        }

        private MenuItem Toggle(string key, bool isChecked, System.Action toggle)
        {
            var item = new MenuItem();
            item.Header = App.Text(key);
            if (isChecked)
                item.Icon = this.CreateMenuIcon("Icons.Check");
            item.Click += (_, e) =>
            {
                toggle();
                e.Handled = true;
            };

            return item;
        }
    }
}
