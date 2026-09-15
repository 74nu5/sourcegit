using System.Text.RegularExpressions;

namespace SourceGit.Models
{
    public partial class Remote
    {
        /// <summary>
        ///     Azure DevOps SSH remotes, which the generic pattern cannot read.
        ///
        ///     Every other forge writes git@host:owner/repo.git, so REG_TO_VISIT_URL_CAPTURE
        ///     requires a .git suffix and turns the path into the web address unchanged. Azure
        ///     DevOps writes neither: no suffix, a v3/ prefix, and a web address that inserts
        ///     _git before the repository name.
        ///
        ///         git@ssh.dev.azure.com:v3/org/project/repo
        ///         org@vs-ssh.visualstudio.com:v3/org/project/repo
        ///
        ///     Without this, TryGetVisitURL returns false for such a remote and the "create a
        ///     pull request" entry silently does nothing.
        /// </summary>
        [GeneratedRegex(@"^[\w\-]+@(?:ssh\.dev\.azure\.com|vs-ssh\.visualstudio\.com):v3/([^/]+)/([^/]+)/([^/]+?)(?:\.git)?$")]
        private static partial Regex REG_AZURE_DEVOPS_SSH();

        private static bool TryGetAzureDevOpsVisitURL(string remoteURL, out string url)
        {
            url = null;

            var match = REG_AZURE_DEVOPS_SSH().Match(remoteURL);
            if (!match.Success)
                return false;

            var org = match.Groups[1].Value;
            var project = match.Groups[2].Value;
            var repo = match.Groups[3].Value;

            url = $"https://dev.azure.com/{org}/{project}/_git/{repo}";
            return true;
        }
    }
}
