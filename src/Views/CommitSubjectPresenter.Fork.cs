using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SourceGit.Views
{
    /// <summary>
    ///     The lane-coloured rule this fork draws in front of commit subjects.
    /// </summary>
    public partial class CommitSubjectPresenter
    {
        public static readonly StyledProperty<int> GraphColorProperty =
            AvaloniaProperty.Register<CommitSubjectPresenter, int>(nameof(GraphColor), -1);

        /// <summary>
        ///     Index in <see cref="Models.CommitGraph.Pens"/> of the lane this commit sits on,
        ///     or -1 when it is unknown.
        /// </summary>
        public int GraphColor
        {
            get => GetValue(GraphColorProperty);
            set => SetValue(GraphColorProperty, value);
        }

        public static readonly StyledProperty<bool> ShowBranchStripeProperty =
            AvaloniaProperty.Register<CommitSubjectPresenter, bool>(nameof(ShowBranchStripe));

        /// <summary>
        ///     Draws a thin rule in the lane colour before the subject, so a row can be traced
        ///     back to its branch without tinting the text itself.
        /// </summary>
        public bool ShowBranchStripe
        {
            get => GetValue(ShowBranchStripeProperty);
            set => SetValue(ShowBranchStripeProperty, value);
        }

        public static readonly StyledProperty<FontStyle> FontStyleProperty =
            TextBlock.FontStyleProperty.AddOwner<CommitSubjectPresenter>();

        /// <summary>
        ///     Slant of the subject. Upstream builds its typefaces with
        ///     <see cref="FontStyle.Normal"/> written in; this makes that a property so a row
        ///     standing for something that is not a commit yet can say so in the text itself.
        /// </summary>
        public FontStyle FontStyle
        {
            get => GetValue(FontStyleProperty);
            set => SetValue(FontStyleProperty, value);
        }
    }
}
