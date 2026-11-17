using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    /// <summary>
    ///     The row of this fork's history rules -- wildcard patterns, "already in" -- under
    ///     upstream's filter bar, with the count of what the graph shows.
    /// </summary>
    public partial class HistoryRulesBar : UserControl
    {
        public HistoryRulesBar()
        {
            InitializeComponent();
        }

        private void OnRemoveHistoryRule(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: Models.HistoryRule rule } &&
                DataContext is ViewModels.Repository repo)
                repo.RemoveHistoryRule(rule);

            e.Handled = true;
        }
    }
}
