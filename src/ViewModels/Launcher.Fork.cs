using System;
using System.Collections.Generic;
using System.IO;

namespace SourceGit.ViewModels
{
    public partial class Launcher
    {
        /// <summary>
        ///     The tab to bring back as active after a workspace's repositories were reopened,
        ///     or null when it did not come back.
        ///
        ///     The workspace remembers the active tab as a rank in its list of repositories,
        ///     and the restore used to read that rank straight into Pages. The two only line
        ///     up while every repository opens. One that no longer does -- its folder moved or
        ///     deleted -- is skipped, every tab after it slides one place to the left, and the
        ///     rank then names the tab that followed the one you left. So the rank is turned
        ///     back into the path it stood for, and the tab is found by that path instead.
        ///
        ///     A tab holding worktrees is named after its repository but remembered by the
        ///     worktree it was showing, so the path is also looked for among what tabs show.
        ///
        ///     Takes the list and the paths rather than reaching for them, so the choice can
        ///     be checked without opening a repository.
        /// </summary>
        public static LauncherPage FindRestoredPage(IList<LauncherPage> pages, string[] repos, int activeIdx)
        {
            if (pages == null || repos == null || activeIdx < 0 || activeIdx >= repos.Length)
                return null;

            // The same normalisation the path went through before it was stored, and the one
            // a tab's node id gets when it is opened.
            var wanted = repos[activeIdx].Replace('\\', '/').TrimEnd('/');

            foreach (var page in pages)
            {
                if (page.Node is { IsRepository: true } node && node.Id.Equals(wanted, StringComparison.Ordinal))
                    return page;

                if (page.Data is Repository shown && shown.FullPath.Equals(wanted, StringComparison.Ordinal))
                    return page;
            }

            // The active repository was the one that failed to open. Nothing is a better
            // guess than any other, so the caller falls back on the first tab.
            return null;
        }

        /// <summary>
        ///     Shows a worktree in its repository's tab. False when it cannot be done now: the
        ///     tab holds no repository, the worktree will not open, or a popup is up -- a popup
        ///     belongs to the worktree that raised it, and switching under it would leave it
        ///     acting on a repository no longer on screen.
        /// </summary>
        public bool SelectWorktree(LauncherPage page, string path)
        {
            var group = page?.EnsureWorktreeGroup();
            if (group == null)
                return false;

            if (page.Popup != null)
            {
                (page.Data as Repository)?.SendNotification(App.Text("WorktreeTabs.Busy"), true);
                return false;
            }

            var tab = group.Find(path) ?? group.Add(path);
            if (tab.Repository == null)
            {
                tab.Repository = OpenWorktreeRepository(tab.Path);
                if (tab.Repository == null)
                    return false;
            }

            if (!ReferenceEquals(page.Data, tab.Repository))
                page.Data = tab.Repository;

            RebuildWorkspaceRepositories();
            if (page == _activePage)
                PostActivePageChanged();

            // The tab's dot still says what the previous worktree reported. Asking the one now
            // on screen to look again is what brings it up to date.
            tab.Repository.MarkWorkingCopyDirtyManually();
            tab.Repository.MarkBranchesDirtyManually();
            return true;
        }

        /// <summary>
        ///     Applies the option to the tabs already open, as soon as it changes: grouped, every
        ///     worktree moves in under its repository's tab; ungrouped, every worktree that was
        ///     open gets its own tab back.
        /// </summary>
        public void ApplyWorktreeGrouping()
        {
            if (Preferences.Instance.GroupWorktreesInTabs)
                GroupOpenWorktrees();
            else
                UngroupWorktrees();

            RebuildWorkspaceRepositories();
            PostActivePageChanged();
        }

