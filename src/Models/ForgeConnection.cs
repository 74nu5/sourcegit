using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Models
{
    /// <summary>
    ///     Asks a forge one question — "who am I?" — and reports what came back.
    ///
    ///     Everything about talking to a forge lives in <see cref="ForgeTransport"/>; what is
    ///     left here is the only thing that is really about testing a connection: knowing the
    ///     cheapest authenticated address each forge offers, and reading a name out of the
    ///     answer.
    ///
    ///     It exists so that a wrong token is found while the user is looking at the field,
    ///     rather than later, as a badge that silently never appears.
    /// </summary>
    public static class ForgeConnection
    {
        public static async Task<ForgeResult<string>> TestAsync(ForgeAccount account, CancellationToken cancel)
        {
            if (account == null)
                return ForgeResult<string>.Failure(ForgeStatus.Unexpected);

            // Anonymous requests are the transport's business to allow; a connection test has
            // nothing to prove without credentials.
            if (string.IsNullOrEmpty(account.ResolveToken()))
                return ForgeResult<string>.Failure(ForgeStatus.NoToken);

            var root = ForgeTransport.NormalizeBase(account.Url);
            if (root == null)
                return ForgeResult<string>.Failure(ForgeStatus.BadAddress);

            // Azure DevOps has no address to ask "who am I" at: everything hangs below an
            // organisation. Testing without one would prove nothing about the token.
            var org = account.Organization?.Trim() ?? string.Empty;
            if (account.Kind == ForgeKind.AzureDevOps && org.Length == 0)
                return ForgeResult<string>.Failure(ForgeStatus.NeedsOrganization);

            var endpoint = BuildProbeEndpoint(account, root, org);
            if (endpoint == null)
                return ForgeResult<string>.Failure(ForgeStatus.BadAddress);

            var reply = await ForgeTransport.GetAsync(account, endpoint, cancel).ConfigureAwait(false);
            if (!reply.IsOk)
                return ForgeResult<string>.Failure(reply.Status, reply.Detail);

            return ForgeResult<string>.Success(ReadIdentity(account.Kind, reply.Body));
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

        private static readonly string[] IDENTITY_FIELDS = ["login", "username", "display_name", "name", "nickname"];
    }
}
