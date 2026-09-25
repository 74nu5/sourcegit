using Avalonia;

namespace SourceGit.Views
{
    /// <summary>
    ///     The bottom line of a stash in the list: either the subject, as upstream draws it, or
    ///     the branch chip followed by stash@{N} once the message has moved to the top.
    ///
    ///     It writes no drawing code at all. Everything on screen is still
    ///     <see cref="StashSubjectPresenter.Render"/>; all this does is choose the string handed
    ///     to it, and the chip appears because that string is shaped the way git shapes one.
    /// </summary>
    public class StashSecondaryPresenter : StashSubjectPresenter
    {
        public static readonly StyledProperty<Models.Stash> StashProperty =
            AvaloniaProperty.Register<StashSecondaryPresenter, Models.Stash>(nameof(Stash));

        public Models.Stash Stash
        {
            get => GetValue(StashProperty);
            set => SetValue(StashProperty, value);
        }

        public static readonly StyledProperty<bool> UseMessageProperty =
            AvaloniaProperty.Register<StashSecondaryPresenter, bool>(nameof(UseMessage));

        public bool UseMessage
        {
            get => GetValue(UseMessageProperty);
            set => SetValue(UseMessageProperty, value);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == StashProperty || change.Property == UseMessageProperty)
                Refresh();
        }

        private void Refresh()
        {
            var stash = Stash;
            if (stash == null)
            {
                SetCurrentValue(SubjectProperty, string.Empty);
                return;
            }

            SetCurrentValue(SubjectProperty, SubjectFor(stash, UseMessage));
        }

        /// <summary>
        ///     What the bottom line is handed. Off, upstream's subject, untouched.
        ///
        ///     On, the string is shaped the way git shapes one -- "On &lt;branch&gt;: &lt;text&gt;" --
        ///     because that is exactly what the base class knows how to split, so the chip gets
        ///     drawn without a line of drawing code here. Without a recognisable branch there
        ///     is no chip to draw and the index stands alone.
        /// </summary>
        public static string SubjectFor(Models.Stash stash, bool useMessage)
        {
            if (stash == null)
                return string.Empty;

            if (!useMessage)
                return stash.Subject;

            var branch = StashSubjectPresenter.Split(stash.Subject).Branch;
            return branch.Length > 0 ? $"On {branch}: {stash.Name}" : stash.Name;
        }
    }
}
