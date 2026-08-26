using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Models
{
    /// <summary>
    ///     Pull requests as Gitea and Forgejo tell them.
    ///
    ///     The answer is shaped almost exactly like GitHub's, but the paging is not: no Link
    ///     header comes back, so the pages are walked here by number, and a page shorter than
    ///     asked for is the last one.
    /// </summary>
    public class GiteaPullRequests : IPullRequestSource
    {
        public async Task<ForgeResult<List<PullRequest>>> ListAsync(
            ForgeAccount account,
            ForgeRepository repo,
            CancellationToken cancel)
        {
            var root = ForgeTransport.NormalizeBase(account?.Url);
            if (root == null || repo == null || string.IsNullOrEmpty(repo.Owner) || string.IsNullOrEmpty(repo.Name))
                return ForgeResult<List<PullRequest>>.Failure(ForgeStatus.BadAddress);

            var found = new List<PullRequest>();

            for (var page = 1; page <= MAX_PAGES; page++)
            {
                var reply = await ForgeTransport.GetAsync(account, BuildUrl(root, repo, page), cancel, ForgeTransport.LIST_TIMEOUT).ConfigureAwait(false);
                if (!reply.IsOk)
                    return ForgeResult<List<PullRequest>>.Failure(reply.Status, reply.Detail);

                var read = Parse(reply.Body, found);
                if (read < 0)
                    return ForgeResult<List<PullRequest>>.Failure(ForgeStatus.Unexpected, "unreadable answer");

                if (read < PAGE_SIZE)
                    return ForgeResult<List<PullRequest>>.Success(found);
            }

            return new ForgeResult<List<PullRequest>>(ForgeStatus.Ok, found, "truncated");
        }

        public static string BuildUrl(string root, ForgeRepository repo, int page)
        {
            return $"{root}/api/v1/repos/{Uri.EscapeDataString(repo.Owner)}/{Uri.EscapeDataString(repo.Name)}/pulls" +
                   $"?state=all&limit={PAGE_SIZE}&page={page}";
        }

        /// <summary>
        ///     Reads one page into <paramref name="into"/> and returns how many entries it
        ///     held, or -1 when the answer made no sense.
        /// </summary>
        public static int Parse(string body, List<PullRequest> into)
        {
            if (string.IsNullOrEmpty(body))
                return -1;

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return -1;

                var count = 0;
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    count++;

                    var pr = ReadOne(item);
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

        private static PullRequest ReadOne(JsonElement item)
        {
            if (item.ValueKind != JsonValueKind.Object)
                return null;

            if (!item.TryGetProperty("number", out var number) || number.ValueKind != JsonValueKind.Number)
                return null;

            var merged = item.TryGetProperty("merged", out var m) && m.ValueKind == JsonValueKind.True;
            var draft = item.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True;

            var author = string.Empty;
            if (item.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object)
                author = ReadString(user, "login") ?? string.Empty;

            var created = DateTime.MinValue;
            var createdText = ReadString(item, "created_at");
            if (createdText != null)
                DateTime.TryParse(createdText, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out created);

            return new PullRequest
            {
                Id = number.GetInt64(),
                Title = ReadString(item, "title") ?? string.Empty,
                Author = author,
                SourceBranch = ReadEnd(item, "head", "ref"),
                TargetBranch = ReadEnd(item, "base", "ref"),
                State = ToState(ReadString(item, "state") ?? string.Empty, draft, merged),
                Url = ReadString(item, "html_url") ?? string.Empty,
                CreatedAt = created,
                SourceRepository = ReadHeadRepository(item),
                Kind = ForgeKind.Gitea,
                MergeState = ReadMergeState(item),
            };
        }

        /// <summary>
        ///     Gitea says "closed" for both a merged request and an abandoned one, and tells
        ///     them apart with a flag of its own.
        /// </summary>
        public static PullRequestState ToState(string state, bool draft, bool merged)
        {
            if (merged)
                return PullRequestState.Merged;

            if (state.Equals("closed", StringComparison.OrdinalIgnoreCase))
                return PullRequestState.Closed;

            return draft ? PullRequestState.Draft : PullRequestState.Open;
        }

        /// <summary>
        ///     Only has_merge_conflicts is trusted, and it is often absent.
        ///
        ///     The tempting field is "mergeable", and it lies for this purpose: a draft
        ///     request reports mergeable=false with no conflict anywhere near it. Reading it
        ///     would paint a red warning on every draft in the tree.
        /// </summary>
        public static PullRequestMergeState ReadMergeState(JsonElement item)
        {
            if (!item.TryGetProperty("has_merge_conflicts", out var conflicts))
                return PullRequestMergeState.Unknown;

            return conflicts.ValueKind switch
            {
                JsonValueKind.True => PullRequestMergeState.Conflicting,
                JsonValueKind.False => PullRequestMergeState.Clean,
                _ => PullRequestMergeState.Unknown,
            };
        }

        private static string ReadHeadRepository(JsonElement item)
        {
            if (!item.TryGetProperty("head", out var head) || head.ValueKind != JsonValueKind.Object)
                return string.Empty;

            if (!head.TryGetProperty("repo", out var repo) || repo.ValueKind != JsonValueKind.Object)
                return string.Empty;

            return ReadString(repo, "full_name") ?? string.Empty;
        }

        private static string ReadEnd(JsonElement item, string side, string field)
        {
            return item.TryGetProperty(side, out var end) && end.ValueKind == JsonValueKind.Object
                ? ReadString(end, field) ?? string.Empty
                : string.Empty;
        }

        private static string ReadString(JsonElement owner, string name)
        {
            return owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private const int PAGE_SIZE = 50;
        private const int MAX_PAGES = 10;
    }
}
