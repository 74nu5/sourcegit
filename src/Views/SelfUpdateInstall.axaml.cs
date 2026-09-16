using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    /// <summary>
    ///     The second half of the update window: what happens after the user says yes.
    ///
    ///     It starts working the moment it appears rather than waiting for another click --
    ///     the click that brought it here was the decision, and asking twice for the same
    ///     thing reads as an application that did not believe the first answer.
    /// </summary>
    public partial class SelfUpdateInstall : UserControl
    {
        public SelfUpdateInstall()
        {
            InitializeComponent();
        }

        protected override async void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            if (DataContext is ViewModels.SelfUpdateInstall vm && !_started)
            {
                _started = true;
                await vm.RunAsync();
            }
        }

        private void RestartNow(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SelfUpdateInstall vm)
                vm.Restart();

            e.Handled = true;
        }

        private void RevealPackage(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SelfUpdateInstall vm)
                vm.RevealPackage();

            e.Handled = true;
        }

        private void OpenReleasePage(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SelfUpdateInstall vm)
                vm.OpenReleasePage();

            e.Handled = true;
        }

        private void CloseWindow(object _, RoutedEventArgs e)
        {
            this.FindAncestorOfType<Window>()?.Close();
            e.Handled = true;
        }

        private bool _started = false;
    }
}
