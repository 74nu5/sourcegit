using Avalonia.Controls;
using Avalonia.Input;

namespace SourceGit.Views
{
    /// <summary>
    ///     The row of worktrees under a repository's tab. It shows the group and hands clicks
    ///     to the launcher; what a switch means -- opening the worktree the first time,
    ///     refusing while a popup is up, keeping the workspace's memory right -- lives there.
    /// </summary>
    public partial class WorktreeTabStrip : UserControl
    {
        public WorktreeTabStrip()
        {
            InitializeComponent();
        }

        private void OnTabPressed(object sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;

            if (sender is Border { DataContext: ViewModels.WorktreeTab tab } &&
                DataContext is ViewModels.LauncherPage page &&
                !tab.IsActive)
                App.GetLauncher()?.SelectWorktree(page, tab.Path);

            e.Handled = true;
        }
    }
}
