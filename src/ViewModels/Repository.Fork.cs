using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    /// <summary>
    ///     What this fork adds to a repository. Repository.cs is one of upstream's busiest
    ///     files; staying out of it removes a whole class of rebase conflicts.
    /// </summary>
    public partial class Repository
    {
        /// <summary>
        ///     The live pull request on each branch, by branch name, or an empty map.
        ///
        ///     Asked for by the badges themselves rather than pushed by a refresh: the cache
        ///     underneath coalesces callers, so two hundred branches asking at once still
        ///     make one request per remote, and nothing has to be wired into upstream's
        ///     reload.
        ///
        ///     Only live ones are indexed. A branch outlives its merged pull requests, and a
        ///     badge lit for a request closed a year ago would say nothing useful.
        /// </summary>
        public async Task<Dictionary<string, Models.PullRequest>> GetPullRequestsAsync(CancellationToken cancel)
        {
            if (!Preferences.Instance.ShowPullRequestIndicator)
                return EMPTY;

            var forges = ResolveForges();
            if (forges.Count == 0)
                return EMPTY;

            // Which repositories our branches could possibly live in. A pull request whose
            // source branch sits somewhere else is somebody else's, however alike the names.
            var ours = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (_, repo) in forges)
                ours.Add(repo.FullName);

            var byBranch = new Dictionary<string, Models.PullRequest>();

            // Every remote is asked, not just the first: someone working on a fork opens
            // their requests against the upstream repository, so that is where they are.
            foreach (var (account, repo) in forges)
            {
                var cached = await Models.PullRequestService.ListAsync(account, repo, cancel).ConfigureAwait(false);
                if (!cached.IsOk)
                    continue;

                foreach (var pr in cached.Result.Value)
                {
                    if (!pr.IsLive || string.IsNullOrEmpty(pr.SourceBranch))
                        continue;

                    if (!string.IsNullOrEmpty(pr.SourceRepository) && !ours.Contains(pr.SourceRepository))
                        continue;

                    // A branch can carry more than one open request over its life; the newest
                    // is the one anybody means.
                    if (!byBranch.TryGetValue(pr.SourceBranch, out var held) || pr.CreatedAt > held.CreatedAt)
                        byBranch[pr.SourceBranch] = pr;
                }
            }

            return byBranch;
        }

        /// <summary>
        ///     Every live pull request this repository's forges know about, newest first.
        ///
        ///     Unlike the map the branch marks use, this is not narrowed to branches that
        ///     exist here: a list of the repository's requests is about the repository, and a
        ///     request whose branch was never fetched still belongs on it.
        /// </summary>
        public async Task<List<Models.PullRequest>> GetPullRequestListAsync(CancellationToken cancel)
        {
            var found = new List<Models.PullRequest>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (account, repo) in ResolveForges())
            {
                var cached = await Models.PullRequestService.ListAsync(account, repo, cancel).ConfigureAwait(false);
                if (!cached.IsOk)
                    continue;

                foreach (var pr in cached.Result.Value)
                {
                    // Two remotes can point at the same repository under different names, and
                    // the address is the one thing a request cannot share with another.
                    if (pr.IsLive && seen.Add(pr.Url.Length > 0 ? pr.Url : $"{pr.Kind}|{pr.Id}"))
                        found.Add(pr);
                }
            }

            found.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
            return found;
        }

        /// <summary>
        ///     Who this repository's tokens say we are, one identity per forge — or an empty
        ///     list when no forge can tell, in which case "mine" has nothing to mean and the
        ///     filter says so rather than quietly showing nothing.
        /// </summary>
        public async Task<List<Models.ForgeUser>> GetForgeIdentitiesAsync(CancellationToken cancel)
        {
            var found = new List<Models.ForgeUser>();

            foreach (var (account, _) in ResolveForges())
            {
                var cached = await Models.ForgeIdentityService.WhoAmIAsync(account, cancel).ConfigureAwait(false);
                if (cached.IsOk && cached.Result.Value != null)
                    found.Add(cached.Result.Value);
            }

            // Always added, and last: a forge that answered is more precise, but git's own
            // idea of who we are is the one that never fails.
            var local = await GetGitIdentityAsync().ConfigureAwait(false);
            if (local != null)
                found.Add(local);

            return found;
        }

        /// <summary>
        ///     Who git thinks we are here.
        ///
        ///     Worth as much as anything a forge could say and it costs no call: on Azure
        ///     DevOps a request is filed under the author's work address, which is exactly
        ///     what user.email holds. It is also the only answer left when the forge cannot
        ///     give one — an Azure DevOps Server on premises, a Bitbucket Data Center.
        /// </summary>
        public async Task<Models.ForgeUser> GetGitIdentityAsync()
        {
            var email = await new Commands.Config(FullPath).GetAsync("user.email").ConfigureAwait(false);
            var name = await new Commands.Config(FullPath).GetAsync("user.name").ConfigureAwait(false);

            return string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(name)
                ? null
                : new Models.ForgeUser(string.Empty, name ?? string.Empty, email ?? string.Empty);
        }

        /// <summary>
        ///     True when at least one configured account covers this repository. What decides
        ///     whether the pull request section appears at all: no account, no section, rather
        ///     than an empty one asking to be ignored.
        /// </summary>
        /// <summary>
        ///     What stands in the way of one pull request, asked of whichever account covers
        ///     the repository it came from.
        ///
        ///     Called when a card opens, never while drawing a list: on most forges this is
        ///     one request for one request, and a list of seventy would be seventy of them.
        /// </summary>
        public async Task<Models.PullRequestChecks> GetPullRequestChecksAsync(
            Models.PullRequest pr,
            CancellationToken cancel)
        {
            if (pr == null)
                return Models.PullRequestChecks.None;

            foreach (var (account, repo) in ResolveForges())
            {
                if (account.Kind != pr.Kind)
                    continue;

                // A fork and its upstream are two remotes on one forge, and both resolve to
                // an account. Asking the wrong one does not fail loudly: it answers about
                // another repository that happens to share the commit.
                if (!string.IsNullOrEmpty(pr.TargetRepository) &&
                    !repo.FullName.Equals(pr.TargetRepository, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                var result = await Models.PullRequestChecksService
                    .GetAsync(account, repo, pr, cancel)
                    .ConfigureAwait(false);

                Models.ForgeLog.Line($"checks {pr.Kind} #{pr.Id} -> {result.Status}");

                if (result.IsOk)
                    return Models.PullRequestChecksService.Merge(pr.Checks, result.Value);

                return pr.Checks;
            }

            return pr.Checks;
        }

        /// <summary>
        ///     The window that lists the local branches with the dead ones already ticked.
        /// </summary>
        public PruneBranches PrepareBranchPruning()
        {
            return new PruneBranches(this);
        }

        /// <summary>
        ///     Which sections of the left panel this repository shows.
        ///
        ///     Two booleans decide whether a list appears: visible, and expanded. The header
        ///     answers to the first; the list under it needs both, which is why each has a
        ///     property of its own rather than sharing one.
        /// </summary>
        public bool IsLocalBranchSectionVisible
        {
            get => UIStates.IsLocalBranchesVisibleInSideBar;
            set => SetSectionVisible(v => UIStates.IsLocalBranchesVisibleInSideBar = v, UIStates.IsLocalBranchesVisibleInSideBar, value);
        }

        public bool IsRemoteSectionVisible
        {
            get => UIStates.IsRemotesVisibleInSideBar;
            set => SetSectionVisible(v => UIStates.IsRemotesVisibleInSideBar = v, UIStates.IsRemotesVisibleInSideBar, value);
        }

        public bool IsTagSectionVisible
        {
            get => UIStates.IsTagsVisibleInSideBar;
            set => SetSectionVisible(v => UIStates.IsTagsVisibleInSideBar = v, UIStates.IsTagsVisibleInSideBar, value);
        }

        public bool IsSubmoduleSectionVisible
        {
            get => UIStates.IsSubmodulesVisibleInSideBar;
            set => SetSectionVisible(v => UIStates.IsSubmodulesVisibleInSideBar = v, UIStates.IsSubmodulesVisibleInSideBar, value);
        }

        public bool IsWorktreeSectionVisible
        {
            get => UIStates.IsWorktreesVisibleInSideBar;
            set => SetSectionVisible(v => UIStates.IsWorktreesVisibleInSideBar = v, UIStates.IsWorktreesVisibleInSideBar, value);
        }

        public bool IsPullRequestSectionVisible
        {
            get => UIStates.IsPullRequestsVisibleInSideBar;
            set => SetSectionVisible(v => UIStates.IsPullRequestsVisibleInSideBar = v, UIStates.IsPullRequestsVisibleInSideBar, value);
        }

        /// <summary>
        ///     A computed property is only as good as what tells it to recompute.
        ///
        ///     The five list properties below read a section's own flag and the folding flag
        ///     upstream owns -- and upstream's setter announces itself and nothing else. So
        ///     folding a section rotated its chevron and left its list on screen: the binding
        ///     was never told to look again. Every fold now re-announces the list that
        ///     depends on it.
        /// </summary>
        protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);

            var dependent = e.PropertyName switch
            {
                nameof(IsLocalBranchGroupExpanded) => nameof(IsLocalBranchListVisible),
                nameof(IsRemoteGroupExpanded) => nameof(IsRemoteListVisible),
                nameof(IsTagGroupExpanded) => nameof(IsTagListVisible),
                nameof(IsSubmoduleGroupExpanded) => nameof(IsSubmoduleListVisible),
                nameof(IsWorktreeGroupExpanded) => nameof(IsWorktreeListVisible),
                _ => null,
            };

            if (dependent != null)
                OnPropertyChanged(dependent);
        }

        // A list needs its section shown and its group expanded. Both, every time.
        public bool IsLocalBranchListVisible => IsLocalBranchSectionVisible && IsLocalBranchGroupExpanded;
        public bool IsRemoteListVisible => IsRemoteSectionVisible && IsRemoteGroupExpanded;
        public bool IsTagListVisible => IsTagSectionVisible && IsTagGroupExpanded;
        public bool IsSubmoduleListVisible => IsSubmoduleSectionVisible && IsSubmoduleGroupExpanded;
        public bool IsWorktreeListVisible => IsWorktreeSectionVisible && IsWorktreeGroupExpanded;

        public SidebarSections HiddenSections { get; } = new();

        /// <summary>
        ///     How many section headers still take their 28 pixels. The panel hands out
        ///     heights by hand and used to count five; counting them for real is the whole
        ///     reason a hidden section gives its room back instead of leaving a gap.
        /// </summary>
        public int VisibleSectionHeaderCount()
        {
            var count = 0;
            if (IsLocalBranchSectionVisible)
                count++;
            if (IsRemoteSectionVisible)
                count++;
            if (IsTagSectionVisible)
                count++;
            if (IsSubmoduleSectionVisible)
                count++;
            if (IsWorktreeSectionVisible)
                count++;
            return count;
        }

        private void SetSectionVisible(Action<bool> assign, bool current, bool value)
        {
            if (current == value)
                return;

            assign(value);
            OnPropertyChanged(nameof(IsLocalBranchSectionVisible));
            OnPropertyChanged(nameof(IsRemoteSectionVisible));
            OnPropertyChanged(nameof(IsTagSectionVisible));
            OnPropertyChanged(nameof(IsSubmoduleSectionVisible));
            OnPropertyChanged(nameof(IsWorktreeSectionVisible));
            OnPropertyChanged(nameof(IsPullRequestSectionVisible));
            OnPropertyChanged(nameof(IsLocalBranchListVisible));
            OnPropertyChanged(nameof(IsRemoteListVisible));
            OnPropertyChanged(nameof(IsTagListVisible));
            OnPropertyChanged(nameof(IsSubmoduleListVisible));
            OnPropertyChanged(nameof(IsWorktreeListVisible));

            RefreshHiddenSections();
        }

        /// <summary>
        ///     Rebuilds the row of chips that brings a hidden section back.
        /// </summary>
        public void RefreshHiddenSections()
        {
            var hidden = new List<SidebarSection>();

            void Consider(bool visible, string key, string label, Action restore)
            {
                if (!visible)
                    hidden.Add(new SidebarSection { Key = key, Label = label, Restore = restore });
            }

            Consider(IsLocalBranchSectionVisible, "local", App.Text("Repository.LocalBranches"), () => IsLocalBranchSectionVisible = true);
            Consider(IsRemoteSectionVisible, "remote", App.Text("Repository.Remotes"), () => IsRemoteSectionVisible = true);
            Consider(IsTagSectionVisible, "tag", App.Text("Repository.Tags"), () => IsTagSectionVisible = true);
            Consider(IsSubmoduleSectionVisible, "submodule", App.Text("Repository.Submodules"), () => IsSubmoduleSectionVisible = true);
            Consider(IsWorktreeSectionVisible, "worktree", App.Text("Repository.Worktrees"), () => IsWorktreeSectionVisible = true);
            Consider(IsPullRequestSectionVisible, "pr", App.Text("Repository.PullRequests"), () => IsPullRequestSectionVisible = true);

            HiddenSections.Rebuild(hidden);
        }

        public bool HasForge() => ResolveForges().Count > 0;

        /// <summary>
        ///     Which forge each remote lives on, by remote name.
        ///
        ///     A declared account wins over the address, because a self-hosted GitLab answers
        ///     to a name nothing can recognise — being told is the only way to know.
        /// </summary>
        public Dictionary<string, Models.ForgeKind> GetRemoteKinds()
        {
            var map = new Dictionary<string, Models.ForgeKind>(StringComparer.OrdinalIgnoreCase);

            foreach (var remote in Remotes)
            {
                if (!Models.Forge.TryParse(remote, out var parsed))
                    continue;

                var account = Preferences.Instance.FindForgeAccount(parsed);
                map[remote.Name] = account?.Kind ?? parsed.Kind;
            }

            return map;
        }

        /// <summary>
        ///     Forget what this repository's forges told us, so the next question reaches
        ///     them. What a fetch or a manual refresh should do.
        /// </summary>
        public void InvalidatePullRequests()
        {
            foreach (var (account, repo) in ResolveForges())
                Models.PullRequestService.Invalidate(account, repo);
        }

        /// <summary>
        ///     Every remote that a configured account covers and a connector can answer for.
        ///     An empty list is the ordinary case and never an error.
        /// </summary>
        private List<(Models.ForgeAccount, Models.ForgeRepository)> ResolveForges()
        {
            var found = new List<(Models.ForgeAccount, Models.ForgeRepository)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var remote in Remotes)
            {
                if (!Models.Forge.TryParse(remote, out var parsed))
                    continue;

                if (!seen.Add(parsed.FullName))
                    continue;

                var account = Preferences.Instance.FindForgeAccount(parsed);
                if (account != null && Models.PullRequestService.Supports(account.Kind))
                    found.Add((account, parsed));
            }

            return found;
        }

        private static readonly Dictionary<string, Models.PullRequest> EMPTY = [];
    }
}
