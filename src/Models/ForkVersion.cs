using System;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace SourceGit.Models
{
    /// <summary>
    ///     A release of this fork, read from the tag that names it.
    ///
    ///     Upstream tags vYYYY.MM. This fork publishes vYYYY.MM-&lt;label&gt;.N on top of the
    ///     release it branched from, and System.Version cannot hold that shape: parsing
    ///     "2026.19-3b.1" throws. Nor can the assembly version stand in for it -- VERSION
    ///     must stay purely numeric to feed AssemblyVersion, so every fork release of a
    ///     given month reports 2026.19.0.0 and they are indistinguishable.
    ///
    ///     So the update check compares tags. This is the tag, parsed.
    /// </summary>
    public sealed partial class ForkVersion
    {
        /// <summary>
        ///     Where this fork publishes. Named once so the update check, the download link
        ///     and the release notes cannot drift apart -- or keep pointing at the project
        ///     this was branched from, which is the whole reason the check was off.
        /// </summary>
        public const string OWNER_AND_NAME = "74nu5/sourcegit";

        public const string REPOSITORY_URL = $"https://github.com/{OWNER_AND_NAME}";
        public const string LATEST_RELEASE_URL = $"{REPOSITORY_URL}/releases/latest";

        /// <summary>
        ///     The releases API answers with exactly the fields <see cref="Version"/> already
        ///     declares -- name, tag_name, published_at, body -- because the file upstream
        ///     hosts is itself a copy of this. So nothing has to be taught a new shape, and
        ///     no page has to be published anywhere for the check to have something to read.
        /// </summary>
        public const string LATEST_RELEASE_API = $"https://api.github.com/repos/{OWNER_AND_NAME}/releases/latest";

        public static string ReleaseNotesUrl(string tag) => $"{REPOSITORY_URL}/releases/tag/{tag}";

        /// <summary>
        ///     What this build is, or null when it cannot tell: a shallow clone, a checkout
        ///     with no tag in reach, a build where `git describe` failed. Callers must read
        ///     that as "unknown", never as "up to date".
        /// </summary>
        public static ForkVersion Current => s_current.Value;

        /// <summary>
        ///     The tag as it was written, which is what a human should be shown.
        /// </summary>
        public string Tag { get; }

        public int Year { get; }
        public int Month { get; }

        /// <summary>
        ///     The fork's own marker, empty on a tag inherited from upstream.
        /// </summary>
        public string Label { get; }

        /// <summary>
        ///     Which release carries that label. Zero without one, which is what puts an
        ///     inherited vYYYY.MM before every fork release of the same month.
        /// </summary>
        public int Sequence { get; }

        public override string ToString() => Tag;

        public static bool TryParse(string tag, out ForkVersion version)
        {
            version = null;

            if (string.IsNullOrEmpty(tag))
                return false;

            var matched = REG_TAG().Match(tag);
            if (!matched.Success)
                return false;

            if (!int.TryParse(matched.Groups[1].ValueSpan, CultureInfo.InvariantCulture, out var year) ||
                !int.TryParse(matched.Groups[2].ValueSpan, CultureInfo.InvariantCulture, out var month))
                return false;

            var label = matched.Groups[3].Success ? matched.Groups[3].Value : string.Empty;
            var sequence = 0;

            if (matched.Groups[4].Success &&
                !int.TryParse(matched.Groups[4].ValueSpan, CultureInfo.InvariantCulture, out sequence))
                return false;

            version = new ForkVersion(tag, year, month, label, sequence);
            return true;
        }

        /// <summary>
        ///     Orders two releases, and says when it cannot.
        ///
        ///     Year and month decide first. Within one month a tag inherited from upstream
        ///     comes before the fork releases built on it, and those follow their number.
        ///     Two different labels in the same month are left incomparable on purpose:
        ///     nothing in the scheme says whether -3b precedes -4a, and a guess would offer
        ///     a downgrade as an update.
        /// </summary>
        public bool TryCompareTo(ForkVersion other, out int result)
        {
            result = 0;

            if (other == null)
                return false;

            var byDate = (Year, Month).CompareTo((other.Year, other.Month));
            if (byDate != 0)
            {
                result = byDate;
                return true;
            }

            if (Label.Length > 0 &&
                other.Label.Length > 0 &&
                !Label.Equals(other.Label, StringComparison.OrdinalIgnoreCase))
                return false;

            result = Sequence.CompareTo(other.Sequence);
            return true;
        }

        /// <summary>
        ///     Recovers the tag from what `git describe` produced.
        ///
        ///     Only what describe itself appends is removed: the -dirty marker, and the
        ///     -&lt;count&gt;-&lt;hash&gt; pair it adds when the build is ahead of the tag.
        ///     Cutting at the first dash instead -- which is what upstream does, every tag
        ///     there looking like vYYYY.MM -- would turn v2026.19-3b.1 into v2026.19 and
        ///     name a release this fork never published.
        /// </summary>
        public static string TagOf(string describeOutput)
        {
            var tag = describeOutput ?? string.Empty;

            if (tag.EndsWith("-dirty", StringComparison.Ordinal))
                tag = tag[..^6];

            var describe = REG_DESCRIBE_SUFFIX().Match(tag);
            return describe.Success ? tag[..describe.Index] : tag;
        }

        private ForkVersion(string tag, int year, int month, string label, int sequence)
        {
            Tag = tag;
            Year = year;
            Month = month;
            Label = label;
            Sequence = sequence;
        }

        /// <summary>
        ///     The build stamps `git describe` into assembly metadata; see GenVersionInfo in
        ///     the project file. It is the only place the suffix survives -- AssemblyVersion
        ///     drops it -- which is why the current version is read from here.
        /// </summary>
        private static ForkVersion ReadCurrent()
        {
            var assembly = Assembly.GetExecutingAssembly();

            foreach (var attr in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
            {
                if (!attr.Key.Equals("FriendlyVersion", StringComparison.OrdinalIgnoreCase))
                    continue;

                return TryParse(TagOf(attr.Value), out var version) ? version : null;
            }

            return null;
        }

        [GeneratedRegex(@"^v(\d{4})\.(\d{1,2})(?:-([0-9A-Za-z]+)\.(\d+))?$")]
        private static partial Regex REG_TAG();

        [GeneratedRegex(@"-\d+-[0-9a-f]{8}$")]
        private static partial Regex REG_DESCRIBE_SUFFIX();

        private static readonly Lazy<ForkVersion> s_current = new(ReadCurrent);
    }
}
