using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    /// <summary>
    ///     The full name of every branch, remote branch and tag, and nothing else.
    ///
    ///     What the history filters check their references against before handing them to
    ///     git log. The branch list the sidebar holds cannot serve: the history is loaded
    ///     before it, so on opening a repository it is still empty.
    /// </summary>
    public class QueryRefNames : Command
    {
        public QueryRefNames(string repo)
        {
            WorkingDirectory = repo;
            Context = repo;
            Args = "for-each-ref --format=%(refname) refs/heads refs/remotes refs/tags";
            RaiseError = false;
        }

        /// <summary>
        ///     The names, or null when git could not list them -- which the caller must read
        ///     as "unknown", not as "none exist".
        /// </summary>
        public async Task<HashSet<string>> GetResultAsync()
        {
            var rs = await ReadToEndAsync().ConfigureAwait(false);
            if (!rs.IsSuccess)
                return null;

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in rs.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                names.Add(line.Trim());

            return names;
        }
    }
}
