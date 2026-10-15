using Avalonia.Controls;

namespace SourceGit.Views
{
    public partial class WorkingCopy
    {
        /// <summary>
        ///     Adds "Exclude Locally" to the menu of the unstaged changes: hide these files from
        ///     the local changes, in this clone only, without touching a .gitignore.
        ///
        ///     Upstream's "Add to .gitignore" can already write to info/exclude, but behind a
        ///     menu that names .gitignore, a dialog that defaults to it, and for one untracked
        ///     file at a time. This entry goes straight to info/exclude, takes any selection, and
        ///     also handles tracked files -- which no ignore rule can hide -- through the
        ///     skip-worktree bit.
        ///
        ///     When the selection is untracked files in a single folder, or an untracked folder,
        ///     it offers the folder as well: generated files tend to arrive in batches.
        /// </summary>
        private void AppendLocalExcludeOptions(ContextMenu menu, ViewModels.Repository repo, ViewModels.ChangeSelection selection)
        {
            if (menu == null || repo == null || selection is not { Count: > 0 })
                return;

            var changes = selection.Changes;
            var plan = Models.LocalExcludePlan.Build(changes);

            var item = new MenuItem();
            item.Header = App.Text("LocalExclude");
            item.Icon = this.CreateMenuIcon("Icons.EyeClose");

            menu.Items.Add(new MenuItem { Header = "-" });
            menu.Items.Add(item);

            if (plan.Refusal != null)
            {
                item.IsEnabled = false;
                ToolTip.SetShowOnDisabled(item, true);
                ToolTip.SetTip(item, App.Text(plan.Refusal == "staged" ? "LocalExclude.Staged" : "LocalExclude.Conflicted"));
                return;
            }

            var folder = Models.LocalExclude.CommonUntrackedFolder(changes);
            if (folder != null && selection.IsSingleFolder)
                folder = selection.SingleFolderPath;

            if (folder == null)
            {
                item.Click += async (_, e) =>
                {
                    e.Handled = true;
                    await repo.ExcludeLocallyAsync(plan);
                };
                return;
            }

            var files = new MenuItem();
            files.Header = App.Text("LocalExclude.Files", changes.Count);
            files.Click += async (_, e) =>
            {
                e.Handled = true;
                await repo.ExcludeLocallyAsync(plan);
            };

            var whole = new MenuItem();
            whole.Header = App.Text("LocalExclude.Folder", $"{folder}/");
            whole.Click += async (_, e) =>
            {
                e.Handled = true;
                await repo.ExcludeLocallyAsync(Models.LocalExcludePlan.Build(changes, folder));
            };

            item.Items.Add(files);
            item.Items.Add(whole);
        }
    }
}
