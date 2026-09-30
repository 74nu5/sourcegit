using Avalonia.Controls;

namespace SourceGit.Views
{
    public partial class LauncherTabBar
    {
        /// <summary>
        ///     What this fork adds to a tab's menu: gathering a repository's worktrees under its
        ///     tab. Here rather than in the preferences window because it is a decision about
        ///     tabs, taken while looking at them -- and it applies at once, to the tabs already
        ///     open, which is the moment it shows what it does.
        /// </summary>
        private void AppendForkTabOptions(ContextMenu menu, ViewModels.Launcher vm)
        {
            var pref = ViewModels.Preferences.Instance;

            var group = new MenuItem();
            group.Header = App.Text("PageTabBar.Tab.GroupWorktrees");
            if (pref.GroupWorktreesInTabs)
                group.Icon = this.CreateMenuIcon("Icons.Check");
            group.Click += (_, ev) =>
            {
                pref.GroupWorktreesInTabs = !pref.GroupWorktreesInTabs;
                vm.ApplyWorktreeGrouping();
                ev.Handled = true;
            };

            menu.Items.Add(new MenuItem { Header = "-" });
            menu.Items.Add(group);
        }
    }
}
