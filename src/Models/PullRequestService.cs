using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Models
{
    /// <summary>
    ///     The way in: give it a repository and an account, and it answers with that
    ///     repository's pull requests, asking the forge only when it has to.
    ///
    ///     It knows which connector speaks to which forge, and it owns the cache. A forge
    ///     with no connector is not an error — the caller simply gets nothing and shows
    ///     nothing, which is what a fork whose features are off by default should do.
    /// </summary>
    public static class PullRequestService
    {
        /// <summary>
        ///     Five minutes of trust, one minute before retrying a failure.
        ///
        ///     Pull requests move on a human timescale, so five minutes is nothing; and a
        ///     minute of remembering a refusal is what keeps a repository with two hundred
        ///     branches from asking two hundred times while the user fixes a token.
        /// </summary>
        public static readonly TimeSpan FRESH_FOR = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan RETRY_FAILURE_AFTER = TimeSpan.FromMinutes(1);

        /// <summary>
        ///     True when this forge has a connector at all. Callers use it to stay silent
        ///     rather than showing an error for something never implemented.
        /// </summary>
        public static bool Supports(ForgeKind kind) => SOURCES.ContainsKey(kind);

        public static IPullRequestSource SourceFor(ForgeKind kind)
        {
            return SOURCES.TryGetValue(kind, out var source) ? source : null;
        }

        public static async Task<ForgeCached<List<PullRequest>>> ListAsync(
            ForgeAccount account,
            ForgeRepository repo,
            CancellationToken cancel)
        {
            if (account == null || repo == null)
                return Nothing(ForgeStatus.Unexpected);

            var source = SourceFor(account.Kind);
            if (source == null)
                return Nothing(ForgeStatus.NotFound);

            return await CACHE
                .GetAsync(KeyOf(account, repo), token => source.ListAsync(account, repo, token), cancel)
                .ConfigureAwait(false);
        }

        /// <summary>
        ///     What is already known, without asking anyone. A view paints from this and
        ///     leaves the asking to whoever owns the refresh.
        /// </summary>
        public static ForgeCached<List<PullRequest>> Peek(ForgeAccount account, ForgeRepository repo)
        {
            return account == null || repo == null ? null : CACHE.Peek(KeyOf(account, repo));
        }

        /// <summary>
        ///     Forget one repository — what a fetch or a manual refresh should do.
        /// </summary>
        public static void Invalidate(ForgeAccount account, ForgeRepository repo)
        {
            if (account != null && repo != null)
                CACHE.Invalidate(KeyOf(account, repo));
        }

        /// <summary>
        ///     Forget everything reached through one account, for when its token changes and
        ///     every answer taken with the old one is worth nothing.
        /// </summary>
        public static void InvalidateAccount(ForgeAccount account)
        {
            if (account == null)
                return;

            var prefix = $"{account.Kind}|{account.Host}|";
            CACHE.InvalidateWhere(key => key.StartsWith(prefix, StringComparison.Ordinal));
        }

        public static void Clear() => CACHE.Clear();

        /// <summary>
        ///     The account is part of the key, not just the repository: the same repository
        ///     read through two different tokens can legitimately show two different sets.
        /// </summary>
        private static string KeyOf(ForgeAccount account, ForgeRepository repo)
        {
            return $"{account.Kind}|{account.Host}|{repo.FullName}";
        }

        private static ForgeCached<List<PullRequest>> Nothing(ForgeStatus status)
        {
            return new ForgeCached<List<PullRequest>>(ForgeResult<List<PullRequest>>.Failure(status), false, DateTime.UtcNow);
        }

        private static readonly Dictionary<ForgeKind, IPullRequestSource> SOURCES = new()
        {
            [ForgeKind.AzureDevOps] = new AzureDevOpsPullRequests(),
            [ForgeKind.GitHub] = new GitHubPullRequests(),
        };

        private static readonly ForgeCache<List<PullRequest>> CACHE = new(FRESH_FOR, RETRY_FAILURE_AFTER);
    }
}
