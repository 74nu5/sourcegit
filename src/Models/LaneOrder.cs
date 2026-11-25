using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SourceGit.Models
{
    /// <summary>
    ///     The order in which branches hold the leftmost columns of the graph: main or
    ///     master first, then integration, then dev, and everything else after.
    ///
    ///     An entry names one branch, as the sidebar shows it, or several: "main|master"
    ///     takes whichever exists, "release/*" any branch under release/. A name without a
    ///     remote also stands for the remote branches of that name, so "main" covers
    ///     origin/main as well, and the column starts at whichever of them is newest.
    /// </summary>
    public static class LaneOrder
    {
        /// <summary>
        ///     The entries of a list typed as text: one per line, or separated by commas.
        /// </summary>
        public static List<string> Parse(string text)
        {
            var entries = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
                return entries;

            foreach (var raw in text.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
            {
                var entry = raw.Trim();
                if (entry.Length > 0 && !entries.Contains(entry))
                    entries.Add(entry);
            }

            return entries;
        }

        public static string Format(IEnumerable<string> entries)
        {
            return entries == null ? string.Empty : string.Join(", ", entries);
        }

        /// <summary>
        ///     The position of the first entry that names this branch, or -1.
        /// </summary>
        public static int IndexOf(IReadOnlyList<string> entries, Branch branch)
        {
            if (entries == null || branch == null || branch.IsDetachedHead)
                return -1;

            var name = branch.IsLocal ? branch.Name : branch.FriendlyName;
            for (var i = 0; i < entries.Count; i++)
            {
                if (Matches(entries[i], branch.IsLocal, name))
                    return i;
            }

            return -1;
        }

        /// <summary>
        ///     The entry that names this branch alone: its name as the sidebar shows it, the
        ///     remote left out -- so that "main" also takes origin/main, as typed entries do.
        /// </summary>
        public static string EntryFor(Branch branch)
        {
            return branch.Name;
        }

        /// <summary>
        ///     The tip of each entry in the loaded history: the newest commit carrying a label
        ///     it names, or null when none is loaded.
        ///
        ///     Read from the labels of the commits rather than from the list of branches. The
        ///     history is loaded before the branches are, so that list can still be empty the
        ///     first time the graph is drawn; and a branch the filters hide has no label, so it
        ///     claims no column either.
        /// </summary>
        public static List<string> ResolveTips(IReadOnlyList<string> entries, List<Commit> commits)
        {
            var tips = new List<string>();
            if (entries == null || entries.Count == 0 || commits == null)
                return tips;

            var found = new string[entries.Count];
            var left = entries.Count;
            foreach (var commit in commits)
            {
                foreach (var d in commit.Decorators)
                {
                    var isLocal = d.Type is DecoratorType.LocalBranchHead or DecoratorType.CurrentBranchHead;
                    if (!isLocal && d.Type != DecoratorType.RemoteBranchHead)
                        continue;

                    for (var i = 0; i < entries.Count; i++)
                    {
                        if (found[i] == null && Matches(entries[i], isLocal, d.Name))
                        {
                            found[i] = commit.SHA;
                            left--;
                        }
                    }
                }

                if (left == 0)
                    break;
            }

            tips.AddRange(found);
            return tips;
        }

        /// <summary>
        ///     Whether an entry names a branch, given by its name as the sidebar shows it --
        ///     team/x for a local one, origin/team/x for a remote one.
        /// </summary>
        public static bool Matches(string entry, bool isLocal, string name)
        {
            if (string.IsNullOrEmpty(entry) || string.IsNullOrEmpty(name))
                return false;

            var withoutRemote = name;
            if (!isLocal)
            {
                var slash = name.IndexOf('/');
                if (slash > 0)
                    withoutRemote = name.Substring(slash + 1);
            }

            foreach (var raw in entry.Split('|'))
            {
                var alt = raw.Trim();
                if (alt.Length == 0)
                    continue;

                if (MatchesName(alt, withoutRemote) || (!isLocal && MatchesName(alt, name)))
                    return true;
            }

            return false;
        }

        private static bool MatchesName(string pattern, string name)
        {
            if (pattern.IndexOf('*') < 0)
                return pattern.Equals(name, StringComparison.Ordinal);

            var regex = $"^{Regex.Escape(pattern).Replace("\\*", ".*")}$";
            return Regex.IsMatch(name, regex, RegexOptions.CultureInvariant);
        }
    }
}
