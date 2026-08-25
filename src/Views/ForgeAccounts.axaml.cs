using System;
using System.Threading;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    /// <summary>
    ///     The forge accounts panel of the preferences window.
    ///
    ///     It lives in its own control rather than in Preferences.axaml because that file is
    ///     upstream's busiest view: a tab declared there would be re-merged on every rebase.
    ///     Here, the window only holds the six lines that point at this control.
    /// </summary>
    public partial class ForgeAccounts : UserControl
    {
        public static readonly DirectProperty<ForgeAccounts, Models.ForgeAccount> SelectedAccountProperty =
            AvaloniaProperty.RegisterDirect<ForgeAccounts, Models.ForgeAccount>(
                nameof(SelectedAccount),
                static o => o.SelectedAccount,
                static (o, v) => o.SelectedAccount = v);

        public static readonly DirectProperty<ForgeAccounts, bool> IsTestingProperty =
            AvaloniaProperty.RegisterDirect<ForgeAccounts, bool>(
                nameof(IsTesting),
                static o => o.IsTesting);

        public static readonly DirectProperty<ForgeAccounts, string> TestMessageProperty =
            AvaloniaProperty.RegisterDirect<ForgeAccounts, string>(
                nameof(TestMessage),
                static o => o.TestMessage);

        public static readonly DirectProperty<ForgeAccounts, bool?> TestSucceededProperty =
            AvaloniaProperty.RegisterDirect<ForgeAccounts, bool?>(
                nameof(TestSucceeded),
                static o => o.TestSucceeded);

        /// <summary>
        ///     The outcome of the last test lives on the panel rather than on the account: it
        ///     says something about a moment, not about the credentials, and has no business
        ///     being written to the preferences file.
        /// </summary>
        public Models.ForgeAccount SelectedAccount
        {
            get => _selectedAccount;
            set
            {
                if (SetAndRaise(SelectedAccountProperty, ref _selectedAccount, value))
                    ClearTestResult();
            }
        }

        public bool IsTesting
        {
            get => _isTesting;
            private set => SetAndRaise(IsTestingProperty, ref _isTesting, value);
        }

        public string TestMessage
        {
            get => _testMessage;
            private set => SetAndRaise(TestMessageProperty, ref _testMessage, value);
        }

        /// <summary>
        ///     Null while nothing has been asked or an answer is on its way, so that the
        ///     message can be grey rather than prematurely red.
        /// </summary>
        public bool? TestSucceeded
        {
            get => _testSucceeded;
            private set => SetAndRaise(TestSucceededProperty, ref _testSucceeded, value);
        }

        public ForgeAccounts()
        {
            InitializeComponent();
        }

        private void OnAddAzureDevOpsAccount(object sender, RoutedEventArgs e)
        {
            Add(Models.ForgeKind.AzureDevOps, e);
        }

        private void OnAddGitHubAccount(object sender, RoutedEventArgs e)
        {
            Add(Models.ForgeKind.GitHub, e);
        }

        private void OnAddGitLabAccount(object sender, RoutedEventArgs e)
        {
            Add(Models.ForgeKind.GitLab, e);
        }

        private void OnAddGiteaAccount(object sender, RoutedEventArgs e)
        {
            Add(Models.ForgeKind.Gitea, e);
        }

        private void OnAddBitbucketAccount(object sender, RoutedEventArgs e)
        {
            Add(Models.ForgeKind.Bitbucket, e);
        }

        private void Add(Models.ForgeKind kind, RoutedEventArgs e)
        {
            var account = Models.ForgeAccount.CreateFor(kind);
            ViewModels.Preferences.Instance.ForgeAccounts.Add(account);
            SelectedAccount = account;

            e.Handled = true;
        }

        private void OnRemoveSelectedAccount(object sender, RoutedEventArgs e)
        {
            if (SelectedAccount == null)
                return;

            ViewModels.Preferences.Instance.ForgeAccounts.Remove(SelectedAccount);
            SelectedAccount = null;
            e.Handled = true;
        }

        /// <summary>
        ///     The only thing in this fork that reaches the network on its own account, and it
        ///     does so once, when asked.
        /// </summary>
        private async void OnTestConnection(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            var account = SelectedAccount;
            if (account == null || IsTesting)
                return;

            // A second click, or moving to another account, abandons the answer to the first.
            var cancel = new CancellationTokenSource();
            var previous = Interlocked.Exchange(ref _testCancellation, cancel);
            previous?.Cancel();
            previous?.Dispose();

            IsTesting = true;
            TestSucceeded = null;
            TestMessage = App.Text("Preferences.Forge.Test.Running");

            Models.ForgeTestResult result;
            try
            {
                result = await Models.ForgeConnection.TestAsync(account, cancel.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                // ConfigureAwait(true) brought us back to the UI thread.
                if (Interlocked.CompareExchange(ref _testCancellation, null, cancel) == cancel)
                {
                    IsTesting = false;
                    cancel.Dispose();
                }
            }

            // The account may have changed under us while the request was in flight.
            if (!ReferenceEquals(SelectedAccount, account))
                return;

            TestSucceeded = result.IsOk;
            TestMessage = Describe(result);
        }

        /// <summary>
        ///     One sentence for the outcome, and whatever the forge said appended to it. The
        ///     model never builds this: it does not know which language the user reads.
        /// </summary>
        private static string Describe(Models.ForgeTestResult result)
        {
            var sentence = App.Text($"Preferences.Forge.Test.{result.Outcome}");
            return string.IsNullOrEmpty(result.Detail) ? sentence : $"{sentence} ({result.Detail})";
        }

        private void ClearTestResult()
        {
            var pending = Interlocked.Exchange(ref _testCancellation, null);
            if (pending != null)
            {
                pending.Cancel();
                pending.Dispose();
            }

            IsTesting = false;
            TestSucceeded = null;
            TestMessage = string.Empty;
        }

        private Models.ForgeAccount _selectedAccount = null;
        private bool _isTesting = false;
        private string _testMessage = string.Empty;
        private bool? _testSucceeded = null;
        private CancellationTokenSource _testCancellation = null;
    }
}