        /// <summary>
        ///     Routes the opening of a repository through its worktree group. Called first thing
        ///     in OpenRepositoryInTab; true when it dealt with the request.
        ///
        ///     A worktree whose repository already has a tab becomes one of its sub-tabs instead
        ///     of a new tab at the end of the bar. A worktree whose repository is not open opens
        ///     that repository's tab, with the worktree on screen. And asking for a repository
        ///     whose tab is showing one of its worktrees brings the main back, rather than only
        ///     bringing the tab forward.
        /// </summary>
        private bool TryOpenInWorktreeGroup(RepositoryNode node, LauncherPage page)
        {
            if (page != null || node == null || !Preferences.Instance.GroupWorktreesInTabs)
                return false;

            var path = Models.WorktreeLayout.Normalize(node.Id);
            if (!Directory.Exists(path))
                return false;

            // The main of a group that is showing something else.
            foreach (var one in Pages)
            {
                if (!Models.WorktreeLayout.SamePath(one.Node.Id, path))
                    continue;

                if (one.EnsureWorktreeGroup() is { } group && !(one.Data is Repository shown && Models.WorktreeLayout.SamePath(shown.FullPath, path)))
                {
                    SelectWorktree(one, group.MainPath);
                    ActivePage = one;
                    return true;
                }

                return false;
            }

            if (new Commands.IsBareRepository(path).GetResult())
                return false;

            var gitDir = GetRepositoryGitDir(path);
            if (string.IsNullOrEmpty(gitDir))
                return false;

            // A main worktree, or one that cannot be placed: upstream opens it, and it forms its
            // own group once it has worktrees to show.
            var main = Models.WorktreeLayout.MainPathOf(gitDir);
            if (main == null)
                return false;

            foreach (var one in Pages)
            {
                if (!Models.WorktreeLayout.SamePath(one.Node.Id, main))
                    continue;

                // Brought forward even when the switch is refused, so the reason why is on screen.
                SelectWorktree(one, path);
                ActivePage = one;
                return true;
            }

            var repo = OpenWorktreeRepository(path, gitDir);
            if (repo == null)
                return false;

            var target = PlaceGroupPage(NodeForMain(main, gitDir), repo);
            target.EnsureWorktreeGroup();

            RebuildWorkspaceRepositories();
            if (_activePage == target)
                PostActivePageChanged();
            else
                ActivePage = target;

            return true;
        }

        /// <summary>
        ///     Closes the worktrees a tab opened besides the one on screen, which upstream closes
        ///     itself. Called first thing in CloseRepositoryInTab: without it, every worktree
        ///     visited in a closed tab would keep its watcher and its auto-fetch timer running.
        /// </summary>
        private void CloseWorktreeMembers(LauncherPage page)
        {
            var group = page?.PeekWorktreeGroup();
            if (group == null)
                return;

            foreach (var tab in group.Tabs)
            {
                if (tab.Repository == null || ReferenceEquals(tab.Repository, page.Data))
                    continue;

                tab.Repository.Close();
                tab.Repository = null;
            }

            // Upstream is about to save the tab's node into the git directory of what the tab
            // shows. For a grouped tab that is the repository's node and a worktree's folder:
            // the worktree would come back later under its repository's name. The repository's
            // node goes where it belongs, and the tab takes the worktree's own for that save.
            if (page.Node.IsUnmanaged && page.Data is Repository shown && !Models.WorktreeLayout.SamePath(shown.FullPath, group.MainPath))
            {
                page.Node.SaveMinimalInfo(Models.WorktreeLayout.CommonDirOf(shown.GitDir));
                page.Node = UnmanagedNode(shown.FullPath, shown.GitDir);
            }
        }

        private void GroupOpenWorktrees()
        {
            // Which tabs share a repository, keyed by the repository's main path.
            var byMain = new Dictionary<string, List<LauncherPage>>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            foreach (var page in new List<LauncherPage>(Pages))
            {
                if (page.Data is not Repository repo)
                    continue;

                var main = Models.WorktreeLayout.Normalize(repo.MainWorktreePath);
                if (!byMain.TryGetValue(main, out var list))
                {
                    list = [];
                    byMain.Add(main, list);
                    order.Add(main);
                }

                list.Add(page);
            }

            foreach (var main in order)
            {
                var pages = byMain[main];

                // The tab that already shows the main keeps its place; otherwise the first one.
                var anchor = pages.Find(p => p.Data is Repository r && Models.WorktreeLayout.SamePath(r.FullPath, main)) ?? pages[0];
                var shown = anchor.Data as Repository;

                if (!Models.WorktreeLayout.SamePath(anchor.Node.Id, main))
                    anchor.Node = NodeForMain(main, shown?.GitDir);

                var group = anchor.EnsureWorktreeGroup();
                if (group == null)
                    continue;

                var keepOnScreen = pages.Contains(_activePage) ? _activePage.Data as Repository : null;

                foreach (var page in pages)
                {
                    // A tab with a popup up stays where it is: its operation is running against
                    // the repository it shows. It can still anchor a group, just not be merged.
                    if (page == anchor || page.Popup != null || page.Data is not Repository repo)
                        continue;

                    // Moved, not reopened: the worktree keeps its watcher and its view state.
                    var tab = group.Add(repo.FullPath);
                    tab.Repository = repo;
                    page.Data = null;
                    Pages.Remove(page);
                }

                if (keepOnScreen != null)
                {
                    if (!ReferenceEquals(anchor.Data, keepOnScreen))
                        anchor.Data = keepOnScreen;
                    ActivePage = anchor;
                }
            }
        }

