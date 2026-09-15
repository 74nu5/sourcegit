using Avalonia.Interactivity;

using SourceGit.Models;

namespace SourceGit.Views
{
    /// <summary>
    ///     What this fork adds to the repository configuration window.
    ///
    ///     Three lists here are managed with a plus and a minus, and all three are edited by
    ///     starting from one that already works. Copying the entry is the shortest way to say
    ///     "the same, but for this other thing".
    /// </summary>
    public partial class RepositoryConfigure
    {
        private void OnDuplicateSelectedCommitTemplate(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            if (DataContext is not ViewModels.RepositoryConfigure vm)
                return;

            var source = vm.SelectedCommitTemplate;
            if (source == null)
                return;

            var copy = source.Clone();
            copy.Name = Models.CopyName.Next(
                source.Name,
                Models.CopyName.TakenBy(vm.CommitTemplates, t => t.Name),
                "Unnamed Template");

            vm.CommitTemplates.Insert(vm.CommitTemplates.IndexOf(source) + 1, copy);
            vm.SelectedCommitTemplate = copy;
        }

        private void OnDuplicateSelectedIssueTracker(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            if (DataContext is not ViewModels.RepositoryConfigure vm)
                return;

            var source = vm.SelectedIssueTracker;
            if (source == null)
                return;

            var copy = source.Clone();
            copy.Name = Models.CopyName.Next(
                source.Name,
                Models.CopyName.TakenBy(vm.IssueTrackers, t => t.Name),
                "Unnamed Rule");

            vm.IssueTrackers.Insert(vm.IssueTrackers.IndexOf(source) + 1, copy);
            vm.SelectedIssueTracker = copy;
        }

        private void OnDuplicateSelectedCustomAction(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            if (DataContext is not ViewModels.RepositoryConfigure vm)
                return;

            var source = vm.SelectedCustomAction;
            if (source == null)
                return;

            var copy = source.Clone();
            copy.Name = Models.CopyName.Next(
                source.Name,
                Models.CopyName.TakenBy(vm.CustomActions, a => a.Name),
                "Unnamed Action");

            vm.CustomActions.Insert(vm.CustomActions.IndexOf(source) + 1, copy);
            vm.SelectedCustomAction = copy;
        }
    }
}
