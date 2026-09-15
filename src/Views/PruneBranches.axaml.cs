using Avalonia.Interactivity;

namespace SourceGit.Views
{
    /// <summary>
    ///     A window rather than a popup, and for one reason: every popup in the application
    ///     is 560 pixels wide, fixed, in a file this fork tries not to touch. A list of
    ///     sixty-one branches named "features/feat-9285-ajout-skills-copilot-git-et-mcp"
    ///     needs more than that, and needs the person reading it to decide how much.
    /// </summary>
    public partial class PruneBranches : ChromelessWindow
    {
        public PruneBranches()
        {
            CloseOnESC = true;
            InitializeComponent();
        }

        private async void OnDelete(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            if (DataContext is not ViewModels.PruneBranches vm)
                return;

            await vm.DeleteSelectedAsync();
            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            Close();
        }
    }
}
