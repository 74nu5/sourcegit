using System;
using System.Diagnostics;
using System.IO;
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
            SweepPreviousUpdate();

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SourceGit");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

            var data = await client.GetStringAsync(Models.ForkVersion.LATEST_RELEASE_API);
            return JsonSerializer.Deserialize(data, JsonCodeGen.Default.Version);
        }

        /// <summary>
        ///     Quits this build and starts the one that has just replaced it.
        ///
        ///     The single-instance lock is released first, and that ordering is the whole
        ///     point. A second instance started while the lock is held does not run: it hands
        ///     its arguments to the instance already there and exits. Launch the new build
        ///     that way and the old one simply comes to the front, which reads as an update
        ///     that did nothing at all.
        ///
        ///     Disposing the channel here also disarms the one on desktop.Exit, which is why
        ///     the field is cleared rather than only closed.
        /// </summary>
        public static void RestartInto(string executablePath)
        {
            if (Current is not App app || string.IsNullOrEmpty(executablePath))
                return;

            app._ipcChannel?.Dispose();
            app._ipcChannel = null;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = executablePath,
                    WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty,
                    UseShellExecute = true,
                });
            }
            catch
            {
                // The files are already replaced, so this build is the stale one either way.
                // Quitting stays the right move; the user starts the new one themselves.
            }

            Quit(0);
        }

        /// <summary>
        ///     Clears what a previous swap had to leave behind -- the old executable could not
        ///     be deleted while it was the one running.
        ///
        ///     Hung off the update check because that is the only moment those files matter,
        ///     and because it needs no hook in the startup path, which lives upstream.
        /// </summary>
        private static void SweepPreviousUpdate()
        {
            try
            {
                var target = Models.SelfUpdatePackage.ResolveForThisProcess();
                if (target.Support == Models.SelfUpdateSupport.InPlace)
                    Models.SelfUpdateSwap.SweepAside(target.InstallRoot);
            }
            catch
            {
                // Housekeeping must never break the update check it rides on.
            }
        }
    }
}
