using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SourceGit.Converters
{
    /// <summary>
    ///     Brand colours for the forges, so that one icon can stand for all of them.
    ///
    ///     Drawing a recognisable logo per forge would mean carrying five trademarked paths;
    ///     tinting the remote icon says as much at a glance and costs nothing. The values are
    ///     the forges' own primary colours, lightened where the dark theme would swallow them.
    /// </summary>
    public static class ForgeConverters
    {
        public static readonly FuncValueConverter<Models.ForgeKind, IBrush> ToBrush = new(BrushOf);

        /// <summary>
        ///     The same answer, reachable from code that draws rather than binds.
        /// </summary>
        public static IBrush BrushOf(Models.ForgeKind kind)
        {
            return kind switch
            {
                Models.ForgeKind.AzureDevOps => new SolidColorBrush(0xFF0078D4),
                Models.ForgeKind.GitHub => new SolidColorBrush(0xFF8B7FD4),
                Models.ForgeKind.GitLab => new SolidColorBrush(0xFFFC6D26),
                Models.ForgeKind.Gitea => new SolidColorBrush(0xFF609926),
                Models.ForgeKind.Bitbucket => new SolidColorBrush(0xFF2684FF),
                _ => Brushes.Gray,
            };
        }

        public static readonly FuncValueConverter<Models.PullRequestState, IBrush> StateToBrush =
            new(BrushOf);

        public static readonly FuncValueConverter<Models.PullRequestState, string> StateToLabel =
            new(state => App.Text($"PullRequest.State.{state}"));

        /// <summary>
        ///     The colours a state is shown in, wherever it is shown.
        /// </summary>
        public static IBrush BrushOf(Models.PullRequestState state)
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
        ///     Grey while nothing is settled, then the verdict. Green and red are picked to
        ///     stay legible on both themes rather than taken from the palette, which has no
        ///     entry for "this worked".
        /// </summary>
        public static readonly FuncValueConverter<bool?, IBrush> TestResultToBrush =
            new(ok => ok switch
            {
                true => new SolidColorBrush(0xFF4CAF50),
                false => new SolidColorBrush(0xFFE05252),
                _ => Application.Current?.FindResource("Brush.FG2") as IBrush,
            });
    }
}
