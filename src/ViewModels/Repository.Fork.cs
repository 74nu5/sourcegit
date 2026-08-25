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
