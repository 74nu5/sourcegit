using System.Collections.Generic;

using Avalonia.Media;

namespace SourceGit.Views
{
    public partial class CommitGraph
    {
        /// <summary>
        ///     The same pen, drawn as a dash.
        ///
        ///     Used for the row standing for work that is not committed yet: it keeps the
        ///     colour of the lane it sits on, so it is plainly part of that branch, and loses
        ///     the continuous stroke, which is what says the rest of the graph is settled and
        ///     this is not.
        ///
        ///     Cached because Render walks every visible row and a new Pen per row would be
        ///     an allocation per frame. The palette is rebuilt on a theme change, so the
        ///     cache is keyed on the pen it derives from and follows it.
        /// </summary>
        private static Pen DashedPen(IPen source)
        {
            if (source == null)
                return null;

            if (s_dashed.TryGetValue(source, out var cached))
                return cached;

            var made = new Pen(source.Brush, source.Thickness)
            {
                DashStyle = new DashStyle([2, 2], 0),
                LineCap = PenLineCap.Round,
            };

            s_dashed[source] = made;
            return made;
        }

        private static readonly Dictionary<IPen, Pen> s_dashed = [];
    }
}
