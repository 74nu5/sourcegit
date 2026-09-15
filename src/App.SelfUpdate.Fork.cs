using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace SourceGit
{
    public partial class App
    {
        /// <summary>
        ///     Asks this fork what its latest release is, rather than the project it was
        ///     branched from.
        ///
        ///     Upstream reads a JSON file from a site it publishes. This fork publishes no
        ///     site, and it does not need one: the GitHub releases API answers with exactly
        ///     the fields <see cref="Models.Version"/> declares, because the file upstream
        ///     hosts is itself derived from that API.
        ///
        ///     The User-Agent is not optional. GitHub refuses a request without one, with a
        ///     403 naming neither the header nor the reason -- which the caller would report
        ///     as an ordinary network failure.
        /// </summary>
        private static async Task<Models.Version> FetchLatestVersionAsync()
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SourceGit");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

            var data = await client.GetStringAsync(Models.ForkVersion.LATEST_RELEASE_API);
            return JsonSerializer.Deserialize(data, JsonCodeGen.Default.Version);
        }
    }
}
