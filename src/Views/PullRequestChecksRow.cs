using System;
using System.Threading;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace SourceGit.Views
{
    /// <summary>
    ///     The conditions of a merge, as a row of small pills inside the card.
    ///
    ///     It draws whatever is already known the moment it appears, and then — only if
    ///     something is still unknown and the forge can be asked — goes and asks, about this
    ///     one request. That is the whole cost model: a card you opened, once, kept for ten
    ///     minutes.
    /// </summary>
    public class PullRequestChecksRow : StackPanel
    {
        public PullRequestChecksRow(Models.PullRequest pr, ViewModels.Repository repo)
        {
            Orientation = Orientation.Horizontal;
            Spacing = 6;
            Margin = new Thickness(0, 10, 0, 0);

            _pr = pr;
            Draw(pr.Checks);

            // GitLab already answered everything while listing, and a forge with no source
            // has nothing more to say either.
            if (repo == null || !pr.Checks.IsEmpty && pr.Checks.Ci != Models.CheckState.Unknown)
                return;

            if (Models.PullRequestChecksService.SourceFor(pr.Kind) == null)
                return;

            Load(repo);
        }

        /// <summary>
        ///     Nothing may escape: this is an async void in all but name, hanging off a
        ///     control that can be gone before the answer is.
        /// </summary>
        private async void Load(ViewModels.Repository repo)
        {
            try
            {
                Draw(_pr.Checks, loading: true);

                var cancel = new CancellationTokenSource();
                Interlocked.Exchange(ref _pending, cancel)?.Cancel();
                var token = cancel.Token;

                var checks = await repo.GetPullRequestChecksAsync(_pr, token).ConfigureAwait(true);

                if (token.IsCancellationRequested || !ReferenceEquals(_pending, cancel))
                    return;

                Draw(checks);
            }
            catch (Exception ex)
            {
                Models.ForgeLog.Failed("pull request checks", ex);

                // Whatever was known before the attempt is still true.
                Draw(_pr.Checks);
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);

            // Cancelled, never disposed: an answer may still be on its way to it.
            Interlocked.Exchange(ref _pending, null)?.Cancel();
        }

        private void Draw(Models.PullRequestChecks checks, bool loading = false)
        {
            Children.Clear();

            Children.Add(Pill(
                "merge",
                _pr.HasConflicts ? Models.CheckState.Failed
                    : _pr.MergeState == Models.PullRequestMergeState.Clean ? Models.CheckState.Passed
                    : Models.CheckState.Unknown));

            Children.Add(Pill("build", checks.Ci, loading));
            Children.Add(Pill("threads", checks.Discussions, loading, checks.OpenDiscussions));
            Children.Add(Pill("review", checks.Approval, loading));
        }

        /// <summary>
        ///     One condition. Unknown is grey and hollow, never red: most forges say nothing
        ///     about most of these, and a wall of red warnings about nothing would be worse
        ///     than no row at all.
        /// </summary>
        private static Control Pill(string label, Models.CheckState state, bool loading = false, int count = -1)
        {
            var (glyph, brush, opacity) = state switch
            {
                Models.CheckState.Passed => ("✓", new SolidColorBrush(0xFF3FB950), 1.0),
                Models.CheckState.Failed => ("✕", new SolidColorBrush(0xFFE5534B), 1.0),
                Models.CheckState.Pending => ("•", new SolidColorBrush(0xFFD29922), 1.0),
                _ => (loading ? "…" : "–", new SolidColorBrush(0xFF8B949E), 0.55),
            };

            var text = count > 0 ? $"{glyph} {count} {label}" : $"{glyph} {label}";

            var border = new Border()
            {
                Background = new SolidColorBrush(brush.Color, 0.12),
                BorderBrush = new SolidColorBrush(brush.Color, 0.45),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 1, 6, 2),
                Opacity = opacity,
                Child = new TextBlock()
                {
                    Text = text,
                    FontSize = 10,
                    Foreground = brush,
                },
            };

            ToolTip.SetTip(border, Describe(label, state));
            return border;
        }

        private static string Describe(string label, Models.CheckState state)
        {
            var what = label switch
            {
                "merge" => App.Text("PullRequest.Check.Merge"),
                "build" => App.Text("PullRequest.Check.Build"),
                "threads" => App.Text("PullRequest.Check.Threads"),
                _ => App.Text("PullRequest.Check.Review"),
            };

            return $"{what} — {App.Text($"PullRequest.Check.{state}")}";
        }

        private readonly Models.PullRequest _pr;
        private CancellationTokenSource _pending = null;
    }
}
