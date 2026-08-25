using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Models
{
    /// <summary>
    ///     What a connection attempt found. The view turns these into sentences, so that the
    ///     model never has to know which language the user reads.
    /// </summary>
    public enum ForgeTestOutcome
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
        Unexpected,
    }

    /// <summary>
    ///     The outcome plus whatever is worth naming: who the forge said we are, the status it
    ///     answered with, the reason a socket refused. Never the token.
    /// </summary>
    public record ForgeTestResult(ForgeTestOutcome Outcome, string Detail)
    {
        public bool IsOk => Outcome == ForgeTestOutcome.Ok;
    }

    /// <summary>
    ///     Asks a forge one question — "who am I?" — and reports what came back.
    ///
    ///     This is the first thing that reaches the network, and it is deliberately narrow: one
    ///     request, no pagination, no cache, nothing running on its own. It exists so that a
    ///     wrong token is found here, where the user is looking at the field, rather than later
    ///     as a badge that silently never appears.
    ///
    ///     The per-forge knowledge it holds — where the API lives and how each one wants to be
    ///     told who is calling — is what the connectors will need next, which is why it lives
    ///     apart from the panel that calls it.
    /// </summary>
    public static class ForgeConnection
    {
        public static async Task<ForgeTestResult> TestAsync(ForgeAccount account, CancellationToken cancel)
        {
            if (account == null)
                return new ForgeTestResult(ForgeTestOutcome.Unexpected, null);

            var token = account.ResolveToken();
            if (string.IsNullOrEmpty(token))
                return new ForgeTestResult(ForgeTestOutcome.NoToken, null);

            var root = NormalizeBase(account.Url);
            if (root == null)
                return new ForgeTestResult(ForgeTestOutcome.BadAddress, null);

            // Azure DevOps has no address to ask "who am I" at: everything hangs below an
            // organisation. Testing without one would prove nothing about the token.
            var org = account.Organization?.Trim() ?? string.Empty;
            if (account.Kind == ForgeKind.AzureDevOps && org.Length == 0)
                return new ForgeTestResult(ForgeTestOutcome.NeedsOrganization, null);

            var endpoint = BuildProbeEndpoint(account, root, org);
            if (endpoint == null)
                return new ForgeTestResult(ForgeTestOutcome.BadAddress, null);

            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(15);

                using var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
                Authenticate(req, account.Kind, token);

                using var rsp = await client.SendAsync(req, cancel).ConfigureAwait(false);

                // Azure DevOps answers a bad token with its sign-in page and a 203 rather than
                // a 401, so the status alone would read as success.
                var media = rsp.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (rsp.StatusCode == HttpStatusCode.NonAuthoritativeInformation || media.Contains("html", StringComparison.OrdinalIgnoreCase))
                    return new ForgeTestResult(ForgeTestOutcome.Unauthorized, null);

                if (!rsp.IsSuccessStatusCode)
                {
                    var outcome = rsp.StatusCode switch
                    {
                        HttpStatusCode.Unauthorized => ForgeTestOutcome.Unauthorized,
                        HttpStatusCode.Forbidden => ForgeTestOutcome.Forbidden,
                        HttpStatusCode.NotFound => ForgeTestOutcome.NotFound,
                        _ => ForgeTestOutcome.Unexpected,
                    };

                    return new ForgeTestResult(outcome, $"HTTP {(int)rsp.StatusCode}");
                }

                var body = await rsp.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
                return new ForgeTestResult(ForgeTestOutcome.Ok, ReadIdentity(account.Kind, body));
            }
            catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
            {
                return new ForgeTestResult(ForgeTestOutcome.Timeout, null);
            }
            catch (HttpRequestException e)
            {
                return new ForgeTestResult(ForgeTestOutcome.Unreachable, e.Message);
            }
            catch (Exception e)
            {
                return new ForgeTestResult(ForgeTestOutcome.Unexpected, e.Message);
            }
        }

        /// <summary>
        ///     The cheapest authenticated address each forge offers.
        ///
        ///     Azure DevOps is the exception: it is asked for the very scope the account
        ///     declares, so a token issued for one project is proved against that project
        ///     rather than against an identity it may not be allowed to read.
        /// </summary>
        public static string BuildProbeEndpoint(ForgeAccount account, string root, string org)
        {
            var host = account.Host;
            var project = account.Project?.Trim() ?? string.Empty;

            switch (account.Kind)
            {
                case ForgeKind.AzureDevOps:
                    return project.Length > 0
                        ? $"{root}/{Uri.EscapeDataString(org)}/_apis/projects/{Uri.EscapeDataString(project)}?api-version=7.1"
                        : $"{root}/{Uri.EscapeDataString(org)}/_apis/projects?api-version=7.1&$top=1";

                case ForgeKind.GitHub:
                    return host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                        ? "https://api.github.com/user"
                        : $"{root}/api/v3/user";

                case ForgeKind.GitLab:
                    return $"{root}/api/v4/user";

                case ForgeKind.Gitea:
                    return $"{root}/api/v1/user";

                case ForgeKind.Bitbucket:
                    return host.Equals("bitbucket.org", StringComparison.OrdinalIgnoreCase)
                        ? "https://api.bitbucket.org/2.0/user"
                        : $"{root}/rest/api/1.0/users?limit=1";

                default:
                    return null;
            }
        }

        /// <summary>
        ///     Each forge invented its own way of being told who is calling.
        /// </summary>
        public static void Authenticate(HttpRequestMessage req, ForgeKind kind, string token)
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

            // GitHub rejects a request without one, and every other forge ignores it.
            req.Headers.UserAgent.ParseAdd("SourceGit");
            req.Headers.Accept.ParseAdd("application/json");
        }

        /// <summary>
        ///     Whatever the answer offers as a name, so the user can see they reached the
        ///     account they meant. Read with JsonDocument rather than a serializer: this runs
        ///     in an ahead-of-time compiled build, where reflection over unknown shapes does
        ///     not survive trimming.
        /// </summary>
        private static string ReadIdentity(ForgeKind kind, string body)
        {
            if (string.IsNullOrEmpty(body))
                return null;

            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return null;

                if (kind == ForgeKind.AzureDevOps)
                {
                    // A single project answers with its name; the listing answers with a count.
                    if (root.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                        return name.GetString();

                    if (root.TryGetProperty("count", out var count) && count.ValueKind == JsonValueKind.Number)
                        return count.GetInt32().ToString();

                    return null;
                }

                foreach (var candidate in IDENTITY_FIELDS)
                {
                    if (root.TryGetProperty(candidate, out var value) && value.ValueKind == JsonValueKind.String)
                    {
                        var text = value.GetString();
                        if (!string.IsNullOrWhiteSpace(text))
                            return text;
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        ///     An address the user typed, made usable: no trailing slash, and https assumed
        ///     when no scheme was given. Anything that is not http or https is refused rather
        ///     than handed to the network stack.
        /// </summary>
        private static string NormalizeBase(string url)
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

        private static readonly string[] IDENTITY_FIELDS = ["login", "username", "display_name", "name", "nickname"];
    }
}
