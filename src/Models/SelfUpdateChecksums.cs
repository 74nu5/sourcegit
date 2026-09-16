using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Models
{
    /// <summary>
    ///     The SHA256SUMS file published beside the packages, and the check it allows.
    ///
    ///     What this proves is that a package arrived whole and unaltered since the release
    ///     job hashed it. It does not prove who produced it: the sums are published next to
    ///     the files they cover, so whoever could replace one could replace the other. That
    ///     is a signature's job, and this fork signs nothing.
    /// </summary>
    public static class SelfUpdateChecksums
    {
        /// <summary>
        ///     The name the release workflow gives the file, and therefore the name of the
        ///     asset to look for.
        /// </summary>
        public const string ASSET_NAME = "SHA256SUMS";

        /// <summary>
        ///     Reads the coreutils format: a hex digest, whitespace, then the file name. The
        ///     separator is two spaces in text mode and a space then '*' in binary mode --
        ///     the runner produces the first, a Windows build of sha256sum the second, and a
        ///     reader that only knows one of them fails on a file that is perfectly valid.
        ///
        ///     Names are kept exactly as written. They are compared against asset names, and
        ///     a path separator in one would mean the sums were computed from the wrong
        ///     directory -- worth failing on, not worth papering over.
        /// </summary>
        public static Dictionary<string, string> Parse(string content)
        {
            var sums = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(content))
                return sums;

            foreach (var raw in content.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0)
                    continue;

                var cut = line.IndexOf(' ');
                if (cut <= 0)
                    continue;

                var digest = line[..cut];
                if (!IsDigest(digest))
                    continue;

                var name = line[(cut + 1)..].TrimStart(' ');
                if (name.StartsWith('*'))
                    name = name[1..];

                if (name.Length == 0)
                    continue;

                sums[name] = digest.ToLowerInvariant();
            }

            return sums;
        }

        public static async Task<string> ComputeAsync(string path, CancellationToken cancellation)
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
            var hash = await SHA256.HashDataAsync(stream, cancellation);
            return Convert.ToHexStringLower(hash);
        }

        /// <summary>
        ///     Compares two digests without leaving the answer to case or to a null.
        /// </summary>
        public static bool Matches(string expected, string actual)
        {
            return !string.IsNullOrEmpty(expected) &&
                   !string.IsNullOrEmpty(actual) &&
                   expected.Equals(actual, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDigest(string value)
        {
            if (value.Length != 64)
                return false;

            foreach (var c in value)
            {
                var isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!isHex)
                    return false;
            }

            return true;
        }
    }
}
