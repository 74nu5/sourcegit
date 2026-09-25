using Avalonia.Controls;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    public partial class StashesPage
    {
        /// <summary>
        ///     Adds this fork's entries to the menu of a stash: where it came from, and where to
        ///     see it in the graph.
        ///
        ///     Finding a stash again in a busy history is the slow part of using one, and git
        ///     records the answer -- the stash's first parent is the commit the work was set
        ///     aside on. Nothing exposed it before.
        ///
        ///     The repository is reached through the view rather than through the page's own
        ///     view-model, which keeps no public handle on it. This is the same walk
        ///     Histories.OnCommitListContextRequested does.
        /// </summary>
        private void AppendForkStashOptions(ContextMenu menu, Models.Stash stash)
        {
            if (menu == null || stash == null)
                return;

            if (this.FindAncestorOfType<Repository>() is not { DataContext: ViewModels.Repository repo })
                return;

            menu.Items.Add(new MenuItem { Header = "-" });
            StashContextMenu.AppendNavigation(menu, this, repo, stash, fromGraph: false);
        }
    }
}
