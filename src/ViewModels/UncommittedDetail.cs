using System;
using System.Collections.Generic;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    /// <summary>
    ///     What the detail panel shows when the graph's uncommitted row is selected.
    ///
    ///     That row answers to no object in the repository, so there is no commit to read
    ///     and nothing to ask git. What there is, is a working copy, and its changes are
    ///     already loaded by the page next door: this borrows that list rather than running
    ///     `git status` a second time.
    ///
    ///     Staged and unstaged are shown as one list, because "what is not committed yet"
    ///     is one question. A file can sit on both sides at once -- edited, partly staged,
    ///     then edited again -- and it appears once, on the unstaged side, which is the one
    ///     that differs from what a commit would record.
    /// </summary>
    public class UncommittedDetail : ObservableObject
    {
        public List<Models.Change> Changes
        {
            get => _changes;
            private set => SetProperty(ref _changes, value);
        }

        public List<Models.Change> VisibleChanges
        {
            get => _visibleChanges;
            private set => SetProperty(ref _visibleChanges, value);
        }

        public ChangeSelection ChangeSelection
        {
            get => _changeSelection;
            set
            {
                if (SetProperty(ref _changeSelection, value))
                    UpdateDiff();
            }
        }

        public DiffContext DiffContext
        {
            get => _diffContext;
            private set => SetProperty(ref _diffContext, value);
        }

        public string SearchChangeFilter
        {
            get => _searchChangeFilter;
            set
            {
                if (SetProperty(ref _searchChangeFilter, value))
                    RefreshVisible();
            }
        }

        public UncommittedDetail(Repository repo)
        {
            _repo = repo;
            Reload();
        }

        public void ClearSearchChangeFilter()
        {
            SearchChangeFilter = string.Empty;
        }

        /// <summary>
        ///     Take the working copy's lists again.
        ///
        ///     Called when the working copy reloads, so the panel says what the repository
        ///     says. The selected file is kept when it is still there, so typing in a file
        ///     while its diff is open does not throw you back to nothing.
        /// </summary>
        public void Reload()
        {
            var working = _repo?.WorkingCopy;
            _unstaged.Clear();
            Changes = Merge(working?.Unstaged, working?.Staged, _unstaged);
            RefreshVisible();

            var wanted = _changeSelection?.Changes;
            if (wanted is { Count: 1 })
            {
                var still = _changes.Find(x => x.Path.Equals(wanted[0].Path, StringComparison.Ordinal));
                if (still != null)
                {
                    UpdateDiff();
                    return;
                }
            }

            DiffContext = null;
        }

        /// <summary>
        ///     The two sides of the working copy as one list, sorted, with a file that sits
        ///     on both kept once -- on the unstaged side, which is the one that differs from
        ///     what a commit would record. Fills <paramref name="unstagedPaths"/> so the diff
        ///     can later ask git for the right side.
        /// </summary>
        public static List<Models.Change> Merge(List<Models.Change> unstaged, List<Models.Change> staged, HashSet<string> unstagedPaths)
        {
            var merged = new List<Models.Change>();

            foreach (var change in unstaged ?? [])
            {
                unstagedPaths.Add(change.Path);
                merged.Add(change);
            }

            foreach (var change in staged ?? [])
            {
                if (!unstagedPaths.Contains(change.Path))
                    merged.Add(change);
            }

            merged.Sort((l, r) => Models.NumericSort.Compare(l.Path, r.Path));
            return merged;
        }

        private void RefreshVisible()
        {
            if (string.IsNullOrWhiteSpace(_searchChangeFilter))
            {
                VisibleChanges = _changes;
                return;
            }

            var visible = new List<Models.Change>();
            foreach (var change in _changes)
            {
                if (change.Path.Contains(_searchChangeFilter, StringComparison.OrdinalIgnoreCase))
                    visible.Add(change);
            }

            VisibleChanges = visible;
        }

        /// <summary>
        ///     One file selected shows its diff; anything else shows none, which is what the
        ///     commit panel does too.
        /// </summary>
        private void UpdateDiff()
        {
            if (_repo == null || _changeSelection is not { Changes.Count: 1 })
            {
                DiffContext = null;
                return;
            }

            var change = _changeSelection.Changes[0];
            var isUnstaged = _unstaged.Contains(change.Path);
            DiffContext = new DiffContext(_repo.FullPath, new Models.DiffOption(change, isUnstaged), _diffContext);
        }

        private readonly Repository _repo = null;
        private readonly HashSet<string> _unstaged = new(StringComparer.Ordinal);
        private List<Models.Change> _changes = [];
        private List<Models.Change> _visibleChanges = [];
        private ChangeSelection _changeSelection = null;
        private DiffContext _diffContext = null;
        private string _searchChangeFilter = string.Empty;
    }
}
