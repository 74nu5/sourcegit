using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace SourceGit.Models
{
    /// <summary>
    ///     A small rolling record of what this fork asks its forges, and what comes back.
    ///
    ///     Upstream keeps a crash file per crash and nothing else, which answers "what threw"
    ///     but never "what was it doing". Eight identical crash files told us the exception
    ///     and not one of them told us which repository, which forge, or how many loads were
    ///     in flight — and that is what a race needs.
    ///
    ///     So: one line per thing worth knowing, capped and rotated, and never a secret.
    ///     Tokens travel in headers and are never part of a URL, so an address can be written
    ///     down whole.
    /// </summary>
    public static class ForgeLog
    {
        /// <summary>
        ///     Small enough that reading it is a pleasure rather than a chore, and three
        ///     generations is a working day.
        /// </summary>
        public const long MAX_BYTES = 256 * 1024;
        public const int GENERATIONS = 3;

        /// <summary>
        ///     How many crash files upstream's handler is allowed to leave behind. It writes
        ///     one per crash and never removes any; eight in twenty minutes is what a loop
        ///     looks like, and a year of them is what a forgotten folder looks like.
        /// </summary>
        public const int KEEP_CRASHES = 20;

        public static void Line(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            Write($"{DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)}  {message}");
        }

        /// <summary>
        ///     Records what threw and where it was going, without the stack: the crash file
        ///     already has that, and what is missing there is the context.
        /// </summary>
        public static void Failed(string context, Exception ex)
        {
            if (ex == null)
                return;

            Line($"!! {context} :: {ex.GetType().Name}: {ex.Message}");
        }

        /// <summary>
        ///     Where the file is, so a person can be told rather than made to guess.
        /// </summary>
        public static string Path()
        {
            var dir = Directory();
            return dir == null ? null : System.IO.Path.Combine(dir, "forge.log");
        }

        private static void Write(string line)
        {
            // A logger that can break the thing it is watching is worse than no logger.
            try
            {
                var path = Path();
                if (path == null)
                    return;

                lock (LOCK)
                {
                    Rotate(path);
                    File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // Nothing to do about it, and nothing worth failing for.
            }
        }

        /// <summary>
        ///     forge.log becomes forge.1.log, which becomes forge.2.log, and the oldest goes.
        /// </summary>
        private static void Rotate(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < MAX_BYTES)
                return;

            var oldest = $"{path}.{GENERATIONS}";
            if (File.Exists(oldest))
                File.Delete(oldest);

            for (var i = GENERATIONS - 1; i >= 1; i--)
            {
                var from = $"{path}.{i}";
                if (File.Exists(from))
                    File.Move(from, $"{path}.{i + 1}", true);
            }

            File.Move(path, $"{path}.1", true);
        }

        private static string Directory()
        {
            if (_directory != null)
                return _directory.Length == 0 ? null : _directory;

            try
            {
                var root = Native.OS.BasicDirectories.CacheDir;
                if (string.IsNullOrEmpty(root))
                {
                    _directory = string.Empty;
                    return null;
                }

                var dir = System.IO.Path.Combine(root, "logs");
                System.IO.Directory.CreateDirectory(dir);
                _directory = dir;

                TrimCrashes(root);
                return dir;
            }
            catch
            {
                _directory = string.Empty;
                return null;
            }
        }

        /// <summary>
        ///     Keeps the newest crash files and forgets the rest. Done here because this is
        ///     the first thing that touches the folder, and because a crash handler is a poor
        ///     place to ask for housekeeping.
        /// </summary>
        private static void TrimCrashes(string root)
        {
            try
            {
                var dir = System.IO.Path.Combine(root, "crashes");
                if (!System.IO.Directory.Exists(dir))
                    return;

                var files = new List<FileInfo>();
                foreach (var path in System.IO.Directory.GetFiles(dir, "*.log"))
                    files.Add(new FileInfo(path));

                if (files.Count <= KEEP_CRASHES)
                    return;

                files.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                for (var i = KEEP_CRASHES; i < files.Count; i++)
                    files[i].Delete();
            }
            catch
            {
                // Housekeeping is never worth an exception.
            }
        }

        private static readonly Lock LOCK = new();
        private static string _directory = null;
    }
}
