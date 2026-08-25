using Avalonia;

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
    }
}
