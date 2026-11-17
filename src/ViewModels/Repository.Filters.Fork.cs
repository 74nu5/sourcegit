using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Avalonia.Threading;

namespace SourceGit.ViewModels
{
    /// <summary>
    ///     The history filters as this fork extends them.
    ///
    ///     Upstream's filters work as before unless something here is in use: the option that
    ///     lets both modes coexist, the one that keeps the current branch in sight, a
    ///     wildcard pattern, a "hide what is already in", or a filter whose branch has gone.
    ///     Only then do the rules build the git arguments; otherwise upstream's builder does,
    ///     untouched.
    /// </summary>
    public partial class Repository
    {
        /// <summary>
        ///     Whether the bar above the graph has anything to show: one of upstream's filters
        ///     or one of this fork's rules.
        /// </summary>
        public bool IsHistoryFilterBarVisible
        {
            get => _uiStates is not null && (_uiStates.HistoryFilters.Count > 0 || _uiStates.HistoryRules.Count > 0);
        }

        /// <summary>
        ///     The row of this fork's rules, and the count of what the graph shows, under
        ///     upstream's filters. Folded away with them.
        /// </summary>
        public bool IsHistoryRulesRowVisible
        {
            get => _uiStates is not null && !_uiStates.IsHistoryFiltersCollapsed &&
                (_uiStates.HistoryRules.Count > 0 || !string.IsNullOrEmpty(HistoryFilterSummary));
        }

        /// <summary>
        ///     "12 branches shown, 40 left out", when both modes coexist -- which is when what
        ///     the filters amount to stops being obvious from the filters themselves.
        /// </summary>
        public string HistoryFilterSummary
        {
            get
            {
                if (!Models.RepositoryUIStates.CombinesFilterModes || !IsHistoryFilterBarVisible || _branches == null)
                    return string.Empty;

                var rules = CreateHistoryRuleSet();
                var shown = 0;
                var left = 0;
                foreach (var branch in _branches)
                {
                    if (branch.IsDetachedHead)
                        continue;

                    if (rules.Evaluate(branch.FullName) == Models.HistoryRuleVerdict.Shown)
                        shown++;
                    else
                        left++;
                }

                return App.Text("HistoryRules.Summary", shown, left);
            }
        }

        public List<Models.HistoryFilterView> HistoryFilterViews
        {
            get => _uiStates?.HistoryFilterViews ?? [];
        }

