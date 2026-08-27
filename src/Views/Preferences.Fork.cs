using Avalonia.Interactivity;

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

            var names = new System.Collections.Generic.List<string>();
            foreach (var service in services)
                names.Add(service.Name);

            var copy = source.Clone();
            copy.Name = AI.Service.NextCopyName(source.Name, names);

            // Next to what it came from rather than at the end, which is where the eye
            // already is and keeps a long list in an order that means something.
            services.Insert(services.IndexOf(source) + 1, copy);
            SelectedOpenAIService = copy;
        }
    }
}
