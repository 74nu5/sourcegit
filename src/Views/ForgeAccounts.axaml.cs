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

        public Models.ForgeAccount SelectedAccount
        {
            get => _selectedAccount;
            set => SetAndRaise(SelectedAccountProperty, ref _selectedAccount, value);
        }

        public ForgeAccounts()
        {
            InitializeComponent();
        }

        private void OnAddAccount(object sender, RoutedEventArgs e)
        {
            var account = new Models.ForgeAccount() { Host = "dev.azure.com" };
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

        private Models.ForgeAccount _selectedAccount = null;
    }
}
