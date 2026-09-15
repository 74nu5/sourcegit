using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    /// <summary>
    ///     One local branch, and why it is or is not offered for deletion.
    /// </summary>
    public class PrunableBranch : ObservableObject
    {
        public Models.Branch Backend { get; init; }

        public string Name => Backend.Name;

        /// <summary>
        ///     Why it cannot be removed at all, or empty when it can. Git would refuse both
        ///     of these anyway; refusing them here means the box never offers itself.
        /// </summary>
        public string Locked { get; init; } = string.Empty;

        public bool IsLocked => Locked.Length > 0;

        /// <summary>
        ///     Its upstream is gone: it tracked something on a remote, and that something no
        ///     longer exists. Almost always a branch that was merged and then deleted.
        /// </summary>
        public bool IsGone { get; init; }

        /// <summary>
        ///     It never had an upstream. Nobody else has it, so deleting it loses the work
        ///     outright -- which is why it is listed but never ticked on its own.
        /// </summary>
        public bool IsNeverPushed { get; init; }

        /// <summary>
        ///     Its commits live nowhere else -- not merged into the branch you are on.
        ///
        ///     Not "unpushed": a branch whose upstream is gone has no upstream left to be
        ///     ahead of, and Ahead is never even computed for one. This is the question git
        ///     itself asks before refusing a `branch -d`, which is why the warning here and
        ///     the refusal there always agree.
        /// </summary>
        public bool IsUnmerged { get; init; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        /// <summary>
        ///     What the row says about it, in one word, with the count of unpushed commits
        ///     winning over everything else because it is the one that decides.
        /// </summary>
        public string State
        {
            get
            {
                if (IsLocked)
                    return Locked;

                if (IsGone)
                    return App.Text("Prune.State.Gone");

                return IsNeverPushed ? App.Text("Prune.State.NeverPushed") : App.Text("Prune.State.Tracked");
            }
        }

        /// <summary>
        ///     Said beside the state rather than instead of it. Replacing it hid the very
        ///     thing the reader is deciding on: whether the branch is dead as well as
        ///     unmerged.
        /// </summary>
        public string Warning => !IsLocked && IsUnmerged ? App.Text("Prune.State.Unmerged") : string.Empty;

        public bool IsWarning => Warning.Length > 0;

        /// <summary>
        ///     Days since its last commit. The rules read this one; the row reads the words
        ///     below, which are the same number said out loud.
        /// </summary>
        public int AgeInDays
        {
            get
            {
                var when = DateTimeOffset.FromUnixTimeSeconds((long)Backend.CommitterDate).LocalDateTime;
                return Math.Max(0, (int)(DateTime.Now - when).TotalDays);
            }
        }

        /// <summary>
        ///     How long ago its last commit was, because on sixty-five branches an age
        ///     settles a hesitation that a name does not.
        /// </summary>
        public string Age
        {
            get
            {
                var days = AgeInDays;

                if (days < 1)
                    return App.Text("Prune.Age.Today");
                if (days < 60)
                    return string.Format(App.Text("Prune.Age.Days"), (int)days);

                return string.Format(App.Text("Prune.Age.Months"), (int)(days / 30));
            }
        }

        private bool _isSelected = false;
    }

    /// <summary>
    ///     Removing the local branches that no longer stand for anything.
    ///
    ///     A repository that has lived a while accumulates branches whose remote counterpart
    ///     was deleted the day they were merged. Finding them one by one in a tree of sixty-
    ///     five is the work this replaces; the deletion itself is git's own, unchanged.
    ///
    ///     Everything it decides comes from what Models.Branch already carries. The one thing
    ///     it does before deciding is prune the stale remote references -- without that,
    ///     nothing looks gone and the window opens reassuringly empty.
    /// </summary>
    public class PruneBranches : ObservableObject
    {
        public AvaloniaList<PrunableBranch> Items { get; } = [];

        /// <summary>
        ///     Whether the never-pushed ones are ticked too. Off by default: a branch that was
        ///     never pushed exists nowhere else, so deleting it loses the work for good --
        ///     unlike one whose upstream is gone, which was almost certainly merged first.
        /// </summary>
        public bool IncludeNeverPushed
        {
            get => _includeNeverPushed;
            set
            {
                if (SetProperty(ref _includeNeverPushed, value))
                    Preselect();
            }
        }

        /// <summary>
        ///     Ticks a dead branch that git cannot call merged, once it is old enough.
        ///
        ///     A squash merge rewrites the commits, so the branch it came from is never
        ///     reachable from anything -- `--merged` will not name it, and neither will
        ///     `branch -d`. On a forge that squashes by default this leaves nearly every dead
        ///     branch unticked, which is exact and useless.
        ///
        ///     Age is the second witness, for when git has no evidence left to give: an
        ///     upstream that was deleted months ago, on a branch nothing has touched since,
        ///     is not work anybody is coming back to. It stays off by default, because it is
        ///     a judgement rather than a fact.
        /// </summary>
        public bool IncludeOldGone
        {
            get => _includeOldGone;
            set
            {
                if (!SetProperty(ref _includeOldGone, value))
                    return;

                // Every branch this rule adds is one git refuses without -D, so the box
                // that allows it is ticked here rather than behind the reader's back --
                // they can untick it, and then see forty refusals, which is their call.
                if (value)
                    Force = true;

                Preselect();
            }
        }

        /// <summary>
        ///     How old "old enough" is. Offered as a few round numbers rather than a free
        ///     field: the point is to pick a habit, not to tune a threshold.
        /// </summary>
        public static int[] MonthChoices { get; } = [1, 3, 6, 12];

        public int OldGoneMonths
        {
            get => _oldGoneMonths;
            set
            {
                if (SetProperty(ref _oldGoneMonths, value))
                    Preselect();
            }
        }

        /// <summary>
        ///     Plain `git branch -d`, which refuses a branch that is not merged. That refusal
        ///     is a free and exact safety net, so it stays on unless asked otherwise.
        /// </summary>
        public bool Force
        {
            get => _force;
            set => SetProperty(ref _force, value);
        }

        public string Summary
        {
            get => _summary;
            private set => SetProperty(ref _summary, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        public PruneBranches(Repository repo)
        {
            _repo = repo;
            _ = LoadAsync();
        }

        /// <summary>
        ///     Prunes the stale remote references, then reads the branches.
        ///
        ///     `git remote prune` rather than a fetch: it asks the remote what it still has
        ///     and drops the references to what it does not, without downloading a single
        ///     object. It is the cheap half of a fetch and the only half that matters here.
        /// </summary>
        private async Task LoadAsync()
        {
            IsLoading = true;
            Summary = App.Text("Prune.Pruning");

            try
            {
                var log = _repo.CreateLog("Prune Remote References");

                foreach (var remote in _repo.Remotes)
                    await new Commands.Remote(_repo.FullPath).Use(log).PruneAsync(remote.Name);

                log.Complete();

                // Lues ici plutot que par le rafraichissement du depot, qui ne s'attend
                // pas : ce qui compte est d'avoir l'etat d'apres l'elagage, maintenant.
                _branches = await new Commands.QueryBranches(_repo.FullPath).GetResultAsync().ConfigureAwait(true);
                _merged = await new Commands.QueryMergedBranches(_repo.FullPath).GetResultAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Models.ForgeLog.Failed("prune branches", ex);
            }

            Fill();
            IsLoading = false;
        }

        private void Fill()
        {
            Items.Clear();

            foreach (var branch in _branches)
            {
                if (!branch.IsLocal)
                    continue;

                var locked = branch.IsCurrent ? App.Text("Prune.Locked.Current")
                    : branch.HasWorktree ? App.Text("Prune.Locked.Worktree")
                    : string.Empty;

                var item = new PrunableBranch
                {
                    Backend = branch,
                    Locked = locked,
                    IsGone = branch.IsUpstreamGone,
                    IsNeverPushed = string.IsNullOrEmpty(branch.Upstream),
                    IsUnmerged = !_merged.Contains(branch.Name),
                };

                // A box ticked by hand has to move the count too, or the line underneath
                // says one thing while the list shows another.
                item.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(PrunableBranch.IsSelected))
                        Describe();
                };

                Items.Add(item);
            }

            Preselect();
        }

        /// <summary>
        ///     Ticks what the rules say to tick, and nothing else. Called again whenever a
        ///     rule changes, which deliberately discards a hand-made selection: the rules are
        ///     the point, and a half-applied one would be worse than either.
        /// </summary>
        private void Preselect()
        {
            foreach (var item in Items)
            {
                if (item.IsLocked)
                {
                    item.IsSelected = false;
                    continue;
                }

                var kind = item.IsGone || (item.IsNeverPushed && _includeNeverPushed);

                // Merged is the fact. Old and dead is the judgement, and only when asked --
                // and never for a branch that was never pushed, which has no upstream whose
                // disappearance could vouch for anything.
                var old = _includeOldGone && item.IsGone && item.AgeInDays >= _oldGoneMonths * 30;

                item.IsSelected = kind && (!item.IsUnmerged || old);
            }

            Describe();
        }

        public void Describe()
        {
            var picked = 0;
            var candidates = 0;

            foreach (var item in Items)
            {
                if (item.IsSelected)
                    picked++;

                if (!item.IsLocked && (item.IsGone || item.IsNeverPushed))
                    candidates++;
            }

            Summary = string.Format(App.Text("Prune.Summary"), picked, candidates, Items.Count);
        }

        /// <summary>
        ///     Deletes what is ticked, and says what git refused.
        /// </summary>
        public async Task DeleteSelectedAsync()
        {
            var targets = new List<PrunableBranch>();
            foreach (var item in Items)
            {
                if (item.IsSelected && !item.IsLocked)
                    targets.Add(item);
            }

            if (targets.Count == 0)
                return;

            using var lockWatcher = _repo.LockWatcher();

            var log = _repo.CreateLog("Prune Local Branches");

            // What git refused matters as much as what it removed: with -d it refuses a
            // branch that is not merged, and a window that reported nothing would leave
            // somebody believing the branch is gone.
            var refused = new List<string>();

            foreach (var target in targets)
            {
                var ok = await new Commands.Branch(_repo.FullPath, target.Name)
                    .Use(log)
                    .DeleteLocalAsync(_force);

                if (!ok)
                    refused.Add(target.Name);
            }

            log.Complete();

            if (refused.Count > 0)
            {
                Models.Notification.Send(_repo.FullPath, string.Format(
                    App.Text("Prune.Refused"), refused.Count, string.Join(", ", refused)), true);
            }

            _repo.MarkBranchesDirtyManually();
        }

        private readonly Repository _repo;
        private List<Models.Branch> _branches = [];
        private HashSet<string> _merged = [];
        private bool _includeNeverPushed = false;
        private bool _includeOldGone = false;
        private int _oldGoneMonths = 3;
        private bool _force = false;
        private bool _isLoading = true;
        private string _summary = string.Empty;
    }
}
