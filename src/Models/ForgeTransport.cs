using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Models
{
    /// <summary>
    ///     How a conversation with a forge ended. Deliberately coarse: the user can act on
    ///     "the token was refused" and on "the host did not answer", and on nothing finer.
    ///     The view turns these into sentences, so the model never learns what language the
    ///     user reads.
    /// </summary>
    public enum ForgeStatus
    {
        Ok,
        NoToken,
        NeedsOrganization,
        BadAddress,
        Unauthorized,
        Forbidden,
        NotFound,
        Unreachable,
        Timeout,
        Cancelled,
        Unexpected,
    }

    /// <summary>
    ///     A value, or the reason there is none. Nothing in this layer throws at its caller:
    ///     a forge being down is an ordinary Tuesday, not an exception.
    /// </summary>
    public record ForgeResult<T>(ForgeStatus Status, T Value, string Detail)
    {
        public bool IsOk => Status == ForgeStatus.Ok;

        public static ForgeResult<T> Success(T value) => new(ForgeStatus.Ok, value, null);

        public static ForgeResult<T> Failure(ForgeStatus status, string detail = null) => new(status, default, detail);

        /// <summary>
        ///     Carries a failure across a change of type, so a caller mapping bodies to
        ///     objects does not have to restate every reason a request can fail.
        /// </summary>
        public static ForgeResult<T> Failure<TOther>(ForgeResult<TOther> other) => new(other.Status, default, other.Detail);
    }

    /// <summary>
    ///     One answer: its body, and the headers, which is where most forges hide the address
    ///     of the next page.
    /// </summary>
    public sealed class ForgeReply
    {
        public ForgeStatus Status { get; init; } = ForgeStatus.Unexpected;
        public string Detail { get; init; }
        public string Body { get; init; }
        public HttpResponseHeaders Headers { get; init; }

        public bool IsOk => Status == ForgeStatus.Ok;
    }

    /// <summary>
    ///     The one road to a forge.
    ///
    ///     Everything above it — the connection test, and the connectors after it — asks its
    ///     questions here, so that authentication, timeouts, paging and the classification of
    ///     failures are written once and behave the same everywhere.
    ///
    ///     Nothing here starts on its own. A request happens because something above asked
    ///     for one, which is what keeps this fork silent for anyone who configures no account.
    /// </summary>
    public static class ForgeTransport
    {
        /// <summary>
        ///     For something a person is waiting on, like the connection test: long enough for
        ///     a slow forge, short enough that a wedged proxy does not hold a panel hostage.
        /// </summary>
        public static readonly TimeSpan DEFAULT_TIMEOUT = TimeSpan.FromSeconds(15);

        /// <summary>
        ///     For listing, which nobody is watching happen.
        ///
        ///     Fifteen seconds was a guess and the guess was wrong: codeberg.org, a real and
        ///     ordinary Gitea instance, takes seventeen to return one page of pull requests.
        ///     A badge that never appears on a slow forge is worse than one that appears late.
        /// </summary>
        public static readonly TimeSpan LIST_TIMEOUT = TimeSpan.FromSeconds(45);

        /// <summary>
        ///     A walk through pages stops here rather than following a forge into a loop. The
        ///     caller is told it was cut short; a silent truncation would read as a complete
        ///     answer.
        /// </summary>
        public const int DEFAULT_MAX_PAGES = 20;

        public static async Task<ForgeReply> GetAsync(ForgeAccount account, string url, CancellationToken cancel, TimeSpan? timeout = null)
        {
            if (account == null || string.IsNullOrEmpty(url))
                return new ForgeReply { Status = ForgeStatus.BadAddress };

            var gate = GateFor(account.Host);
            var token = account.ResolveToken();

            // Two requests at a time per host: enough to keep a panel responsive, few enough
            // that a burst never looks like an attack to a rate limiter.
            try
            {
                await gate.WaitAsync(cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new ForgeReply { Status = ForgeStatus.Cancelled };
            }

            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                deadline.CancelAfter(timeout ?? DEFAULT_TIMEOUT);

                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                Authenticate(req, account.Kind, token);

                var started = System.Diagnostics.Stopwatch.StartNew();
                using var rsp = await HTTP.SendAsync(req, HttpCompletionOption.ResponseContentRead, deadline.Token).ConfigureAwait(false);
                var body = await rsp.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false);

                var media = rsp.Content.Headers.ContentType?.MediaType ?? string.Empty;
                var status = Classify(rsp.StatusCode, media);

                // A token never appears in an address, so this can be written down whole.
                ForgeLog.Line($"GET {url} -> {status} ({(int)rsp.StatusCode}) {started.ElapsedMilliseconds}ms {body?.Length ?? 0}B");

                // A refusal that does not say why costs an afternoon. Forges explain
                // themselves in the body; the first line of it is enough and is never long
                // enough to be a secret.
                if (status != ForgeStatus.Ok && !string.IsNullOrEmpty(body))
                    ForgeLog.Line($"     said: {Excerpt(body)}");

                return new ForgeReply
                {
                    Status = status,
                    Detail = status == ForgeStatus.Ok ? null : $"HTTP {(int)rsp.StatusCode}",
                    Body = body,
                    Headers = rsp.Headers,
                };
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                ForgeLog.Line($"GET {url} -> abandoned");
                return new ForgeReply { Status = ForgeStatus.Cancelled };
            }
            catch (OperationCanceledException)
            {
                ForgeLog.Line($"GET {url} -> timed out after {DEFAULT_TIMEOUT.TotalSeconds:0}s");
                return new ForgeReply { Status = ForgeStatus.Timeout };
            }
            catch (HttpRequestException e)
            {
                ForgeLog.Failed($"GET {url}", e);
                return new ForgeReply { Status = ForgeStatus.Unreachable, Detail = e.Message };
            }
            catch (Exception e)
            {
                ForgeLog.Failed($"GET {url}", e);
                return new ForgeReply { Status = ForgeStatus.Unexpected, Detail = e.Message };
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        ///     Follows a forge from page to page and hands back every body it read.
        ///
        ///     Whether the walk was complete is part of the answer: <paramref name="complete"/>
        ///     is false when the page budget ran out, so a caller can say "the first four
        ///     hundred" rather than implying it saw them all.
        /// </summary>
        public static async Task<ForgeResult<List<string>>> GetPagesAsync(
            ForgeAccount account,
            string firstUrl,
            CancellationToken cancel,
            int maxPages = DEFAULT_MAX_PAGES,
            TimeSpan? timeout = null)
        {
            var pages = new List<string>();
            var url = firstUrl;

            for (var i = 0; i < maxPages; i++)
            {
                var reply = await GetAsync(account, url, cancel, timeout ?? LIST_TIMEOUT).ConfigureAwait(false);
                if (!reply.IsOk)
                    return ForgeResult<List<string>>.Failure(reply.Status, reply.Detail);

                pages.Add(reply.Body);

                if (!TryGetNextPage(account.Kind, url, reply, out var next))
                    return ForgeResult<List<string>>.Success(pages);

                url = next;
            }

            return new ForgeResult<List<string>>(ForgeStatus.Ok, pages, TRUNCATED);
        }

        /// <summary>
        ///     True when the caller stopped short of the last page, which
        ///     <see cref="GetPagesAsync"/> reports rather than hides.
        /// </summary>
        /// <summary>
        ///     The first useful line of an error body, short enough for a log line. Never a
        ///     secret: a token travels in a header and is never echoed back in an answer.
        /// </summary>
        private static string Excerpt(string body)
        {
            var text = body.Replace('\n', ' ').Replace('\r', ' ').Trim();
            return text.Length > 200 ? text[..200] + "..." : text;
        }

        public static bool WasTruncated<T>(ForgeResult<T> result) => result.IsOk && result.Detail == TRUNCATED;

        /// <summary>
        ///     Each forge invented its own way of being told who is calling. An account with
        ///     no usable token still gets a request: public repositories answer anonymously,
        ///     and refusing to ask would be a policy this layer has no business holding.
        /// </summary>
        public static void Authenticate(HttpRequestMessage req, ForgeKind kind, string token)
        {
            if (!string.IsNullOrEmpty(token))
            {
                switch (kind)
                {
                    case ForgeKind.AzureDevOps:
                        // A personal access token goes in as the password of an empty user.
                        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{token}"));
                        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
                        break;

                    case ForgeKind.GitLab:
                        req.Headers.Add("PRIVATE-TOKEN", token);
                        break;

                    case ForgeKind.Gitea:
                        req.Headers.Authorization = new AuthenticationHeaderValue("token", token);
                        break;

                    default:
                        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                        break;
                }
            }

            // GitHub rejects a request without one, and every other forge ignores it.
            req.Headers.UserAgent.ParseAdd("SourceGit");
            req.Headers.Accept.ParseAdd("application/json");
        }

        /// <summary>
        ///     Where the next page lives, which every forge chose to say differently.
        ///
        ///     GitHub, Gitea and GitLab publish a Link header. Bitbucket puts the address in
        ///     the body. Azure DevOps hands back an opaque token in a header and expects it
        ///     appended to the same query.
        /// </summary>
        public static bool TryGetNextPage(ForgeKind kind, string current, ForgeReply reply, out string next)
        {
            next = null;

            if (reply?.Headers == null)
                return false;

            switch (kind)
            {
                case ForgeKind.AzureDevOps:
                    if (!reply.Headers.TryGetValues("x-ms-continuationtoken", out var tokens))
                        return false;

                    foreach (var value in tokens)
                    {
                        if (string.IsNullOrEmpty(value))
                            continue;

                        var glue = current.Contains('?', StringComparison.Ordinal) ? '&' : '?';
                        next = $"{current}{glue}continuationToken={Uri.EscapeDataString(value)}";
                        return true;
                    }

                    return false;

                case ForgeKind.Bitbucket:
                    next = ReadBitbucketNext(reply.Body);
                    return next != null;

                default:
                    if (!reply.Headers.TryGetValues("Link", out var links))
                        return false;

                    foreach (var value in links)
                    {
                        next = ReadLinkRelNext(value);
                        if (next != null)
                            return true;
                    }

                    return false;
            }
        }

        /// <summary>
        ///     An address the user typed, made usable: no trailing slash, and https assumed
        ///     when no scheme was given. Anything that is not http or https is refused rather
        ///     than handed to the network stack.
        /// </summary>
        public static string NormalizeBase(string url)
        {
            var text = (url ?? string.Empty).Trim().TrimEnd('/');
            if (text.Length == 0)
                return null;

            if (!text.Contains("://", StringComparison.Ordinal))
                text = $"https://{text}";

            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
                return null;

            var isWeb = uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                        uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);

            return isWeb && !string.IsNullOrEmpty(uri.Host) ? text : null;
        }

        /// <summary>
        ///     Azure DevOps answers a refused token with its sign-in page and a 203 rather
        ///     than a 401, so the status alone would read as success.
        /// </summary>
        private static ForgeStatus Classify(HttpStatusCode code, string mediaType)
        {
            if (code == HttpStatusCode.NonAuthoritativeInformation ||
                mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
                return ForgeStatus.Unauthorized;

            if ((int)code is >= 200 and < 300)
                return ForgeStatus.Ok;

            return code switch
            {
                HttpStatusCode.Unauthorized => ForgeStatus.Unauthorized,
                HttpStatusCode.Forbidden => ForgeStatus.Forbidden,
                HttpStatusCode.NotFound => ForgeStatus.NotFound,
                _ => ForgeStatus.Unexpected,
            };
        }

        /// <summary>
        ///     &lt;https://…?page=2&gt;; rel="next", &lt;https://…?page=9&gt;; rel="last"
        /// </summary>
        private static string ReadLinkRelNext(string header)
        {
            if (string.IsNullOrEmpty(header))
                return null;

            foreach (var part in header.Split(','))
            {
                var open = part.IndexOf('<');
                var close = part.IndexOf('>');
                if (open < 0 || close <= open)
                    continue;

                if (part.IndexOf("rel=\"next\"", StringComparison.OrdinalIgnoreCase) < 0 &&
                    part.IndexOf("rel=next", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var url = part.Substring(open + 1, close - open - 1).Trim();
                if (url.Length > 0)
                    return url;
            }

            return null;
        }

        private static string ReadBitbucketNext(string body)
        {
            if (string.IsNullOrEmpty(body))
                return null;

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                    return null;

                if (!doc.RootElement.TryGetProperty("next", out var next) ||
                    next.ValueKind != System.Text.Json.JsonValueKind.String)
                    return null;

                var url = next.GetString();
                return string.IsNullOrWhiteSpace(url) ? null : url;
            }
            catch
            {
                return null;
            }
        }

        private static SemaphoreSlim GateFor(string host)
        {
            return GATES.GetOrAdd(host ?? string.Empty, static _ => new SemaphoreSlim(2, 2));
        }

        private const string TRUNCATED = "truncated";

        /// <summary>
        ///     One client for the whole process. A fresh HttpClient per call leaks sockets in
        ///     the TIME_WAIT state, and this is meant to be called often. The per-request
        ///     deadline is a linked token rather than HttpClient.Timeout, which cannot tell a
        ///     timeout from a caller who walked away.
        /// </summary>
        private static readonly HttpClient HTTP = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

        private static readonly ConcurrentDictionary<string, SemaphoreSlim> GATES = new();
    }
}
