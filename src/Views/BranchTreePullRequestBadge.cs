using System;
using System.Globalization;
using System.Threading;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    /// <summary>
    ///     The mark on a branch that carries an open pull request.
    ///
    ///     It asks for the data itself rather than waiting to be given it. That looks
    ///     wasteful and is not: the cache underneath joins callers, so a tree of two hundred
    ///     branches drawing at once makes one request — and nothing has to be wired into
    ///     upstream's reload, which is the whole reason this fork stays cheap to rebase.
    ///
    ///     It draws nothing at all when the option is off, when no account covers the
    ///     repository, or when the branch carries no live request. Nothing here is an error
    ///     state; a branch with no pull request is the ordinary case.
    /// </summary>
    public class BranchTreePullRequestBadge : Control
    {
        public static readonly StyledProperty<FontFamily> FontFamilyProperty =
            TextBlock.FontFamilyProperty.AddOwner<BranchTreePullRequestBadge>();

        public FontFamily FontFamily
        {
            get => GetValue(FontFamilyProperty);
            set => SetValue(FontFamilyProperty, value);
        }

        public static readonly StyledProperty<double> FontSizeProperty =
            TextBlock.FontSizeProperty.AddOwner<BranchTreePullRequestBadge>();

        public double FontSize
        {
            get => GetValue(FontSizeProperty);
            set => SetValue(FontSizeProperty, value);
        }

        static BranchTreePullRequestBadge()
        {
            AffectsMeasure<BranchTreePullRequestBadge>(FontFamilyProperty, FontSizeProperty);
        }

        public override void Render(DrawingContext context)
        {
            var label = Layout();
            if (label == null)
                return;

            var brush = BrushFor(_state);
            var h = label.Height + 2;
            var rect = new RoundedRect(new Rect(0, (Bounds.Height - h) * 0.5, label.Width + 12, h), h * 0.5);

            // Open requests are filled and drafts are outlined, so the two are told apart
            // without relying on colour alone.
            if (_state == Models.PullRequestState.Draft)
                context.DrawRectangle(null, new Pen(brush, 1), rect);
            else
                context.DrawRectangle(brush, null, rect);

            context.DrawText(label, new Point(6, (Bounds.Height - label.Height) * 0.5));
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var label = Layout();
            return label == null ? new Size(0, 0) : new Size(label.Width + 12, label.Height + 2);
        }

        /// <summary>
        ///     Laid out on demand rather than kept, so that a change of theme picks up the new
        ///     palette instead of drawing the previous one's colours.
        /// </summary>
        private FormattedText Layout()
        {
            if (string.IsNullOrEmpty(_text))
                return null;

            var brush = BrushFor(_state);
            var ink = _state == Models.PullRequestState.Draft ? brush : Brushes.White;

            return new FormattedText(
                _text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily),
                FontSize,
                ink);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            Load();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);

            var pending = Interlocked.Exchange(ref _pending, null);
            pending?.Cancel();
            pending?.Dispose();

            Clear();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            Load();
        }

        /// <summary>
        ///     Rows are recycled as the list scrolls, so a badge must forget the branch it
        ///     was drawn for before it is asked about the next one — otherwise a stale answer
        ///     lands on someone else's row.
        /// </summary>
        private async void Load()
        {
            var previous = Interlocked.Exchange(ref _pending, null);
            previous?.Cancel();
            previous?.Dispose();

            Clear();

            if (DataContext is not ViewModels.BranchTreeNode { Backend: Models.Branch branch })
                return;

            if (this.FindAncestorOfType<BranchTree>() is not { DataContext: ViewModels.Repository repo })
                return;

            var cancel = new CancellationTokenSource();
            _pending = cancel;

            var wanted = branch.Name;
            var map = await repo.GetPullRequestsAsync(cancel.Token).ConfigureAwait(true);

            if (cancel.IsCancellationRequested || !ReferenceEquals(_pending, cancel))
                return;

            if (!map.TryGetValue(wanted, out var pr))
                return;

            // The row may have been recycled onto another branch while we waited.
            if (DataContext is not ViewModels.BranchTreeNode { Backend: Models.Branch current } || current.Name != wanted)
                return;

            _state = pr.State;
            _text = $"#{pr.Id}";

            ToolTip.SetTip(this, $"#{pr.Id} · {pr.Title}\n{pr.SourceBranch} → {pr.TargetBranch}\n{pr.Author}");
            InvalidateMeasure();
            InvalidateVisual();
        }

        /// <summary>
        ///     Emptied rather than hidden. A hidden control is skipped by the layout pass, so
        ///     an answer arriving afterwards had nothing to re-measure and the badge stayed
        ///     invisible — but only when the answer came over the network, since a cached one
        ///     lands before the first measure and papered over it.
        /// </summary>
        private void Clear()
        {
            _text = null;
            ToolTip.SetTip(this, null);
            InvalidateMeasure();
            InvalidateVisual();
        }

        /// <summary>
        ///     Resolved here rather than bound, so a change of theme repaints in the new
        ///     palette instead of keeping the old one's colours.
        /// </summary>
        private static IBrush BrushFor(Models.PullRequestState state)
        {
            return state == Models.PullRequestState.Draft
                ? Application.Current?.FindResource("Brush.FG2") as IBrush ?? Brushes.Gray
                : new SolidColorBrush(0xFF3FB950);
        }

        private string _text = null;
        private Models.PullRequestState _state = Models.PullRequestState.Open;
        private CancellationTokenSource _pending = null;
    }
}
