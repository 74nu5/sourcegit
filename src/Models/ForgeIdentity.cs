using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Models
{
    /// <summary>
    ///     Who the token belongs to, in the several names a forge may know them by.
    ///
    ///     All three are kept because forges disagree about which one appears on a pull
    ///     request: GitHub writes the login, Azure DevOps the display name, and its unique
    ///     name is an address. Matching on any of them is how "mine" stays true across all
    ///     five.
    /// </summary>
    public record ForgeUser(string Login, string DisplayName, string Email)
    {
        /// <summary>
        ///     Whether a pull request's author is this person.
        /// </summary>
        public bool Wrote(PullRequest pr)
        {
            if (pr == null)
                return false;

            return Same(pr.AuthorId) || Same(pr.Author);
        }

        private bool Same(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return false;

            return Equals(candidate, Login) || Equals(candidate, DisplayName) || Equals(candidate, Email);
        }

        private static bool Equals(string a, string b)
        {
            return !string.IsNullOrWhiteSpace(b) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    ///     The second capability, sitting beside pull requests rather than inside them.
    ///
    ///     A forge implements what it supports and no more, which is why this is its own
    ///     interface: a forge could answer for pull requests and not for identity, and
    ///     nothing above would have to care.
    /// </summary>
    public interface IForgeIdentity
    {
        Task<ForgeResult<ForgeUser>> WhoAmIAsync(ForgeAccount account, CancellationToken cancel);
    }

    /// <summary>
    ///     Who am I, on each of the five.
    ///
    ///     All of them answer the same shape of question — one GET, two or three strings out
    ///     of the answer — so they are written together rather than one class apiece. Azure
    ///     DevOps is the odd one: its identity lives on another host entirely, not under the
    ///     organisation, and there is no equivalent for a server on premises.
    /// </summary>
    public class ForgeIdentity : IForgeIdentity
    {
        public async Task<ForgeResult<ForgeUser>> WhoAmIAsync(ForgeAccount account, CancellationToken cancel)
        {
            if (account == null)
                return ForgeResult<ForgeUser>.Failure(ForgeStatus.Unexpected);

            if (string.IsNullOrEmpty(account.ResolveToken()))
                return ForgeResult<ForgeUser>.Failure(ForgeStatus.NoToken);

            var root = ForgeTransport.NormalizeBase(account.Url);
            if (root == null)
                return ForgeResult<ForgeUser>.Failure(ForgeStatus.BadAddress);

            var endpoint = BuildUrl(account.Kind, account.Host, root);
            if (endpoint == null)
                return ForgeResult<ForgeUser>.Failure(ForgeStatus.NotFound, "no identity endpoint for this forge");

            var reply = await ForgeTransport.GetAsync(account, endpoint, cancel).ConfigureAwait(false);
            if (!reply.IsOk)
                return ForgeResult<ForgeUser>.Failure(reply.Status, reply.Detail);

            var user = Parse(account.Kind, reply.Body);
            return user == null
                ? ForgeResult<ForgeUser>.Failure(ForgeStatus.Unexpected, "unreadable answer")
                : ForgeResult<ForgeUser>.Success(user);
        }

        public static string BuildUrl(ForgeKind kind, string host, string root)
        {
            switch (kind)
            {
                case ForgeKind.AzureDevOps:
                    // Not under the organisation, and not on dev.azure.com either. A server on
                    // premises has no such service, so it simply cannot say.
                    return host.EndsWith("visualstudio.com", StringComparison.OrdinalIgnoreCase) ||
                           host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase)
                        ? "https://app.vssps.visualstudio.com/_apis/profile/profiles/me?api-version=7.1"
                        : null;

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
                        : null;

                default:
                    return null;
            }
        }

        public static ForgeUser Parse(ForgeKind kind, string body)
        {
            if (string.IsNullOrEmpty(body))
                return null;

            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return null;

                var user = kind switch
                {
                    ForgeKind.AzureDevOps => new ForgeUser(
                        Read(root, "publicAlias"),
                        Read(root, "displayName"),
                        Read(root, "emailAddress")),

                    ForgeKind.GitLab => new ForgeUser(
                        Read(root, "username"),
                        Read(root, "name"),
                        Read(root, "email")),

                    ForgeKind.Bitbucket => new ForgeUser(
                        Read(root, "nickname"),
                        Read(root, "display_name"),
                        Read(root, "account_id")),

                    // GitHub and Gitea answer alike.
                    _ => new ForgeUser(
                        Read(root, "login"),
                        Read(root, "name") ?? Read(root, "full_name"),
                        Read(root, "email")),
                };

                var known = !string.IsNullOrWhiteSpace(user.Login) ||
                            !string.IsNullOrWhiteSpace(user.DisplayName) ||
                            !string.IsNullOrWhiteSpace(user.Email);

                return known ? user : null;
            }
            catch
            {
                return null;
            }
        }

        private static string Read(JsonElement owner, string name)
        {
            return owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
    }

    /// <summary>
    ///     The way in, with the same cache discipline as everything else.
    ///
    ///     An identity changes about as often as a person changes name, so it is held for an
    ///     hour; a refusal is remembered for a minute, because a wrong token would otherwise
    ///     be re-asked every time a list is drawn.
    /// </summary>
    public static class ForgeIdentityService
    {
        public static async Task<ForgeCached<ForgeUser>> WhoAmIAsync(ForgeAccount account, CancellationToken cancel)
        {
            if (account == null)
                return new ForgeCached<ForgeUser>(ForgeResult<ForgeUser>.Failure(ForgeStatus.Unexpected), false, DateTime.UtcNow);

            var key = $"{account.Kind}|{account.Host}|{account.Organization}";
            return await CACHE.GetAsync(key, token => SOURCE.WhoAmIAsync(account, token), cancel).ConfigureAwait(false);
        }

        public static void Clear() => CACHE.Clear();

        private static readonly IForgeIdentity SOURCE = new ForgeIdentity();
        private static readonly ForgeCache<ForgeUser> CACHE = new(TimeSpan.FromHours(1), TimeSpan.FromMinutes(1));
    }
}
