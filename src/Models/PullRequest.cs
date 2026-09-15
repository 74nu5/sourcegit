using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Models
{
    /// <summary>
    ///     Where a pull request stands, in the four states every forge agrees on under its own
    ///     vocabulary.
    ///
    ///     Azure DevOps calls a closed-without-merging one "abandoned" and GitHub simply calls
    ///     it closed; they are the same thing, and splitting them would buy a distinction no
    ///     forge could fill in for the others.
    /// </summary>
    public enum PullRequestState
    {
        Open,
        Draft,
        Merged,
        Closed,
    }

    /// <summary>
    ///     Whether the request would merge as it stands.
    ///
    ///     Unknown is the honest answer for most forges: GitHub and Bitbucket say nothing
    ///     about it when listing, and finding out would cost one request per request — the
    ///     very arithmetic this whole layer exists to avoid.
    /// </summary>
    public enum PullRequestMergeState
    {
        Unknown,
        Clean,
        Conflicting,
    }

    /// <summary>
    ///     A pull request, said the same way whichever forge it came from.
    ///
    ///     Only what a branch indicator needs is kept. Anything richer belongs to whoever
    ///     asks for it, not to every forge that has to fill this in.
    /// </summary>
    public sealed class PullRequest
    {
        public long Id { get; init; }
        public string Title { get; init; } = string.Empty;
        public string Author { get; init; } = string.Empty;

        /// <summary>
        ///     The author as the forge files them — a login, a username, an address —
        ///     alongside the name it shows. Which of the two appears on a request differs by
        ///     forge, so telling "mine" from "someone else's" needs both.
        /// </summary>
        public string AuthorId { get; init; } = string.Empty;

        /// <summary>
        ///     Branch names as git knows them — "feature/x", never "refs/heads/feature/x".
        ///     This is what the indicator matches a local branch against.
        /// </summary>
        public string SourceBranch { get; init; } = string.Empty;
        public string TargetBranch { get; init; } = string.Empty;

        /// <summary>
        ///     The repository the source branch lives in, as its forge names it.
        ///
        ///     Without this, a branch is matched on its name alone — and "develop" in someone
        ///     else's fork would light up the badge on ours. On a forge where a pull request
        ///     can only come from the same repository this is simply that repository.
        /// </summary>
        public string SourceRepository { get; init; } = string.Empty;

        public PullRequestState State { get; init; }

        /// <summary>
        ///     Which forge answered for it. Carried so that whatever shows a request can say
        ///     where it lives without having to trace the account back.
        /// </summary>
        public ForgeKind Kind { get; init; }

        /// <summary>
        ///     Whether it still merges cleanly, when the forge said so while listing.
        /// </summary>
        public PullRequestMergeState MergeState { get; init; }

        public bool HasConflicts => MergeState == PullRequestMergeState.Conflicting;

        /// <summary>
        ///     What stands between it and its target branch, as far as is known.
        ///
        ///     Filled from the list answer by the forges that volunteer it, and left empty by
        ///     the ones that would charge a request per request for it -- those are asked
        ///     later, and only about a request somebody opened.
        /// </summary>
        public PullRequestChecks Checks { get; init; } = PullRequestChecks.None;

        /// <summary>
        ///     The commit at the tip of the source branch. Kept because a build is reported
        ///     against a commit, never against a pull request, on every forge that reports one
        ///     at all. Empty when the list did not carry it, and then the build is simply not
        ///     asked about.
        /// </summary>
        public string HeadSha { get; init; } = string.Empty;

        /// <summary>
        ///     Azure DevOps alone needs this: its policy engine names a pull request by the
        ///     project's identifier rather than by its path.
        /// </summary>
        public string ProjectId { get; init; } = string.Empty;

        /// <summary>
        ///     The repository this was listed from, as its forge names it.
        ///
        ///     Not the same as <see cref="SourceRepository"/>, which on GitHub is usually a
        ///     contributor's fork. A repository with two remotes on one forge -- a fork and
        ///     its upstream -- has two accounts covering it, and without this the second
        ///     question about a request would be asked of whichever came first. It was, and
        ///     the answer came back plausible and wrong.
        /// </summary>
        public string TargetRepository { get; init; } = string.Empty;
        public string Url { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; }

        /// <summary>
        ///     Still worth acting on. A branch may carry several pull requests over its life,
        ///     and the open one is the one a badge should speak for.
        /// </summary>
        public bool IsLive => State is PullRequestState.Open or PullRequestState.Draft;
    }

    /// <summary>
    ///     The one thing a forge has to be able to do for the branch indicator to exist.
    ///
    ///     It lists a whole repository rather than answering per branch, and that is the
    ///     design, not a convenience: a repository with two hundred branches costs one
    ///     request this way and two hundred the other. The count of branches never enters
    ///     the arithmetic.
    ///
    ///     This is the first capability. Others — a ticket behind a commit message, a build
    ///     status — will sit beside it as their own interfaces rather than swelling this one,
    ///     so a forge can implement what it supports and no more.
    /// </summary>
    public interface IPullRequestSource
    {
        Task<ForgeResult<List<PullRequest>>> ListAsync(
            ForgeAccount account,
            ForgeRepository repo,
            CancellationToken cancel);
    }
}
