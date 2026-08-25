using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Models
{
    /// <summary>
    ///     Merge requests as GitLab tells them.
    ///
    ///     A project is addressed by its full path, escaped whole — "group/sub/repo" becomes
    ///     one path segment — which is how GitLab lets a repository be named without knowing
    ///     its numeric id.
    /// </summary>
    public class GitLabMergeRequests : IPullRequestSource
    {
        public async Task<ForgeResult<List<PullRequest>>> ListAsync(
            ForgeAccount account,
            ForgeRepository repo,
            CancellationToken cancel)
        {
            var root = ForgeTransport.NormalizeBase(account?.Url);
            if (root == null || repo == null || string.IsNullOrEmpty(repo.Owner) || string.IsNullOrEmpty(repo.Name))
                return ForgeResult<List<PullRequest>>.Failure(ForgeStatus.BadAddress);

            var pages = await ForgeTransport.GetPagesAsync(account, BuildUrl(root, repo), cancel, MAX_PAGES)
                .ConfigureAwait(false);

            if (!pages.IsOk)
                return ForgeResult<List<PullRequest>>.Failure(pages);

            var found = new List<PullRequest>();
            foreach (var page in pages.Value)
            {
                if (Parse(page, repo, found) < 0)
                    return ForgeResult<List<PullRequest>>.Failure(ForgeStatus.Unexpected, "unreadable answer");
            }

            return new ForgeResult<List<PullRequest>>(ForgeStatus.Ok, found, pages.Detail);
        }

        public static string BuildUrl(string root, ForgeRepository repo)
        {
            return $"{root}/api/v4/projects/{Uri.EscapeDataString(repo.FullName)}/merge_requests" +
                   "?state=all&per_page=100&order_by=created_at&sort=desc";
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
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return -1;

                var count = 0;
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    count++;

                    var mr = ReadOne(item, repo);
                    if (mr != null)
                        into.Add(mr);
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

            // iid is the number a human sees; id is unique across the whole instance.
            if (!item.TryGetProperty("iid", out var iid) || iid.ValueKind != JsonValueKind.Number)
                return null;

            var draft = item.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True;
            if (!draft)
                draft = item.TryGetProperty("work_in_progress", out var wip) && wip.ValueKind == JsonValueKind.True;

            var author = string.Empty;
            if (item.TryGetProperty("author", out var who) && who.ValueKind == JsonValueKind.Object)
                author = ReadString(who, "username") ?? ReadString(who, "name") ?? string.Empty;

            var created = DateTime.MinValue;
            var createdText = ReadString(item, "created_at");
            if (createdText != null)
                DateTime.TryParse(createdText, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out created);

            return new PullRequest
            {
                Id = iid.GetInt64(),
                Title = ReadString(item, "title") ?? string.Empty,
                Author = author,
                SourceBranch = ReadString(item, "source_branch") ?? string.Empty,
                TargetBranch = ReadString(item, "target_branch") ?? string.Empty,
                State = ToState(ReadString(item, "state") ?? string.Empty, draft),
                Url = ReadString(item, "web_url") ?? string.Empty,
                CreatedAt = created,
                SourceRepository = ReadSourceProject(item, repo),
            };
        }

        /// <summary>
        ///     GitLab names the source project by number, not by path, so there is nothing to
        ///     compare a remote against. What can be said is whether it is this project: when
        ///     it is not, a name is returned that no repository can ever match, which keeps a
        ///     fork's merge request from lighting up a branch of the same name here.
        /// </summary>
        public static string ReadSourceProject(JsonElement item, ForgeRepository repo)
        {
            var source = ReadNumber(item, "source_project_id");
            var target = ReadNumber(item, "target_project_id") ?? ReadNumber(item, "project_id");

            if (source == null || target == null)
                return string.Empty;

            return source == target ? repo.FullName : $"gitlab-project:{source}";
        }

        public static PullRequestState ToState(string state, bool draft)
        {
            if (state.Equals("merged", StringComparison.OrdinalIgnoreCase))
                return PullRequestState.Merged;

            if (state.Equals("closed", StringComparison.OrdinalIgnoreCase))
                return PullRequestState.Closed;

            // "locked" is an open request whose discussion is frozen.
            return draft ? PullRequestState.Draft : PullRequestState.Open;
        }

        private static long? ReadNumber(JsonElement owner, string name)
        {
            return owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
                ? value.GetInt64()
                : null;
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
