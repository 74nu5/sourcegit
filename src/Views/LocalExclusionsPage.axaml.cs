using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    public partial class LocalExclusionsPage : UserControl
    {
        public static readonly Avalonia.Data.Converters.FuncValueConverter<int, bool> IsPageIndex =
            new(index => index == ViewModels.Repository.LOCAL_EXCLUSIONS_VIEW_INDEX);

        public LocalExclusionsPage()
        {
            InitializeComponent();
        }

        /// <summary>
        ///     Which files each pattern hides is only asked of git while the page is on screen,
        ///     so it is asked again each time the page comes back: the files may have changed
        ///     in the meantime, and nothing else would have noticed.
        /// </summary>
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            _host = Parent as Control;
            if (_host != null)
                _host.PropertyChanged += OnHostPropertyChanged;

            if (_host is { IsVisible: true })
                Reload();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (_host != null)
                _host.PropertyChanged -= OnHostPropertyChanged;
            _host = null;

            base.OnDetachedFromVisualTree(e);
        }

        private void OnHostPropertyChanged(object sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == IsVisibleProperty && _host is { IsVisible: true })
                Reload();
        }

        private void Reload()
        {
            if (DataContext is ViewModels.LocalExclusionsPage vm)
                _ = vm.ReloadAsync(true);
        }

        private void OnPatternContextRequested(object sender, ContextRequestedEventArgs e)
        {
            e.Handled = true;

            if (DataContext is not ViewModels.LocalExclusionsPage vm ||
                sender is not ListBox { SelectedItem: ViewModels.ExcludedPattern pattern })
                return;

            var menu = new ContextMenu();

            var stop = new MenuItem();
            stop.Header = App.Text("LocalExclusions.StopExcluding");
            stop.Icon = this.CreateMenuIcon("Icons.Eye");
            stop.Click += async (_, ev) =>
            {
                ev.Handled = true;
                await vm.RemovePatternAsync(pattern);
            };
            menu.Items.Add(stop);

            if (pattern.Files is [var only] && FindRepository() is { } repo)
            {
                var explore = new MenuItem();
                explore.Header = App.Text("RevealFile");
                explore.Icon = this.CreateMenuIcon("Icons.Explore");
                explore.Click += (_, ev) =>
                {
                    Native.OS.OpenInFileManager(Native.OS.GetAbsPath(repo.FullPath, only.TrimEnd('/')));
                    ev.Handled = true;
                };
                menu.Items.Add(explore);
            }

            var copy = new MenuItem();
            copy.Header = App.Text("LocalExclusions.CopyPattern");
            copy.Icon = this.CreateMenuIcon("Icons.Copy");
            copy.Click += async (_, ev) =>
            {
                ev.Handled = true;
                await this.CopyTextAsync(pattern.Pattern);
            };
            menu.Items.Add(new MenuItem { Header = "-" });
            menu.Items.Add(copy);

            menu.Open(sender as Control);
        }

        private void OnFrozenContextRequested(object sender, ContextRequestedEventArgs e)
        {
            e.Handled = true;

            if (DataContext is not ViewModels.LocalExclusionsPage vm ||
                sender is not ListBox { SelectedItem: Models.FrozenFile file })
                return;

            var menu = new ContextMenu();

            var stop = new MenuItem();
            stop.Header = App.Text("LocalExclusions.StopExcluding");
            stop.Icon = this.CreateMenuIcon("Icons.Eye");
            stop.Click += async (_, ev) =>
            {
                ev.Handled = true;
                await vm.UnfreezeAsync(file);
            };
            menu.Items.Add(stop);

            if (FindRepository() is { } repo)
            {
                var explore = new MenuItem();
                explore.Header = App.Text("RevealFile");
                explore.Icon = this.CreateMenuIcon("Icons.Explore");
                explore.Click += (_, ev) =>
                {
                    Native.OS.OpenInFileManager(Native.OS.GetAbsPath(repo.FullPath, file.Path));
                    ev.Handled = true;
                };
                menu.Items.Add(explore);
            }

            var copy = new MenuItem();
            copy.Header = App.Text("CopyPath");
            copy.Icon = this.CreateMenuIcon("Icons.Copy");
            copy.Click += async (_, ev) =>
            {
                ev.Handled = true;
                await this.CopyTextAsync(file.Path);
            };
            menu.Items.Add(new MenuItem { Header = "-" });
            menu.Items.Add(copy);

            menu.Open(sender as Control);
        }

        private ViewModels.Repository FindRepository()
        {
            return this.FindAncestorOfType<Repository>()?.DataContext as ViewModels.Repository;
        }

        private Control _host = null;
    }
}
