using System.Collections.Generic;

namespace SourceGit.AI
{
    /// <summary>
    ///     Copying a service, which is how a second model on the same provider gets set up.
    ///
    ///     The point of it is the credential: two entries against one endpoint -- a cheap
    ///     model for commit messages and a strong one for review, say -- differ by one field
    ///     out of eight. Retyping a key to change a model is the reason nobody bothers.
    /// </summary>
    public partial class Service
    {
        /// <summary>
        ///     Every field, the name apart.
        ///
        ///     Written out rather than reflected over, because reflection does not survive
        ///     trimming and this application is published ahead of time. The cost is that a
        ///     field upstream adds is silently missed here, so the fork's harness walks the
        ///     type and fails when this list stops covering it.
        /// </summary>
        public Service Clone()
        {
            return new Service
            {
                Name = Name,
                Server = Server,

                // Copied on purpose. It is the whole point: the two entries are the same
                // account. Whether it holds a key or the name of a variable, it is the same
                // secret either way, and it never travels further than the file it came from.
                ApiKey = ApiKey,
                ReadApiKeyFromEnv = ReadApiKeyFromEnv,

                Model = Model,
                AutoFetchAvailableModels = AutoFetchAvailableModels,
                ReasoningEffortLevel = ReasoningEffortLevel,
                AdditionalPrompt = AdditionalPrompt,

                // A fresh list, not the same one: the copy is going to be pointed at another
                // model, and the two would otherwise refetch into each other.
                AvailableModels = new List<string>(AvailableModels),
            };
        }

        /// <summary>
        ///     A name that is not taken yet: "GPT" becomes "GPT (copy)", then "GPT (copy 2)".
        ///
        ///     Copying a copy trims the suffix first, so a third one is "GPT (copy 3)" rather
        ///     than a name that grows a word every time it is duplicated.
        ///
        ///     English, like upstream's own "Unnamed Service": these are stored values, and a
        ///     name written in the language of the day would outlive that choice.
        /// </summary>
        public static string NextCopyName(string name, IEnumerable<string> taken)
        {
            var root = TrimCopySuffix(name ?? string.Empty).Trim();
            if (root.Length == 0)
                root = "Unnamed Service";

            var used = new HashSet<string>(taken ?? [], System.StringComparer.Ordinal);

            var candidate = $"{root} (copy)";
            for (var n = 2; used.Contains(candidate); n++)
                candidate = $"{root} (copy {n})";

            return candidate;
        }

        /// <summary>
        ///     Removes one trailing "(copy)" or "(copy N)", and only that. A service somebody
        ///     named "Backup (copy of prod)" keeps its name.
        /// </summary>
        private static string TrimCopySuffix(string name)
        {
            var open = name.LastIndexOf('(');
            if (open < 1 || !name.EndsWith(')'))
                return name;

            var inside = name[(open + 1)..^1];
            if (!inside.StartsWith("copy", System.StringComparison.Ordinal))
                return name;

            var rest = inside[4..].Trim();
            if (rest.Length > 0 && !int.TryParse(rest, out _))
                return name;

            return name[..open];
        }
    }
}