        private void UngroupWorktrees()
        {
            foreach (var page in new List<LauncherPage>(Pages))
            {
                var group = page.PeekWorktreeGroup();
                if (group == null)
                    continue;

                var at = Pages.IndexOf(page);
                foreach (var tab in group.Tabs)
                {
                    if (tab.Repository == null || ReferenceEquals(tab.Repository, page.Data))
                        continue;

                    var node = Preferences.Instance.FindNode(tab.Path) ?? UnmanagedNode(tab.Path, tab.Repository.GitDir);
                    Pages.Insert(++at, new LauncherPage(node, tab.Repository));
                }

                // The tab keeps what it shows, under that worktree's own name.
                if (page.Data is Repository shown && !Models.WorktreeLayout.SamePath(shown.FullPath, page.Node.Id))
                    page.Node = Preferences.Instance.FindNode(shown.FullPath) ?? UnmanagedNode(shown.FullPath, shown.GitDir);

                page.DropWorktreeGroup();
            }
        }

        /// <summary>
        ///     Where a new grouped tab goes: into the welcome tab on screen if there is one, the
        ///     way upstream reuses it, otherwise at the end.
        /// </summary>
        private LauncherPage PlaceGroupPage(RepositoryNode node, Repository repo)
        {
            if (_activePage == null || _activePage.Node.IsRepository)
            {
                var page = new LauncherPage(node, repo);
                Pages.Add(page);
                return page;
            }

            _activePage.Node = node;
            _activePage.Data = repo;
            return _activePage;
        }

        private Repository OpenWorktreeRepository(string path, string gitDir = null)
        {
            if (!Directory.Exists(path))
                return null;

            var isBare = new Commands.IsBareRepository(path).GetResult();
            gitDir ??= isBare ? path : GetRepositoryGitDir(path);
            if (string.IsNullOrEmpty(gitDir))
                return null;

            var repo = new Repository(isBare, path, gitDir);
            repo.Open();
            return repo;
        }

        /// <summary>
        ///     The node a group's tab is named after: the repository's, from the list of known
        ///     repositories if it is there, otherwise one kept beside the repository the way
        ///     upstream keeps an unmanaged one.
        /// </summary>
        private static RepositoryNode NodeForMain(string main, string anyGitDir)
        {
            var known = Preferences.Instance.FindNode(main);
            if (known != null)
                return known;

            var commonDir = string.IsNullOrEmpty(anyGitDir) ? Path.Combine(main, ".git") : Models.WorktreeLayout.CommonDirOf(anyGitDir);
            return UnmanagedNode(main, commonDir);
        }

        private static RepositoryNode UnmanagedNode(string path, string gitDir)
        {
            var normalized = Models.WorktreeLayout.Normalize(path);
            var node = new RepositoryNode
            {
                Id = normalized,
                Name = Path.GetFileName(normalized),
                Bookmark = 0,
                IsRepository = true,
                IsUnmanaged = true,
            };

            if (!string.IsNullOrEmpty(gitDir))
                node.LoadMinimalInfo(gitDir);

            return node;
        }

        /// <summary>
        ///     The same rebuild upstream runs after opening a repository: the workspace
        ///     remembers what each tab shows, in tab order.
        /// </summary>
        private void RebuildWorkspaceRepositories()
        {
            _activeWorkspace.Repositories.Clear();
            foreach (var p in Pages)
            {
                if (p.Data is Repository r)
                    _activeWorkspace.Repositories.Add(r.FullPath);
            }
        }
    }
}
