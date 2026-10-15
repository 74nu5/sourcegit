using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    /// <summary>
    ///     Sets or clears the skip-worktree bit, which keeps a tracked file's local modification
    ///     out of the changes git reports -- and out of commits, stashes and "git add -A".
    ///
    ///     Not assume-unchanged, which upstream already offers. That one is a promise that the
    ///     file does not change, made for speed, and git is free to drop it; skip-worktree is
    ///     the bit meant for "it changed, and that is mine".
    /// </summary>
    public class SkipWorktree : Command
    {
        // Well under Windows' command-line limit, whatever the paths.
        private const int MAX_ARGS_LENGTH = 8000;

        public SkipWorktree(string repo, IEnumerable<string> files, bool skip)
        {
            WorkingDirectory = repo;
            Context = repo;

            var builder = new StringBuilder("update-index ");
            builder.Append(skip ? "--skip-worktree" : "--no-skip-worktree").Append(" --");
            foreach (var f in files)
                builder.Append(' ').Append(f.Quoted());

            Args = builder.ToString();
        }

        /// <summary>
        ///     Runs as many commands as the length of the paths requires.
        /// </summary>
        public static async Task<bool> RunAsync(string repo, IList<string> files, bool skip, Models.ICommandLog log)
        {
            var batch = new List<string>();
            var length = 0;

            foreach (var f in files)
            {
                if (batch.Count > 0 && length + f.Length + 3 > MAX_ARGS_LENGTH)
                {
                    if (!await new SkipWorktree(repo, batch, skip).Use(log).ExecAsync().ConfigureAwait(false))
                        return false;

                    batch.Clear();
                    length = 0;
                }

                batch.Add(f);
                length += f.Length + 3;
            }

            if (batch.Count == 0)
                return true;

            return await new SkipWorktree(repo, batch, skip).Use(log).ExecAsync().ConfigureAwait(false);
        }
    }
}
