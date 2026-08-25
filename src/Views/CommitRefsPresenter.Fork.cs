using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

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

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);

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
                item.Label.MaxTextWidth = Math.Max(1, room - 24);
                item.Label.MaxTextHeight = double.PositiveInfinity;
                item.Label.MaxLineCount = 1;
                item.Label.Trimming = TextTrimming.CharacterEllipsis;
                item.Width = room;
                used += room + 4;
                _truncated = true;
            }
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
