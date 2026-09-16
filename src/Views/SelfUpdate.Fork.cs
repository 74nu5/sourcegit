using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class SelfUpdate
    {
        /// <summary>
        ///     Turns the offer into the update itself.
        ///
        ///     The window keeps one object in <c>Data</c> and picks its template from that
        ///     object's type, so handing it the install view-model is the whole transition --
        ///     no second window, and no state in this one to get out of step with what is
        ///     actually happening.
        /// </summary>
        private void StartSelfUpdate(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SelfUpdate vm && vm.Data is Models.Version release)
                vm.Data = new ViewModels.SelfUpdateInstall(release);

            e.Handled = true;
        }
    }
}
