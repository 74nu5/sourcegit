using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace SourceGit.ViewModels
{
    /// <summary>
    ///     A repository's tab that also holds its worktrees, shown as a row underneath.
    ///
    ///     The tab keeps the repository's identity -- its node, its name, its bookmark -- and
    ///     its Data points at whichever worktree is on screen. Everything upstream reads off
    ///     Data, the toolbar, the view, the shortcuts, the palette, then follows the worktree
    ///     without being touched. What does not follow on its own is the way a repository
    ///     finds its tab, which is why IsOwnerOf and ReceivesNotificationsFor exist.
    /// </summary>
    public partial class LauncherPage
    {
        /// <summary>
        ///     The row of worktrees, or null when the option is off or this tab holds no
        ///     repository. Built on first need: pages are created in several places upstream,
        ///     some of which set their repository without raising a change.
        /// </summary>
        public WorktreeGroup WorktreeGroup => EnsureWorktreeGroup();

        /// <summary>
        ///     Only when there is something to choose between. A repository with no linked
        ///     worktree gets no row at all, and the tab looks exactly as upstream draws it.
        /// </summary>
        public bool ShowsWorktreeRow => EnsureWorktreeGroup() is { Tabs.Count: > 1 };

        public WorktreeGroup EnsureWorktreeGroup()
        {
            if (_worktreeGroup != null || !Preferences.Instance.GroupWorktreesInTabs)
                return _worktreeGroup;

            if (_data is not Repository repo)
                return null;

            var group = new WorktreeGroup(repo.MainWorktreePath);
            var tab = group.Find(repo.FullPath) ?? group.Add(repo.FullPath);
            tab.Repository = repo;
            group.MarkActive(tab);

            _worktreeGroup = group;
            Watch(repo);

            // A list that is already there is a real one; an empty list might only mean it
            // has not been read yet, so the first rebuild waits for the repository to report.
            if (repo.Worktrees is { Count: > 0 })
                ApplyWorktrees(repo);

            // Announced here too: a group formed because the option was just switched on has
            // nothing else to tell the row to appear.
            OnPropertyChanged(nameof(WorktreeGroup));
            OnPropertyChanged(nameof(ShowsWorktreeRow));
            return _worktreeGroup;
        }

        /// <summary>
        ///     The group as it stands, without creating one.
        /// </summary>
        public WorktreeGroup PeekWorktreeGroup() => _worktreeGroup;

        public void DropWorktreeGroup()
        {
            Watch(null);
            _worktreeGroup = null;
            OnPropertyChanged(nameof(WorktreeGroup));
            OnPropertyChanged(nameof(ShowsWorktreeRow));
        }

        /// <summary>
        ///     Whether this is the tab a repository should show its popups in and report its
        ///     state to. Replaces the comparison in Repository.GetOwnerPage.
        ///
        ///     Upstream compares the tab's node with the repository's path. A worktree held in
        ///     its main repository's tab would never match -- and CanCreatePopup, which every
        ///     action goes through, would then answer no: fetch, pull, push and commit would
        ///     silently do nothing. So a grouped tab answers for the worktree on screen, and
        ///     only for it. The others are not orphans: they share their remotes with the one on
        ///     screen, so one auto-fetch serves them all, and their popups would otherwise open
        ///     over a worktree they do not belong to.
        ///
        ///     Without a group, exactly upstream's comparison.
        /// </summary>
        public bool IsOwnerOf(Repository repo)
        {
            if (repo == null)
                return false;

            if (_worktreeGroup == null)
                return _node.Id.Equals(repo.FullPath);

            return ReferenceEquals(_data, repo);
        }

        /// <summary>
        ///     Whether a notification about this path belongs in this tab. Replaces the
        ///     comparison in Launcher.DispatchNotification, and falls back on it: notifications
        ///     carry the path of the repository that raised them, which for a worktree is not
        ///     the path the tab is named after.
        /// </summary>
        public bool ReceivesNotificationsFor(string group)
        {
            var id = _node.Id.Replace('\\', '/').TrimEnd('/');
            if (id.Equals(group, StringComparison.OrdinalIgnoreCase))
                return true;

            if (_worktreeGroup == null)
                return false;

            foreach (var tab in _worktreeGroup.Tabs)
            {
                if (tab.Repository != null && Models.WorktreeLayout.SamePath(tab.Path, group))
                    return true;
            }

            return false;
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);

            if (e.PropertyName != nameof(Data) || _worktreeGroup == null)
                return;

            // Switching worktree: the group follows. Anything else -- the tab closed, reused
            // for another repository -- ends the group; it is rebuilt on next need.
            var tab = _worktreeGroup.FindOpened(_data as Repository);
            if (tab != null)
            {
                _worktreeGroup.MarkActive(tab);
                Watch(tab.Repository);
                return;
            }

            DropWorktreeGroup();
        }

        /// <summary>
        ///     Listens to the worktree on screen only. Every worktree of a repository lists the
        ///     same worktrees, so one list is enough, and the one on screen is the one whose
        ///     watcher is sure to be running.
        /// </summary>
        private void Watch(Repository repo)
        {
            if (ReferenceEquals(_watched, repo))
                return;

            if (_watched != null)
                _watched.PropertyChanged -= OnWatchedRepositoryChanged;

            _watched = repo;

            if (_watched != null)
                _watched.PropertyChanged += OnWatchedRepositoryChanged;
        }

        private void OnWatchedRepositoryChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Repository.Worktrees) && sender is Repository repo)
                ApplyWorktrees(repo);
        }

        /// <summary>
        ///     Rebuilds the row from the repository's list, then deals with what vanished: a
        ///     removed worktree's tab is closed, and if it was the one on screen the main takes
        ///     its place before it is closed, so the view is never left on a closed repository.
        /// </summary>
        private void ApplyWorktrees(Repository repo)
        {
            var group = _worktreeGroup;
            if (group == null)
                return;

            var listed = new List<Models.Worktree>();
            foreach (var wt in repo.Worktrees)
                listed.Add(wt.Backend);

            var gone = group.Rebuild(listed);

            if (gone.Exists(t => t.Repository != null && ReferenceEquals(t.Repository, _data)))
                App.GetLauncher()?.SelectWorktree(this, group.MainPath);

            foreach (var tab in gone)
            {
                if (tab.Repository == null || ReferenceEquals(tab.Repository, _data))
                    continue;

                tab.Repository.Close();
                tab.Repository = null;
            }

            OnPropertyChanged(nameof(ShowsWorktreeRow));
        }

        private WorktreeGroup _worktreeGroup = null;
        private Repository _watched = null;
    }
}
