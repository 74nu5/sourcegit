using System;
using System.Collections.Generic;

namespace SourceGit.Models
{
    /// <summary>
    ///     Copying a settings entry, for every list in the application that is managed with a
    ///     plus and a minus.
    ///
    ///     Written as extension methods rather than as members of each type: the models are
    ///     upstream's, and a method added here costs their files nothing at all -- not even
    ///     the one word a partial class would.
    ///
    ///     Every one of them writes its fields out by hand. Reflection would spare the typing
    ///     and does not survive trimming, and this application is published ahead of time. The
    ///     cost is that a field upstream adds is missed here in silence, so the fork's harness
    ///     walks each type and fails when one of these lists stops covering it.
    /// </summary>
    public static class Cloning
    {
        public static CustomAction Clone(this CustomAction source)
        {
            var copy = new CustomAction
            {
                Name = source.Name,
                Scope = source.Scope,
                Executable = source.Executable,
                Arguments = source.Arguments,
                WaitForExit = source.WaitForExit,
            };

            // Its parameters are objects of their own. Sharing the list would make two
            // actions edit each other, which is the opposite of what duplicating one is for.
            foreach (var control in source.Controls)
                copy.Controls.Add(control.Clone());

            return copy;
        }

        public static CustomActionControl Clone(this CustomActionControl source)
        {
            return new CustomActionControl
            {
                Type = source.Type,
                Label = source.Label,
                Description = source.Description,
                StringValue = source.StringValue,
                StringFormatter = source.StringFormatter,
                BoolValue = source.BoolValue,
            };
        }

        public static IssueTracker Clone(this IssueTracker source)
        {
            return new IssueTracker
            {
                IsShared = source.IsShared,
                Name = source.Name,
                RegexString = source.RegexString,
                URLTemplate = source.URLTemplate,
            };

            // IsRegexValid is not copied: it says whether the expression above compiles, and
            // it recomputes from it. A copied verdict is one that can outlive its subject.
        }

        public static CommitTemplate Clone(this CommitTemplate source)
        {
            return new CommitTemplate
            {
                Name = source.Name,
                Content = source.Content,
            };
        }

        public static ViewModels.Workspace Clone(this ViewModels.Workspace source)
        {
            return new ViewModels.Workspace
            {
                Name = source.Name,
                Color = source.Color,
                ActiveIdx = source.ActiveIdx,
                RestoreOnStartup = source.RestoreOnStartup,
                DefaultCloneDir = source.DefaultCloneDir,

                // A fresh list: the copy exists to hold a different set of repositories.
                Repositories = new List<string>(source.Repositories),

                // Deliberately not copied. Exactly one workspace is the open one, and a
                // second claiming to be would leave the launcher with two.
                IsActive = false,
            };
        }
    }

    /// <summary>
    ///     A name for a copy that is not taken yet: "Build" becomes "Build (copy)", then
    ///     "Build (copy 2)".
    ///
    ///     Copying a copy trims the suffix before adding one, so the third is "Build (copy 3)"
    ///     rather than a name that grows a word every time it is duplicated.
    ///
    ///     English, like upstream's own "Unnamed Action": these are stored values, and a name
    ///     written in the language of the day would outlive that choice.
    /// </summary>
    public static class CopyName
    {
        public static string Next(string name, IEnumerable<string> taken, string fallback = "Unnamed")
        {
            var root = TrimSuffix(name ?? string.Empty).Trim();
            if (root.Length == 0)
                root = fallback;

            var used = new HashSet<string>(taken ?? [], StringComparer.Ordinal);

            var candidate = $"{root} (copy)";
            for (var n = 2; used.Contains(candidate); n++)
                candidate = $"{root} (copy {n})";

            return candidate;
        }

        /// <summary>
        ///     Reads the names already in a list, so a caller does not have to.
        /// </summary>
        public static List<string> TakenBy<T>(IEnumerable<T> items, Func<T, string> name)
        {
            var taken = new List<string>();
            foreach (var item in items)
                taken.Add(name(item));

            return taken;
        }

        /// <summary>
        ///     Removes one trailing "(copy)" or "(copy N)", and only that. Something named
        ///     "Deploy (copy of prod)" keeps its name.
        /// </summary>
        private static string TrimSuffix(string name)
        {
            var open = name.LastIndexOf('(');
            if (open < 1 || !name.EndsWith(')'))
                return name;

            var inside = name[(open + 1)..^1];
            if (!inside.StartsWith("copy", StringComparison.Ordinal))
                return name;

            var rest = inside[4..].Trim();
            if (rest.Length > 0 && !int.TryParse(rest, out _))
                return name;

            return name[..open];
        }
    }
}
