using Avalonia.Interactivity;

using SourceGit.Models;

namespace SourceGit.Views
{
    /// <summary>
    ///     What this fork adds to the preferences window.
    ///
    ///     Preferences.axaml.cs is one of upstream's busiest files; a handler declared here
    ///     is wired from XAML just the same, and never has to be re-merged.
    /// </summary>
    public partial class Preferences
    {
        /// <summary>
        ///     Copies the selected service and selects the copy, so the one field that
        ///     usually differs -- the model -- can be changed straight away.
        /// </summary>
        private void OnDuplicateSelectedOpenAIService(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            var source = SelectedOpenAIService;
            if (source == null)
                return;

            var services = ViewModels.Preferences.Instance.OpenAIServices;

            var copy = source.Clone();
            copy.Name = Models.CopyName.Next(
                source.Name,
                Models.CopyName.TakenBy(services, s => s.Name),
                "Unnamed Service");

            // Next to what it came from rather than at the end, which is where the eye
            // already is and keeps a long list in an order that means something.
            services.Insert(services.IndexOf(source) + 1, copy);
            SelectedOpenAIService = copy;
        }

        /// <summary>
        ///     The global custom actions, copied the same way. An action differing from
        ///     another by one argument is the ordinary case, and its parameters come with it.
        /// </summary>
        private void OnDuplicateSelectedCustomAction(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            var source = SelectedCustomAction;
            if (source == null)
                return;

            var actions = ViewModels.Preferences.Instance.CustomActions;

            var copy = source.Clone();
            copy.Name = Models.CopyName.Next(
                source.Name,
                Models.CopyName.TakenBy(actions, a => a.Name),
                "Unnamed Action");

            actions.Insert(actions.IndexOf(source) + 1, copy);
            SelectedCustomAction = copy;
        }
    }
}
