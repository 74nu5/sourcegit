using System;
using System.Collections.Generic;
using System.Threading;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    /// <summary>
    ///     The pull requests of a repository, as a section of the left panel.
    ///
    ///     It shows itself only when a configured account covers one of the repository's
    ///     remotes; with no forge there is nothing to list, and an empty section asking to be
    ///     ignored is worse than none.
    /// </summary>
    public partial class PullRequestsView : UserControl
    {
        public static readonly StyledProperty<bool> IsExpandedProperty =
            AvaloniaProperty.Register<PullRequestsView, bool>(nameof(IsExpanded), true);

        public bool IsExpanded
        {
            get => GetValue(IsExpandedProperty);
            set => SetValue(IsExpandedProperty, value);
        }

        /// <summary>
        ///     Show only what this token's owner opened.
        /// </summary>
        public static readonly StyledProperty<bool> MineOnlyProperty =
            AvaloniaProperty.Register<PullRequestsView, bool>(nameof(MineOnly));

        public bool MineOnly
        {
            get => GetValue(MineOnlyProperty);
            set => SetValue(MineOnlyProperty, value);
        }

        public static readonly DirectProperty<PullRequestsView, AvaloniaList<Models.PullRequest>> VisibleProperty =
            AvaloniaProperty.RegisterDirect<PullRequestsView, AvaloniaList<Models.PullRequest>>(
                nameof(Visible),
                static o => o.Visible);

        public AvaloniaList<Models.PullRequest> Visible
        {
            get => _visible;
            private set => SetAndRaise(VisibleProperty, ref _visible, value);
        }

        public static readonly DirectProperty<PullRequestsView, string> CounterProperty =
            AvaloniaProperty.RegisterDirect<PullRequestsView, string>(
                nameof(Counter),
                static o => o.Counter);

        public string Counter
        {
            get => _counter;
            private set => SetAndRaise(CounterProperty, ref _counter, value);
        }

        /// <summary>
        ///     How tall the list wants to be, so the panel can reserve room for it.
        /// </summary>
        public double DesiredListHeight => IsExpanded ? Math.Min(_visible.Count, 8) * 24.0 : 0;

        public PullRequestsView()
        {
            InitializeComponent();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            Watch();
            Reload(false);
        }

        /// <summary>
        ///     Switching tabs does not build a new panel: the same views are kept and handed
        ///     another repository. Without this the section would keep showing the requests of
        ///     whichever repository happened to be open when it was created.
        /// </summary>
        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            _all = [];
            _me = [];
            Apply();

            Watch();
            Reload(false);
        }

        /// <summary>
        ///     The panel is built before the repository has read its remotes, so the first
        ///     look always finds none and would hide this section for good. Listening is what
        ///     makes it appear once they arrive.
        /// </summary>
        private void Watch()
        {
            if (_watched != null)
            {
                _watched.PropertyChanged -= OnRepositoryChanged;
                _watched = null;
            }

            if (DataContext is ViewModels.Repository repo)
            {
                _watched = repo;
                _watched.PropertyChanged += OnRepositoryChanged;
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);

            if (_watched != null)
            {
                _watched.PropertyChanged -= OnRepositoryChanged;
                _watched = null;
            }

            // Cancelled, never disposed: an answer may still be on its way to it, and
            // reading a token from a disposed source throws.
            Interlocked.Exchange(ref _pending, null)?.Cancel();
        }

        private void OnRepositoryChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModels.Repository.Remotes))
                Reload(false);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == MineOnlyProperty)
                Apply();
            else if (change.Property == IsExpandedProperty)
                Announce();
        }

        /// <summary>
        ///     Flipped here rather than by a two-way binding onto this control's own property,
        ///     which never wrote back from inside the header button.
        /// </summary>
        private void OnToggleMineOnly(object sender, RoutedEventArgs e)
        {
            MineOnly = !MineOnly;
            e.Handled = true;
        }

        private void OnRefresh(object sender, RoutedEventArgs e)
        {
            Reload(true);
            e.Handled = true;
        }

        /// <summary>
        ///     The same card the branch mark shows, opened on demand rather than on hover.
        ///
        ///     A row this narrow shows a truncated title and nothing else; what a person wants
        ///     here is to read the whole of it without it vanishing when the pointer moves.
        ///
        ///     Built here rather than declared in XAML because a flyout is not in the visual
        ///     tree of the button that owns it, so a binding inside it has no data context to
        ///     inherit — the request would arrive null and the card would be empty.
        /// </summary>
        private void OnShowCard(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            if (sender is not Control { DataContext: Models.PullRequest pr } anchor)
                return;

            var flyout = new Flyout()
            {
                Content = PullRequestCard.Build(pr),
                Placement = PlacementMode.RightEdgeAlignedTop,
            };

            FlyoutBase.SetAttachedFlyout(anchor, flyout);
            FlyoutBase.ShowAttachedFlyout(anchor);
        }

        private void OnOpenPullRequest(object sender, TappedEventArgs e)
        {
            if (sender is Control { DataContext: Models.PullRequest pr } && !string.IsNullOrEmpty(pr.Url))
                Native.OS.OpenBrowser(pr.Url);

            e.Handled = true;
        }

        /// <summary>
        ///     Asks the repository for its requests, and for who we are on each forge so that
        ///     "mine" can mean something. Refreshing forgets first, which is the difference
        ///     between the button and simply drawing again.
        /// </summary>
        private async void Reload(bool forget)
        {
            // Nothing may escape: this runs from an event handler, where an exception is
            // not caught by anyone and takes the whole window down with it.
            try
            {
                if (DataContext is not ViewModels.Repository repo)
                {
                    Interlocked.Exchange(ref _pending, null)?.Cancel();
                    return;
                }

                // No account covering this repository means nothing to list, and an empty
                // section asking to be ignored is worse than none.
                IsVisible = repo.HasForge();
                if (!IsVisible)
                {
                    Interlocked.Exchange(ref _pending, null)?.Cancel();
                    Announce();
                    return;
                }

                if (forget)
                    repo.InvalidatePullRequests();

                var cancel = new CancellationTokenSource();
                Interlocked.Exchange(ref _pending, cancel)?.Cancel();

                // Copied once. Asking the source for it again after an await is what broke:
                // switching tabs mid-load replaced this one, and the token of a source
                // somebody else disposed throws rather than answering.
                var token = cancel.Token;

                var all = await repo.GetPullRequestListAsync(token).ConfigureAwait(true);
                var me = await repo.GetForgeIdentitiesAsync(token).ConfigureAwait(true);

                if (token.IsCancellationRequested || !ReferenceEquals(_pending, cancel))
                    return;

                _all = all;
                _me = me;
                Apply();
            }
            catch (Exception ex)
            {
                Native.OS.LogException(ex);
            }
        }

        private void Apply()
        {
            var kept = new AvaloniaList<Models.PullRequest>();

            foreach (var pr in _all)
            {
                if (MineOnly && !IsMine(pr))
                    continue;

                kept.Add(pr);
            }

            Visible = kept;
            Counter = kept.Count > 0 ? $"({kept.Count})" : string.Empty;
            Announce();
        }

        /// <summary>
        ///     Mine when any of this repository's forges says its token belongs to the author.
        ///     A forge that cannot say who we are simply never claims anything, which is why
        ///     the filter empties rather than lying.
        /// </summary>
        private bool IsMine(Models.PullRequest pr)
        {
            foreach (var user in _me)
            {
                if (user.Wrote(pr))
                    return true;
            }

            return false;
        }

        /// <summary>
        ///     The panel hands out heights by hand and has no way to notice this list grew, so
        ///     it is told, and it comes back to ask for the height below.
        /// </summary>
        private void Announce()
        {
            if (this.FindAncestorOfType<Repository>() is { } view)
                view.UpdateForkSidebarLayout();
        }

        /// <summary>
        ///     Eight rows at most. Beyond that the list would crowd out the branches, which
        ///     are what the panel is mostly for; the rest is a scroll away.
        /// </summary>
        public double Measure(double room)
        {
            Requests.Height = IsExpanded ? Math.Min(DesiredListHeight, Math.Max(0, room)) : 0;
            return HEADER + Requests.Height;
        }

        /// <summary>
        ///     The section's own header, the same height as the ones above it.
        /// </summary>
        private const double HEADER = 28;

        private AvaloniaList<Models.PullRequest> _visible = [];
        private List<Models.PullRequest> _all = [];
        private List<Models.ForgeUser> _me = [];
        private string _counter = string.Empty;
        private CancellationTokenSource _pending = null;
        private ViewModels.Repository _watched = null;
    }
}
