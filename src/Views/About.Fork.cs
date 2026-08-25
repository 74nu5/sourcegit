using System;
using System.Text.RegularExpressions;

using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class About
    {
        /// <summary>
        ///     Where the release notes live. This fork publishes its own, so the link has to
        ///     point here rather than at the project it branched from.
        /// </summary>
        private const string RELEASES_URL = "https://github.com/74nu5/sourcegit/releases/tag";

        private void OnVisitReleaseNotes(object _, RoutedEventArgs e)
        {
            Native.OS.OpenBrowser($"{RELEASES_URL}/{TagOf(TxtVersion.Text)}");
            e.Handled = true;
        }

        /// <summary>
        ///     Recovers the tag from what `git describe` produced.
        ///
        ///     Upstream cut the string at its first dash, which worked while every tag looked
        ///     like vYYYY.MM. A fork tag such as v2026.18-3b would be cut down to v2026.18 and
        ///     the link would land on someone else's release. Only what describe itself appends
        ///     is removed: the -dirty marker, and the -&lt;count&gt;-&lt;hash&gt; pair it adds
        ///     when the build is ahead of the tag.
        /// </summary>
        private static string TagOf(string version)
        {
            var tag = version ?? string.Empty;

            if (tag.EndsWith("-dirty", StringComparison.Ordinal))
                tag = tag[..^6];

            var describe = REG_DESCRIBE_SUFFIX().Match(tag);
            return describe.Success ? tag[..describe.Index] : tag;
        }

        [GeneratedRegex(@"-\d+-[0-9a-f]{8}$")]
        private static partial Regex REG_DESCRIBE_SUFFIX();

        /// <summary>
        ///     Accepts the version string `git describe` produces, this fork's own tags
        ///     included.
        ///
        ///     Upstream only ever tags vYYYY.MM, so its pattern allowed nothing beyond the
        ///     commit count and hash that describe appends. A fork tagging v2026.18-3b failed
        ///     that check and fell back to showing the bare assembly version, which is exactly
        ///     the number it was trying not to be confused with.
        ///
        ///     Anything that is not a version at all — an error message from describe when no
        ///     tag is reachable — still fails to match, which is what the check is for.
        /// </summary>
        [GeneratedRegex(@"^v\d{4}\.\d{1,2}(?:-[0-9A-Za-z.]+)*$")]
        private static partial Regex REG_FRIENDLY_VERSION();
    }
}
