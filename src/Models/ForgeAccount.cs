using System;
using System.Text.Json.Serialization;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.Models
{
    /// <summary>
    ///     Credentials for one forge, declared rather than guessed.
    ///
    ///     A host is not an identity: every Azure DevOps organisation on earth answers to
    ///     dev.azure.com, and a token issued for one of them says nothing about the next. So an
    ///     account names the forge it talks to, the address it lives at, and how far its reach
    ///     goes — a whole host, one organisation, or a single project.
    ///
    ///     A token is stored either as itself or as the name of an environment variable holding
    ///     it, and the second is the default on purpose: an Azure DevOps token opens the source
    ///     of whoever issued it, and SourceGit keeps its preferences in plain text. Naming a
    ///     variable lets the secret stay out of the file.
    /// </summary>
    public class ForgeAccount : ObservableObject
    {
        /// <summary>
        ///     Which forge this is. Chosen when the account is created and never derived from
        ///     the address: a self-hosted GitLab answers to any name at all.
        ///
        ///     Written as a string so that inserting a forge in the middle of the enum does not
        ///     silently turn every stored account into its neighbour.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter<ForgeKind>))]
        public ForgeKind Kind
        {
            get => _kind;
            set
            {
                if (SetProperty(ref _kind, value))
                {
                    OnPropertyChanged(nameof(KindName));
                    OnPropertyChanged(nameof(SupportsProject));
                    OnPropertyChanged(nameof(Name));
                }
            }
        }

        /// <summary>
        ///     Where the forge lives: https://dev.azure.com, https://github.com, or the address
        ///     of a self-hosted instance. Editable, which is the whole point — GitHub
        ///     Enterprise, an on-premises Azure DevOps Server and a company GitLab differ from
        ///     their public counterparts by nothing else.
        /// </summary>
        public string Url
        {
            get => _url;
            set
            {
                if (SetProperty(ref _url, value))
                {
                    OnPropertyChanged(nameof(Host));
                    OnPropertyChanged(nameof(Name));
                }
            }
        }

        /// <summary>
        ///     The organisation on Azure DevOps, the owner or organisation on GitHub, the group
        ///     on GitLab. Empty means the account covers every repository on the host.
        /// </summary>
        public string Organization
        {
            get => _organization;
            set
            {
                if (SetProperty(ref _organization, value))
                    OnPropertyChanged(nameof(Name));
            }
        }

        /// <summary>
        ///     Azure DevOps files repositories under organisation / project / repository, and
        ///     is alone in doing so. Empty means every project of the organisation.
        /// </summary>
        public string Project
        {
            get => _project;
            set
            {
                if (SetProperty(ref _project, value))
                    OnPropertyChanged(nameof(Name));
            }
        }

        /// <summary>
        ///     The token, or the name of the environment variable holding it, depending on
        ///     <see cref="ReadTokenFromEnv"/>.
        /// </summary>
        public string Token
        {
            get => _token;
            set
            {
                if (SetProperty(ref _token, value))
                    OnPropertyChanged(nameof(Description));
            }
        }

        public bool ReadTokenFromEnv
        {
            get => _readTokenFromEnv;
            set
            {
                if (SetProperty(ref _readTokenFromEnv, value))
                    OnPropertyChanged(nameof(Description));
            }
        }

        /// <summary>
        ///     The host part of <see cref="Url"/>, which is what a remote URL offers to match
        ///     against. An address typed without a scheme still yields one.
        /// </summary>
        [JsonIgnore]
        public string Host
        {
            get
            {
                var url = _url?.Trim() ?? string.Empty;
                if (url.Length == 0)
                    return string.Empty;

                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
                    return uri.Host;

                var scheme = url.IndexOf("://", StringComparison.Ordinal);
                if (scheme >= 0)
                    url = url[(scheme + 3)..];

                var end = url.IndexOfAny(SEPARATORS);
                return end >= 0 ? url[..end] : url;
            }
        }

        [JsonIgnore]
        public bool SupportsProject => _kind == ForgeKind.AzureDevOps;

        [JsonIgnore]
        public string KindName => NameOf(_kind);

        /// <summary>
        ///     What to call this account in a list: the narrowest thing it is bound to.
        /// </summary>
        [JsonIgnore]
        public string Name
        {
            get
            {
                var org = _organization?.Trim() ?? string.Empty;
                var project = _project?.Trim() ?? string.Empty;
                var host = Host;

                if (org.Length == 0)
                    return host.Length == 0 ? KindName : host;

                if (project.Length == 0 || !SupportsProject)
                    return org;

                return $"{org}/{project}";
            }
        }

        /// <summary>
        ///     One line saying where the token comes from, without ever showing it. An
        ///     unresolved variable is worth naming: it is the difference between "no pull
        ///     requests" and "I could not ask".
        /// </summary>
        [JsonIgnore]
        public string Description
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_token))
                    return "no token";

                if (!_readTokenFromEnv)
                    return "token stored here";

                return Environment.GetEnvironmentVariable(_token) is { Length: > 0 }
                    ? $"${_token}"
                    : $"${_token} — not set";
            }
        }

        /// <summary>
        ///     The token to send, or null when there is nothing usable. Read on every call
        ///     rather than cached: an environment variable can be set after startup, and a
        ///     stale null would keep an account silent for the rest of the session.
        /// </summary>
        public string ResolveToken()
        {
            if (string.IsNullOrWhiteSpace(_token))
                return null;

            if (!_readTokenFromEnv)
                return _token;

            var value = Environment.GetEnvironmentVariable(_token);
            return string.IsNullOrEmpty(value) ? null : value;
        }

        /// <summary>
        ///     How well this account fits a repository: -1 when it does not, and otherwise the
        ///     number of things it had to agree on. The caller keeps the highest, so an account
        ///     narrowed to one project beats the one covering its whole organisation, which in
        ///     turn beats the one covering the host.
        /// </summary>
        public int Match(ForgeRepository repo)
        {
            if (repo == null || !string.Equals(Host, repo.Host, StringComparison.OrdinalIgnoreCase))
                return -1;

            var score = 0;

            var org = _organization?.Trim() ?? string.Empty;
            if (org.Length > 0)
            {
                if (!string.Equals(org, repo.Owner, StringComparison.OrdinalIgnoreCase))
                    return -1;

                score += 2;
            }

            var project = _project?.Trim() ?? string.Empty;
            if (project.Length > 0 && SupportsProject)
            {
                if (!string.Equals(project, repo.Project, StringComparison.OrdinalIgnoreCase))
                    return -1;

                score += 1;
            }

            return score;
        }

        /// <summary>
        ///     A new account pointing at the public instance of the chosen forge, which is what
        ///     most people want and what everyone else edits.
        /// </summary>
        public static ForgeAccount CreateFor(ForgeKind kind)
        {
            return new ForgeAccount() { Kind = kind, Url = DefaultUrlOf(kind) };
        }

        public static string NameOf(ForgeKind kind)
        {
            return kind switch
            {
                ForgeKind.AzureDevOps => "Azure DevOps",
                ForgeKind.GitHub => "GitHub",
                ForgeKind.GitLab => "GitLab",
                ForgeKind.Gitea => "Gitea",
                ForgeKind.Bitbucket => "Bitbucket",
                _ => "Unknown",
            };
        }

        public static string DefaultUrlOf(ForgeKind kind)
        {
            return kind switch
            {
                ForgeKind.AzureDevOps => "https://dev.azure.com",
                ForgeKind.GitHub => "https://github.com",
                ForgeKind.GitLab => "https://gitlab.com",
                ForgeKind.Gitea => "https://gitea.com",
                ForgeKind.Bitbucket => "https://bitbucket.org",
                _ => string.Empty,
            };
        }

        private static readonly char[] SEPARATORS = ['/', '?', '#'];

        private ForgeKind _kind = ForgeKind.Unknown;
        private string _url = string.Empty;
        private string _organization = string.Empty;
        private string _project = string.Empty;
        private string _token = string.Empty;
        private bool _readTokenFromEnv = true;
    }
}
