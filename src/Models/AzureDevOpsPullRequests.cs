using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Models
{
    /// <summary>
    ///     Pull requests as Azure DevOps tells them.
    ///
    ///     It is alone in paging with $top and $skip rather than the continuation token its
    ///     other endpoints use, so the walk is written here instead of leaning on the
    ///     transport's paging. A page shorter than asked for is the last one.
    /// </summary>
    public class AzureDevOpsPullRequests : IPullRequestSource
    {
        public async Task<ForgeResult<List<PullRequest>>> ListAsync(
            ForgeAccount account,
            ForgeRepository repo,
            CancellationToken cancel)
        {
            var root = ForgeTransport.NormalizeBase(account?.Url);
            if (root == null || repo == null)
                return ForgeResult<List<PullRequest>>.Failure(ForgeStatus.BadAddress);

            if (string.IsNullOrEmpty(repo.Owner) || string.IsNullOrEmpty(repo.Project) || string.IsNullOrEmpty(repo.Name))
                return ForgeResult<List<PullRequest>>.Failure(ForgeStatus.BadAddress);

            var found = new List<PullRequest>();

            for (var page = 0; page < MAX_PAGES; page++)
            {
                var url = BuildUrl(root, repo, page * PAGE_SIZE);
                var reply = await ForgeTransport.GetAsync(account, url, cancel, ForgeTransport.LIST_TIMEOUT).ConfigureAwait(false);
                if (!reply.IsOk)
                    return ForgeResult<List<PullRequest>>.Failure(reply.Status, reply.Detail);

                var read = Parse(reply.Body, repo, found);
                if (read < 0)
                    return ForgeResult<List<PullRequest>>.Failure(ForgeStatus.Unexpected, "unreadable answer");

                // Azure DevOps offers no "there is more" flag: a short page is the last one.
                if (read < PAGE_SIZE)
                    return ForgeResult<List<PullRequest>>.Success(found);
            }

            return new ForgeResult<List<PullRequest>>(ForgeStatus.Ok, found, "truncated");
        }

        public static string BuildUrl(string root, ForgeRepository repo, int skip)
        {
            // Only what is still open. Asking for every state brought back nine hundred
            // requests on a real repository — six megabytes, ten pages, the page budget
            // exhausted — to keep the seventy-four that are alive. Nothing shows the rest.
            return $"{root}/{Esc(repo.Owner)}/{Esc(repo.Project)}/_apis/git/repositories/{Esc(repo.Name)}/pullrequests" +
                   $"?searchCriteria.status=active&$top={PAGE_SIZE}&$skip={skip}&api-version=7.1";
        }

        /// <summary>
        ///     Reads one page into <paramref name="into"/> and returns how many entries it
        ///     held, or -1 when the answer made no sense.
        /// </summary>
        public static int Parse(string body, ForgeRepository repo, List<PullRequest> into)
        {
            if (string.IsNullOrEmpty(body))
                return -1;

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                    !doc.RootElement.TryGetProperty("value", out var value) ||
                    value.ValueKind != JsonValueKind.Array)
                    return -1;

                var count = 0;
                foreach (var item in value.EnumerateArray())
                {
                    count++;

                    var pr = ReadOne(item, repo);
                    if (pr != null)
                        into.Add(pr);
                }

                return count;
            }
            catch
            {
                return -1;
            }
        }

        private static PullRequest ReadOne(JsonElement item, ForgeRepository repo)
        {
            if (item.ValueKind != JsonValueKind.Object)
                return null;

            if (!item.TryGetProperty("pullRequestId", out var idElement) ||
                idElement.ValueKind != JsonValueKind.Number)
                return null;

            var id = idElement.GetInt64();
            var status = ReadString(item, "status") ?? string.Empty;
            var isDraft = item.TryGetProperty("isDraft", out var draft) && draft.ValueKind == JsonValueKind.True;

            var author = string.Empty;
            var authorId = string.Empty;
            if (item.TryGetProperty("createdBy", out var createdBy) && createdBy.ValueKind == JsonValueKind.Object)
            {
                author = ReadString(createdBy, "displayName") ?? ReadString(createdBy, "uniqueName") ?? string.Empty;
                authorId = ReadString(createdBy, "uniqueName") ?? string.Empty;
            }

            var created = DateTime.MinValue;
            var createdText = ReadString(item, "creationDate");
            if (createdText != null)
                DateTime.TryParse(createdText, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out created);

            return new PullRequest
            {
                Id = id,
                Title = ReadString(item, "title") ?? string.Empty,
                Author = author,
                AuthorId = authorId,
                SourceBranch = ShortBranch(ReadString(item, "sourceRefName")),
                TargetBranch = ShortBranch(ReadString(item, "targetRefName")),
                SourceRepository = repo.FullName,
                Kind = ForgeKind.AzureDevOps,
                MergeState = ToMergeState(ReadString(item, "mergeStatus")),

                // Free: the votes come with the list. The build and the required policies do
                // not -- those live behind policy/evaluations, one request per request, and
                // are asked for only when a card is opened.
                Checks = Models.Checks.FromVotes(ReadVotes(item)),

                // Its policy engine names a request by the project's identifier, which only
                // the list answer carries.
                TargetRepository = repo.FullName,
                ProjectId = ReadProjectId(item),
                HeadSha = ReadCommit(item, "lastMergeSourceCommit"),
                State = ToState(status, isDraft),
                Url = BuildWebUrl(repo, id),
                CreatedAt = created,
            };
        }

        /// <summary>
        ///     repository.project.id, which is what the policy endpoint asks for.
        /// </summary>
        public static string ReadProjectId(JsonElement item)
        {
            if (!item.TryGetProperty("repository", out var repo) || repo.ValueKind != JsonValueKind.Object)
                return string.Empty;

            if (!repo.TryGetProperty("project", out var project) || project.ValueKind != JsonValueKind.Object)
                return string.Empty;

            return ReadString(project, "id") ?? string.Empty;
        }

        public static string ReadCommit(JsonElement item, string name)
        {
            return item.TryGetProperty(name, out var commit) && commit.ValueKind == JsonValueKind.Object
                ? ReadString(commit, "commitId") ?? string.Empty
                : string.Empty;
        }

        /// <summary>
        ///     Every reviewer's vote, when the list carries them. An answer without a
        ///     reviewers array yields none, and none means the approval state stays unknown
        ///     rather than reading as "nobody approved".
        /// </summary>
        public static List<int> ReadVotes(JsonElement item)
        {
            var votes = new List<int>();

            if (!item.TryGetProperty("reviewers", out var reviewers) || reviewers.ValueKind != JsonValueKind.Array)
                return votes;

            foreach (var reviewer in reviewers.EnumerateArray())
            {
                if (reviewer.ValueKind != JsonValueKind.Object)
                    continue;

                if (reviewer.TryGetProperty("vote", out var vote) && vote.ValueKind == JsonValueKind.Number)
                    votes.Add(vote.GetInt32());
            }

            return votes;
        }

        /// <summary>
        ///     The address the REST answer carries points back at the API. What a person wants
        ///     to open is the page, which has to be built.
        /// </summary>
        public static string BuildWebUrl(ForgeRepository repo, long id)
        {
            return $"https://{repo.Host}/{Esc(repo.Owner)}/{Esc(repo.Project)}/_git/{Esc(repo.Name)}/pullrequest/{id}";
        }

        /// <summary>
        ///     Azure DevOps says outright whether a request conflicts. Anything other than a
        ///     settled yes or no — still queued, still being worked out — is left unknown
        ///     rather than reported as clean.
        /// </summary>
        public static PullRequestMergeState ToMergeState(string mergeStatus)
        {
            if (string.IsNullOrEmpty(mergeStatus))
                return PullRequestMergeState.Unknown;

            if (mergeStatus.Equals("conflicts", StringComparison.OrdinalIgnoreCase))
                return PullRequestMergeState.Conflicting;

            if (mergeStatus.Equals("succeeded", StringComparison.OrdinalIgnoreCase))
                return PullRequestMergeState.Clean;

            return PullRequestMergeState.Unknown;
        }

        public static PullRequestState ToState(string status, bool isDraft)
        {
            // A draft is still active; the flag is what separates it, not the status.
            if (status.Equals("completed", StringComparison.OrdinalIgnoreCase))
                return PullRequestState.Merged;

            if (status.Equals("abandoned", StringComparison.OrdinalIgnoreCase))
                return PullRequestState.Closed;

            return isDraft ? PullRequestState.Draft : PullRequestState.Open;
        }

        public static string ShortBranch(string refName)
        {
            if (string.IsNullOrEmpty(refName))
                return string.Empty;

            return refName.StartsWith(HEADS, StringComparison.Ordinal) ? refName[HEADS.Length..] : refName;
        }

        private static string ReadString(JsonElement owner, string name)
        {
            return owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static string Esc(string segment) => Uri.EscapeDataString(segment);

        private const string HEADS = "refs/heads/";
        private const int PAGE_SIZE = 100;
        private const int MAX_PAGES = 10;
    }
}
