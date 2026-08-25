using System;
using System.Collections.Generic;

namespace SourceGit.Models
{
    public enum ForgeKind
    {
        Unknown = 0,
        AzureDevOps,
        GitHub,
        GitLab,
        Gitea,
        Bitbucket,
    }

    /// <summary>
    ///     A repository as its forge names it.
    ///
    ///     Azure DevOps is the reason <see cref="Project"/> exists: it files repositories under
    ///     organisation / project / repository, where every other forge stops at owner /
    ///     repository. The others leave it empty.
    /// </summary>
    public record ForgeRepository(ForgeKind Kind, string Host, string Owner, string Project, string Name)
    {
        /// <summary>
        ///     What to show a human: "org/project/repo" or "owner/repo".
        /// </summary>
        public string FullName =>
            string.IsNullOrEmpty(Project) ? $"{Owner}/{Name}" : $"{Owner}/{Project}/{Name}";
    }

    /// <summary>
    ///     Reads a remote and says which forge hosts it.
    ///
    ///     This is the fork's single answer to that question. Upstream answers it in three
    ///     places — Remote.TryGetCreatePullRequestURL, CommitLink.Get and HTTPSValidator —
    ///     each with its own host list, which is why Azure DevOps can open a pull request
    ///     there but not a commit. Those are left alone: touching them would buy consistency
    ///     at the price of three more files to merge on every rebase, and nothing here
    ///     depends on them.
    /// </summary>
    public static class Forge
    {
        public static bool TryParse(Remote remote, out ForgeRepository repo)
        {
            repo = null;

            if (remote == null || !remote.TryGetVisitURL(out var visit))
                return false;

            if (!Uri.TryCreate(visit, UriKind.Absolute, out var uri))
                return false;

            var host = uri.Host;
            var segments = SplitPath(uri.AbsolutePath);
            if (segments.Count < 2)
                return false;

            var kind = KindOf(host);
            repo = kind == ForgeKind.AzureDevOps
                ? ParseAzureDevOps(host, segments)
                : new ForgeRepository(kind, host, string.Join('/', segments.GetRange(0, segments.Count - 1)), string.Empty, segments[^1]);

            return repo != null;
        }

        /// <summary>
        ///     Recognised by host name, which is all a remote URL offers. Self-hosted GitLab
        ///     and Gitea instances answer to any name, so they can only be told apart by what
        ///     the user configured; an unknown host stays Unknown rather than being guessed
        ///     wrong, and the caller simply offers nothing for it.
        /// </summary>
        public static ForgeKind KindOf(string host)
        {
            if (host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase))
                return ForgeKind.AzureDevOps;

            if (host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
                return ForgeKind.GitHub;

            if (host.Equals("gitlab.com", StringComparison.OrdinalIgnoreCase))
                return ForgeKind.GitLab;

            if (host.Equals("bitbucket.org", StringComparison.OrdinalIgnoreCase))
                return ForgeKind.Bitbucket;

            if (host.Equals("codeberg.org", StringComparison.OrdinalIgnoreCase) ||
                host.Equals("gitea.com", StringComparison.OrdinalIgnoreCase))
                return ForgeKind.Gitea;

            return ForgeKind.Unknown;
        }

        /// <summary>
        ///     dev.azure.com/{org}/{project}/_git/{repo}, with the marker segment dropped.
        ///     The historical {org}.visualstudio.com/{project}/_git/{repo} omits the
        ///     organisation from the path, so it is taken from the host instead.
        /// </summary>
        private static ForgeRepository ParseAzureDevOps(string host, List<string> segments)
        {
            var marker = segments.IndexOf("_git");
            if (marker < 1 || marker == segments.Count - 1)
                return null;

            var name = segments[marker + 1];
            var org = string.Empty;
            var project = string.Empty;

            if (host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase))
            {
                org = host[..host.IndexOf('.')];
                project = string.Join('/', segments.GetRange(0, marker));
            }
            else if (marker >= 2)
            {
                org = segments[0];
                project = string.Join('/', segments.GetRange(1, marker - 1));
            }
            else
            {
                return null;
            }

            return string.IsNullOrEmpty(org) || string.IsNullOrEmpty(project)
                ? null
                : new ForgeRepository(ForgeKind.AzureDevOps, host, org, project, name);
        }

        private static List<string> SplitPath(string path)
        {
            var result = new List<string>();
            foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
                result.Add(part.EndsWith(".git", StringComparison.Ordinal) ? part[..^4] : part);

            return result;
        }
    }
}
