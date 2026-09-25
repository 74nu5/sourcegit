using System;

using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace SourceGit.Views
{
    /// <summary>
    ///     The menu a stash deserves, wherever it is shown.
    ///
    ///     A stash drawn in the graph used to inherit the commit menu, which offers to reset to
    ///     it, rebase onto it, cherry-pick it and start an interactive rebase from it. None of
    ///     those mean anything on a stash, and some of them are traps.
    ///
    ///     The MenuItem plumbing below is deliberately a second copy of what
    ///     StashesPage.axaml.cs builds. Rewriting that handler to call this one would delete
    ///     seventy lines from an upstream file, and deletions are what conflict when upstream
    ///     moves -- the one thing DEVELOPMENT.md asks us not to do. What is not copied is the
    ///     work: every entry calls the same public method on ViewModels.StashesPage that
    ///     upstream's own menu calls. The cost of this choice is that a stash action added
    ///     upstream will not appear in the graph until someone adds it here too.
    /// </summary>
    internal static class StashContextMenu
    {
        /// <summary>
        ///     The full menu for a stash row in the graph.
        /// </summary>
        internal static ContextMenu BuildForGraph(Control host, ViewModels.Repository repo, Models.Stash stash)
        {
            var menu = new ContextMenu();
            AppendActions(menu, host, repo, stash);

            menu.Items.Add(new MenuItem { Header = "-" });
            AppendNavigation(menu, host, repo, stash, fromGraph: true);

            return menu;
        }

        /// <summary>
        ///     Apply, branch, drop, patch, copy -- mirrored from the stashes page.
        /// </summary>
        internal static void AppendActions(ContextMenu menu, Control host, ViewModels.Repository repo, Models.Stash stash)
        {
            var page = repo.StashesPage;
            if (page == null)
                return;

            var apply = new MenuItem();
            apply.Header = App.Text("StashCM.Apply");
            apply.Icon = host.CreateMenuIcon("Icons.CheckCircled");
            apply.Click += (_, ev) =>
            {
                page.Apply(stash);
                ev.Handled = true;
            };

            var branch = new MenuItem();
            branch.Header = App.Text("StashCM.Branch");
            branch.Icon = host.CreateMenuIcon("Icons.Branch.Add");
            branch.Click += (_, ev) =>
            {
                page.CheckoutBranch(stash);
                ev.Handled = true;
            };

            var drop = new MenuItem();
            drop.Header = App.Text("StashCM.Drop");
            drop.Icon = host.CreateMenuIcon("Icons.Clear");
            drop.Click += (_, ev) =>
            {
                page.Drop(stash);
                ev.Handled = true;
            };

            var patch = new MenuItem();
            patch.Header = App.Text("StashCM.SaveAsPatch");
            patch.Icon = host.CreateMenuIcon("Icons.Save");
            patch.Click += async (_, ev) =>
            {
                var storageProvider = TopLevel.GetTopLevel(host)?.StorageProvider;
                if (storageProvider == null)
                    return;

                var options = new FilePickerSaveOptions();
                options.Title = App.Text("StashCM.SaveAsPatch");
                options.DefaultExtension = ".patch";
                options.FileTypeChoices = [new FilePickerFileType("Patch File") { Patterns = ["*.patch"] }];

                try
                {
                    var storageFile = await storageProvider.SaveFilePickerAsync(options);
                    if (storageFile != null)
                        await page.SaveStashAsPatchAsync(stash, storageFile.Path.LocalPath);
                }
                catch (Exception exception)
                {
                    Models.Notification.Send(null, $"Failed to save as patch: {exception.Message}", true);
                }

                ev.Handled = true;
            };

            var copy = new MenuItem();
            copy.Header = App.Text("StashCM.CopyMessage");
            copy.Icon = host.CreateMenuIcon("Icons.Copy");
            copy.Click += async (_, ev) =>
            {
                await host.CopyTextAsync(stash.Message);
                ev.Handled = true;
            };

            menu.Items.Add(apply);
            menu.Items.Add(branch);
            menu.Items.Add(drop);
            menu.Items.Add(new MenuItem { Header = "-" });
            menu.Items.Add(patch);
            menu.Items.Add(new MenuItem { Header = "-" });
            menu.Items.Add(copy);
        }

        /// <summary>
        ///     Where to go from a stash. The two sides offer opposite jumps: from the graph, the
        ///     page that lists it; from the page, the graph row -- and from either, the commit
        ///     the stash was taken from, which is the one that says when the work was set aside.
        /// </summary>
        internal static void AppendNavigation(ContextMenu menu, Control host, ViewModels.Repository repo, Models.Stash stash, bool fromGraph)
        {
            var origin = new MenuItem();
            origin.Header = App.Text("StashCM.GotoOrigin");
            origin.Icon = host.CreateMenuIcon("Icons.GotoParent");
            origin.IsEnabled = repo.CanNavigateToStashOrigin(stash);
            if (!origin.IsEnabled)
                ToolTip.SetTip(origin, App.Text("StashCM.GotoOrigin.Missing"));
            origin.Click += (_, ev) =>
            {
                repo.NavigateToStashOrigin(stash);
                ev.Handled = true;
            };
            menu.Items.Add(origin);

            if (fromGraph)
            {
                var reveal = new MenuItem();
                reveal.Header = App.Text("StashCM.RevealInPage");
                reveal.Icon = host.CreateMenuIcon("Icons.Stashes");
                reveal.Click += (_, ev) =>
                {
                    repo.RevealStashInPage(stash);
                    ev.Handled = true;
                };
                menu.Items.Add(reveal);
                return;
            }

            var row = new MenuItem();
            row.Header = App.Text("StashCM.GotoRow");
            row.Icon = host.CreateMenuIcon("Icons.Target");
            row.IsEnabled = repo.CanNavigateToStashRow(stash);
            if (!row.IsEnabled)
                ToolTip.SetTip(row, App.Text("StashCM.GotoRow.Disabled"));
            row.Click += (_, ev) =>
            {
                repo.NavigateToStashRow(stash);
                ev.Handled = true;
            };
            menu.Items.Add(row);
        }
    }
}
