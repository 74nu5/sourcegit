using System;
using System.Collections.Generic;
using System.Threading;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    /// <summary>
    ///     Folding, trimming and the hover popup, added by this fork. The upstream file keeps
    ///     only the calls it cannot avoid, inside Render and MeasureOverride.
    /// </summary>
    public partial class CommitRefsPresenter
    {
        public static readonly StyledProperty<bool> StackVerticallyProperty =
            AvaloniaProperty.Register<CommitRefsPresenter, bool>(nameof(StackVertically));

        public static readonly StyledProperty<bool> CollapseExtraRefsProperty =
            AvaloniaProperty.Register<CommitRefsPresenter, bool>(nameof(CollapseExtraRefs));

        /// <summary>
        ///     Shows only the first reference followed by a "+N" counter, and lists them all
        ///     on hover. Meant for the narrow branch column, where several references on the
        ///     same commit would otherwise push each other out of view.
        /// </summary>
        public bool CollapseExtraRefs
        {
            get => GetValue(CollapseExtraRefsProperty);
            set => SetValue(CollapseExtraRefsProperty, value);
        }

        /// <summary>
        ///     Lays the references out one per line. Used by the hover popup.
        /// </summary>
        public bool StackVertically
        {
            get => GetValue(StackVerticallyProperty);
            set => SetValue(StackVerticallyProperty, value);
        }

        /// <summary>
        ///     Every reference this presenter stands for, folded ones included, in the order
        ///     they are drawn.
        /// </summary>
        public List<Models.Decorator> Decorators()
        {
            var result = new List<Models.Decorator>(_items.Count);
            foreach (var item in _items)
                result.Add(item.Decorator);

            return result;
        }

        /// <summary>
        ///     Hooked on attachment rather than on the data context, which upstream already
        ///     watches: what is loaded here belongs to the repository, not to the commit, so
        ///     a row being recycled onto another commit changes nothing about it.
        /// </summary>
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            EnsurePullRequests();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);

            var pending = Interlocked.Exchange(ref _prCancellation, null);
            pending?.Cancel();
            pending?.Dispose();
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);

            // The mark answers for itself before the chip does: it is the smaller target and
            // the more specific one.
            var over = MarkerAt(e.GetPosition(this));
            if (over != null)
            {
                if (!ReferenceEquals(_hovered, over))
                {
                    _hovered = over;
                    ToolTip.SetTip(this, CreatePullRequestCard(over));
                    ToolTip.SetPlacement(this, PlacementMode.Pointer);
                    ToolTip.SetHorizontalOffset(this, 0);
                    ToolTip.SetVerticalOffset(this, 0);
                }

                Cursor = HAND;
                return;
            }

            var remote = RemoteAt(e.GetPosition(this));
            if (remote != null)
            {
                if (!string.Equals(_hoveredRemote, remote, StringComparison.Ordinal))
                {
                    _hovered = null;
                    _hoveredRemote = remote;
                    ToolTip.SetTip(this, remote);
                    ToolTip.SetPlacement(this, PlacementMode.Pointer);
                    ToolTip.SetHorizontalOffset(this, 0);
                    ToolTip.SetVerticalOffset(this, 0);
                }

                Cursor = null;
                return;
            }

            if (_hovered != null || _hoveredRemote != null)
            {
                _hovered = null;
                _hoveredRemote = null;
                Cursor = null;
                ToolTip.SetTip(this, null);
            }

            // Folded references are only reachable through the popup, and a trimmed name is
            // only readable there, so both call for the full chips.
            if (_collapsed || _truncated)
            {
                if (ToolTip.GetTip(this) is not CommitRefsPresenter)
                {
                    ToolTip.SetTip(this, CreateHoverPopup());
                    ToolTip.SetPlacement(this, PlacementMode.RightEdgeAlignedTop);
                    ToolTip.SetHorizontalOffset(this, -Bounds.Width);
                    ToolTip.SetVerticalOffset(this, 0);
                }

                return;
            }

            ToolTip.SetTip(this, null);
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);

            _hovered = null;
            _hoveredRemote = null;
            Cursor = null;
        }

        /// <summary>
        ///     A click on the mark opens the request, and only a click on the mark: everywhere
        ///     else the row keeps behaving as it always did.
        /// </summary>
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            var point = e.GetCurrentPoint(this);
            if (point.Properties.IsLeftButtonPressed)
            {
                var over = MarkerAt(point.Position);
                if (over != null && !string.IsNullOrEmpty(over.Url))
                {
                    Native.OS.OpenBrowser(over.Url);
                    e.Handled = true;
                    return;
                }
            }

            base.OnPointerPressed(e);
        }

        /// <summary>
        ///     The request whose mark sits under a point, or null. Filled while drawing, which
        ///     is the only place the chips' real positions are known.
        /// </summary>
        private string RemoteAt(Point point)
        {
            for (var i = 0; i < _remoteAreas.Count; i++)
            {
                if (_remoteAreas[i].Area.Contains(point))
                    return _remoteAreas[i].Name;
            }

            return null;
        }

        private Models.PullRequest MarkerAt(Point point)
        {
            for (var i = 0; i < _markers.Count; i++)
            {
                if (_markers[i].Area.Contains(point))
                    return _markers[i].Request;
            }

            return null;
        }

        /// <summary>
        ///     What the mark stands for, spelled out: the request, where it goes, and who
        ///     opened it. Built here rather than as a resource because it belongs to no window.
        /// </summary>
        private Control CreatePullRequestCard(Models.PullRequest pr)
        {
            var panel = new StackPanel() { Orientation = Avalonia.Layout.Orientation.Vertical, MaxWidth = 420 };

            panel.Children.Add(new TextBlock()
            {
                Text = $"#{pr.Id}  {pr.Title}",
                FontWeight = FontWeight.Bold,
                TextWrapping = TextWrapping.Wrap,
            });

            var line = new StackPanel() { Orientation = Avalonia.Layout.Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            line.Children.Add(new Border()
            {
                Background = StateBrush(pr.State),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Child = new TextBlock()
                {
                    Text = App.Text($"PullRequest.State.{pr.State}"),
                    FontSize = 11,
                    Foreground = Brushes.White,
                },
            });
            line.Children.Add(new TextBlock()
            {
                Text = $"{pr.SourceBranch} → {pr.TargetBranch}",
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            panel.Children.Add(line);

            var who = pr.Author ?? string.Empty;
            if (pr.CreatedAt.Year > 2000)
                who = who.Length > 0 ? $"{who} · {pr.CreatedAt.ToLocalTime():d}" : $"{pr.CreatedAt.ToLocalTime():d}";

            if (who.Length > 0)
            {
                panel.Children.Add(new TextBlock()
                {
                    Text = who,
                    Margin = new Thickness(0, 6, 0, 0),
                    FontSize = 11,
                    Opacity = 0.75,
                });
            }

            panel.Children.Add(new TextBlock()
            {
                Text = App.Text("PullRequest.OpenInBrowser"),
                Margin = new Thickness(0, 8, 0, 0),
                FontSize = 11,
                Opacity = 0.55,
            });

            return panel;
        }

        private static IBrush StateBrush(Models.PullRequestState state)
        {
            return state switch
            {
                Models.PullRequestState.Draft => new SolidColorBrush(0xFF8B949E),
                Models.PullRequestState.Merged => new SolidColorBrush(0xFF8957E5),
                Models.PullRequestState.Closed => new SolidColorBrush(0xFFDA3633),
                _ => new SolidColorBrush(0xFF3FB950),
            };
        }

        /// <summary>
        ///     A second presenter showing every reference, one per line, reusing this one's
        ///     look so the popup and the row cannot drift apart.
        /// </summary>
        private Control CreateHoverPopup()
        {
            return new CommitRefsPresenter()
            {
                DataContext = DataContext,
                StackVertically = true,
                FontFamily = FontFamily,
                FontSize = FontSize,
                Foreground = Foreground,
                Background = Background,
                UseGraphColor = UseGraphColor,
                UseCompactBranchNames = UseCompactBranchNames,
                ShowTags = ShowTags,
                _pullRequests = _pullRequests,
                _remoteKinds = _remoteKinds,
            };
        }

        /// <summary>
        ///     Shrinks the chips that do not fit the width actually granted, so the column ends
        ///     a name with an ellipsis rather than cutting it mid-letter. Called from Render
        ///     because only then is the real width known: the column may be sized to its
        ///     contents and then capped, in which case measuring never sees the final value.
        /// </summary>
        private void Truncate(double available)
        {
            // Called at the top of every Render, which makes it the one place that knows a new
            // pass is starting: the marks about to be drawn replace the previous ones.
            _markers.Clear();
            _remoteAreas.Clear();

            var drawn = _collapsed ? 1 : _items.Count;
            for (var i = 0; i < drawn; i++)
            {
                var item = _items[i];
                item.Width = item.NaturalWidth;
                item.Label.MaxTextWidth = double.PositiveInfinity;
                item.Label.Trimming = TextTrimming.None;
            }

            _truncated = false;

            if (double.IsInfinity(available) || available <= 0 || StackVertically)
                return;

            var budget = available - 2 - (_collapsed && _extraCounter != null ? _extraCounter.Width + 14 : 0);
            var used = 0.0;

            for (var i = 0; i < drawn; i++)
            {
                var item = _items[i];
                if (used + item.Width <= budget)
                {
                    used += item.Width + 4;
                    continue;
                }

                var room = budget - used;
                if (room < MIN_CHIP_WIDTH)
                {
                    // Nothing usable left: drop the chip rather than draw a stub of it.
                    item.Width = 0;
                    _truncated = true;
                    continue;
                }

                // A trimmed name has no room for the remotes it was sharing its chip with;
                // the hover popup still shows them.
                item.Remotes.Clear();

                // Losing the remotes may be all it took to fit. Taking the whole of what is
                // left regardless would pad the chip with the very space they had claimed,
                // which reads as a name mysteriously followed by nothing.
                var adornments = AdornmentWidth(item);
                var needed = item.Label.Width + 24 + adornments;
                var use = Math.Min(room, needed);

                item.Label.MaxTextWidth = Math.Max(1, use - 24 - adornments);
                item.Label.MaxTextHeight = double.PositiveInfinity;
                item.Label.MaxLineCount = 1;
                item.Label.Trimming = TextTrimming.CharacterEllipsis;
                item.Width = use;
                used += use + 4;
                _truncated = true;
            }
        }

        /// <summary>
        ///     Room the pull request marker needs at the end of a chip, added to the width the
        ///     chip asked for. Zero when the reference carries no open request, which is the
        ///     ordinary case and costs nothing.
        /// </summary>
        private void MeasureForkAdornments(RenderItem item)
        {
            // The remote names, if they are becoming icons, give back the width their text
            // took before the icons claim their own.
            var given = 0.0;
            if (RemoteIconsFor(item).Count > 0)
            {
                foreach (var remote in item.Remotes)
                    given += remote.Width + 9;

                item.Remotes.Clear();
            }

            var extra = AdornmentWidth(item);
            if (extra <= 0 && given <= 0)
                return;

            item.Width += extra - given;
            item.NaturalWidth = item.Width;
        }

        /// <summary>
        ///     The mark itself, inside the chip and at the end of it — not a separate badge
        ///     beside it, which would take its own width and read as unrelated to the name.
        ///
        ///     An open request is a filled diamond and a draft a hollow one, so the two are
        ///     told apart without relying on colour alone.
        /// </summary>
        private void DrawForkAdornments(DrawingContext context, RenderItem item, double x, double y)
        {
            if (item.Width < MIN_CHIP_WIDTH)
                return;

            // Laid out from the chip's right edge leftwards, which is what makes trimming
            // work: the room reserved before a name is cut is exactly the room used here.
            var right = x + item.Width - ADORNMENT_GAP;

            var pr = PullRequestFor(item.Decorator);
            if (pr != null)
            {
                DrawPullRequestMark(context, item, pr, right - MARKER * 0.5, y);
                right -= MARKER + 4;
            }

            var kinds = RemoteIconsFor(item);
            for (var i = kinds.Count - 1; i >= 0; i--)
            {
                var icon = CommitRefsIconCache.Instance.GetIcon(Models.DecoratorType.RemoteBranchHead);
                if (icon == null)
                    break;

                right -= REMOTE_ICON;
                using (context.PushTransform(Matrix.CreateTranslation(right, y + 3)))
                    context.DrawGeometry(Converters.ForgeConverters.BrushOf(kinds[i].Kind), null, icon);

                // Turning a name into a picture loses the name; hovering gives it back.
                _remoteAreas.Add((new Rect(right - 2, y, REMOTE_ICON + 4, 16), kinds[i].Name));
                right -= 4;
            }
        }

        private void DrawPullRequestMark(DrawingContext context, RenderItem item, Models.PullRequest pr, double cx, double y)
        {
            var brush = pr.State == Models.PullRequestState.Draft
                ? item.Brush
                : new SolidColorBrush(0xFF3FB950);

            var cy = y + 8.0;
            var r = MARKER * 0.5;

            // A seven pixel diamond is a poor target for a mouse, so what answers to one is
            // the height of the chip and twice the width of the mark.
            _markers.Add((new Rect(cx - MARKER, y, MARKER * 2, 16), pr));

            var diamond = new StreamGeometry();
            using (var draw = diamond.Open())
            {
                draw.BeginFigure(new Point(cx, cy - r), pr.State != Models.PullRequestState.Draft);
                draw.LineTo(new Point(cx + r, cy));
                draw.LineTo(new Point(cx, cy + r));
                draw.LineTo(new Point(cx - r, cy));
                draw.EndFigure(true);
            }

            if (pr.State == Models.PullRequestState.Draft)
                context.DrawGeometry(null, new Pen(brush, 1.2), diamond);
            else
                context.DrawGeometry(brush, null, diamond);
        }

        /// <summary>
        ///     The forges of the remotes folded into a chip, when their names are to be shown
        ///     as icons — empty otherwise, and empty whenever nothing was folded.
        ///
        ///     Worked out from the commit's own references rather than kept alongside them:
        ///     the chips are rebuilt on every measure, and a value cached beside them would
        ///     only be one more thing able to fall out of step.
        /// </summary>
        private List<(string Name, Models.ForgeKind Kind)> RemoteIconsFor(RenderItem item)
        {
            if (!ViewModels.Preferences.Instance.ShowRemoteIconInsteadOfName || !UseCompactBranchNames)
                return NO_REMOTES;

            if (item?.Decorator == null || DataContext is not Models.Commit commit)
                return NO_REMOTES;

            if (item.Decorator.Type is not (Models.DecoratorType.CurrentBranchHead or Models.DecoratorType.LocalBranchHead))
                return NO_REMOTES;

            List<(string, Models.ForgeKind)> kinds = null;
            foreach (var decorator in commit.Decorators)
            {
                if (decorator.Type != Models.DecoratorType.RemoteBranchHead)
                    continue;

                var slash = decorator.Name.IndexOf('/');
                if (slash < 1 || slash == decorator.Name.Length - 1)
                    continue;

                if (!decorator.Name[(slash + 1)..].Equals(item.Decorator.Name, StringComparison.Ordinal))
                    continue;

                var remote = decorator.Name[..slash];
                kinds ??= [];
                kinds.Add((remote, _remoteKinds.TryGetValue(remote, out var kind) ? kind : Models.ForgeKind.Unknown));
            }

            return kinds ?? NO_REMOTES;
        }

        private double AdornmentWidth(RenderItem item)
        {
            var width = PullRequestFor(item?.Decorator) == null ? 0 : MARKER + ADORNMENT_GAP;

            var kinds = RemoteIconsFor(item);
            if (kinds.Count > 0)
            {
                width += kinds.Count * (REMOTE_ICON + 4);
                if (width <= REMOTE_ICON + 4)
                    width += ADORNMENT_GAP;
            }

            return width;
        }

        /// <summary>
        ///     The open request on the branch a reference stands for, or null.
        ///
        ///     A remote reference names its branch as "origin/feature/x" while a forge names
        ///     it "feature/x", so the remote is dropped before matching. Tags never carry one.
        /// </summary>
        private Models.PullRequest PullRequestFor(Models.Decorator decorator)
        {
            if (_pullRequests.Count == 0 || decorator == null)
                return null;

            string name;
            switch (decorator.Type)
            {
                case Models.DecoratorType.CurrentBranchHead:
                case Models.DecoratorType.LocalBranchHead:
                    name = decorator.Name;
                    break;

                case Models.DecoratorType.RemoteBranchHead:
                    var slash = decorator.Name.IndexOf('/');
                    if (slash < 1 || slash == decorator.Name.Length - 1)
                        return null;

                    name = decorator.Name[(slash + 1)..];
                    break;

                default:
                    return null;
            }

            return _pullRequests.TryGetValue(name, out var pr) ? pr : null;
        }

        /// <summary>
        ///     Asks the repository once per presenter. The cache underneath joins callers, so
        ///     a screenful of rows makes one request, not one per row.
        /// </summary>
        private async void EnsurePullRequests()
        {
            var pending = Interlocked.Exchange(ref _prCancellation, null);
            pending?.Cancel();
            pending?.Dispose();

            if (this.FindAncestorOfType<Repository>() is not { DataContext: ViewModels.Repository repo })
                return;

            // Costs nothing and needs no network, so it is read whether or not the pull
            // request indicator is on: the remote icons stand on their own.
            _remoteKinds = repo.GetRemoteKinds();

            var cancel = new CancellationTokenSource();
            _prCancellation = cancel;

            var map = await repo.GetPullRequestsAsync(cancel.Token).ConfigureAwait(true);

            if (cancel.IsCancellationRequested || !ReferenceEquals(_prCancellation, cancel))
                return;

            if (map.Count == 0 && _pullRequests.Count == 0)
                return;

            _pullRequests = map;
            InvalidateMeasure();
            InvalidateVisual();
        }

        /// <summary>
        ///     "+N" chip standing for the references that are not drawn.
        /// </summary>
        private void DrawExtraCounter(DrawingContext context, double x, double y, IBrush fg)
        {
            if (_extraCounter == null)
                return;

            var rect = new RoundedRect(new Rect(x, y, _extraCounter.Width + 10, 16), new CornerRadius(4));
            var bg = Background;
            if (bg != null)
                context.DrawRectangle(bg, null, rect);

            using (context.PushOpacity(.15))
                context.DrawRectangle(fg, null, rect);

            using (context.PushOpacity(.5))
                context.DrawRectangle(null, new Pen(fg), rect);

            context.DrawText(_extraCounter, new Point(x + 5, y + 8.0 - _extraCounter.Height * 0.5));
        }

        /// <summary>
        ///     True when only the first reference is drawn and the others sit behind the
        ///     counter, in which case they can only be reached through the context menu.
        /// </summary>
        public bool IsCollapsed => _collapsed;

        private readonly List<(Rect Area, Models.PullRequest Request)> _markers = [];

        private readonly List<(Rect Area, string Name)> _remoteAreas = [];

        private Models.PullRequest _hovered = null;

        private string _hoveredRemote = null;

        private static readonly Cursor HAND = new(StandardCursorType.Hand);

        private Dictionary<string, Models.ForgeKind> _remoteKinds = EMPTY_REMOTE_KINDS;

        private static readonly Dictionary<string, Models.ForgeKind> EMPTY_REMOTE_KINDS = [];

        private static readonly List<(string Name, Models.ForgeKind Kind)> NO_REMOTES = [];

        /// <summary>
        ///     Side of the remote icon, which the icon cache draws ten pixels square.
        /// </summary>
        private const double REMOTE_ICON = 10;

        private Dictionary<string, Models.PullRequest> _pullRequests = EMPTY_PULL_REQUESTS;

        private CancellationTokenSource _prCancellation = null;

        private static readonly Dictionary<string, Models.PullRequest> EMPTY_PULL_REQUESTS = [];

        /// <summary>
        ///     Side of the diamond, and the room left between it and the chip's edge.
        /// </summary>
        private const double MARKER = 7;
        private const double ADORNMENT_GAP = 6;

        private FormattedText _extraCounter = null;

        private bool _collapsed = false;

        private bool _truncated = false;

        private double _requiredWidth = 0;

        /// <summary>
        ///     Below this a chip would show its icon and nothing readable.
        /// </summary>
        private const double MIN_CHIP_WIDTH = 40;
    }
}
