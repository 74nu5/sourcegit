namespace SourceGit.Models
{
    /// <summary>
    ///     How this fork decides that a release is worth telling the user about.
    ///
    ///     Upstream compares System.Version values and can afford to: every tag it publishes
    ///     is vYYYY.MM, which parses. This fork's tags carry a suffix, so the same comparison
    ///     throws -- and the exception would be swallowed by the check's own catch, leaving
    ///     an update mechanism that silently never fires.
    /// </summary>
    public partial class Version
    {
        /// <summary>
        ///     True when the release this describes comes after the build asking.
        ///
        ///     The tags are compared, not the assembly versions, because the assembly cannot
        ///     tell -3b.1 from -3b.2. When this build has no tag to offer -- a shallow clone,
        ///     a checkout with none in reach -- it falls back to what the assembly does carry,
        ///     year and month, so an update is still announced when a new month is published
        ///     and never on a suffix it has no way to place.
        /// </summary>
        private bool IsNewerThanCurrent()
        {
            if (!ForkVersion.TryParse(TagName, out var latest))
                return false;

            var current = ForkVersion.Current;
            if (current != null)
                return current.TryCompareTo(latest, out var order) && order < 0;

            return (CurrentVersion.Major, CurrentVersion.Minor).CompareTo((latest.Year, latest.Month)) < 0;
        }

        /// <summary>
        ///     What the update window should call this build.
        ///
        ///     Upstream shows major and minor, which here would read "v2026.19" to someone
        ///     running v2026.19-3b.2 -- the one part of the number that cannot tell them
        ///     what they have. The tag is shown instead, and upstream's form remains the
        ///     fallback for a build that has none.
        /// </summary>
        private string CurrentVersionDisplay()
        {
            return ForkVersion.Current?.Tag ?? $"v{CurrentVersion.Major}.{CurrentVersion.Minor:D2}";
        }
    }
}
