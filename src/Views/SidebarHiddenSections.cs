using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    /// <summary>
    ///     The row that brings a hidden section back.
    ///
    ///     A hidden section leaves no header to right-click, so without somewhere to click
    ///     there would be no way back short of a dialog nobody would think to open. It shows
    ///     itself only when something is in it, and takes one row when it does.
    ///
    ///     Built in code rather than declared, because it is a row of chips whose number and
    ///     labels change -- an ItemsControl and a template would be three files for what fits
    ///     in one.
    /// </summary>
    public class SidebarHiddenSections : Border
    {
        public SidebarHiddenSections()
        {
            Margin = new Thickness(0, 4, 0, 0);
            Padding = new Thickness(6, 3, 6, 4);

            _chips = new WrapPanel() { Orientation = Orientation.Horizontal };
            Child = _chips;

            IsVisible = false;
        }

        protected override void OnDataContextChanged(System.EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (_watched != null)
            {
                _watched.Hidden.CollectionChanged -= OnHiddenChanged;
                _watched = null;
            }

            if (DataContext is ViewModels.SidebarSections sections)
            {
                _watched = sections;
                _watched.Hidden.CollectionChanged += OnHiddenChanged;
            }

            Rebuild();
        }

        private void OnHiddenChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            Rebuild();
        }

        private void Rebuild()
        {
            _chips.Children.Clear();

            if (_watched == null || _watched.Hidden.Count == 0)
            {
                IsVisible = false;
                return;
            }

            IsVisible = true;

            _chips.Children.Add(new TextBlock()
            {
                Text = App.Text("Sidebar.Hidden"),
                FontSize = 10,
                Opacity = 0.6,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });

            foreach (var section in _watched.Hidden)
            {
                var one = section;

                var chip = new Button()
                {
                    Content = one.Label,
                    FontSize = 10,
                    Padding = new Thickness(6, 1),
                    Margin = new Thickness(0, 0, 4, 2),
                    CornerRadius = new CornerRadius(3),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(1),
                    Foreground = this.FindResource("Brush.FG2") as IBrush,
                    BorderBrush = this.FindResource("Brush.Border1") as IBrush,
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                };

                ToolTip.SetTip(chip, App.Text("Sidebar.Restore"));
                chip.Click += (_, e) =>
                {
                    one.Restore?.Invoke();

                    // Showing the button again is not enough: its row is still zero pixels
                    // tall, and nothing resized, so no layout pass would ever come to fix
                    // it. The section came back squeezed into nothing until this line.
                    this.FindAncestorOfType<Repository>()?.UpdateLeftSidebarLayoutFromFork();

                    e.Handled = true;
                };

                _chips.Children.Add(chip);
            }
        }

        private readonly WrapPanel _chips;
        private ViewModels.SidebarSections _watched = null;
    }
}
