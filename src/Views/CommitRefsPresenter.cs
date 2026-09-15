using System;
using System.Collections.Generic;
using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SourceGit.Views
{
    public class CommitRefsIconCache
    {
        public static CommitRefsIconCache Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new CommitRefsIconCache();
                return _instance;
            }
        }

        public CommitRefsIconCache()
        {
            _head = LoadIcon("Icons.Head");
            _branch = LoadIcon("Icons.Branch");
            _remote = LoadIcon("Icons.Remote");
            _tag = LoadIcon("Icons.Tag");
        }

        public Geometry GetIcon(Models.DecoratorType type)
        {
            return type switch
            {
                Models.DecoratorType.CurrentBranchHead => _head,
                Models.DecoratorType.CurrentCommitHead => _head,
                Models.DecoratorType.LocalBranchHead => _branch,
                Models.DecoratorType.RemoteBranchHead => _remote,
                Models.DecoratorType.Tag => _tag,
                _ => null,
            };
        }

        private Geometry LoadIcon(string resourceKey)
        {
            var geo = App.Current.FindResource(resourceKey) as StreamGeometry;
            var drawGeo = geo!.Clone();
            var iconBounds = drawGeo.Bounds;
            var translation = Matrix.CreateTranslation(-(Vector)iconBounds.Position);
            var scale = Math.Min(10.0 / iconBounds.Width, 10.0 / iconBounds.Height);
            var transform = translation * Matrix.CreateScale(scale, scale);
            if (drawGeo.Transform == null || drawGeo.Transform.Value == Matrix.Identity)
                drawGeo.Transform = new MatrixTransform(transform);
            else
                drawGeo.Transform = new MatrixTransform(drawGeo.Transform.Value * transform);

            return drawGeo;
        }

        private static CommitRefsIconCache _instance = null;
        private Geometry _head = null;
        private Geometry _branch = null;
        private Geometry _remote = null;
        private Geometry _tag = null;
    }

    public partial class CommitRefsPresenter : Control
    {
        public class RenderItem
        {
            public Models.Decorator Decorator { get; set; } = null;
            public FormattedText Label { get; set; } = null;
            public IBrush Brush { get; set; } = null;
            public bool IsHead { get; set; } = false;
            public double Width { get; set; } = 0.0;

            /// <summary>
            ///     Width the chip asks for. Width may be shrunk to fit the column; this is the
            ///     value truncation always starts from, so it never compounds.
            /// </summary>
            public double NaturalWidth { get; set; } = 0.0;
            public List<FormattedText> Remotes { get; set; } = [];
        }

        public static readonly StyledProperty<FontFamily> FontFamilyProperty =
            TextBlock.FontFamilyProperty.AddOwner<CommitRefsPresenter>();

        public FontFamily FontFamily
        {
            get => GetValue(FontFamilyProperty);
            set => SetValue(FontFamilyProperty, value);
        }

        public static readonly StyledProperty<double> FontSizeProperty =
           TextBlock.FontSizeProperty.AddOwner<CommitRefsPresenter>();

        public double FontSize
        {
            get => GetValue(FontSizeProperty);
            set => SetValue(FontSizeProperty, value);
        }

        public static readonly StyledProperty<IBrush> BackgroundProperty =
            AvaloniaProperty.Register<CommitRefsPresenter, IBrush>(nameof(Background), Brushes.Transparent);

        public IBrush Background
        {
            get => GetValue(BackgroundProperty);
            set => SetValue(BackgroundProperty, value);
        }

        public static readonly StyledProperty<IBrush> ForegroundProperty =
            AvaloniaProperty.Register<CommitRefsPresenter, IBrush>(nameof(Foreground), Brushes.White);

        public IBrush Foreground
        {
            get => GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        public static readonly StyledProperty<bool> UseCompactBranchNamesProperty =
            AvaloniaProperty.Register<CommitRefsPresenter, bool>(nameof(UseCompactBranchNames));

        public bool UseCompactBranchNames
        {
            get => GetValue(UseCompactBranchNamesProperty);
            set => SetValue(UseCompactBranchNamesProperty, value);
        }

        public static readonly StyledProperty<bool> HasSingleRemoteProperty =
            AvaloniaProperty.Register<CommitRefsPresenter, bool>(nameof(HasSingleRemote));

        public bool HasSingleRemote
        {
            get => GetValue(HasSingleRemoteProperty);
            set => SetValue(HasSingleRemoteProperty, value);
        }

        public static readonly StyledProperty<bool> UseGraphColorProperty =
            AvaloniaProperty.Register<CommitRefsPresenter, bool>(nameof(UseGraphColor));

        public bool UseGraphColor
        {
            get => GetValue(UseGraphColorProperty);
            set => SetValue(UseGraphColorProperty, value);
        }

        public static readonly StyledProperty<bool> AllowWrapProperty =
            AvaloniaProperty.Register<CommitRefsPresenter, bool>(nameof(AllowWrap));

        public bool AllowWrap
        {
            get => GetValue(AllowWrapProperty);
            set => SetValue(AllowWrapProperty, value);
        }

        public static readonly StyledProperty<bool> ShowTagsProperty =
            AvaloniaProperty.Register<CommitRefsPresenter, bool>(nameof(ShowTags), true);

        public bool ShowTags
        {
            get => GetValue(ShowTagsProperty);
            set => SetValue(ShowTagsProperty, value);
        }

        public Models.Decorator DecoratorAt(Point point)
        {
            if (_items.Count == 0)
                return null;

            // Folded away references have no hit area of their own; the visible one answers
            // for the whole cell so right-clicking it still acts on a real reference.
            if (_collapsed)
                return _items[0].Decorator;

            if (StackVertically)
            {
                var row = (int)(point.Y / 20.0);
                return row >= 0 && row < _items.Count ? _items[row].Decorator : null;
            }

            var x = 0.0;
            foreach (var item in _items)
            {
                x += item.Width;
                if (point.X < x)
                    return item.Decorator;
            }

            return null;
        }

        public override void Render(DrawingContext context)
        {
            if (_items.Count == 0)
                return;

            var useGraphColor = UseGraphColor;
            var fg = Foreground;
            var bg = Background;
            var allowWrap = AllowWrap;
            var stacked = StackVertically;
            var x = 1.5;
            var y = 0.5;
            var remoteIcon = CommitRefsIconCache.Instance.GetIcon(Models.DecoratorType.RemoteBranchHead);
            var hasSingleRemote = HasSingleRemote;

            context.FillRectangle(Brushes.Transparent, Bounds);

            Truncate(Bounds.Width);

            var count = _collapsed ? 1 : _items.Count;
            for (var i = 0; i < count; i++)
            {
                var item = _items[i];
                if (item.Width <= 0)
                    continue;

                if (stacked && i > 0)
                {
                    x = 1.5;
                    y += 20.0;
                }
                else if (allowWrap && x > 1.5 && x + item.Width > Bounds.Width)
                {
                    x = 1.5;
                    y += 20.0;
                }

                var entireRect = new RoundedRect(new Rect(x, y, item.Width, 16), new CornerRadius(4));
                if (item.IsHead)
                {
                    if (useGraphColor)
                    {
                        if (bg != null)
                            context.DrawRectangle(bg, null, entireRect);

                        using (context.PushOpacity(.6))
                            context.DrawRectangle(item.Brush, null, entireRect);
                    }
                }
                else
                {
                    if (bg != null)
                        context.DrawRectangle(bg, null, entireRect);

                    var labelRect = new RoundedRect(new Rect(x + 16, y, item.Width - 16, 16), new CornerRadius(4, 0, 0, 4));
                    using (context.PushOpacity(.2))
                        context.DrawRectangle(item.Brush, null, labelRect);
                }

                context.DrawLine(new Pen(item.Brush), new Point(x + 16, y), new Point(x + 16, y + 16));
                context.DrawText(item.Label, new Point(x + 20, y + 8.0 - item.Label.Height * 0.5));

                if (item.Remotes.Count > 0)
                {
                    var rx = x + 20 + item.Label.WidthIncludingTrailingWhitespace + 4;

                    if (hasSingleRemote)
                    {
                        context.DrawLine(new Pen(item.Brush), new Point(rx, y), new Point(rx, y + 16));
                        using (context.PushTransform(Matrix.CreateTranslation(rx + 4, y + 4)))
                            context.DrawGeometry(fg, null, remoteIcon);
                    }
                    else
                    {
                        foreach (var remote in item.Remotes)
                        {
                            context.DrawLine(new Pen(item.Brush), new Point(rx, y), new Point(rx, y + 16));
                            using (context.PushTransform(Matrix.CreateTranslation(rx + 4, y + 4)))
                                context.DrawGeometry(fg, null, remoteIcon);
                            context.DrawText(remote, new Point(rx + 16, y + 8.0 - remote.Height * 0.5));
                            rx += remote.WidthIncludingTrailingWhitespace + 22;
                        }
                    }
                }

                DrawForkAdornments(context, item, x, y);

                context.DrawRectangle(null, new Pen(item.Brush), entireRect);

                var icon = CommitRefsIconCache.Instance.GetIcon(item.Decorator.Type);
                using (context.PushTransform(Matrix.CreateTranslation(x + 3, y + 3)))
                    context.DrawGeometry(fg, null, icon);

                x += item.Width + 4;
            }

            if (_collapsed)
                DrawExtraCounter(context, x, y, fg);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == FontFamilyProperty ||
                change.Property == FontSizeProperty ||
                change.Property == ForegroundProperty ||
                change.Property == UseGraphColorProperty ||
                change.Property == UseCompactBranchNamesProperty ||
                change.Property == HasSingleRemoteProperty ||
                change.Property == BackgroundProperty ||
                change.Property == CollapseExtraRefsProperty ||
                change.Property == StackVerticallyProperty ||
                change.Property == ShowTagsProperty)
                InvalidateMeasure();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            InvalidateMeasure();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            _items.Clear();

            if (DataContext is not Models.Commit commit)
                return new Size(0, 0);

            var refs = commit.Decorators;
            var count = refs.Count;
            if (count == 0)
            {
                InvalidateVisual();
                return new Size(0, 0);
            }

            var useCompactBranchNames = UseCompactBranchNames;
            var hasSingleRemote = HasSingleRemote;
            var typeface = new Typeface(FontFamily);
            var typefaceHead = new Typeface(FontFamily, FontStyle.Normal, FontWeight.Bold);
            var typefaceRemote = new Typeface(FontFamily, FontStyle.Italic, FontWeight.Bold);
            var fg = Foreground;
            var normalBG = UseGraphColor ? Models.CommitGraph.Pens[commit.Color].Brush : Brushes.Gray;
            var labelSize = FontSize;
            var requiredHeight = 16.0;
            var x = 0.0;
            var allowWrap = AllowWrap;
            var showTags = ShowTags;
            var skippedIdx = new HashSet<int>();

            for (var i = 0; i < count; i++)
            {
                if (skippedIdx.Contains(i))
                    continue;

                var decorator = refs[i];
                if (!showTags && decorator.Type == Models.DecoratorType.Tag)
                    continue;

                var item = new RenderItem()
                {
                    Decorator = decorator,
                    Brush = decorator.Type == Models.DecoratorType.Tag ? Brushes.Gray : normalBG,
                    IsHead = decorator.Type is Models.DecoratorType.CurrentBranchHead or Models.DecoratorType.CurrentCommitHead,
                };
                _items.Add(item);

                if (item.IsHead)
                {
                    item.Label = new FormattedText(
                        decorator.Name,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typefaceHead,
                        labelSize + 1,
                        fg);
                }
                else
                {
                    item.Label = new FormattedText(
                        decorator.Name,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        labelSize,
                        fg);
                }

                item.Width = item.Label.Width + 24;
                item.NaturalWidth = item.Width;

                var findRemotes = useCompactBranchNames && (decorator.Type == Models.DecoratorType.CurrentBranchHead || decorator.Type == Models.DecoratorType.LocalBranchHead);
                if (findRemotes)
                {
                    for (var j = i + 1; j < count; j++)
                    {
                        var test = refs[j];
                        if (test.Type != Models.DecoratorType.RemoteBranchHead)
                            continue;

                        var idxOfSlash = test.Name.IndexOf('/');
                        if (idxOfSlash < 1 || idxOfSlash == test.Name.Length - 1)
                            continue;

                        var name = test.Name.Substring(idxOfSlash + 1);
                        if (decorator.Name.Equals(name, StringComparison.Ordinal))
                        {
                            var remote = new FormattedText(
                                test.Name.Substring(0, idxOfSlash),
                                CultureInfo.CurrentCulture,
                                FlowDirection.LeftToRight,
                                typefaceRemote,
                                labelSize,
                                fg);

                            item.Remotes.Add(remote);

                            if (hasSingleRemote)
                                item.Width += 18;
                            else
                                item.Width += remote.WidthIncludingTrailingWhitespace + 22;

                            item.NaturalWidth = item.Width;
                            skippedIdx.Add(j);
                        }
                    }
                }

                MeasureForkAdornments(item);

                x += item.Width + 4;
                if (allowWrap)
                {
                    if (x > availableSize.Width)
                    {
                        requiredHeight += 20.0;
                        x = item.Width;
                    }
                }
            }

            _collapsed = CollapseExtraRefs && _items.Count > 1;
            _extraCounter = null;
            _truncated = false;

            double requiredWidth = 0;
            if (_collapsed)
            {
                _extraCounter = new FormattedText(
                    $"+{_items.Count - 1}",
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    labelSize,
                    fg);

                requiredWidth = _items[0].Width + 4 + _extraCounter.Width + 10 + 2;
                requiredHeight = 16.0;
            }
            else if (StackVertically && _items.Count > 0)
            {
                foreach (var item in _items)
                    requiredWidth = Math.Max(requiredWidth, item.Width);

                requiredWidth += 4;
                requiredHeight = _items.Count * 20.0 - 4.0;
            }
            else if (_items.Count > 0)
            {
                if (allowWrap && requiredHeight > 16.0)
                {
                    requiredWidth = double.IsInfinity(availableSize.Width) ? x + 2 : availableSize.Width;
                }
                else
                {
                    requiredWidth = x + 2;
                }
            }

            _requiredWidth = requiredWidth;

            if (!double.IsInfinity(availableSize.Width) && availableSize.Width > 0)
                requiredWidth = Math.Min(requiredWidth, availableSize.Width);

            InvalidateVisual();
            return new Size(requiredWidth, requiredHeight);
        }

        private List<RenderItem> _items = new List<RenderItem>();
    }
}
