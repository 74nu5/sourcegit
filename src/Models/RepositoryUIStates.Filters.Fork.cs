using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json.Serialization;

using Avalonia.Collections;
using Avalonia.Threading;

namespace SourceGit.Models
{
    /// <summary>
    ///     The history filters this fork adds, and what it changes in upstream's.
    ///
    ///     Upstream's list stays where it is and keeps its format, so upstream reads this
    ///     repository's filters as before. What upstream has no type for -- wildcard
    ///     patterns, "already in that branch" -- lives in HistoryRules, in this fork's file.
    /// </summary>
    public partial class RepositoryUIStates
    {
        /// <summary>
        ///     Whether filters of both modes coexist, mirrored from the preferences. A static
        ///     because the models do not reach into the view models, and the setting is the
        ///     same for every repository.
        /// </summary>
        public static bool CombinesFilterModes { get; set; } = false;

        [JsonIgnore]
        public AvaloniaList<HistoryRule> HistoryRules { get; set; } = [];

        [JsonIgnore]
        public List<HistoryFilterView> HistoryFilterViews { get; set; } = [];

        /// <summary>
        ///     The filters "Focus on Current Branch" set aside, given back when it ends. Kept
        ///     on disk, so a repository closed during the focus still has them next time.
        /// </summary>
        [JsonIgnore]
        public List<HistoryFilter> SuspendedHistoryFilters { get; set; } = [];

        [JsonIgnore]
        public List<HistoryRule> SuspendedHistoryRules { get; set; } = [];

        /// <summary>
        ///     Raised on the UI thread once a burst of changes to either list is over, after
        ///     the state has been written.
        /// </summary>
        public event Action HistoryRulesChanged;

        /// <summary>
        ///     The git log arguments when the rules decide: upstream's ordering and display
        ///     flags, then the revisions the rules choose.
        /// </summary>
        public string BuildHistoryParams_Rules(string gitDir, HistoryRuleSet rules)
        {
            var builder = new StringBuilder();

            if (EnableTopoOrderInHistory)
                builder.Append("--topo-order ");
            else
                builder.Append("--date-order ");

            if (HistoryShowFlags.HasFlag(HistoryShowFlags.Reflog))
                builder.Append("--reflog ");

            if (HistoryShowFlags.HasFlag(HistoryShowFlags.FirstParentOnly))
                builder.Append("--first-parent ");

            if (HistoryShowFlags.HasFlag(HistoryShowFlags.SimplifyByDecoration))
                builder.Append("--simplify-by-decoration ");

            builder.Append(rules.BuildRevisions(IsOperationInProgress(gitDir)));
            return builder.ToString();
        }

        /// <summary>
        ///     UpdateHistoryFilters when both modes coexist. Upstream's version empties the
        ///     list as soon as a filter of the other mode arrives; this one only ever touches
        ///     the filter it is asked about, and changes its mode in place.
        /// </summary>
        private bool UpdateHistoryRule(string pattern, FilterType type, FilterMode mode)
        {
            for (var i = 0; i < HistoryFilters.Count; i++)
            {
                var filter = HistoryFilters[i];
                if (filter.Type != type || !filter.Pattern.Equals(pattern, StringComparison.Ordinal))
                    continue;

                if (mode == FilterMode.None)
                {
                    HistoryFilters.RemoveAt(i);
                    return true;
                }

                if (filter.Mode == mode)
                    return false;

                filter.Mode = mode;
                return true;
            }

            if (mode == FilterMode.None)
                return false;

            HistoryFilters.Add(new HistoryFilter(pattern, type, mode));
            return true;
        }

        /// <summary>
        ///     The same test upstream makes inline when it builds its hiding mode: whether an
        ///     operation is under way, in which case HEAD may be on no branch at all.
        /// </summary>
        private static bool IsOperationInProgress(string gitDir)
        {
            return File.Exists(Path.Combine(gitDir, "CHERRY_PICK_HEAD")) ||
                Directory.Exists(Path.Combine(gitDir, "rebase-merge")) ||
                Directory.Exists(Path.Combine(gitDir, "rebase-apply")) ||
                File.Exists(Path.Combine(gitDir, "REVERT_HEAD")) ||
                File.Exists(Path.Combine(gitDir, "MERGE_HEAD"));
        }

        /// <summary>
        ///     Writes the state as soon as the filters change, instead of waiting for the
        ///     repository to close: an application stopped any other way -- a crash, an
        ///     update, the end of a session -- otherwise lost every filter set since it opened.
        /// </summary>
        private void WatchHistoryRules()
        {
            HistoryFilters.CollectionChanged += OnHistoryRulesCollectionChanged;
            HistoryRules.CollectionChanged += OnHistoryRulesCollectionChanged;

            foreach (var filter in HistoryFilters)
                filter.PropertyChanged += OnHistoryFilterPropertyChanged;
        }

        private void OnHistoryRulesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems)
                {
                    if (item is HistoryFilter filter)
                        filter.PropertyChanged += OnHistoryFilterPropertyChanged;
                }
            }

            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems)
                {
                    if (item is HistoryFilter filter)
                        filter.PropertyChanged -= OnHistoryFilterPropertyChanged;
                }
            }

            QueueHistoryRulesSave();
        }

        private void OnHistoryFilterPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(HistoryFilter.Mode) or nameof(HistoryFilter.Pattern))
                QueueHistoryRulesSave();
        }

        /// <summary>
        ///     One write per burst: a single gesture empties the list and refills it, which is
        ///     several changes, and they all land before the UI thread is idle again.
        /// </summary>
        private void QueueHistoryRulesSave()
        {
            if (_historyRulesSaveQueued)
                return;

            _historyRulesSaveQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                _historyRulesSaveQueued = false;
                Save();
                HistoryRulesChanged?.Invoke();
            }, DispatcherPriority.Background);
        }

        private bool _historyRulesSaveQueued = false;
    }
}
