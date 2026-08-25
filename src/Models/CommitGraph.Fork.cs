using System.Collections.Generic;

namespace SourceGit.Models
{
    /// <summary>
    ///     Stable lane allocation, added by this fork. Generate() keeps only the branches that
    ///     choose between the historical placement and this one.
    /// </summary>
    public partial class CommitGraph
    {
        private static bool HasCurrentHead(List<Commit> commits)
        {
            foreach (var c in commits)
            {
                if (c.IsCurrentHead)
                    return true;
            }

            return false;
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
