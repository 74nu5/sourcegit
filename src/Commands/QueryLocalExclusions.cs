using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    /// <summary>
    ///     The tracked files whose local modification git has been told to ignore: the
    ///     skip-worktree bit, and upstream's assume-unchanged.
    ///
    ///     From "git ls-files -v": 'S' marks skip-worktree, and a lowercase tag marks
    ///     assume-unchanged ('s' is both). -z, because a path with a quote or a non-ASCII
    ///     character is otherwise escaped and quoted, and would not match the file.
    /// </summary>
    public class QueryFrozenFiles : Command
    {
        public QueryFrozenFiles(string repo)
        {
            WorkingDirectory = repo;
            Context = repo;
            Args = "ls-files -v -z";
            RaiseError = false;
        }

        public async Task<List<Models.FrozenFile>> GetResultAsync()
        {
            var outs = new List<Models.FrozenFile>();
            var rs = await ReadToEndAsync().ConfigureAwait(false);
            if (!rs.IsSuccess)
                return outs;

            foreach (var entry in rs.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                if (entry.Length < 3 || entry[1] != ' ')
                    continue;

                var tag = entry[0];
                var skip = tag is 'S' or 's';
                var assume = char.IsLower(tag);
                if (skip || assume)
                    outs.Add(new Models.FrozenFile(entry.Substring(2), skip, assume));
            }

            return outs;
        }
    }

    /// <summary>
    ///     The untracked files one exclude pattern hides. A folder pattern reports the folder
    ///     once rather than everything under it -- node_modules/ would otherwise be walked and
    ///     listed file by file.
    ///
    ///     Only for a folder pattern: --directory collapses any folder whose untracked content
    ///     is all ignored, so a pattern naming the only file of a folder would be reported as
    ///     the folder, and seem to hide more than it does.
    ///
    ///     Only this pattern is applied -- no .gitignore, no info/exclude -- so the answer is
    ///     what this one line is responsible for. A file hidden by it and by a .gitignore as
    ///     well is listed here too; removing the line would not bring that one back.
    /// </summary>
    public class QueryExcludedFiles : Command
    {
        public QueryExcludedFiles(string repo, string pattern)
        {
            WorkingDirectory = repo;
            Context = repo;
            var directory = pattern.TrimEnd().EndsWith('/') ? "--directory --no-empty-directory " : string.Empty;
            Args = $"ls-files -z --others --ignored {directory}--exclude={pattern.Quoted()}";
            RaiseError = false;
        }

        public async Task<List<string>> GetResultAsync()
        {
            var rs = await ReadToEndAsync().ConfigureAwait(false);
            if (!rs.IsSuccess)
                return [];

            return [.. rs.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries)];
        }
    }
}
