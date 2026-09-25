using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SourceGit.Views
{
    /// <summary>
    ///     The top line of a stash in the list: either stash@{N}, as upstream draws it, or the
    ///     message.
    ///
    ///     Reported as #6: the list is already ordered by date, so the index carries the least
    ///     information of anything on the row and sits in the most prominent place. Off by
    ///     default all the same, and then this behaves exactly as the TextBlock it replaced.
    ///
    ///     A control rather than a converter, for one reason: a converter reading
    ///     Preferences.Instance would never be asked again when the setting changes, and the
    ///     list would keep the old shape until it was rebuilt.
    /// </summary>
    public class StashPrimaryLabel : TextBlock
    {
        public static readonly StyledProperty<Models.Stash> StashProperty =
            AvaloniaProperty.Register<StashPrimaryLabel, Models.Stash>(nameof(Stash));

        public Models.Stash Stash
        {
            get => GetValue(StashProperty);
            set => SetValue(StashProperty, value);
        }

        public static readonly StyledProperty<bool> UseMessageProperty =
            AvaloniaProperty.Register<StashPrimaryLabel, bool>(nameof(UseMessage));

        public bool UseMessage
        {
            get => GetValue(UseMessageProperty);
            set => SetValue(UseMessageProperty, value);
        }

        /// <summary>
        ///     The colour the message takes, fed by DynamicResource from the XAML.
        ///
        ///     Not resolved here: a brush looked up in code keeps the palette it was born with,
        ///     and switching theme would leave this line in the previous one -- the mistake
        ///     CLAUDE.md records for the graph's pens.
        /// </summary>
        public static readonly StyledProperty<IBrush> MessageForegroundProperty =
            AvaloniaProperty.Register<StashPrimaryLabel, IBrush>(nameof(MessageForeground));

        public IBrush MessageForeground
        {
            get => GetValue(MessageForegroundProperty);
            set => SetValue(MessageForegroundProperty, value);
        }

        public StashPrimaryLabel()
        {
            // A message can be any length, and this column shares its row with the date.
            TextTrimming = TextTrimming.CharacterEllipsis;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == StashProperty ||
                change.Property == UseMessageProperty ||
                change.Property == MessageForegroundProperty)
            {
                Refresh();
            }
        }

        private void Refresh()
        {
            var stash = Stash;
            if (stash == null)
            {
                SetCurrentValue(TextProperty, string.Empty);
                return;
            }

            // Upstream's own appearance, down to the literal colour. DarkOrange is not a theme
            // resource, so assigning it here costs nothing on a theme change.
            if (!UseMessage)
            {
                SetCurrentValue(TextProperty, TextFor(stash, false));
                SetCurrentValue(ForegroundProperty, Brushes.DarkOrange);
                return;
            }

            SetCurrentValue(TextProperty, TextFor(stash, true));
            SetCurrentValue(ForegroundProperty, MessageForeground ?? Brushes.DarkOrange);
        }

        /// <summary>
        ///     What the top line reads. Separated from the drawing so the fallbacks can be
        ///     checked without a window, since they are the part worth getting right.
        ///
        ///     With the option off, upstream's answer: the index. With it on, what git wrote
        ///     minus the prefix the second line already shows -- and back to the index when
        ///     that leaves nothing, because a blank line is worse than the index it replaced.
        /// </summary>
        public static string TextFor(Models.Stash stash, bool useMessage)
        {
            if (stash == null)
                return string.Empty;

            if (!useMessage)
                return stash.Name;

            var body = StashSubjectPresenter.Split(stash.Subject).Body;
            return body.Length > 0 ? body : stash.Name;
        }
    }
}
