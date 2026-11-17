using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class LauncherPage
    {
        /// <summary>
        ///     Runs what a notification offers -- "Undo", for the history filters -- and
        ///     dismisses it: done once, it has nothing left to offer.
        /// </summary>
        private void OnRunNotificationAction(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: Models.Notification notice } &&
                DataContext is ViewModels.LauncherPage page)
            {
                page.Notifications.Remove(notice);
                notice.Action?.Invoke();
            }

            e.Handled = true;
        }
    }
}
