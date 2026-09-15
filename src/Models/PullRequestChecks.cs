using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Models
{
    /// <summary>
    ///     Where one condition of a merge stands.
    ///
    ///     Unknown is not a failure and must never read as one. Most forges say nothing about
    ///     most of these while listing, and painting silence red would turn every request on
    ///     GitHub into a warning about nothing.
    /// </summary>
    public enum CheckState
    {
        Unknown,
        Pending,
        Passed,
        Failed,
    }

    /// <summary>
    ///     What a forge said stands between a pull request and its target branch.
    ///
    ///     Three conditions, because they are the three every forge models under some name:
    ///     did the build pass, is the conversation finished, has anyone approved. Conflicts
    ///     are the fourth and live on the request itself, where they were already answered.
    ///
    ///     A forge fills in what it knows and leaves the rest Unknown. GitLab answers all
    ///     three while listing; the others answer none, and are asked one request at a time,
    ///     only for a request somebody actually opened.
    /// </summary>
    public sealed record PullRequestChecks
    {
        public static readonly PullRequestChecks None = new();

        public CheckState Ci { get; init; } = CheckState.Unknown;
        public CheckState Discussions { get; init; } = CheckState.Unknown;
        public CheckState Approval { get; init; } = CheckState.Unknown;

        /// <summary>
        ///     How many conversations are still open, when the forge counts them. -1 when it
        ///     does not, which is not the same as zero.
        /// </summary>
        public int OpenDiscussions { get; init; } = -1;

        /// <summary>
        ///     What the forge literally answered — "ci_must_pass", "changes_requested". Shown
        ///     as an aside, never parsed: it is there so an unexpected answer can be read
        ///     rather than guessed at.
        /// </summary>
        public string Detail { get; init; } = string.Empty;

        /// <summary>
        ///     Whether anything at all is known. A card asks this before offering to go and
        ///     find out.
        /// </summary>
        public bool IsEmpty =>
            Ci == CheckState.Unknown &&
            Discussions == CheckState.Unknown &&
            Approval == CheckState.Unknown;

        /// <summary>
        ///     The three conditions and the conflict state folded into one word.
        ///
        ///     Blocked outranks Waiting, and Waiting outranks Ready: what is missing is worth
        ///     more than what is done. Ready is claimed only when every condition is known and
        ///     green — silence never adds up to a green light.
        /// </summary>
        public PullRequestVerdict Verdict(PullRequestMergeState merge)
        {
            if (merge == PullRequestMergeState.Conflicting ||
                Ci == CheckState.Failed ||
                Approval == CheckState.Failed)
                return PullRequestVerdict.Blocked;

            if (Ci == CheckState.Pending ||
                Discussions == CheckState.Failed ||
                Approval == CheckState.Pending)
                return PullRequestVerdict.Waiting;

            if (IsEmpty)
                return PullRequestVerdict.Unknown;

            // Everything that spoke said yes. A condition the forge never mentions does not
            // hold the verdict back -- it would leave every request grey forever.
            return PullRequestVerdict.Ready;
        }
    }

    /// <summary>
    ///     One word for the whole thing, in the order a reader cares about.
    /// </summary>
    public enum PullRequestVerdict
    {
        Unknown,
        Ready,
        Waiting,
        Blocked,
    }

    /// <summary>
    ///     Asking a forge what stands in the way of one pull request.
    ///
    ///     Separate from <see cref="IPullRequestSource"/> on purpose. Listing costs one
    ///     request for a whole repository; this costs one request for one request, so it is
    ///     never called while drawing a list — only when somebody opens a card and asks.
    ///
    ///     A forge that answers everything while listing does not implement this at all.
    /// </summary>
    public interface IPullRequestChecksSource
    {
        Task<ForgeResult<PullRequestChecks>> FetchAsync(
            ForgeAccount account,
            ForgeRepository repo,
            PullRequest request,
            CancellationToken cancel);
    }

    /// <summary>
    ///     Reads the conditions a forge volunteers in its own list answer, so that what is
    ///     free stays free.
    /// </summary>
    public static class Checks
    {
        /// <summary>
        ///     GitLab says why a merge request cannot be merged, in one word, while listing.
        ///     The vocabulary is theirs; only the ones that name a condition are read, and
        ///     anything unrecognised leaves the conditions alone rather than guessing.
        /// </summary>
        public static PullRequestChecks FromGitLab(string detailedMergeStatus, bool? discussionsResolved)
        {
            var status = detailedMergeStatus ?? string.Empty;

            var ci = status switch
            {
                "ci_must_pass" => CheckState.Pending,
                "ci_still_running" => CheckState.Pending,
                "mergeable" => CheckState.Passed,
                _ => CheckState.Unknown,
            };

            var approval = status switch
            {
                "not_approved" => CheckState.Pending,
                "mergeable" => CheckState.Passed,
                _ => CheckState.Unknown,
            };

            var discussions = discussionsResolved switch
            {
                true => CheckState.Passed,
                false => CheckState.Failed,
                _ => status == "discussions_not_resolved" ? CheckState.Failed : CheckState.Unknown,
            };

            return new PullRequestChecks
            {
                Ci = ci,
                Approval = approval,
                Discussions = discussions,
                Detail = status,
            };
        }

        /// <summary>
        ///     Azure DevOps files a vote per reviewer, and hands them over while listing:
        ///     10 approved, 5 approved with suggestions, 0 no vote yet, -5 waiting for the
        ///     author, -10 rejected.
        ///
        ///     One rejection outweighs any number of approvals, which is also how the forge
        ///     itself treats it.
        /// </summary>
        public static PullRequestChecks FromVotes(IEnumerable<int> votes)
        {
            var approvals = 0;
            var rejected = false;
            var waiting = false;
            var any = false;

            foreach (var vote in votes)
            {
                any = true;
                if (vote <= -10)
                    rejected = true;
                else if (vote < 0)
                    waiting = true;
                else if (vote >= 5)
                    approvals++;
            }

            if (!any)
                return PullRequestChecks.None;

            var approval = rejected ? CheckState.Failed
                : waiting ? CheckState.Pending
                : approvals > 0 ? CheckState.Passed
                : CheckState.Pending;

            return new PullRequestChecks
            {
                Approval = approval,
                Detail = rejected ? "rejected" : $"{approvals} approved",
            };
        }

        /// <summary>
        ///     Bitbucket counts open tasks on a pull request while listing. It is not quite
        ///     "unresolved comments", but it is the same question — is anything still asked of
        ///     the author — and it is the only one Bitbucket answers for free.
        /// </summary>
        public static PullRequestChecks FromTaskCount(int openTasks)
        {
            if (openTasks < 0)
                return PullRequestChecks.None;

            return new PullRequestChecks
            {
                Discussions = openTasks == 0 ? CheckState.Passed : CheckState.Failed,
                OpenDiscussions = openTasks,
                Detail = openTasks == 0 ? "no open task" : $"{openTasks} open task(s)",
            };
        }
    }
}
