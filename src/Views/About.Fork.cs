using System.Text.RegularExpressions;

namespace SourceGit.Views
{
    public partial class About
    {
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
