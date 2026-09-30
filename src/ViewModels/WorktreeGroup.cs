using System;
using System.Collections.Generic;

using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    /// <summary>
    ///     One worktree in the row under its repository's tab.
    /// </summary>
    public class WorktreeTab : ObservableObject
    {
        public WorktreeTab(string path, bool isMain)
        {
            Path = Models.WorktreeLayout.Normalize(path);
            IsMain = isMain;
            _label = System.IO.Path.GetFileName(Path);
        }

        public string Path { get; }

        public bool IsMain { get; }

        public string Label
        {
            get => _label;
            set => SetProperty(ref _label, value);
        }

        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }

        /// <summary>
        ///     Null until the tab is first visited. A worktree nobody opens costs nothing: no
        ///     watcher, no auto-fetch timer.
        /// </summary>
        public Repository Repository { get; set; }

        private string _label;
        private bool _isActive;
    }

    /// <summary>
    ///     The worktrees of one repository, shown as a row under its tab.
    ///
    ///     This holds no git and no files: which worktrees exist comes from the repository's
    ///     own list, and what happens to the ones that vanish is decided by the caller. That
    ///     keeps the part that is easy to get wrong -- what stays, what goes, what becomes
    ///     active -- checkable without opening a repository.
    /// </summary>
    public class WorktreeGroup : ObservableObject
    {
        public WorktreeGroup(string mainPath)
        {
            MainPath = Models.WorktreeLayout.Normalize(mainPath);
            Tabs.Add(new WorktreeTab(MainPath, true));
        }

        public string MainPath { get; }

        public AvaloniaList<WorktreeTab> Tabs { get; } = [];

        public WorktreeTab Active
        {
            get => _active;
            private set => SetProperty(ref _active, value);
        }

        public WorktreeTab Main => Tabs.Count > 0 ? Tabs[0] : null;

        public WorktreeTab Find(string path)
        {
            foreach (var tab in Tabs)
            {
                if (Models.WorktreeLayout.SamePath(tab.Path, path))
                    return tab;
            }

            return null;
        }

        public WorktreeTab FindOpened(Repository repo)
        {
            if (repo == null)
                return null;

            foreach (var tab in Tabs)
            {
                if (ReferenceEquals(tab.Repository, repo))
                    return tab;
            }

            return null;
        }

        /// <summary>
        ///     The tab for a path the list does not know yet -- a worktree opened before its
        ///     repository's list caught up. The next rebuild gives it its real label and place.
        /// </summary>
        public WorktreeTab Add(string path)
        {
            var tab = Find(path);
            if (tab != null)
                return tab;

            tab = new WorktreeTab(path, false);
            Tabs.Add(tab);
            return tab;
        }

        public void MarkActive(WorktreeTab tab)
        {
            foreach (var one in Tabs)
                one.IsActive = ReferenceEquals(one, tab);

            Active = tab;
        }

        /// <summary>
        ///     Brings the row in line with what git lists, and returns the tabs that are gone.
        ///
        ///     A repository with no linked worktree lists nothing at all -- upstream only builds
        ///     the list once there are two -- so an empty list means the main alone. The main
        ///     is never dropped: it is where a vanished active worktree falls back to.
        ///
        ///     A tab missing from the list is only gone if its folder is gone too. That is what
        ///     `git worktree remove` does, and it is what separates a removed worktree from a
        ///     list that came back empty because git failed once: taking the second for the
        ///     first would close every worktree the user has open and throw them back to the
        ///     main.
        ///
        ///     Tabs that stay keep their opened repository, so a rebuild never reopens anything
        ///     and never loses what a view was showing.
        /// </summary>
        public List<WorktreeTab> Rebuild(IReadOnlyList<Models.Worktree> worktrees, Func<string, bool> stillExists = null)
        {
            stillExists ??= System.IO.Directory.Exists;

            var next = new List<WorktreeTab>();
            var main = Main ?? new WorktreeTab(MainPath, true);
            next.Add(main);

            if (worktrees != null)
            {
                for (var i = 0; i < worktrees.Count; i++)
                {
                    var wt = worktrees[i];
                    var path = Models.WorktreeLayout.Normalize(wt.FullPath);

                    if (i == 0 || Models.WorktreeLayout.SamePath(path, MainPath))
                    {
                        main.Label = Models.WorktreeLayout.LabelOf(wt);
                        continue;
                    }

                    var tab = Find(path) ?? new WorktreeTab(path, false);
                    tab.Label = Models.WorktreeLayout.LabelOf(wt);
                    next.Add(tab);
                }
            }

            var gone = new List<WorktreeTab>();
            foreach (var tab in Tabs)
            {
                if (next.Contains(tab))
                    continue;

                if (stillExists(tab.Path))
                    next.Add(tab);
                else
                    gone.Add(tab);
            }

            Tabs.Clear();
            Tabs.AddRange(next);

            if (Active != null && gone.Contains(Active))
                MarkActive(main);

            return gone;
        }

        private WorktreeTab _active;
    }
}
