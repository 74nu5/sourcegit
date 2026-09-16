using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Models
{
    public enum SelfUpdateStage
    {
        Preparing,
        Downloading,
        Verifying,
        Extracting,
        Installing,
        Installed,
    }

    public sealed record SelfUpdateReport(SelfUpdateStage Stage, double Fraction);

    /// <summary>
    ///     Raised for the failures a user is meant to read, as opposed to the ones that are
    ///     bugs. Carries no inner exception on purpose: "the checksum does not match" is the
    ///     whole message, and a stack of network plumbing underneath it only obscures that.
    /// </summary>
    public sealed class SelfUpdateRefused(string message) : Exception(message);

    /// <summary>
    ///     Fetches the package that belongs to this machine, proves it arrived intact, and
    ///     puts it in place.
    ///
    ///     The order is not negotiable. SHA256SUMS is fetched first so that the expected
    ///     digest exists before there is anything to check; the package is verified before it
    ///     is unpacked, so nothing from an unverified archive ever touches the disk outside
    ///     the cache; and the swap runs last, on files that have already been proven.
    ///
    ///     Every failure leaves the installation as it was. There is no "install anyway".
    /// </summary>
    public sealed class SelfUpdateInstaller
    {
        public SelfUpdateInstaller(SelfUpdateTarget target, Version release)
        {
            _target = target;
            _release = release;
        }

        /// <summary>
        ///     The downloaded package, once its digest has been checked. Set even when the
        ///     installation cannot be replaced in place, because a verified file the user can
        ///     open themselves is still worth more than a link to a page.
        /// </summary>
        public string VerifiedPackagePath { get; private set; } = string.Empty;

        /// <summary>
        ///     Downloads and verifies, and stops there. This is all that happens for an
        ///     installation whose files belong to someone else.
        /// </summary>
        public async Task DownloadAndVerifyAsync(IProgress<SelfUpdateReport> progress, CancellationToken cancellation)
        {
            progress?.Report(new SelfUpdateReport(SelfUpdateStage.Preparing, 0));

            var asset = SelfUpdatePackage.Select(_release.Assets, _target.AssetSuffix);
            if (asset == null)
                throw new SelfUpdateRefused($"This release publishes no package ending in {_target.AssetSuffix}.");

            var sums = SelfUpdatePackage.Select(_release.Assets, SelfUpdateChecksums.ASSET_NAME);
            if (sums == null)
                throw new SelfUpdateRefused($"This release publishes no {SelfUpdateChecksums.ASSET_NAME}, so the download cannot be verified.");

            var folder = Directory.CreateDirectory(Path.Combine(Native.OS.BasicDirectories.CacheDir, "updates", _release.TagName));

            using var client = NewClient();

            var expected = SelfUpdateChecksums.Parse(await client.GetStringAsync(sums.DownloadUrl, cancellation));
            if (!expected.TryGetValue(asset.Name, out var digest))
                throw new SelfUpdateRefused($"{SelfUpdateChecksums.ASSET_NAME} says nothing about {asset.Name}.");

            var downloaded = Path.Combine(folder.FullName, asset.Name);
            await DownloadAsync(client, asset, downloaded, progress, cancellation);

            progress?.Report(new SelfUpdateReport(SelfUpdateStage.Verifying, 0));

            var actual = await SelfUpdateChecksums.ComputeAsync(downloaded, cancellation);
            if (!SelfUpdateChecksums.Matches(digest, actual))
            {
                // Nothing that failed its digest is left lying around to be opened by hand.
                TryDelete(downloaded);
                throw new SelfUpdateRefused($"{asset.Name} does not match the checksum published with it. Nothing was installed.");
            }

            VerifiedPackagePath = downloaded;
        }

        /// <summary>
        ///     Unpacks the verified package and swaps it over the installation. Only ever
        ///     called after <see cref="DownloadAndVerifyAsync"/>, and only when the target
        ///     said the files are ours to replace.
        /// </summary>
        public void Install(IProgress<SelfUpdateReport> progress)
        {
            if (VerifiedPackagePath.Length == 0)
                throw new InvalidOperationException("Install called before a package was verified.");

            if (_target.Support != SelfUpdateSupport.InPlace)
                throw new SelfUpdateRefused(_target.Reason);

            if (!SelfUpdateSwap.CanWriteTo(_target.InstallRoot))
                throw new SelfUpdateRefused($"This copy is installed in {_target.InstallRoot}, where it cannot write.");

            SelfUpdateSwap.SweepAside(_target.InstallRoot);

            progress?.Report(new SelfUpdateReport(SelfUpdateStage.Extracting, 0));

            var stage = Path.Combine(Path.GetDirectoryName(VerifiedPackagePath)!, "staged");
            if (Directory.Exists(stage))
                Directory.Delete(stage, true);

            string tree;
            if (_target.IsArchive)
            {
                ZipFile.ExtractToDirectory(VerifiedPackagePath, stage);

                tree = Path.Combine(stage, _target.ArchiveRootEntry);
                if (!Directory.Exists(tree))
                    throw new SelfUpdateRefused($"The package does not contain the expected {_target.ArchiveRootEntry} folder.");
            }
            else
            {
                // The AppImage is the executable. Staging it under the name it will take lets
                // the swap treat it exactly like any other tree.
                Directory.CreateDirectory(stage);
                tree = stage;
                File.Copy(VerifiedPackagePath, Path.Combine(stage, Path.GetFileName(_target.ExecutablePath)), true);
            }

            progress?.Report(new SelfUpdateReport(SelfUpdateStage.Installing, 0));

            SelfUpdateSwap.Apply(tree, _target.InstallRoot);

            TryDeleteTree(stage);

            progress?.Report(new SelfUpdateReport(SelfUpdateStage.Installed, 1));
        }

        private static HttpClient NewClient()
        {
            // The redirect to the asset storage carries no credentials, but GitHub still
            // refuses a request with no User-Agent -- with a 403 naming neither the header
            // nor the reason, which would surface here as an ordinary network failure.
            var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SourceGit");
            return client;
        }

        private static async Task DownloadAsync(HttpClient client, ReleaseAsset asset, string destination, IProgress<SelfUpdateReport> progress, CancellationToken cancellation)
        {
            progress?.Report(new SelfUpdateReport(SelfUpdateStage.Downloading, 0));

            using var response = await client.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellation);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? asset.Size;

            await using (var source = await response.Content.ReadAsStreamAsync(cancellation))
            await using (var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, true))
            {
                var buffer = new byte[128 * 1024];
                var copied = 0L;

                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellation);
                    if (read == 0)
                        break;

                    await target.WriteAsync(buffer.AsMemory(0, read), cancellation);

                    copied += read;
                    if (total > 0)
                        progress?.Report(new SelfUpdateReport(SelfUpdateStage.Downloading, (double)copied / total));
                }
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Cache hygiene only.
            }
        }

        private static void TryDeleteTree(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch
            {
                // Cache hygiene only.
            }
        }

        private readonly SelfUpdateTarget _target;
        private readonly Version _release;
    }
}
