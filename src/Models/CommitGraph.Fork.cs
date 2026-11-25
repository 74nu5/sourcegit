using System;
using System.Collections.Generic;

namespace SourceGit.Models
{
    /// <summary>
    ///     Stable lane allocation and the lane order, added by this fork. Generate() keeps
    ///     only the branches that choose between the historical placement and these.
    /// </summary>
    public partial class CommitGraph
    {
        /// <summary>
        ///     The tips the leftmost columns are held for, in column order.
        ///
        ///     The lane order names branches -- main, then integration, then dev -- and the
        ///     caller hands over their tips. A tip outside the loaded window claims nothing,
        ///     and neither does a tip lying on the line of a branch that comes earlier: an
        ///     integration fully merged into main, say, would only hold an empty column. So
        ///     the first-parent line of each tip is walked in order, the way the graph's paths
        ///     follow it, and a tip already walked over is dropped. What is dropped leaves no
        ///     gap: dev takes the second column when there is no integration.
        ///
        ///     With no order, the stable layout holds the leftmost column for the branch
        ///     checked out, as it always has. The compact one holds nothing -- a fallback
        ///     there would move the default layout of every repository, which has to stay
        ///     byte for byte what upstream produces.
        /// </summary>
        private static List<string> ResolveLaneSlots(List<Commit> commits, GraphLaneMode laneMode, IReadOnlyList<string> laneOrder)
        {
            var tips = new List<string>();
            if (laneOrder != null)
            {
                foreach (var tip in laneOrder)
                {
                    if (!string.IsNullOrEmpty(tip))
                        tips.Add(tip);
                }
            }

            var slots = new List<string>();
            if (tips.Count > 0)
            {
                var bySHA = new Dictionary<string, Commit>(commits.Count);
                foreach (var c in commits)
                    bySHA.TryAdd(c.SHA, c);

                var walked = new HashSet<string>();
                foreach (var tip in tips)
                {
                    if (slots.Count == MAX_LANES - 1)
                        break;

                    if (!bySHA.ContainsKey(tip) || walked.Contains(tip))
                        continue;

                    slots.Add(tip);

                    var sha = tip;
                    while (bySHA.TryGetValue(sha, out var c) && walked.Add(sha) && c.Parents.Count > 0)
                        sha = c.Parents[0];
                }
            }

            if (slots.Count == 0 && laneMode == GraphLaneMode.Stable)
            {
                foreach (var c in commits)
                {
                    if (c.IsCurrentHead)
                    {
                        slots.Add(c.SHA);
                        break;
                    }
                }
            }

            return slots;
        }

        /// <summary>
        ///     The column of the leftmost held path that reaches this commit, or -1.
        /// </summary>
        private static int HeldPathReaching(PathHelper[] holders, string sha)
        {
            for (var i = 0; i < holders.Length; i++)
            {
                if (holders[i] != null && holders[i].Next.Equals(sha, StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }

        /// <summary>
        ///     The rightmost column a live path currently occupies.
        ///
        ///     Compact hands out columns in list order, so the widest is normally just the
        ///     last one -- which is what Generate reads when nothing is held. Holding a
        ///     column breaks that order, and then the answer has to be looked for.
        /// </summary>
        private static double WidestLastX(List<PathHelper> unsolved)
        {
            var widest = 0.0;

            foreach (var path in unsolved)
            {
                if (path.LastX > widest)
                    widest = path.LastX;
            }

            return widest;
        }

        /// <summary>
        ///     Hands out lanes that a path keeps for its whole life. A released lane is only
        ///     handed out again after a quarantine, so two unrelated branches never appear
        ///     back to back in the same column.
        /// </summary>
        private class LaneAllocator
        {
            /// <summary>
            ///     The first lanes are held for the branches of the lane order: the allocator
            ///     never hands them out, and they count in the width from the start.
            /// </summary>
            public LaneAllocator(int reserved)
            {
                _reserved = reserved;
                _next = reserved;
                MaxLane = reserved > 0 ? reserved - 1 : 0;
            }

            public int MaxLane { get; private set; }
            public int Overflow { get; private set; } = 0;

            public int Acquire(int row)
            {
                var best = -1;
                for (var i = 0; i < _freed.Count; i++)
                {
                    if (row - _freed[i].Row < QUARANTINE_ROWS)
                        continue;

                    if (best == -1 || _freed[i].Lane < _freed[best].Lane)
                        best = i;
                }

                if (best != -1)
                {
                    var reused = _freed[best].Lane;
                    _freed.RemoveAt(best);
                    return reused;
                }

                // Beyond the budget the extra paths share the last lane rather than pushing
                // the subject out of view. The overflow is reported so the UI can say so.
                if (_next >= MAX_LANES)
                {
                    Overflow++;
                    return MAX_LANES - 1;
                }

                var lane = _next++;
                if (lane > MaxLane)
                    MaxLane = lane;

                return lane;
            }

            public void Release(int lane, int row)
            {
                if (lane < _reserved)
                    return;

                _freed.Add((lane, row));
            }

            private readonly List<(int Lane, int Row)> _freed = [];
            private readonly int _reserved;
            private int _next;
        }

        /// <summary>
        ///     Rows a released lane stays untouched before it can be handed out again, about
        ///     one screenful of commits.
        /// </summary>
        private const int QUARANTINE_ROWS = 25;

        /// <summary>
        ///     Lane budget, matching the 240px cap of the graph column.
        /// </summary>
        private const int MAX_LANES = 20;
    }
}
