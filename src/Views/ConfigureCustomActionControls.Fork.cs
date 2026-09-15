using Avalonia.Interactivity;

using SourceGit.Models;

namespace SourceGit.Views
{
    public partial class ConfigureCustomActionControls
    {
        /// <summary>
        ///     Copies one parameter of a custom action. These come in families -- three paths,
        ///     four flags -- differing by a label and a default.
        /// </summary>
        private void OnDuplicateSelectedControl(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            if (DataContext is not ViewModels.ConfigureCustomActionControls vm)
                return;

            var source = vm.Edit;
            if (source == null)
                return;

            var copy = source.Clone();
            copy.Label = Models.CopyName.Next(
                source.Label,
                Models.CopyName.TakenBy(vm.Controls, c => c.Label),
                "Unnamed");

            vm.Controls.Insert(vm.Controls.IndexOf(source) + 1, copy);
            vm.Edit = copy;
        }
    }
}
