namespace SourceGit.Views
{
    public partial class StashSubjectPresenter
    {
        /// <summary>
        ///     Splits a stash subject into the prefix this control draws as a chip, the branch
        ///     it names, and what is left to read.
        ///
        ///     Git writes one of two shapes: "On &lt;branch&gt;: &lt;message&gt;" when the stash was
        ///     given a message, and "WIP on &lt;branch&gt;: &lt;sha&gt; &lt;subject of HEAD&gt;" when it was
        ///     not. Neither is modelled anywhere -- <see cref="Models.Stash"/> has no branch --
        ///     so until now the only code that knew how to take them apart was the drawing
        ///     below, which cannot be called by anything that does not draw.
        ///
        ///     This is that knowledge, reachable. It reuses the very regexes Render uses,
        ///     because they are private members of this same partial class: one reading of the
        ///     format, not two that can drift.
        ///
        ///     Public so it can be exercised without a window, the way AttachGraphStashes is:
        ///     a text format read out of git is exactly the kind of thing worth pinning down
        ///     outside the interface.
        /// </summary>
        public static (string Prefix, string Branch, string Body) Split(string subject)
        {
            if (string.IsNullOrEmpty(subject))
                return (string.Empty, string.Empty, string.Empty);

            var on = REG_KEYWORD_ON().Match(subject);
            if (on.Success)
            {
                var branch = on.Groups[1].Value;
                return (branch, branch, subject[on.Length..]);
            }

            var wip = REG_KEYWORD_WIP().Match(subject);
            if (wip.Success)
            {
                var branch = wip.Groups[1].Value;
                return ($"WIP | {branch}", branch, subject[wip.Length..]);
            }

            // Neither shape. A stash created by a tool that writes its own reflog message
            // lands here, and so does anything a future git decides to write: the whole
            // subject is the body, and callers fall back on the stash's name for a label.
            return (string.Empty, string.Empty, subject);
        }
    }
}
