using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace SourceGit.Views
{
    /// <summary>
    ///     The card that says what a pull request is.
    ///
    ///     It lives on its own because two things show it: the mark at the end of a branch
    ///     chip, as a tooltip, and the list in the left panel, as a flyout. One description,
    ///     one place to change it.
    /// </summary>
    public static class PullRequestCard
    {
        /// <summary>
        ///     What the mark stands for, spelled out.
        ///
        ///     The two branches are stacked rather than written side by side, and each sits in
        ///     its own block that wraps. Branch names in the wild run to forty characters and
        ///     more; on one line the second of them was simply cut off, which is the one thing
        ///     a card meant to inform must not do.
        /// </summary>
        public static Control Build(Models.PullRequest pr)
        {
            // A fixed width, not a range. Left to choose between a minimum and a maximum the
            // tooltip settled on the minimum and then laid its contents out wider, so a long
            // title ran off the right edge of its own window. One width, and everything wraps
            // inside it.
            var root = new StackPanel()
            {
                Orientation = Avalonia.Layout.Orientation.Vertical,
                Width = 420,
            };

            root.Children.Add(Header(pr));

            if (pr.HasConflicts)
                root.Children.Add(ConflictBanner());

            root.Children.Add(new TextBlock()
            {
                Text = pr.Title,
                FontWeight = FontWeight.SemiBold,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0),
            });

            root.Children.Add(BranchBlock(pr.SourceBranch, true));
            root.Children.Add(new TextBlock()
            {
                Text = "↓",
                Margin = new Thickness(9, 1, 0, 1),
                FontSize = 11,
                Opacity = 0.45,
            });
            root.Children.Add(BranchBlock(pr.TargetBranch, false));

            var meta = Meta(pr);
            if (meta.Length > 0)
            {
                root.Children.Add(new TextBlock()
                {
                    Text = meta,
                    Margin = new Thickness(0, 10, 0, 0),
                    FontSize = 11,
                    Opacity = 0.7,
                    TextWrapping = TextWrapping.Wrap,
                });
            }

            root.Children.Add(new Avalonia.Controls.Shapes.Rectangle()
            {
                Height = 1,
                Margin = new Thickness(0, 10, 0, 0),
                Opacity = 0.15,
                Fill = Brushes.Gray,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            });

            root.Children.Add(new TextBlock()
            {
                Text = App.Text("PullRequest.OpenInBrowser"),
                Margin = new Thickness(0, 7, 0, 0),
                FontSize = 11,
                Opacity = 0.55,
            });

            // True in a flyout, inert in a tooltip, and the hint at the foot says so
            // either way.
            if (!string.IsNullOrEmpty(pr.Url))
            {
                root.Cursor = new Cursor(StandardCursorType.Hand);
                root.PointerPressed += (_, _) => Native.OS.OpenBrowser(pr.Url);
            }

            return root;
        }

        /// <summary>
        ///     The one thing on this card worth interrupting for.
        ///
        ///     Red, at the top, above the title: a request that no longer merges needs work
        ///     before anything else on the card matters. Left as a band rather than a word in
        ///     the corner, because a colour alone would be lost on whoever cannot see it.
        /// </summary>
        private static Control ConflictBanner()
        {
            var red = new SolidColorBrush(0xFFDA3633);

            var row = new Grid() { ColumnDefinitions = new ColumnDefinitions("Auto,*") };

            if (App.Current?.FindResource("Icons.Error") is StreamGeometry warning)
            {
                var icon = new Avalonia.Controls.Shapes.Path()
                {
                    Width = 12,
                    Height = 12,
                    Data = warning,
                    Fill = red,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                    Margin = new Thickness(0, 1, 0, 0),
                };

                Grid.SetColumn(icon, 0);
                row.Children.Add(icon);
            }

            var text = new TextBlock()
            {
                Text = App.Text("PullRequest.Conflict"),
                Margin = new Thickness(7, 0, 0, 0),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = red,
                TextWrapping = TextWrapping.Wrap,
            };

            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            return new Border()
            {
                Background = new SolidColorBrush(Color.FromUInt32(0xFFDA3633), 0.14),
                BorderBrush = new SolidColorBrush(Color.FromUInt32(0xFFDA3633), 0.45),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6),
                Margin = new Thickness(0, 9, 0, 0),
                Child = row,
            };
        }

        /// <summary>
        ///     Number and forge on the left, state on the right.
        /// </summary>
        private static Control Header(Models.PullRequest pr)
        {
            var header = new Grid() { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };

            var left = new StackPanel()
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };

            if (App.Current?.FindResource("Icons.Remote") is StreamGeometry forge)
            {
                left.Children.Add(new Avalonia.Controls.Shapes.Path()
                {
                    Width = 12,
                    Height = 12,
                    Data = forge,
                    Fill = Converters.ForgeConverters.BrushOf(pr.Kind),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                });
            }

            left.Children.Add(new TextBlock()
            {
                Text = $"#{pr.Id}",
                FontWeight = FontWeight.Bold,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            });

            left.Children.Add(new TextBlock()
            {
                Text = Models.ForgeAccount.NameOf(pr.Kind),
                Margin = new Thickness(7, 0, 0, 0),
                FontSize = 11,
                Opacity = 0.55,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            });

            Grid.SetColumn(left, 0);
            header.Children.Add(left);

            var pill = new Border()
            {
                Background = Converters.ForgeConverters.BrushOf(pr.State),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(8, 2),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Child = new TextBlock()
                {
                    Text = App.Text($"PullRequest.State.{pr.State}"),
                    FontSize = 10,
                    FontWeight = FontWeight.Bold,
                    Foreground = Brushes.White,
                },
            };

            Grid.SetColumn(pill, 2);
            header.Children.Add(pill);

            return header;
        }

        /// <summary>
        ///     One branch, in a block of its own so that a long name wraps inside it instead
        ///     of pushing the rest of the line out of sight.
        /// </summary>
        private static Control BranchBlock(string name, bool isSource)
        {
            var grid = new Grid() { ColumnDefinitions = new ColumnDefinitions("Auto,*") };

            if (App.Current?.FindResource("Icons.Branch") is StreamGeometry branch)
            {
                var icon = new Avalonia.Controls.Shapes.Path()
                {
                    Width = 11,
                    Height = 11,
                    Data = branch,
                    Opacity = isSource ? 0.9 : 0.55,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                    Margin = new Thickness(0, 2, 0, 0),
                };

                Grid.SetColumn(icon, 0);
                grid.Children.Add(icon);
            }

            var text = new TextBlock()
            {
                Text = string.IsNullOrEmpty(name) ? "?" : name,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(7, 0, 0, 0),
                FontSize = 12,
                Opacity = isSource ? 1 : 0.8,
            };

            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            return new Border()
            {
                Background = new SolidColorBrush(Colors.Gray, 0.16),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 5),
                Margin = new Thickness(0, 10, 0, 0),
                Child = grid,
            };
        }

        private static string Meta(Models.PullRequest pr)
        {
            var who = pr.Author ?? string.Empty;
            if (pr.CreatedAt.Year <= 2000)
                return who;

            var when = pr.CreatedAt.ToLocalTime().ToString("d");
            return who.Length > 0 ? $"{who} · {when}" : when;
        }
    }
}
