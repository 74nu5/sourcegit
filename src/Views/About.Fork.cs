using System.Text.RegularExpressions;

using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class About
    {
        private void OnVisitReleaseNotes(object _, RoutedEventArgs e)
        {
            var tag = Models.ForkVersion.TagOf(TxtVersion.Text);
            Native.OS.OpenBrowser(Models.ForkVersion.ReleaseNotesUrl(tag));
            e.Handled = true;
        }

        /// <summary>
        ///     Accepts the version string `git describe` produces, this fork's own tags
        ///     included.
        ///
        ///     Upstream only ever tags vYYYY.MM, so its pattern allowed nothing beyond the
        ///     commit count and hash that describe appends. A fork tagging v2026.18-3b failed
        ///     that check and fell back to showing the bare assembly version, which is exactly
        ///     the number it was trying not to be confused with.
        ///
        ///     This stays looser than what <see cref="Models.ForkVersion.TryParse"/> accepts,
        ///     and deliberately: showing a tag whose shape nothing can order is better than
        ///     showing no tag at all. Anything that is not a version — an error message from
        ///     describe when no tag is reachable — still fails to match, which is what the
        ///     check is for.
        /// </summary>
        [GeneratedRegex(@"^v\d{4}\.\d{1,2}(?:-[0-9A-Za-z.]+)*$")]
        private static partial Regex REG_FRIENDLY_VERSION();
    }
}
