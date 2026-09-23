using System;
using System.Collections.Generic;

namespace SourceGit.Models
{
    /// <summary>
    ///     Stable lane allocation, added by this fork. Generate() keeps only the branches that
    ///     choose between the historical placement and this one.
    /// </summary>
    public partial class CommitGraph
    {
        /// <summary>
        ///     Which commit the leftmost lane is held for, or null when nobody claims it.
        ///
        ///     A pinned branch wins, so that the trunk stays in the same column whatever is
        ///     checked out -- that is the whole point of pinning one. When the pin points
        ///     outside the loaded window, or when nothing is pinned, the branch that is
        ///     checked out takes the lane, which is what this fork has always done. Pinning
        ///     adds a claimant; it never leaves the lane empty that would otherwise be held.
        /// </summary>
        private static string ResolveLaneAnchor(List<Commit> commits, string pinnedHead)
        {
            Commit head = null;

            foreach (var c in commits)
            {
                if (!string.IsNullOrEmpty(pinnedHead) && c.SHA.Equals(pinnedHead, StringComparison.Ordinal))
                    return c.SHA;

                if (head == null && c.IsCurrentHead)
                    head = c;
            }

            return head?.SHA;
        }

        /// <summary>
        ///     Which commit the leftmost column is held for in the compact layout, or null
        ///     when nobody claims it.
        ///
        ///     Deliberately not <see cref="ResolveLaneAnchor"/>. That one falls back to the
        ///     checked-out branch, which is right for the stable layout -- it reserves a lane
        ///     either way -- and wrong here: the compact placement with nothing pinned has to
        ///     stay byte for byte what upstream produces, and a fallback would move it for
        ///     every repository at once.
        ///
        ///     A pin pointing outside the loaded window claims nothing, or the leftmost
        ///     column would be held empty for a branch that is not on screen.
        /// </summary>
        private static string ResolveCompactPin(List<Commit> commits, string pinnedHead)
        {
            if (string.IsNullOrEmpty(pinnedHead))
                return null;

            foreach (var c in commits)
            {
                if (c.SHA.Equals(pinnedHead, StringComparison.Ordinal))
                    return c.SHA;
            }

            return null;
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
            public LaneAllocator(bool reserveCurrentBranch)
            {
                _reserveCurrentBranch = reserveCurrentBranch;
                _reservedAvailable = reserveCurrentBranch;
                _next = reserveCurrentBranch ? 1 : 0;
            }

            public int MaxLane { get; private set; } = 0;
            public int Overflow { get; private set; } = 0;

            public int Acquire(int row, bool isCurrentBranch)
            {
                if (isCurrentBranch && _reservedAvailable)
                {
                    _reservedAvailable = false;
                    return 0;
                }

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
                if (lane == 0 && _reserveCurrentBranch)
                    return;

                _freed.Add((lane, row));
            }

            private readonly List<(int Lane, int Row)> _freed = [];
            private readonly bool _reserveCurrentBranch;
            private bool _reservedAvailable;
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
