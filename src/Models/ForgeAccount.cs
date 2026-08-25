using System;
using System.Text.Json.Serialization;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.Models
{
    /// <summary>
    ///     Credentials for one forge host.
    ///
    ///     A token is stored either as itself or as the name of an environment variable
    ///     holding it, and the second is the default on purpose: an Azure DevOps token opens
    ///     the source of whoever issued it, and SourceGit keeps its preferences in plain text.
    ///     Naming a variable lets the secret stay out of the file entirely.
    /// </summary>
    public class ForgeAccount : ObservableObject
    {
        /// <summary>
        ///     Host as it appears in the remote URL: dev.azure.com, github.com, or the address
        ///     of a self-hosted instance.
        /// </summary>
        public string Host
        {
            get => _host;
            set
            {
                if (SetProperty(ref _host, value))
                {
                    OnPropertyChanged(nameof(Kind));
                    OnPropertyChanged(nameof(Description));
                }
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

        [JsonIgnore]
        public ForgeKind Kind => Forge.KindOf(_host ?? string.Empty);

        /// <summary>
        ///     One line for the list, saying where the token comes from without ever showing
        ///     it. An unresolved variable is worth naming: it is the difference between "no
        ///     pull requests" and "I could not ask".
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

        private string _host = string.Empty;
        private string _token = string.Empty;
        private bool _readTokenFromEnv = true;
    }
}
