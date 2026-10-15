using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    /// <summary>
    ///     One line of info/exclude, and the untracked files it hides right now.
    /// </summary>
    public class ExcludedPattern : ObservableObject
    {
        // Enough to recognise what a pattern catches; a pattern such as node_modules/ is
        // listed as the folder anyway, so long lists are rare.
        public const int MAX_FILES = 50;

        public string Pattern { get; }

        /// <summary>
        ///     A "!pattern" line takes files back out of the exclusions above it. It hides
        ///     nothing, so it has no files of its own.
        /// </summary>
        public bool IsNegation => Pattern.StartsWith('!');

        public List<string> Files
        {
            get => _files;
            private set => SetProperty(ref _files, value);
        }

        public int MoreFiles
        {
            get => _moreFiles;
            private set => SetProperty(ref _moreFiles, value);
        }

        public bool HasMoreFiles => _moreFiles > 0;

        public string MoreFilesText => App.Text("LocalExclusions.MoreFiles", _moreFiles);

        public ExcludedPattern(string pattern)
        {
            Pattern = pattern;
        }

        internal void SetFiles(List<string> files)
        {
            if (files.Count > MAX_FILES)
            {
                MoreFiles = files.Count - MAX_FILES;
                Files = files.GetRange(0, MAX_FILES);
            }
            else
            {
                MoreFiles = 0;
                Files = files;
            }

            OnPropertyChanged(nameof(HasMoreFiles));
            OnPropertyChanged(nameof(MoreFilesText));
        }

        private List<string> _files = [];
        private int _moreFiles = 0;
    }

    /// <summary>
    ///     The page listing what this clone hides from its local changes without any
    ///     .gitignore: the patterns of info/exclude, and the tracked files whose modification
    ///     git has been told to ignore.
    ///
    ///     The second list is the one that matters most. A tracked file excluded and then
    ///     forgotten makes a pull fail weeks later, with git naming a file that the changes
    ///     list does not show -- the page is where it can be seen, and given back.
    /// </summary>
    public class LocalExclusionsPage : ObservableObject
    {
        public string ExcludeFile => Models.LocalExclude.FileOf(_repo.GitCommonDir);

        public List<ExcludedPattern> Patterns
        {
            get => _patterns;
            private set => SetProperty(ref _patterns, value);
        }

        public List<Models.FrozenFile> FrozenFiles
        {
            get => _frozenFiles;
            private set => SetProperty(ref _frozenFiles, value);
        }

        /// <summary>
        ///     In a sparse checkout, git sets skip-worktree on every file outside the sparse
        ///     cone. Those are not listed: they are not exclusions, and clearing the bit would
        ///     check them out.
        /// </summary>
        public bool IsSparseCheckout
        {
            get => _isSparseCheckout;
            private set => SetProperty(ref _isSparseCheckout, value);
        }

        public bool IsEmpty => _patterns.Count == 0 && _frozenFiles.Count == 0;

        public int Count => _patterns.Count + _frozenFiles.Count;

        public LocalExclusionsPage(Repository repo)
        {
            _repo = repo;
        }

        /// <summary>
        ///     Reads both lists again; with <paramref name="withFiles"/>, also asks git which
        ///     files each pattern hides -- one command per pattern, so only while the page is
        ///     on screen.
        ///
        ///     A newer call supersedes an older one still running: only the last result lands.
        /// </summary>
        public async Task ReloadAsync(bool withFiles)
        {
            var version = Interlocked.Increment(ref _version);
            var repo = _repo.FullPath;
            var file = ExcludeFile;

            var (patterns, frozen, sparse) = await Task.Run(async () =>
            {
                var lines = Models.LocalExclude.ReadPatterns(file);
                var items = new List<ExcludedPattern>(lines.Count);
                foreach (var line in lines)
                    items.Add(new ExcludedPattern(line));

                var sparseValue = await new Commands.Config(repo).GetAsync("core.sparseCheckout").ConfigureAwait(false);
                var isSparse = Models.LocalExclude.IsGitTrue(sparseValue);

                var all = await new Commands.QueryFrozenFiles(repo).GetResultAsync().ConfigureAwait(false);
                var kept = new List<Models.FrozenFile>(all.Count);
                foreach (var f in all)
                {
                    if (isSparse && f.IsSkipWorktree && !f.IsAssumeUnchanged)
                        continue;
                    kept.Add(f);
                }

                if (withFiles)
                {
                    foreach (var item in items)
                    {
                        if (item.IsNegation)
                            continue;

                        var files = await new Commands.QueryExcludedFiles(repo, item.Pattern).GetResultAsync().ConfigureAwait(false);
                        item.SetFiles(files);
                    }
                }

                return (items, kept, isSparse);
            }).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (version != _version)
                    return;

                Patterns = patterns;
                FrozenFiles = frozen;
                IsSparseCheckout = sparse;
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(Count));
                _repo.OnLocalExclusionsReloaded(patterns.Count + frozen.Count);
            });
        }

        /// <summary>
        ///     Deletes the pattern's line from info/exclude: what it hid comes back into the
        ///     local changes, unless something else -- a .gitignore -- hides it too.
        /// </summary>
        public async Task RemovePatternAsync(ExcludedPattern pattern)
        {
            if (pattern == null)
                return;

            var log = _repo.CreateLog("Stop Excluding Locally");
            log.AppendLine($"$ remove from {ExcludeFile}");
            log.AppendLine(pattern.Pattern);

            try
            {
                await Models.LocalExclude.RemovePatternAsync(ExcludeFile, pattern.Pattern);
            }
            catch (System.Exception e)
            {
                log.AppendLine(e.Message);
                _repo.SendNotification($"Failed to write {ExcludeFile}: {e.Message}", true);
            }

            log.Complete();
            await AfterChangeAsync();
        }

        /// <summary>
        ///     Clears whichever bits the file carries; its local modification is listed in the
        ///     local changes again.
        /// </summary>
        public async Task UnfreezeAsync(Models.FrozenFile file)
        {
            if (file == null)
                return;

            var log = _repo.CreateLog("Stop Excluding Locally");

            if (file.IsSkipWorktree)
                await Commands.SkipWorktree.RunAsync(_repo.FullPath, [file.Path], false, log);
            if (file.IsAssumeUnchanged)
                await new Commands.AssumeUnchanged(_repo.FullPath, file.Path, false).Use(log).ExecAsync();

            log.Complete();
            await AfterChangeAsync();
        }

        private async Task AfterChangeAsync()
        {
            _repo.MarkWorkingCopyDirtyManually();
            await ReloadAsync(true);
        }

        private readonly Repository _repo;
        private List<ExcludedPattern> _patterns = [];
        private List<Models.FrozenFile> _frozenFiles = [];
        private bool _isSparseCheckout = false;
        private int _version = 0;
    }
}
