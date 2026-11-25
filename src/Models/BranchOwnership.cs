using System;
using System.Collections.Generic;

namespace SourceGit.Models
{
    public enum BranchColumnMode
    {
        /// <summary>
        ///     Only rows carrying a reference show anything.
        /// </summary>
        RefsOnly = 0,

        /// <summary>
        ///     Rows without a reference show the branch they are reached from.
        /// </summary>
        AllRows,
    }

    /// <summary>
    ///     Git does not record which branch a commit belongs to: a commit is merely reachable
    ///     from zero, one or many of them. This assigns a display-only owner by walking the
    ///     first-parent chain of every branch in priority order and stopping as soon as it
    ///     reaches a commit another branch already claimed.
    ///
    ///     It is a presentation heuristic, never a statement about Git itself. Commits whose
    ///     branch tip lies outside the loaded window are left without an owner rather than
    ///     being attributed to something plausible but wrong.
    /// </summary>
    public static class BranchOwnership
    {
        public static void Clear(List<Commit> commits)
        {
            foreach (var c in commits)
                c.OwnerBranch = string.Empty;
        }

        public static void Resolve(List<Commit> commits, List<Branch> branches, IReadOnlyList<string> laneOrder = null)
        {
            Clear(commits);

            if (commits.Count == 0 || branches == null || branches.Count == 0)
                return;

            var bySHA = new Dictionary<string, Commit>(commits.Count);
            foreach (var c in commits)
                bySHA[c.SHA] = c;

            // Each commit is visited once, plus one stopping visit per branch.
            foreach (var branch in Prioritize(branches, laneOrder))
            {
                var sha = branch.Head;
                while (!string.IsNullOrEmpty(sha) && bySHA.TryGetValue(sha, out var commit))
                {
                    if (commit.OwnerBranch.Length > 0)
                        break;

                    commit.OwnerBranch = branch.FriendlyName;
                    if (commit.Parents.Count == 0)
                        break;

                    sha = commit.Parents[0];
                }
            }
        }

        private static List<Branch> Prioritize(List<Branch> branches, IReadOnlyList<string> laneOrder)
        {
            var tracked = new HashSet<string>();
            foreach (var b in branches)
            {
                if (b.IsLocal && !string.IsNullOrEmpty(b.Upstream))
                    tracked.Add(b.Upstream);
            }

            var sorted = new List<Branch>(branches);
            sorted.Sort((l, r) =>
            {
                var delta = Rank(l, tracked, laneOrder) - Rank(r, tracked, laneOrder);
                if (delta != 0)
                    return delta;

                // Recent work claims its commits before forgotten branches do.
                return r.CommitterDate.CompareTo(l.CommitterDate);
            });

            return sorted;
        }

        private static int Rank(Branch branch, HashSet<string> tracked, IReadOnlyList<string> laneOrder)
        {
            // The branches of the lane order first, in its order: the commits a branch owns
            // are the ones drawn in its column, and the column and the graph have to agree.
            var ordered = LaneOrder.IndexOf(laneOrder, branch);
            if (ordered >= 0)
                return ordered * 2 - 2 * MAX_ORDERED + (branch.IsLocal ? 0 : 1);

            // Trunks come first, remote ones included: a feature branch only owns what it
            // adds on top of the trunk, never the shared history it was forked from. Putting
            // the current branch first instead would make it swallow the whole trunk.
            if (IsTrunk(branch.Name))
                return branch.IsLocal ? 0 : 1;

            if (branch.IsCurrent)
                return 2;

            if (branch.IsLocal)
                return 3;

            // A remote whose local counterpart is already in the list would only repeat it.
            return tracked.Contains(branch.FullName) ? 5 : 4;
        }

        /// <summary>
        ///     Room for the lane order below every other rank.
        /// </summary>
        private const int MAX_ORDERED = 1000;

        private static bool IsTrunk(string name)
        {
            return name.Equals("main", StringComparison.Ordinal) ||
                   name.Equals("master", StringComparison.Ordinal) ||
                   name.Equals("develop", StringComparison.Ordinal);
        }
    }
}
