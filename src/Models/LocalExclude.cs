using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace SourceGit.Models
{
    /// <summary>
    ///     What "Exclude Locally" will do with a selection of unstaged changes, decided before
    ///     anything is touched.
    ///
    ///     Git hides the two kinds of file by two unrelated means. An untracked file is hidden by
    ///     a pattern in info/exclude -- an ignore file of this clone alone, never committed. A
    ///     tracked file is out of reach of every ignore rule; what hides its modification is the
    ///     skip-worktree bit in the index, which says "the copy on disk is mine, leave it".
    /// </summary>
    public class LocalExcludePlan
    {
        public List<string> Patterns { get; } = [];
        public List<string> SkipWorktree { get; } = [];

        /// <summary>
        ///     Why nothing can be done, or null. Set when a selected file has staged changes: the
        ///     skip-worktree bit hides the file on disk, not the index, so what is staged would
        ///     still go out with the next commit -- the opposite of what was asked.
        /// </summary>
        public string Refusal { get; private set; }

        public bool IsEmpty => Patterns.Count == 0 && SkipWorktree.Count == 0;

        /// <summary>
        ///     Plans the exclusion of <paramref name="changes"/>, or of the whole of
        ///     <paramref name="folder"/> when one is given. A folder is only ever offered when
        ///     everything in it is untracked: a pattern does nothing to the tracked files under it.
        /// </summary>
        public static LocalExcludePlan Build(IList<Change> changes, string folder = null)
        {
            var plan = new LocalExcludePlan();

            foreach (var c in changes)
            {
                if (c.IsConflicted)
                {
                    plan.Refusal = "conflicted";
                    return plan;
                }

                if (c.WorkTree != ChangeState.Untracked && c.Index != ChangeState.None)
                {
                    plan.Refusal = "staged";
                    return plan;
                }
            }

            if (!string.IsNullOrEmpty(folder))
            {
                plan.Patterns.Add(LocalExclude.ToPattern(folder, true));
                return plan;
            }

            foreach (var c in changes)
            {
                if (c.WorkTree == ChangeState.Untracked)
                    plan.Patterns.Add(LocalExclude.ToPattern(c.Path, false));
                else if (c.WorkTree != ChangeState.None)
                    plan.SkipWorktree.Add(c.Path);
            }

            return plan;
        }
    }

    /// <summary>
    ///     A tracked file whose local modification git ignores.
    /// </summary>
    public record FrozenFile(string Path, bool IsSkipWorktree, bool IsAssumeUnchanged)
    {
        /// <summary>
        ///     The bit as git names it: the two differ in what git may do with the file, and
        ///     someone reading a failed pull will look for that word.
        /// </summary>
        public string Kind => IsSkipWorktree
            ? (IsAssumeUnchanged ? "skip-worktree, assume-unchanged" : "skip-worktree")
            : "assume-unchanged";
    }

    public static class LocalExclude
    {
        /// <summary>
        ///     Whether a git boolean read with plain `git config <key>` is set: git takes yes,
        ///     on and 1 for true as well, in any case.
        /// </summary>
        public static bool IsGitTrue(string value)
        {
            return value?.Trim().ToLowerInvariant() is "true" or "yes" or "on" or "1";
        }

        /// <summary>
        ///     The patterns of an exclude file, in order: every line that is neither blank nor
        ///     a comment, as git reads it. Git's own template ships with comment lines only,
        ///     which is why a fresh clone counts no exclusion.
        /// </summary>
        public static List<string> ReadPatterns(string file)
        {
            var patterns = new List<string>();
            if (!File.Exists(file))
                return patterns;

            try
            {
                foreach (var raw in File.ReadAllLines(file))
                {
                    var line = raw.TrimEnd('\r');
                    if (line.Length == 0 || line[0] == '#' || line.Trim().Length == 0)
                        continue;

                    patterns.Add(line);
                }
            }
            catch
            {
                // Unreadable: nothing is listed, and nothing is lost by that.
            }

            return patterns;
        }

        /// <summary>
        ///     Removes every line that is exactly <paramref name="pattern"/>, keeping the others,
        ///     their comments and their line endings as they were.
        /// </summary>
        public static async Task<bool> RemovePatternAsync(string file, string pattern)
        {
            if (!File.Exists(file))
                return false;

            var text = await File.ReadAllTextAsync(file).ConfigureAwait(false);
            var builder = new StringBuilder(text.Length);
            var removed = false;
            var start = 0;

            while (start < text.Length)
            {
                var end = text.IndexOf('\n', start);
                var next = end < 0 ? text.Length : end + 1;
                var line = text.Substring(start, (end < 0 ? text.Length : end) - start).TrimEnd('\r');

                if (line.Equals(pattern, StringComparison.Ordinal))
                    removed = true;
                else
                    builder.Append(text, start, next - start);

                start = next;
            }

            if (!removed)
                return false;

            var tmp = $"{file}.tmp";
            await File.WriteAllTextAsync(tmp, builder.ToString(), new UTF8Encoding(false)).ConfigureAwait(false);
            File.Move(tmp, file, true);
            return true;
        }

        /// <summary>
        ///     The folder that every one of <paramref name="changes"/> sits in, when they are all
        ///     untracked and not at the root -- the only case where excluding the folder is the
        ///     same request as excluding the files, plus whatever lands there next.
        /// </summary>
        public static string CommonUntrackedFolder(IList<Change> changes)
        {
            string folder = null;
            foreach (var c in changes)
            {
                if (c.WorkTree != ChangeState.Untracked)
                    return null;

                var idx = c.Path.LastIndexOf('/');
                if (idx <= 0)
                    return null;

                var dir = c.Path.Substring(0, idx);
                if (folder == null)
                    folder = dir;
                else if (!folder.Equals(dir, StringComparison.Ordinal))
                    return null;
            }

            return folder;
        }

        /// <summary>
        ///     The pattern that matches <paramref name="path"/> and nothing else.
        ///
        ///     Anchored with a leading slash, so "out.html" does not also hide every out.html
        ///     in every subfolder -- and so the pattern never starts with '#' or '!', which would
        ///     make it a comment or a negation. Wildcards and brackets are escaped, because a
        ///     file named "[draft].html" is otherwise the pattern for d.html, r.html, a.html...
        ///     Trailing spaces are escaped too: git strips them unless they are.
        /// </summary>
        public static string ToPattern(string path, bool isFolder)
        {
            var normalized = path.Replace('\\', '/').Trim('/');
            var builder = new StringBuilder("/", normalized.Length + 8);

            foreach (var ch in normalized)
            {
                if (ch is '*' or '?' or '[' or '\\')
                    builder.Append('\\');
                builder.Append(ch);
            }

            var end = builder.Length;
            while (end > 1 && builder[end - 1] == ' ')
                end--;
            if (end < builder.Length)
            {
                var spaces = builder.Length - end;
                builder.Length = end;
                for (var i = 0; i < spaces; i++)
                    builder.Append("\\ ");
            }

            if (isFolder)
                builder.Append('/');

            return builder.ToString();
        }

        /// <summary>
        ///     The info/exclude git reads for this clone. It lives in the common git directory,
        ///     never in a linked worktree's own: git does not read one there.
        /// </summary>
        public static string FileOf(string gitCommonDir)
        {
            return Path.Combine(gitCommonDir, "info", "exclude");
        }

        /// <summary>
        ///     Appends the patterns the file does not hold yet, after making sure the last line
        ///     it already has is terminated. Returns the ones actually written.
        /// </summary>
        public static async Task<List<string>> AppendAsync(string file, IEnumerable<string> patterns)
        {
            var existing = new HashSet<string>(StringComparer.Ordinal);
            var text = string.Empty;
            if (File.Exists(file))
            {
                text = await File.ReadAllTextAsync(file).ConfigureAwait(false);
                foreach (var line in text.Split('\n'))
                    existing.Add(line.TrimEnd('\r'));
            }

            var added = new List<string>();
            foreach (var p in patterns)
            {
                if (existing.Add(p))
                    added.Add(p);
            }

            if (added.Count == 0)
                return added;

            var builder = new StringBuilder();
            if (text.Length > 0 && !text.EndsWith('\n'))
                builder.Append('\n');
            foreach (var p in added)
                builder.Append(p).Append('\n');

            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.AppendAllTextAsync(file, builder.ToString(), new UTF8Encoding(false)).ConfigureAwait(false);
            return added;
        }
    }
}
