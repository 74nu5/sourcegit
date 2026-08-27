using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Models
{
    /// <summary>
    ///     Asking a forge what stands in the way of one pull request, when it would not say
    ///     while listing.
    ///
    ///     Everything here costs one request for one request, which is the arithmetic the
    ///     listing layer exists to avoid. So nothing here is ever called while drawing a list:
    ///     it is called when somebody opens a card, about the request in that card, and the
    ///     answer is kept long enough that closing and reopening the card is free.
    /// </summary>
    public static class PullRequestChecksService
    {
        /// <summary>
        ///     Ten minutes for an answer, one for a refusal. A build finishes on its own
        ///     schedule and a card that lied for ten minutes is worse than one asked twice;
        ///     a refusal, on the other hand, usually means a scope the token will never grow.
        /// </summary>
        private static readonly ForgeCache<PullRequestChecks> _cache = new(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));

        public static IPullRequestChecksSource SourceFor(ForgeKind kind)
        {
            return kind switch
            {
                ForgeKind.GitHub => new GitHubChecks(),
                ForgeKind.AzureDevOps => new AzureDevOpsChecks(),
                ForgeKind.Gitea => new GiteaChecks(),
                ForgeKind.Bitbucket => new BitbucketChecks(),

                // GitLab answered everything while listing. Asking again would buy nothing.
                _ => null,
            };
        }

        public static async Task<ForgeResult<PullRequestChecks>> GetAsync(
            ForgeAccount account,
            ForgeRepository repo,
            PullRequest request,
            CancellationToken cancel)
        {
            var source = SourceFor(request.Kind);
            if (source == null)
                return ForgeResult<PullRequestChecks>.Success(request.Checks);

            var key = $"checks|{request.Kind}|{repo.Host}|{repo.FullName}|{request.Id}";

            var cached = await _cache.GetAsync(
                key,
                token => source.FetchAsync(account, repo, request, token),
                cancel).ConfigureAwait(false);

            return cached.Result;
        }

        public static void Forget()
        {
            _cache.Clear();
        }

        /// <summary>
        ///     Merges what the list already said with what was just fetched. The list wins
        ///     nothing and loses nothing: each condition is taken from whichever of the two
        ///     actually has an answer.
        /// </summary>
        public static PullRequestChecks Merge(PullRequestChecks free, PullRequestChecks fetched)
        {
            if (fetched == null)
                return free ?? PullRequestChecks.None;

            if (free == null)
                return fetched;

            static CheckState Pick(CheckState a, CheckState b) => a == CheckState.Unknown ? b : a;

            return new PullRequestChecks
            {
                Ci = Pick(fetched.Ci, free.Ci),
                Discussions = Pick(fetched.Discussions, free.Discussions),
                Approval = Pick(fetched.Approval, free.Approval),
                OpenDiscussions = fetched.OpenDiscussions >= 0 ? fetched.OpenDiscussions : free.OpenDiscussions,
                Detail = string.IsNullOrEmpty(fetched.Detail) ? free.Detail : fetched.Detail,
            };
        }

        /// <summary>
        ///     Reads a combined state out of many individual ones: one failure fails the whole
        ///     thing, one still running holds it, and nothing at all stays unknown.
        /// </summary>
        internal static CheckState Roll(IEnumerable<CheckState> states)
        {
            var seen = false;
            var pending = false;

            foreach (var state in states)
            {
                if (state == CheckState.Failed)
                    return CheckState.Failed;

                if (state == CheckState.Pending)
                    pending = true;

                if (state != CheckState.Unknown)
                    seen = true;
            }

            return pending ? CheckState.Pending : seen ? CheckState.Passed : CheckState.Unknown;
        }

        internal static string Read(JsonElement owner, string name)
        {
            return owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
    }

    /// <summary>
    ///     GitHub, in two requests: the check rollup for the head commit, and the reviews.
    ///
    ///     GraphQL would fold both into one and give it for every request at once, and it
    ///     would also stop working on a public repository without a token -- which is how this
    ///     fork is tested and how a good many people use it. Two REST calls for a card
    ///     somebody opened is the cheaper trade.
    /// </summary>
    public class GitHubChecks : IPullRequestChecksSource
    {
        public async Task<ForgeResult<PullRequestChecks>> FetchAsync(
            ForgeAccount account, ForgeRepository repo, PullRequest request, CancellationToken cancel)
        {
            var root = ForgeTransport.NormalizeBase(account?.Url);
            if (root == null || repo == null)
                return ForgeResult<PullRequestChecks>.Failure(ForgeStatus.BadAddress);

            var api = GitHubPullRequests.ApiRoot(repo.Host, root);
            var slug = $"{Uri.EscapeDataString(repo.Owner)}/{Uri.EscapeDataString(repo.Name)}";

            var head = Uri.EscapeDataString(request.HeadSha);
            var ci = CheckState.Unknown;
            var detail = string.Empty;

            // Actions report as check runs. The older /status endpoint knows nothing about
            // them and answers "pending, total_count 0" for a repository that has just built
            // fourteen packages successfully -- so this one is asked first.
            var runs = await ForgeTransport.GetAsync(
                account, $"{api}/repos/{slug}/commits/{head}/check-runs?per_page=100", cancel)
                .ConfigureAwait(false);

            if (runs.IsOk)
            {
                ci = ReadCheckRuns(runs.Body);
                detail = "check runs";
            }

            // Only when nothing answered there: a repository on Travis or an external CI
            // still reports the old way, and that costs a second request but only for them.
            if (ci == CheckState.Unknown)
            {
                var status = await ForgeTransport.GetAsync(
                    account, $"{api}/repos/{slug}/commits/{head}/status", cancel)
                    .ConfigureAwait(false);

                if (status.IsOk)
                {
                    ci = ReadCombined(status.Body);
                    if (ci != CheckState.Unknown)
                        detail = "commit statuses";
                }
            }

            var reviews = await ForgeTransport.GetAsync(
                account, $"{api}/repos/{slug}/pulls/{request.Id}/reviews?per_page=100", cancel)
                .ConfigureAwait(false);

            if (!runs.IsOk && !reviews.IsOk)
                return ForgeResult<PullRequestChecks>.Failure(runs.Status, runs.Detail);

            return ForgeResult<PullRequestChecks>.Success(new PullRequestChecks
            {
                Ci = ci,
                Approval = reviews.IsOk ? ReadReviews(reviews.Body) : CheckState.Unknown,
                Detail = detail,
            });
        }

        /// <summary>
        ///     One entry per job. A run that has not finished holds the whole thing; a
        ///     conclusion that is neither a pass nor a failure -- skipped, neutral, stale --
        ///     is not held against it.
        /// </summary>
        public static CheckState ReadCheckRuns(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("check_runs", out var list) || list.ValueKind != JsonValueKind.Array)
                    return CheckState.Unknown;

                var states = new List<CheckState>();
                foreach (var run in list.EnumerateArray())
                {
                    if (run.ValueKind != JsonValueKind.Object)
                        continue;

                    if (PullRequestChecksService.Read(run, "status") != "completed")
                    {
                        states.Add(CheckState.Pending);
                        continue;
                    }

                    states.Add(PullRequestChecksService.Read(run, "conclusion") switch
                    {
                        "success" => CheckState.Passed,
                        "failure" or "timed_out" or "cancelled" or "action_required" or "startup_failure"
                            => CheckState.Failed,
                        _ => CheckState.Unknown,
                    });
                }

                return PullRequestChecksService.Roll(states);
            }
            catch
            {
                return CheckState.Unknown;
            }
        }

        /// <summary>
        ///     "success", "pending", "failure" -- GitHub already rolls its statuses up.
        ///     A commit nobody ever reported on answers "pending" with an empty list, which
        ///     is a promise of nothing rather than a build in progress.
        /// </summary>
        public static CheckState ReadCombined(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return CheckState.Unknown;

                var count = root.TryGetProperty("total_count", out var total) && total.ValueKind == JsonValueKind.Number
                    ? total.GetInt32()
                    : -1;

                if (count == 0)
                    return CheckState.Unknown;

                return PullRequestChecksService.Read(root, "state") switch
                {
                    "success" => CheckState.Passed,
                    "pending" => CheckState.Pending,
                    "failure" or "error" => CheckState.Failed,
                    _ => CheckState.Unknown,
                };
            }
            catch
            {
                return CheckState.Unknown;
            }
        }

        /// <summary>
        ///     Only the last review of each person counts -- somebody who asked for changes
        ///     and then approved has approved. A request for changes outweighs any number of
        ///     approvals, which is how GitHub itself blocks the merge.
        /// </summary>
        public static CheckState ReadReviews(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return CheckState.Unknown;

                var last = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (var review in doc.RootElement.EnumerateArray())
                {
                    if (review.ValueKind != JsonValueKind.Object)
                        continue;

                    var state = PullRequestChecksService.Read(review, "state");
                    if (state is null or "COMMENTED" or "PENDING")
                        continue;

                    var who = review.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object
                        ? PullRequestChecksService.Read(user, "login")
                        : null;

                    last[who ?? Guid.NewGuid().ToString()] = state;
                }

                if (last.Count == 0)
                    return CheckState.Unknown;

                var approvals = 0;
                foreach (var state in last.Values)
                {
                    if (state == "CHANGES_REQUESTED")
                        return CheckState.Failed;

                    if (state == "APPROVED")
                        approvals++;
                }

                return approvals > 0 ? CheckState.Passed : CheckState.Pending;
            }
            catch
            {
                return CheckState.Unknown;
            }
        }
    }

    /// <summary>
    ///     Azure DevOps files everything that gates a merge as a "policy evaluation": the
    ///     build, the required reviewers, whether every comment is resolved. One request
    ///     returns them all for one pull request.
    /// </summary>
    public class AzureDevOpsChecks : IPullRequestChecksSource
    {
        public async Task<ForgeResult<PullRequestChecks>> FetchAsync(
            ForgeAccount account, ForgeRepository repo, PullRequest request, CancellationToken cancel)
        {
            var root = ForgeTransport.NormalizeBase(account?.Url);
            if (root == null || repo == null || string.IsNullOrEmpty(request.ProjectId))
                return ForgeResult<PullRequestChecks>.Failure(ForgeStatus.BadAddress);

            // The artifact id is how Azure DevOps names a pull request to its policy engine.
            var artifact = Uri.EscapeDataString(
                $"vstfs:///CodeReview/CodeReviewId/{request.ProjectId}/{request.Id}");

            var url = $"{root}/{Uri.EscapeDataString(repo.Owner)}/{Uri.EscapeDataString(repo.Project)}" +
                      $"/_apis/policy/evaluations?artifactId={artifact}&api-version={POLICY_API}";

            var reply = await ForgeTransport.GetAsync(account, url, cancel).ConfigureAwait(false);
            var checks = reply.IsOk ? Parse(reply.Body) : PullRequestChecks.None;

            // Comments are asked separately rather than read off a policy, because the
            // policy only exists if somebody configured one -- and an unanswered comment
            // holds a merge up whether or not a rule says it should.
            var threadsUrl = $"{root}/{Uri.EscapeDataString(repo.Owner)}/{Uri.EscapeDataString(repo.Project)}" +
                             $"/_apis/git/repositories/{Uri.EscapeDataString(repo.Name)}" +
                             $"/pullRequests/{request.Id}/threads?api-version=7.1";

            var threads = await ForgeTransport.GetAsync(account, threadsUrl, cancel).ConfigureAwait(false);
            if (threads.IsOk)
            {
                var open = CountActive(threads.Body);
                if (open >= 0)
                {
                    checks = checks with
                    {
                        Discussions = open == 0 ? CheckState.Passed : CheckState.Failed,
                        OpenDiscussions = open,
                    };
                }
            }

            if (!reply.IsOk && !threads.IsOk)
                return ForgeResult<PullRequestChecks>.Failure(reply.Status, reply.Detail);

            return ForgeResult<PullRequestChecks>.Success(checks);
        }

        /// <summary>
        ///     How many conversations are still open, or -1 when the answer made no sense.
        ///
        ///     Only "active" counts. A thread the forge itself wrote -- "X added Y as a
        ///     reviewer" -- carries no status at all, and a resolved one is filed under one of
        ///     four different words, none of which means anything is still asked of anybody.
        /// </summary>
        public static int CountActive(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("value", out var list) || list.ValueKind != JsonValueKind.Array)
                    return -1;

                var open = 0;
                foreach (var thread in list.EnumerateArray())
                {
                    if (thread.ValueKind != JsonValueKind.Object)
                        continue;

                    if (thread.TryGetProperty("isDeleted", out var deleted) && deleted.ValueKind == JsonValueKind.True)
                        continue;

                    if (PullRequestChecksService.Read(thread, "status") == "active")
                        open++;
                }

                return open;
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        ///     Each evaluation names the kind of policy it is and where it stands. Only the
        ///     ones that map onto a condition a person recognises are read; the rest -- merge
        ///     strategy, work item links -- are left alone rather than folded into a verdict
        ///     they would silently change.
        /// </summary>
        public static PullRequestChecks Parse(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("value", out var list) || list.ValueKind != JsonValueKind.Array)
                    return PullRequestChecks.None;

                var builds = new List<CheckState>();
                var reviewers = new List<CheckState>();
                var comments = new List<CheckState>();

                foreach (var item in list.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                        continue;

                    var type = string.Empty;
                    if (item.TryGetProperty("configuration", out var config) &&
                        config.ValueKind == JsonValueKind.Object &&
                        config.TryGetProperty("type", out var kind) &&
                        kind.ValueKind == JsonValueKind.Object)
                        type = PullRequestChecksService.Read(kind, "displayName") ?? string.Empty;

                    var state = ToState(PullRequestChecksService.Read(item, "status"));

                    if (type.Contains("Build", StringComparison.OrdinalIgnoreCase))
                        builds.Add(state);
                    else if (type.Contains("Comment", StringComparison.OrdinalIgnoreCase))
                        comments.Add(state);
                    else if (type.Contains("Reviewer", StringComparison.OrdinalIgnoreCase) ||
                             type.Contains("Approver", StringComparison.OrdinalIgnoreCase))
                        reviewers.Add(state);
                }

                var discussions = PullRequestChecksService.Roll(comments);

                return new PullRequestChecks
                {
                    Ci = PullRequestChecksService.Roll(builds),
                    Approval = PullRequestChecksService.Roll(reviewers),

                    // A comment policy that is merely "running" says nothing either way.
                    Discussions = discussions == CheckState.Pending ? CheckState.Failed : discussions,
                    Detail = $"{builds.Count} build, {reviewers.Count} reviewer, {comments.Count} comment policies",
                };
            }
            catch
            {
                return PullRequestChecks.None;
            }
        }

        /// <summary>
        ///     Policy evaluations has only ever shipped as a preview API. Asking for plain
        ///     "7.1" is refused outright, with a 400 that says so in its body and nowhere
        ///     else -- which is why the transport now logs that body.
        /// </summary>
        private const string POLICY_API = "7.1-preview.1";

        public static CheckState ToState(string status)
        {
            return status switch
            {
                "approved" => CheckState.Passed,
                "rejected" => CheckState.Failed,
                "queued" or "running" => CheckState.Pending,

                // "broken" is the policy itself failing to run, not the request failing it.
                _ => CheckState.Unknown,
            };
        }
    }

    /// <summary>
    ///     Gitea and Forgejo report a combined commit status, shaped like GitHub's.
    ///     Reviews are a separate call and are left alone: the list already carries who was
    ///     asked, and what it does not carry is not worth a second request per card.
    /// </summary>
    public class GiteaChecks : IPullRequestChecksSource
    {
        public async Task<ForgeResult<PullRequestChecks>> FetchAsync(
            ForgeAccount account, ForgeRepository repo, PullRequest request, CancellationToken cancel)
        {
            var root = ForgeTransport.NormalizeBase(account?.Url);
            if (root == null || repo == null || string.IsNullOrEmpty(request.HeadSha))
                return ForgeResult<PullRequestChecks>.Failure(ForgeStatus.BadAddress);

            var url = $"{root}/api/v1/repos/{Uri.EscapeDataString(repo.Owner)}/{Uri.EscapeDataString(repo.Name)}" +
                      $"/commits/{Uri.EscapeDataString(request.HeadSha)}/status";

            var reply = await ForgeTransport.GetAsync(account, url, cancel).ConfigureAwait(false);
            if (!reply.IsOk)
                return ForgeResult<PullRequestChecks>.Failure(reply.Status, reply.Detail);

            return ForgeResult<PullRequestChecks>.Success(new PullRequestChecks
            {
                Ci = ReadCombined(reply.Body),
            });
        }

        public static CheckState ReadCombined(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return CheckState.Unknown;

                if (root.TryGetProperty("statuses", out var statuses) &&
                    statuses.ValueKind == JsonValueKind.Array &&
                    statuses.GetArrayLength() == 0)
                    return CheckState.Unknown;

                return PullRequestChecksService.Read(root, "state") switch
                {
                    "success" => CheckState.Passed,
                    "pending" => CheckState.Pending,
                    "failure" or "error" => CheckState.Failed,
                    _ => CheckState.Unknown,
                };
            }
            catch
            {
                return CheckState.Unknown;
            }
        }
    }

    /// <summary>
    ///     Bitbucket reports build statuses per commit, one entry per pipeline.
    /// </summary>
    public class BitbucketChecks : IPullRequestChecksSource
    {
        public async Task<ForgeResult<PullRequestChecks>> FetchAsync(
            ForgeAccount account, ForgeRepository repo, PullRequest request, CancellationToken cancel)
        {
            if (repo == null)
                return ForgeResult<PullRequestChecks>.Failure(ForgeStatus.BadAddress);

            var url = $"https://api.bitbucket.org/2.0/repositories/{Uri.EscapeDataString(repo.Owner)}" +
                      $"/{Uri.EscapeDataString(repo.Name)}/pullrequests/{request.Id}/statuses?pagelen=50";

            var reply = await ForgeTransport.GetAsync(account, url, cancel).ConfigureAwait(false);
            if (!reply.IsOk)
                return ForgeResult<PullRequestChecks>.Failure(reply.Status, reply.Detail);

            return ForgeResult<PullRequestChecks>.Success(new PullRequestChecks
            {
                Ci = ReadStatuses(reply.Body),
            });
        }

        public static CheckState ReadStatuses(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array)
                    return CheckState.Unknown;

                var states = new List<CheckState>();
                foreach (var item in values.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                        continue;

                    states.Add(PullRequestChecksService.Read(item, "state") switch
                    {
                        "SUCCESSFUL" => CheckState.Passed,
                        "INPROGRESS" => CheckState.Pending,
                        "FAILED" or "ERROR" => CheckState.Failed,
                        "STOPPED" => CheckState.Failed,
                        _ => CheckState.Unknown,
                    });
                }

                return PullRequestChecksService.Roll(states);
            }
            catch
            {
                return CheckState.Unknown;
            }
        }
    }
}
