using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    /// <summary>
    ///     The local branches whose tip is already reachable from HEAD.
    ///
    ///     This is the one question worth asking before removing a branch, and it is exactly
    ///     the question `git branch -d` asks before refusing. Asking it the same way means a
    ///     window that warns and a git that refuses never disagree.
    ///
    ///     It is not "has it been pushed": a branch whose upstream is gone has no upstream
    ///     left to be compared against, so nothing can be said about what it is ahead of. What
    ///     can be said is whether its commits live anywhere else, and that is this.
    /// </summary>
    public class QueryMergedBranches : Command
    {
        public QueryMergedBranches(string repo)
        {
            WorkingDirectory = repo;
            Context = repo;
            Args = "branch --format=%(refname:short) --merged";
        }

        public async Task<HashSet<string>> GetResultAsync()
        {
            var merged = new HashSet<string>(StringComparer.Ordinal);

            var rs = await ReadToEndAsync().ConfigureAwait(false);
            if (!rs.IsSuccess)
                return merged;

            foreach (var line in rs.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var name = line.Trim();
                if (name.Length > 0)
                    merged.Add(name);
            }

            return merged;
        }
    }
}
