using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace SourceGit.Models
{
    /// <summary>
    ///     This fork's application settings, kept in preference.fork.json beside upstream's
    ///     preference.json.
    ///
    ///     They used to live in preference.json itself. Upstream SourceGit reads that file
    ///     without complaint, but it does not know these settings, and when it saves it
    ///     rewrites the whole file with only its own: running upstream once, on a machine that
    ///     has both, erased them all -- forge accounts and their tokens included. A file
    ///     upstream never opens cannot be rewritten by it.
    ///
    ///     Property names match the keys they had in preference.json, which is what lets the
    ///     old file be read straight into this type when moving the settings across.
    /// </summary>
    public class ForkPreferences
    {
        public bool SplitGraphColumnInHistories { get; set; } = false;
        public bool ShowBranchColumnInHistories { get; set; } = false;
        public GraphLaneMode GraphLaneMode { get; set; } = GraphLaneMode.Compact;
        public bool ColorizeRowsByBranch { get; set; } = false;
        public bool ShowStashMessageAsLabel { get; set; } = false;
        public bool GroupWorktreesInTabs { get; set; } = false;
        public List<ForgeAccount> ForgeAccounts { get; set; } = [];
        public bool ShowPullRequestIndicator { get; set; } = false;
        public bool ShowRemoteIconInsteadOfName { get; set; } = false;
        public BranchColumnMode BranchColumnMode { get; set; } = BranchColumnMode.RefsOnly;
    }

    /// <summary>
    ///     This fork's per-repository state, kept in sourcegit.fork.uistates beside upstream's
    ///     sourcegit.uistates in the repository's git directory -- for the same reason, and
    ///     with the same key-for-key correspondence.
    /// </summary>
    public class ForkRepositoryStates
    {
        public bool IsLocalBranchesVisibleInSideBar { get; set; } = true;
        public bool IsRemotesVisibleInSideBar { get; set; } = true;
        public bool IsTagsVisibleInSideBar { get; set; } = true;
        public bool IsSubmodulesVisibleInSideBar { get; set; } = true;
        public bool IsWorktreesVisibleInSideBar { get; set; } = true;
        public bool IsPullRequestsVisibleInSideBar { get; set; } = true;
        public string PinnedLaneBranch { get; set; } = string.Empty;
        public bool ShowStashesInGraph { get; set; } = false;
        public bool ShowUncommittedInGraph { get; set; } = false;
        public double BranchColumnWidth { get; set; } = 0;
        public double GraphColumnWidth { get; set; } = 0;
    }

    public static class ForkSettingsFile
    {
        public const string PREFERENCES = "preference.fork.json";
        public const string UI_STATES = "sourcegit.fork.uistates";

        /// <summary>
        ///     Reads this fork's settings: from their own file when it exists, otherwise from the
        ///     shared file they were kept in before -- which is how settings saved by an earlier
        ///     version of this fork come across, once, without the user doing anything.
        ///
        ///     Unknown keys are skipped by the deserializer, so reading a whole upstream file
        ///     into this type picks out exactly this fork's keys and leaves the rest. A key
        ///     that is absent keeps its default: an upstream-only file yields the defaults.
        ///
        ///     A side file that cannot be read falls back on the shared one rather than on the
        ///     defaults, because the shared one may still hold the settings.
        /// </summary>
        public static T Read<T>(string sidecar, string shared, JsonTypeInfo<T> info) where T : new()
        {
            foreach (var path in new[] { sidecar, shared })
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    continue;

                try
                {
                    using var stream = File.OpenRead(path);
                    var value = JsonSerializer.Deserialize(stream, info);
                    if (value != null)
                        return value;
                }
                catch
                {
                    // Try the next source.
                }
            }

            return new T();
        }

        /// <summary>
        ///     Writes through a temporary file and a move, the way upstream writes its own, so
        ///     a crash mid-write never leaves half a file where the settings were.
        /// </summary>
        public static void Write<T>(string path, T value, JsonTypeInfo<T> info)
        {
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                var tmp = $"{path}.tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(value, info));
                File.Move(tmp, path, true);
            }
            catch
            {
                // Same policy as upstream's saves: a failed write is not worth a crash.
            }
        }
    }
}
