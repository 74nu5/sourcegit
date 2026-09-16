using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    /// <summary>
    ///     What the update window shows once the user has asked for the new version.
    ///
    ///     It has four resting states and one moving one, and they are exclusive: working,
    ///     then exactly one of installed, downloaded-only, or refused. Nothing here decides
    ///     anything about the update itself -- that belongs to the models, which can be
    ///     exercised without a window -- it only turns their outcome into something readable
    ///     and offers the one or two actions that outcome allows.
    /// </summary>
    public class SelfUpdateInstall : ObservableObject
    {
        public SelfUpdateInstall(Models.Version release)
        {
            _release = release;
            _target = Models.SelfUpdatePackage.ResolveForThisProcess();
        }

        public bool IsWorking
        {
            get => _isWorking;
            private set => SetProperty(ref _isWorking, value);
        }

        /// <summary>
        ///     Zero while a step cannot say how far along it is, which is every step but the
        ///     download. The bar shows itself as indeterminate then rather than sitting at a
        ///     figure it made up.
        /// </summary>
        public double Progress
        {
            get => _progress;
            private set => SetProperty(ref _progress, value);
        }

        public bool IsIndeterminate
        {
            get => _isIndeterminate;
            private set => SetProperty(ref _isIndeterminate, value);
        }

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        /// <summary>The files were replaced; only a restart is left.</summary>
        public bool IsInstalled
        {
            get => _isInstalled;
            private set => SetProperty(ref _isInstalled, value);
        }

        /// <summary>
        ///     Verified and sitting in the cache, but this installation is not ours to
        ///     replace. The user is told where the file is and why it stops here.
        /// </summary>
        public bool IsDownloadedOnly
        {
            get => _isDownloadedOnly;
            private set
            {
                if (SetProperty(ref _isDownloadedOnly, value))
                    OnPropertyChanged(nameof(ShowManualActions));
            }
        }

        /// <summary>
        ///     These files belong to a package manager. Nothing is downloaded and nothing is
        ///     touched; the user is told to update the way they installed.
        /// </summary>
        public bool IsManagedExternally
        {
            get => _isManagedExternally;
            private set
            {
                if (SetProperty(ref _isManagedExternally, value))
                    OnPropertyChanged(nameof(ShowManualActions));
            }
        }

        public bool HasFailed
        {
            get => _hasFailed;
            private set
            {
                if (SetProperty(ref _hasFailed, value))
                    OnPropertyChanged(nameof(ShowManualActions));
            }
        }

        /// <summary>The three outcomes that leave the user something to do by hand.</summary>
        public bool ShowManualActions => IsDownloadedOnly || IsManagedExternally || HasFailed;

        public string Details
        {
            get => _details;
            private set => SetProperty(ref _details, value);
        }

        /// <summary>
        ///     Whether there is a verified file to show. False until the download has been
        ///     checked, and false again if it was discarded for failing that check.
        /// </summary>
        public bool CanRevealPackage
        {
            get => _packagePath.Length > 0;
        }

        public async Task RunAsync()
        {
            IsWorking = true;
            HasFailed = false;
            IsInstalled = false;
            IsDownloadedOnly = false;
            IsManagedExternally = false;
            Details = string.Empty;

            // Decided before anything is fetched: a package manager's files are not ours to
            // replace, so there is no package of ours to download either.
            if (_target.Support == Models.SelfUpdateSupport.ManagedExternally)
            {
                StatusText = App.Text("SelfUpdate.Install.ManagedExternally");
                Details = _target.Reason;
                IsManagedExternally = true;
                IsWorking = false;
                return;
            }

            var progress = new Progress<Models.SelfUpdateReport>(OnProgress);
            var installer = new Models.SelfUpdateInstaller(_target, _release);

            try
            {
                await installer.DownloadAndVerifyAsync(progress, CancellationToken.None);
                SetPackagePath(installer.VerifiedPackagePath);

                if (_target.Support == Models.SelfUpdateSupport.InPlace)
                {
                    // Off the UI thread: the swap is a few dozen megabytes of file copying,
                    // and running it inline would freeze the window it is reporting into.
                    await Task.Run(() => installer.Install(progress));

                    StatusText = App.Text("SelfUpdate.Install.Done");
                    IsInstalled = true;
                }
                else
                {
                    StatusText = App.Text("SelfUpdate.Install.DownloadedOnly");
                    Details = _target.Reason;
                    IsDownloadedOnly = true;
                }
            }
            catch (Models.SelfUpdateRefused refused)
            {
                Fail(refused.Message);
            }
            catch (Exception e)
            {
                Fail(e.InnerException?.Message ?? e.Message);
            }
            finally
            {
                IsWorking = false;
            }
        }

        /// <summary>
        ///     Quits and comes back on the version that was just installed.
        ///
        ///     The order matters and is enforced by <see cref="App.RestartInto"/>: this
        ///     process is single-instance, so a new one started while this one still holds
        ///     the lock would hand its request over and exit, leaving the old build running
        ///     and the user convinced the update did nothing.
        /// </summary>
        public void Restart()
        {
            App.RestartInto(_target.ExecutablePath);
        }

        public void RevealPackage()
        {
            if (_packagePath.Length > 0)
                Native.OS.OpenInFileManager(_packagePath);
        }

        public void OpenReleasePage()
        {
            Native.OS.OpenBrowser(Models.ForkVersion.ReleaseNotesUrl(_release.TagName));
        }

        private void OnProgress(Models.SelfUpdateReport report)
        {
            switch (report.Stage)
            {
                case Models.SelfUpdateStage.Downloading:
                    StatusText = App.Text("SelfUpdate.Install.Downloading");
                    IsIndeterminate = report.Fraction <= 0;
                    Progress = report.Fraction * 100;
                    break;
                case Models.SelfUpdateStage.Verifying:
                    StatusText = App.Text("SelfUpdate.Install.Verifying");
                    IsIndeterminate = true;
                    break;
                case Models.SelfUpdateStage.Extracting:
                    StatusText = App.Text("SelfUpdate.Install.Extracting");
                    IsIndeterminate = true;
                    break;
                case Models.SelfUpdateStage.Installing:
                    StatusText = App.Text("SelfUpdate.Install.Installing");
                    IsIndeterminate = true;
                    break;
                default:
                    StatusText = App.Text("SelfUpdate.Install.Preparing");
                    IsIndeterminate = true;
                    break;
            }
        }

        private void Fail(string reason)
        {
            StatusText = App.Text("SelfUpdate.Install.Failed");
            Details = reason;
            HasFailed = true;

            // A package that was verified before a later step failed is still good, so the
            // user keeps the option of opening it -- but only if it is actually on disk.
            SetPackagePath(_packagePath.Length > 0 && File.Exists(_packagePath) ? _packagePath : string.Empty);
        }

        private void SetPackagePath(string path)
        {
            _packagePath = path ?? string.Empty;
            OnPropertyChanged(nameof(CanRevealPackage));
        }

        private readonly Models.Version _release;
        private readonly Models.SelfUpdateTarget _target;

        private string _packagePath = string.Empty;
        private bool _isWorking = false;
        private bool _isInstalled = false;
        private bool _isDownloadedOnly = false;
        private bool _isManagedExternally = false;
        private bool _hasFailed = false;
        private bool _isIndeterminate = true;
        private double _progress = 0;
        private string _statusText = string.Empty;
        private string _details = string.Empty;
    }
}
