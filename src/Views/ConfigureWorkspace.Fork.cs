using Avalonia.Interactivity;

using SourceGit.Models;

namespace SourceGit.Views
{
    public partial class ConfigureWorkspace
    {
        /// <summary>
        ///     Copies a workspace, its list of repositories included.
        ///
        ///     The copy is never the active one, however the original stood: exactly one
        ///     workspace is open at a time, and a second claiming to be would leave the
        ///     launcher with two.
        /// </summary>
        private void OnDuplicateSelectedWorkspace(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            if (DataContext is not ViewModels.ConfigureWorkspace vm)
                return;

            var source = vm.Selected;
            if (source == null)
                return;

            var copy = source.Clone();
            copy.Name = Models.CopyName.Next(
                source.Name,
                Models.CopyName.TakenBy(vm.Workspaces, w => w.Name),
                "Unnamed Workspace");

            vm.Workspaces.Insert(vm.Workspaces.IndexOf(source) + 1, copy);
            vm.Selected = copy;
        }
    }
}