        /// <summary>
        ///     Called once the repository is open: follows the changes that the bar, the
        ///     count and the sidebar depend on.
        /// </summary>
        private void AttachHistoryRules()
        {
            // The first trees are built in the background, from what is primed here.
            PrimeSidebarHistoryRules();

            _uiStates.HistoryRulesChanged += OnHistoryRulesChanged;
            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(Branches) or nameof(IsHistoryFiltersCollapsed))
                    OnHistoryRulesChanged();
            };
        }

        private void OnHistoryRulesChanged()
        {
            PrimeSidebarHistoryRules();
            OnPropertyChanged(nameof(IsHistoryFilterBarVisible));
            OnPropertyChanged(nameof(HistoryFilterSummary));
            OnPropertyChanged(nameof(IsHistoryRulesRowVisible));
        }

        private Models.HistoryRuleSet CreateHistoryRuleSet(ICollection<string> existingRefs = null)
        {
            var current = _currentBranch is { IsDetachedHead: false } branch ? branch.FullName : null;
            return new Models.HistoryRuleSet(_uiStates.HistoryFilters, _uiStates.HistoryRules, existingRefs, current, Preferences.Instance.AlwaysShowCurrentBranch);
        }

        /// <summary>
        ///     The rules to build this history load with, or null to let upstream's builder do
        ///     it as it always has.
        ///
        ///     The references are listed by git each time rather than taken from the sidebar:
        ///     the history loads before the branches, so on opening a repository the sidebar's
        ///     list is still empty, and every filtered branch would look gone.
        /// </summary>
        private async Task<Models.HistoryRuleSet> PrepareHistoryRulesAsync()
        {
            List<Models.HistoryFilter> filters = null;
            List<Models.HistoryRule> rules = null;
            string current = null;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                filters = new List<Models.HistoryFilter>(_uiStates.HistoryFilters);
                rules = new List<Models.HistoryRule>(_uiStates.HistoryRules);
                current = _currentBranch is { IsDetachedHead: false } branch ? branch.FullName : null;
            });

            if (filters.Count == 0 && rules.Count == 0)
                return null;

            var showCurrent = Preferences.Instance.AlwaysShowCurrentBranch;
            var ours = rules.Count > 0 || showCurrent || HasBothModes(filters);

            // Upstream's hiding mode names references only in --exclude, which never fails;
            // its showing mode names them outright, and one that is gone fails the whole log.
            var namesReferences = filters.Exists(f => f.Mode == Models.FilterMode.Included &&
                f.Type is Models.FilterType.LocalBranch or Models.FilterType.RemoteBranch or Models.FilterType.Tag);
            if (!ours && !namesReferences)
                return null;

            var refs = await new Commands.QueryRefNames(FullPath).GetResultAsync().ConfigureAwait(false);
            var set = new Models.HistoryRuleSet(filters, rules, refs, current, showCurrent);

            if (refs != null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    foreach (var rule in rules)
                    {
                        if (rule.Kind == Models.HistoryRuleKind.HistoryOf)
                            rule.IsValid = refs.Contains(rule.Pattern);
                    }
                });
            }

            return ours || set.HasMissingReferences ? set : null;
        }

        private static bool HasBothModes(List<Models.HistoryFilter> filters)
        {
            for (var i = 1; i < filters.Count; i++)
            {
                if (filters[i].Mode != filters[0].Mode)
                    return true;
            }

            return false;
        }

        /// <summary>
        ///     Whether checking out or creating this branch should add it to the filters, as
        ///     upstream does in its showing mode so the branch does not vanish from the graph.
        ///
        ///     Not when the current branch is always shown: it cannot vanish, and the filters
        ///     stop growing by one branch -- and its upstream -- at every checkout.
        /// </summary>
        private bool ShouldFilterInOnCheckout(Models.Branch branch)
        {
            if (Preferences.Instance.AlwaysShowCurrentBranch)
                return false;

            if (Models.RepositoryUIStates.CombinesFilterModes || _uiStates.HistoryRules.Count > 0)
            {
                var rules = CreateHistoryRuleSet();
                return rules.IsSelection && rules.Evaluate(branch.FullName) != Models.HistoryRuleVerdict.Shown;
            }

            return _historyFilterMode == Models.FilterMode.Included &&
                !_uiStates.IsCoveredByFolderFilter(branch.FullName, Models.FilterType.LocalBranchFolder);
        }

        /// <summary>
        ///     Whether the sidebar should dim this branch. Only when both modes coexist, like
        ///     the count above the graph.
        ///
        ///     The rules are rebuilt on the UI thread, where the filters are current; the trees
        ///     are also built in the background, where they are read from the last set made.
        /// </summary>
        private bool IsHiddenByHistoryRules(BranchTreeNode node)
        {
            if (!Models.RepositoryUIStates.CombinesFilterModes || node.Backend is not Models.Branch branch)
                return false;

            if (Dispatcher.UIThread.CheckAccess())
                PrimeSidebarHistoryRules();

            var rules = _sidebarHistoryRules;
            return rules != null && rules.Evaluate(branch.FullName) != Models.HistoryRuleVerdict.Shown;
        }

        private void PrimeSidebarHistoryRules()
        {
            var signature = HistoryRulesSignature();
            if (signature.Equals(_sidebarHistoryRulesSignature, StringComparison.Ordinal))
                return;

            _sidebarHistoryRulesSignature = signature;
            _sidebarHistoryRules = _uiStates.HistoryFilters.Count == 0 && _uiStates.HistoryRules.Count == 0 ? null : CreateHistoryRuleSet();
        }

        /// <summary>
        ///     What the sidebar's rules were built from. Called for every branch of the tree,
        ///     so the rules are only rebuilt when this changes.
        /// </summary>
        private string HistoryRulesSignature()
        {
            var builder = new System.Text.StringBuilder();
            builder.Append(_currentBranch?.FullName).Append('|').Append(Preferences.Instance.AlwaysShowCurrentBranch);
            foreach (var f in _uiStates.HistoryFilters)
                builder.Append('|').Append((int)f.Type).Append((int)f.Mode).Append(f.Pattern);
            foreach (var r in _uiStates.HistoryRules)
                builder.Append('|').Append((int)r.Kind).Append((int)r.Mode).Append(r.Pattern);
            return builder.ToString();
        }

        // ------------------------------------------------------------ undo

        /// <summary>
        ///     A copy of both lists, to compare against or to put back.
        /// </summary>
        private (List<Models.HistoryFilter> Filters, List<Models.HistoryRule> Rules) SnapshotHistoryFilters()
        {
            var filters = new List<Models.HistoryFilter>();
            foreach (var f in _uiStates.HistoryFilters)
                filters.Add(new Models.HistoryFilter(f.Pattern, f.Type, f.Mode));

            var rules = new List<Models.HistoryRule>();
            foreach (var r in _uiStates.HistoryRules)
                rules.Add(r.Clone());

            return (filters, rules);
        }

        /// <summary>
        ///     Says which filters a gesture removed besides the ones it was aimed at, and
        ///     offers them back. Upstream removes them without a word: setting a filter of the
        ///     other mode empties the list, a branch takes its folder's filter away, a folder
        ///     its branches'. "My filters changed and I did nothing" is what that looks like.
        /// </summary>
        private void ReportDroppedHistoryFilters((List<Models.HistoryFilter> Filters, List<Models.HistoryRule> Rules) before, params string[] targets)
        {
            if (_historyReportSuppressed > 0)
                return;

            var dropped = new List<string>();
            foreach (var f in before.Filters)
            {
                if (Array.IndexOf(targets, f.Pattern) >= 0)
                    continue;

                var kept = false;
                foreach (var now in _uiStates.HistoryFilters)
                {
                    if (now.Type == f.Type && now.Mode == f.Mode && now.Pattern.Equals(f.Pattern, StringComparison.Ordinal))
                    {
                        kept = true;
                        break;
                    }
                }

                if (!kept)
                    dropped.Add(ShortFilterName(f.Pattern));
            }

            foreach (var r in before.Rules)
            {
                var kept = false;
                foreach (var now in _uiStates.HistoryRules)
                {
                    if (now.Kind == r.Kind && now.Mode == r.Mode && now.Pattern.Equals(r.Pattern, StringComparison.Ordinal))
                    {
                        kept = true;
                        break;
                    }
                }

                if (!kept)
                    dropped.Add(r.Label);
            }

            if (dropped.Count == 0)
                return;

            Models.Notification.Send(
                FullPath,
                App.Text("HistoryRules.Dropped", dropped.Count, string.Join(", ", dropped)),
                App.Text("HistoryRules.Undo"),
                () => RestoreHistoryFilters(before));
        }

        private void RestoreHistoryFilters((List<Models.HistoryFilter> Filters, List<Models.HistoryRule> Rules) state)
        {
            // Filters given back are meant to apply, and the focus ignores every filter.
            if (_uiStates.FocusOnCurrentBranch)
            {
                _uiStates.FocusOnCurrentBranch = false;
                OnPropertyChanged(nameof(FocusOnCurrentBranch));
            }

            _uiStates.HistoryFilters.Clear();
            foreach (var f in state.Filters)
                _uiStates.HistoryFilters.Add(new Models.HistoryFilter(f.Pattern, f.Type, f.Mode));

            _uiStates.HistoryRules.Clear();
            foreach (var r in state.Rules)
                _uiStates.HistoryRules.Add(r.Clone());

            RefreshHistoryFilters(true);
        }

        private static string ShortFilterName(string pattern)
        {
            if (pattern.StartsWith("refs/heads/", StringComparison.Ordinal))
                return pattern.Substring(11);
            if (pattern.StartsWith("refs/remotes/", StringComparison.Ordinal))
                return pattern.Substring(13);
            return pattern;
        }

        /// <summary>
        ///     The end of SetBranchFilterMode: report what went besides the branch and its
        ///     upstream, then refresh.
        /// </summary>
        private void FinishBranchFilterGesture((List<Models.HistoryFilter> Filters, List<Models.HistoryRule> Rules) before, BranchTreeNode node, bool refresh)
        {
            var upstream = node.Backend is Models.Branch { Upstream: { Length: > 0 } u } ? u : string.Empty;
            ReportDroppedHistoryFilters(before, node.Path, upstream);
            RefreshHistoryFilters(refresh);
        }

        // ------------------------------------------------------------ focus

        /// <summary>
        ///     What happens to the filters when "Focus on Current Branch" is turned on. Upstream
        ///     erases them; with the option, they are set aside until it is turned off.
        /// </summary>
        private void SetAsideFiltersForFocus()
        {
            var before = SnapshotHistoryFilters();
            if (Preferences.Instance.KeepFiltersDuringFocus)
            {
                _uiStates.SuspendedHistoryFilters = before.Filters;
                _uiStates.SuspendedHistoryRules = before.Rules;
                ClearHistoryFilters();
                SendNotification(App.Text("HistoryRules.SetAside", before.Filters.Count + before.Rules.Count));
                return;
            }

            ClearHistoryFilters();
            ReportDroppedHistoryFilters(before);
        }

        /// <summary>
        ///     Gives back the filters the focus set aside, if any. True when it did, in which
        ///     case the history has been reloaded.
        /// </summary>
        private bool BringBackFiltersAfterFocus()
        {
            if (_uiStates.SuspendedHistoryFilters.Count == 0 && _uiStates.SuspendedHistoryRules.Count == 0)
                return false;

            var state = (_uiStates.SuspendedHistoryFilters, _uiStates.SuspendedHistoryRules);
            _uiStates.SuspendedHistoryFilters = [];
            _uiStates.SuspendedHistoryRules = [];
            RestoreHistoryFilters(state);
            return true;
        }

        /// <summary>
        ///     Setting a filter ends the focus, as upstream does for its own filters.
        /// </summary>
        private void LeaveFocusForFilters()
        {
            if (!_uiStates.FocusOnCurrentBranch)
                return;

            _uiStates.FocusOnCurrentBranch = false;
            OnPropertyChanged(nameof(FocusOnCurrentBranch));
            BringBackFiltersAfterFocus();
        }

        /// <summary>
        ///     After one of the filter options changed: the marks in the sidebar, the count
        ///     and the history all depend on them.
        /// </summary>
        public void RefreshHistoryFiltersFromMenu()
        {
            RefreshHistoryFilters(true);
            OnHistoryRulesChanged();
        }

        // ------------------------------------------------------------ rules

        public void AddHistoryPatternRule(string pattern, Models.FilterMode mode)
        {
            if (string.IsNullOrWhiteSpace(pattern) || mode == Models.FilterMode.None)
                return;

            LeaveFocusForFilters();

            pattern = pattern.Trim();
            RemoveHistoryRules(Models.HistoryRuleKind.Pattern, pattern);
            _uiStates.HistoryRules.Add(new Models.HistoryRule() { Kind = Models.HistoryRuleKind.Pattern, Pattern = pattern, Mode = mode });
            RefreshHistoryFilters(true);
        }

        public bool IsHistoryOfHidden(Models.Branch branch)
        {
            foreach (var rule in _uiStates.HistoryRules)
            {
                if (rule.Kind == Models.HistoryRuleKind.HistoryOf && rule.Pattern.Equals(branch.FullName, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        /// <summary>
        ///     Takes the commits this branch already holds out of the history, or puts them
        ///     back. What is left is what is not in it yet.
        /// </summary>
        public void ToggleHistoryOf(Models.Branch branch)
        {
            if (IsHistoryOfHidden(branch))
            {
                RemoveHistoryRules(Models.HistoryRuleKind.HistoryOf, branch.FullName);
            }
            else
            {
                LeaveFocusForFilters();
                _uiStates.HistoryRules.Add(new Models.HistoryRule() { Kind = Models.HistoryRuleKind.HistoryOf, Pattern = branch.FullName, Mode = Models.FilterMode.Excluded });
            }

            RefreshHistoryFilters(true);
        }

        public void RemoveHistoryRule(Models.HistoryRule rule)
        {
            if (_uiStates.HistoryRules.Remove(rule))
                RefreshHistoryFilters(true);
        }

        private void RemoveHistoryRules(Models.HistoryRuleKind kind, string pattern)
        {
            for (var i = _uiStates.HistoryRules.Count - 1; i >= 0; i--)
            {
                var rule = _uiStates.HistoryRules[i];
                if (rule.Kind == kind && rule.Pattern.Equals(pattern, StringComparison.Ordinal))
                    _uiStates.HistoryRules.RemoveAt(i);
            }
        }

        // ------------------------------------------------------------ views

        public void ApplyHistoryFilterView(Models.HistoryFilterView view)
        {
            LeaveFocusForFilters();

            var before = SnapshotHistoryFilters();
            RestoreHistoryFilters((view.Filters, view.Rules));
            SendHistoryUndo(App.Text("HistoryRules.ViewApplied", view.Name), before);
        }

        /// <summary>
        ///     Keeps the filters as they are under a name; a view of the same name is replaced.
        /// </summary>
        public void SaveHistoryFilterView(string name)
        {
            var current = SnapshotHistoryFilters();
            var view = new Models.HistoryFilterView() { Name = name.Trim(), Filters = current.Filters, Rules = current.Rules };

            var views = _uiStates.HistoryFilterViews;
            var index = views.FindIndex(v => v.Name.Equals(view.Name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                views[index] = view;
            else
                views.Add(view);

            views.Sort((l, r) => string.Compare(l.Name, r.Name, StringComparison.CurrentCultureIgnoreCase));
            _uiStates.Save();
        }

        public void DeleteHistoryFilterView(Models.HistoryFilterView view)
        {
            if (_uiStates.HistoryFilterViews.Remove(view))
                _uiStates.Save();
        }

        private void SendHistoryUndo(string message, (List<Models.HistoryFilter> Filters, List<Models.HistoryRule> Rules) before)
        {
            Models.Notification.Send(FullPath, message, App.Text("HistoryRules.Undo"), () => RestoreHistoryFilters(before));
        }

        private Models.HistoryRuleSet _sidebarHistoryRules = null;
        private string _sidebarHistoryRulesSignature = string.Empty;
        private int _historyReportSuppressed = 0;
    }
}
