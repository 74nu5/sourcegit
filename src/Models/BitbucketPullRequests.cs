using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Models
{
    /// <summary>
    ///     Pull requests as Bitbucket Cloud tells them.
    ///
    ///     Only the hosted service. Bitbucket Data Center speaks a different API entirely —
    ///     /rest/api/1.0 rather than /2.0, with its own shapes — and guessing at it without an
    ///     instance to try would be writing fiction.
    ///
    ///     Every state has to be asked for by name: left alone, Bitbucket answers with the
    ///     open ones only.
    /// </summary>
    public class BitbucketPullRequests : IPullRequestSource
    {
        public async Task<ForgeResult<List<PullRequest>>> ListAsync(
            ForgeAccount account,
            ForgeRepository repo,
            CancellationToken cancel)
        {
            var root = ForgeTransport.NormalizeBase(account?.Url);
            if (root == null || repo == null || string.IsNullOrEmpty(repo.Owner) || string.IsNullOrEmpty(repo.Name))
                return ForgeResult<List<PullRequest>>.Failure(ForgeStatus.BadAddress);

            if (!repo.Host.Equals("bitbucket.org", StringComparison.OrdinalIgnoreCase))
                return ForgeResult<List<PullRequest>>.Failure(ForgeStatus.NotFound, "Bitbucket Data Center is not supported");

            var pages = await ForgeTransport.GetPagesAsync(account, BuildUrl(repo), cancel, MAX_PAGES)
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

        public static string BuildUrl(ForgeRepository repo)
        {
            return $"https://api.bitbucket.org/2.0/repositories/{Uri.EscapeDataString(repo.Owner)}/{Uri.EscapeDataString(repo.Name)}/pullrequests" +
                   "?state=OPEN&state=MERGED&state=DECLINED&state=SUPERSEDED&pagelen=50";
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
                if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                    !doc.RootElement.TryGetProperty("values", out var values) ||
                    values.ValueKind != JsonValueKind.Array)
                    return -1;

                var count = 0;
                foreach (var item in values.EnumerateArray())
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

            if (!item.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number)
                return null;

            var author = string.Empty;
            var authorId = string.Empty;
            if (item.TryGetProperty("author", out var who) && who.ValueKind == JsonValueKind.Object)
            {
                author = ReadString(who, "display_name") ?? ReadString(who, "nickname") ?? string.Empty;
                authorId = ReadString(who, "nickname") ?? ReadString(who, "account_id") ?? string.Empty;
            }

            var created = DateTime.MinValue;
            var createdText = ReadString(item, "created_on");
            if (createdText != null)
                DateTime.TryParse(createdText, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out created);

            return new PullRequest
            {
                Id = id.GetInt64(),
                Title = ReadString(item, "title") ?? string.Empty,
                Author = author,
                AuthorId = authorId,
                SourceBranch = ReadBranch(item, "source"),
                TargetBranch = ReadBranch(item, "destination"),
                State = ToState(ReadString(item, "state") ?? string.Empty),
                Url = ReadWebUrl(item),
                CreatedAt = created,
                SourceRepository = ReadSourceRepository(item),
                Kind = ForgeKind.Bitbucket,

                // MergeState stays unknown: Bitbucket says nothing about conflicts here.
            };
        }

        /// <summary>
        ///     Bitbucket has no notion of a draft, so a request is only ever open, merged, or
        ///     given up on. Superseded means a newer request replaced it, which for a branch
        ///     indicator is the same as closed.
        /// </summary>
        public static PullRequestState ToState(string state)
        {
            if (state.Equals("MERGED", StringComparison.OrdinalIgnoreCase))
                return PullRequestState.Merged;

            if (state.Equals("DECLINED", StringComparison.OrdinalIgnoreCase) ||
                state.Equals("SUPERSEDED", StringComparison.OrdinalIgnoreCase))
                return PullRequestState.Closed;

            return PullRequestState.Open;
        }

        private static string ReadBranch(JsonElement item, string side)
        {
            if (!item.TryGetProperty(side, out var end) || end.ValueKind != JsonValueKind.Object)
                return string.Empty;

            if (!end.TryGetProperty("branch", out var branch) || branch.ValueKind != JsonValueKind.Object)
                return string.Empty;

            return ReadString(branch, "name") ?? string.Empty;
        }

        private static string ReadSourceRepository(JsonElement item)
        {
            if (!item.TryGetProperty("source", out var source) || source.ValueKind != JsonValueKind.Object)
                return string.Empty;

            if (!source.TryGetProperty("repository", out var repo) || repo.ValueKind != JsonValueKind.Object)
                return string.Empty;

            return ReadString(repo, "full_name") ?? string.Empty;
        }

        private static string ReadWebUrl(JsonElement item)
        {
            if (!item.TryGetProperty("links", out var links) || links.ValueKind != JsonValueKind.Object)
                return string.Empty;

            if (!links.TryGetProperty("html", out var html) || html.ValueKind != JsonValueKind.Object)
                return string.Empty;

            return ReadString(html, "href") ?? string.Empty;
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
