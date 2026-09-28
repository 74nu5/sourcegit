using System;
using System.Collections.Generic;

namespace SourceGit.ViewModels
{
    public partial class Launcher
    {
        /// <summary>
        ///     The tab to bring back as active after a workspace's repositories were reopened,
        ///     or null when it did not come back.
        ///
        ///     The workspace remembers the active tab as a rank in its list of repositories,
        ///     and the restore used to read that rank straight into Pages. The two only line
        ///     up while every repository opens. One that no longer does -- its folder moved or
        ///     deleted -- is skipped, every tab after it slides one place to the left, and the
        ///     rank then names the tab that followed the one you left. So the rank is turned
        ///     back into the path it stood for, and the tab is found by that path instead.
        ///
        ///     Takes the list and the paths rather than reaching for them, so the choice can
        ///     be checked without opening a repository.
        /// </summary>
        public static LauncherPage FindRestoredPage(IList<LauncherPage> pages, string[] repos, int activeIdx)
        {
            if (pages == null || repos == null || activeIdx < 0 || activeIdx >= repos.Length)
                return null;

            // The same normalisation the path went through before it was stored, and the one
            // a tab's node id gets when it is opened.
            var wanted = repos[activeIdx].Replace('\\', '/').TrimEnd('/');

            foreach (var page in pages)
            {
                if (page.Node is { IsRepository: true } node && node.Id.Equals(wanted, StringComparison.Ordinal))
                    return page;
            }

            // The active repository was the one that failed to open. Nothing is a better
            // guess than any other, so the caller falls back on the first tab.
            return null;
        }
    }
}
