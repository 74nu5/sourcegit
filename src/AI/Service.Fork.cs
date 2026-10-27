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

                // The key is not copied: since upstream's develop after 2026.23 it is no longer a field of
                // the service but kept by ApiKeyManager under the server's address. Same
                // server, same key -- the copy has it without carrying it.
                Server = Server,

                Model = Model,
                AutoFetchAvailableModels = AutoFetchAvailableModels,
                ReasoningEffortLevel = ReasoningEffortLevel,
                AdditionalPrompt = AdditionalPrompt,

                // A fresh list, not the same one: the copy is going to be pointed at another
                // model, and the two would otherwise refetch into each other.
                AvailableModels = new List<string>(AvailableModels),
            };
        }
    }
}
