using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Models
{
    /// <summary>
    ///     Pull requests as GitHub tells them.
    ///
    ///     Written alongside the Azure DevOps one rather than after it, because a public
    ///     repository answers this endpoint without a token — which makes it the only way to
    ///     prove the whole chain, from request to badge, against a forge that really exists.
    ///
    ///     It also happens to be the forge this very repository lives on.
    /// </summary>
    public class GitHubPullRequests : IPullRequestSource
    {
        public async Task<ForgeResult<List<PullRequest>>> ListAsync(
            ForgeAccount account,
            ForgeRepository repo,
            CancellationToken cancel)
        {
            var root = ForgeTransport.NormalizeBase(account?.Url);
            if (root == null || repo == null || string.IsNullOrEmpty(repo.Owner) || string.IsNullOrEmpty(repo.Name))
                return ForgeResult<List<PullRequest>>.Failure(ForgeStatus.BadAddress);

            var pages = await ForgeTransport.GetPagesAsync(account, BuildUrl(ApiRoot(repo.Host, root), repo), cancel, MAX_PAGES)
                .ConfigureAwait(false);

            if (!pages.IsOk)
                return ForgeResult<List<PullRequest>>.Failure(pages);

            var found = new List<PullRequest>();
            foreach (var page in pages.Value)
            {
                if (Parse(page, found) < 0)
                    return ForgeResult<List<PullRequest>>.Failure(ForgeStatus.Unexpected, "unreadable answer");
            }

            return new ForgeResult<List<PullRequest>>(ForgeStatus.Ok, found, pages.Detail);
        }

        /// <summary>
        ///     github.com serves its API from another host entirely; an Enterprise server
        ///     serves it from a path on its own.
        /// </summary>
        public static string ApiRoot(string host, string root)
        {
            return host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                ? "https://api.github.com"
                : $"{root}/api/v3";
        }

        public static string BuildUrl(string apiRoot, ForgeRepository repo)
        {
            return $"{apiRoot}/repos/{Uri.EscapeDataString(repo.Owner)}/{Uri.EscapeDataString(repo.Name)}/pulls" +
                   "?state=all&per_page=100&sort=updated&direction=desc";
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

            var merged = item.TryGetProperty("merged_at", out var mergedAt) && mergedAt.ValueKind == JsonValueKind.String;
            var isDraft = item.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True;
            var state = ReadString(item, "state") ?? string.Empty;

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
                AuthorId = author,
                SourceBranch = ReadRef(item, "head"),
                TargetBranch = ReadRef(item, "base"),
                SourceRepository = ReadHeadRepository(item),
                Kind = ForgeKind.GitHub,

                // MergeState stays unknown on purpose: GitHub leaves "mergeable" out of the
                // list entirely and only fills it in when a request is asked for by itself,
                // which would cost one call per request.
                State = ToState(state, isDraft, merged),
                Url = ReadString(item, "html_url") ?? string.Empty,
                CreatedAt = created,
            };
        }

        /// <summary>
        ///     GitHub says "closed" for both a merged pull request and an abandoned one, and
        ///     tells them apart only by whether merged_at is filled in.
        /// </summary>
        public static PullRequestState ToState(string state, bool isDraft, bool merged)
        {
            if (merged)
                return PullRequestState.Merged;

            if (state.Equals("closed", StringComparison.OrdinalIgnoreCase))
                return PullRequestState.Closed;

            return isDraft ? PullRequestState.Draft : PullRequestState.Open;
        }

        /// <summary>
        ///     A pull request on GitHub usually comes from a fork, and head.repo names it.
        ///     A deleted fork leaves it null, and such a request can no longer be matched to
        ///     anything local.
        /// </summary>
        private static string ReadHeadRepository(JsonElement item)
        {
            if (!item.TryGetProperty("head", out var head) || head.ValueKind != JsonValueKind.Object)
                return string.Empty;

            if (!head.TryGetProperty("repo", out var repo) || repo.ValueKind != JsonValueKind.Object)
                return string.Empty;

            return ReadString(repo, "full_name") ?? string.Empty;
        }

        private static string ReadRef(JsonElement item, string side)
        {
            return item.TryGetProperty(side, out var end) && end.ValueKind == JsonValueKind.Object
                ? ReadString(end, "ref") ?? string.Empty
                : string.Empty;
        }

        private static string ReadString(JsonElement owner, string name)
        {
            return owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private const int MAX_PAGES = 5;
    }
}
