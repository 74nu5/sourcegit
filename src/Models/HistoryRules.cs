using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.Models
{
    public enum HistoryRuleKind
    {
        /// <summary>Every branch whose name matches a wildcard pattern.</summary>
        Pattern = 0,

        /// <summary>The commits a branch already holds, taken out of the history.</summary>
        HistoryOf,
    }

    /// <summary>
    ///     A filter this fork adds beside upstream's: what upstream's list has no type for.
    ///
    ///     Kept in a list of its own, in this fork's file, rather than in HistoryFilters.
    ///     Upstream reads that list and acts on it -- it empties it when the mode changes,
    ///     indexes it by pattern, builds git arguments from it -- and none of that knows
    ///     what a wildcard or a "not in" is.
    /// </summary>
    public class HistoryRule : ObservableObject
    {
        public HistoryRuleKind Kind { get; set; } = HistoryRuleKind.Pattern;

        /// <summary>
        ///     The pattern, matched against names as the sidebar shows them -- team/x for a
        ///     local branch, origin/team/x for a remote one -- or the full name of the branch
        ///     for a HistoryOf rule.
        /// </summary>
        public string Pattern { get; set; } = string.Empty;

        public FilterMode Mode { get; set; } = FilterMode.Excluded;

        [JsonIgnore]
        public bool IsValid
        {
            get => _isValid;
            set => SetProperty(ref _isValid, value);
        }

        [JsonIgnore]
        public string Label
        {
            get
            {
                if (Kind != HistoryRuleKind.HistoryOf)
                    return Pattern;

                if (Pattern.StartsWith("refs/heads/", StringComparison.Ordinal))
                    return $"^{Pattern.Substring(11)}";
                if (Pattern.StartsWith("refs/remotes/", StringComparison.Ordinal))
                    return $"^{Pattern.Substring(13)}";
                return $"^{Pattern}";
            }
        }

        public HistoryRule Clone()
        {
            return new HistoryRule() { Kind = Kind, Pattern = Pattern, Mode = Mode };
        }

        private bool _isValid = true;
    }

    /// <summary>
    ///     A named set of filters, to come back to without rebuilding it.
    /// </summary>
    public class HistoryFilterView
    {
        public string Name { get; set; } = string.Empty;
        public List<HistoryFilter> Filters { get; set; } = [];
        public List<HistoryRule> Rules { get; set; } = [];
    }

    public enum HistoryRuleVerdict
    {
        /// <summary>A rule says to show it, or nothing is selected and nothing hides it.</summary>
        Shown,

        /// <summary>Something else is selected and this is not.</summary>
        NotSelected,

        /// <summary>A rule says to hide it.</summary>
        Hidden,
    }

    /// <summary>
    ///     The history filters, read as rules that coexist rather than as one mode.
    ///
    ///     Upstream gives the whole list the mode of its first entry and empties it when an
    ///     entry of the other mode arrives, so "this folder except that branch" cannot be
    ///     said. Here every rule keeps its own mode, and for any given branch the rule that
    ///     names it most precisely decides: the branch itself, then the deepest folder that
    ///     holds it, a wildcard pattern by the number of characters it fixes. At equal
    ///     precision, hiding wins.
    ///
    ///     A "show" that sits inside a hidden folder is an exception to that folder, not a
    ///     selection: it does not make everything else disappear. Only a "show" that nothing
    ///     hides turns the history into a selection, where what is not named is left out.
    ///
    ///     The git arguments are written with git's own patterns -- an --exclude in front of
    ///     each --branches it applies to -- rather than by listing every visible branch, so
    ///     their length follows the number of rules and not the number of branches. A
    ///     repository with a few hundred branches would otherwise run into the length limit
    ///     of a command line on Windows.
    ///
    ///     References that no longer exist are left out of the arguments: a single one makes
    ///     git log fail as a whole, and the history then shows nothing at all.
    /// </summary>
    public sealed class HistoryRuleSet
    {
        public HistoryRuleSet(IEnumerable<HistoryFilter> filters, IEnumerable<HistoryRule> rules, ICollection<string> existingRefs, string currentBranch, bool showCurrentBranch)
        {
            if (filters != null)
            {
                foreach (var filter in filters)
                {
                    var rule = Rule.From(filter);
                    if (rule != null)
                        Add(rule, existingRefs);
                }
            }

            if (rules != null)
            {
                foreach (var rule in rules)
                {
                    var parsed = Rule.From(rule);
                    if (parsed != null)
                        Add(parsed, existingRefs);
                }
            }

            _currentBranch = currentBranch ?? string.Empty;
            _showCurrentBranch = showCurrentBranch;

            foreach (var rule in _rules)
            {
                if (rule.IsValid && rule.Kind != RuleKind.HistoryOf && rule.Mode == FilterMode.Included && !IsException(rule))
                {
                    IsSelection = true;
                    break;
                }
            }
        }

        /// <summary>
        ///     Whether some branch is shown on its own, so that whatever no rule names is left
        ///     out.
        /// </summary>
        public bool IsSelection { get; }

        /// <summary>
        ///     Whether a reference these rules name no longer exists.
        /// </summary>
        public bool HasMissingReferences { get; private set; }

        public bool HasHidingRules
        {
            get
            {
                foreach (var rule in _rules)
                {
                    if (rule.IsValid && rule.Kind != RuleKind.HistoryOf && rule.Mode == FilterMode.Excluded)
                        return true;
                }

                return false;
            }
        }

        /// <summary>
        ///     What happens to one reference, given by its full name.
        /// </summary>
        public HistoryRuleVerdict Evaluate(string fullName)
        {
            if (!Split(fullName, out var side, out var shortName) || shortName.EndsWith("/HEAD", StringComparison.Ordinal))
                return HistoryRuleVerdict.Shown;

            var best = BestMatch(side, shortName, fullName);

            if (_showCurrentBranch && fullName.Equals(_currentBranch, StringComparison.Ordinal) && !IsHiddenExactly(best))
                return HistoryRuleVerdict.Shown;

            if (best != null)
                return best.Mode == FilterMode.Included ? HistoryRuleVerdict.Shown : HistoryRuleVerdict.Hidden;

            return IsSelection ? HistoryRuleVerdict.NotSelected : HistoryRuleVerdict.Shown;
        }

        /// <summary>
        ///     The revisions to hand git log: what upstream's builder writes after the ordering
        ///     and display flags.
        /// </summary>
        public string BuildRevisions(bool isInProgress)
        {
            var builder = new StringBuilder();
            var tips = new List<Rule>();
            foreach (var rule in _rules)
            {
                if (rule.IsValid && rule.Kind != RuleKind.HistoryOf)
                    tips.Add(rule);
            }

            if (tips.Count == 0)
            {
                builder.Append("--branches --remotes --tags HEAD");
                foreach (var rule in _rules)
                {
                    if (rule.IsValid && rule.Kind == RuleKind.HistoryOf)
                        builder.Append(" ^").Append(rule.Full);
                }

                return builder.ToString();
            }

            var showHead = _showCurrentBranch && !IsCurrentBranchHiddenExactly(tips);
            if (IsSelection)
            {
                foreach (var rule in tips)
                {
                    if (rule.Mode == FilterMode.Included)
                        AppendShown(builder, tips, rule);
                }

                if (showHead)
                    builder.Append("HEAD ");
            }
            else
            {
                AppendExcludes(builder, tips, Side.Local, 0, null);
                builder.Append("--branches ");

                if (isInProgress || showHead)
                    builder.Append("HEAD ");

                AppendExcludes(builder, tips, Side.Remote, 0, null);
                builder.Append("--exclude=origin/HEAD --remotes ");

                foreach (var rule in tips)
                {
                    if (rule.Side == Side.Tag && rule.Mode == FilterMode.Excluded)
                        builder.Append("--exclude=\"").Append(rule.Short).Append("\" ");
                }

                builder.Append("--tags ");

                // What is left to show is an exception to something hidden.
                foreach (var rule in tips)
                {
                    if (rule.Mode == FilterMode.Included)
                        AppendShown(builder, tips, rule);
                }
            }

            foreach (var rule in _rules)
            {
                if (rule.IsValid && rule.Kind == RuleKind.HistoryOf)
                    builder.Append('^').Append(rule.Full).Append(' ');
            }

            return builder.ToString();
        }

        /// <summary>
        ///     Takes the labels of what a rule hides off the commits, the way upstream's
        ///     --decorate-refs-exclude does in its hiding mode. That flag cannot be used here:
        ///     git always lets an exclusion win over an inclusion, so a branch shown inside a
        ///     hidden folder would lose its label with the rest of the folder.
        ///
        ///     The current branch keeps its label whatever the rules say.
        /// </summary>
        public void FilterDecorators(List<Commit> commits)
        {
            if (commits == null || !HasHidingRules)
                return;

            foreach (var commit in commits)
            {
                if (commit.Decorators.Count == 0)
                    continue;

                commit.Decorators.RemoveAll(d =>
                {
                    var full = d.Type switch
                    {
                        DecoratorType.LocalBranchHead => $"refs/heads/{d.Name}",
                        DecoratorType.RemoteBranchHead => $"refs/remotes/{d.Name}",
                        DecoratorType.Tag => $"refs/tags/{d.Name}",
                        _ => null,
                    };

                    return full != null && Evaluate(full) == HistoryRuleVerdict.Hidden;
                });
            }
        }

        /// <summary>
        ///     Whether a wildcard pattern matches a name, the way git matches the pattern of
        ///     --branches and --exclude: * and ? also match a slash, [...] is a class, and a
        ///     pattern with no wildcard at all stands for a folder.
        /// </summary>
        public static bool Matches(string pattern, string name)
        {
            return ToRegex(Normalize(pattern)).IsMatch(name);
        }

        /// <summary>
        ///     The pattern as git is given it. git reads a pattern with no wildcard as a folder
        ///     in --branches but as an exact name in --exclude; writing the /* out makes both
        ///     agree with what the person meant.
        /// </summary>
        public static string Normalize(string pattern)
        {
            var trimmed = (pattern ?? string.Empty).Trim();
            if (trimmed.IndexOfAny(['*', '?', '[']) >= 0)
                return trimmed;

            return $"{trimmed.TrimEnd('/')}/*";
        }

        private void Add(Rule rule, ICollection<string> existingRefs)
        {
            if (rule.Kind is RuleKind.Exact or RuleKind.HistoryOf && existingRefs != null && !existingRefs.Contains(rule.Full))
            {
                rule.IsValid = false;
                HasMissingReferences = true;
            }

            _rules.Add(rule);
        }

        private void AppendShown(StringBuilder builder, List<Rule> tips, Rule rule)
        {
            switch (rule.Kind)
            {
                case RuleKind.Exact:
                    builder.Append(rule.Full).Append(' ');
                    break;
                case RuleKind.Folder:
                    AppendExcludes(builder, tips, rule.Side, rule.Precision, rule);
                    builder.Append(rule.Side == Side.Local ? "--branches=" : "--remotes=").Append(rule.Short).Append("/* ");
                    break;
                case RuleKind.Glob:
                    AppendExcludes(builder, tips, Side.Local, rule.Precision, null);
                    builder.Append("--branches=\"").Append(rule.Glob).Append("\" ");
                    AppendExcludes(builder, tips, Side.Remote, rule.Precision, null);
                    builder.Append("--remotes=\"").Append(rule.Glob).Append("\" ");
                    break;
            }
        }

        /// <summary>
        ///     The hiding rules that apply to the next --branches or --remotes: those on that
        ///     side, at least as precise as what that option shows -- a less precise one would
        ///     lose to it -- and, for a folder, inside it.
        /// </summary>
        private static void AppendExcludes(StringBuilder builder, List<Rule> tips, Side side, int minPrecision, Rule within)
        {
            foreach (var hide in tips)
            {
                if (hide.Mode != FilterMode.Excluded || hide.Precision < minPrecision)
                    continue;

                switch (hide.Kind)
                {
                    case RuleKind.Exact when hide.Side == side && IsInside(hide.Short, within):
                        builder.Append("--exclude=\"").Append(hide.Short).Append("\" ");
                        break;
                    case RuleKind.Folder when hide.Side == side && IsInside(hide.Short, within):
                        builder.Append("--exclude=\"").Append(hide.Short).Append("/*\" ");
                        break;
                    case RuleKind.Glob:
                        builder.Append("--exclude=\"").Append(hide.Glob).Append("\" ");
                        break;
                }
            }
        }

        private static bool IsInside(string shortName, Rule folder)
        {
            return folder == null || shortName.StartsWith($"{folder.Short}/", StringComparison.Ordinal);
        }

        private Rule BestMatch(Side side, string shortName, string fullName)
        {
            Rule best = null;
            foreach (var rule in _rules)
            {
                if (!rule.IsValid || !rule.Matches(side, shortName, fullName))
                    continue;

                if (best == null ||
                    rule.Precision > best.Precision ||
                    (rule.Precision == best.Precision && rule.Mode == FilterMode.Excluded))
                    best = rule;
            }

            return best;
        }

        /// <summary>
        ///     A "show" that something hides around it: a branch or a folder inside a hidden
        ///     folder or pattern. A shown pattern is never one -- nothing is "around" it.
        /// </summary>
        private bool IsException(Rule shown)
        {
            if (shown.Kind is not (RuleKind.Exact or RuleKind.Folder) || shown.Side == Side.Tag)
                return false;

            var probe = shown.Kind == RuleKind.Exact ? shown.Short : $"{shown.Short}/";
            foreach (var hide in _rules)
            {
                if (!hide.IsValid || hide.Mode != FilterMode.Excluded)
                    continue;

                if (hide.Kind == RuleKind.Folder && hide.Side == shown.Side && probe.StartsWith($"{hide.Short}/", StringComparison.Ordinal))
                    return true;

                if (hide.Kind == RuleKind.Glob && hide.Matcher.IsMatch(probe))
                    return true;
            }

            return false;
        }

        private bool IsCurrentBranchHiddenExactly(List<Rule> tips)
        {
            foreach (var rule in tips)
            {
                if (IsHiddenExactly(rule) && rule.Full.Equals(_currentBranch, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static bool IsHiddenExactly(Rule rule)
        {
            return rule is { Kind: RuleKind.Exact, Mode: FilterMode.Excluded };
        }

        private static bool Split(string fullName, out Side side, out string shortName)
        {
            if (fullName.StartsWith("refs/heads/", StringComparison.Ordinal))
            {
                side = Side.Local;
                shortName = fullName.Substring(11);
                return true;
            }

            if (fullName.StartsWith("refs/remotes/", StringComparison.Ordinal))
            {
                side = Side.Remote;
                shortName = fullName.Substring(13);
                return true;
            }

            if (fullName.StartsWith("refs/tags/", StringComparison.Ordinal))
            {
                side = Side.Tag;
                shortName = fullName.Substring(10);
                return true;
            }

            side = Side.Local;
            shortName = fullName;
            return false;
        }

        private static Regex ToRegex(string glob)
        {
            var builder = new StringBuilder("^");
            for (var i = 0; i < glob.Length; i++)
            {
                var c = glob[i];
                if (c == '*')
                {
                    builder.Append(".*");
                }
                else if (c == '?')
                {
                    builder.Append('.');
                }
                else if (c == '[' && i + 2 < glob.Length && glob.IndexOf(']', i + 2) is var end and > 0)
                {
                    var content = glob.Substring(i + 1, end - i - 1).Replace("\\", "\\\\");
                    if (content.StartsWith('!'))
                        content = $"^{content.Substring(1)}";
                    builder.Append('[').Append(content).Append(']');
                    i = end;
                }
                else
                {
                    builder.Append(Regex.Escape(c.ToString()));
                }
            }

            builder.Append('$');
            return new Regex(builder.ToString(), RegexOptions.CultureInvariant);
        }

        /// <summary>
        ///     How precisely a pattern names something: the number of characters it fixes.
        /// </summary>
        private static int PrecisionOf(string glob)
        {
            var count = 0;
            var inClass = false;
            foreach (var c in glob)
            {
                if (inClass)
                {
                    if (c == ']')
                    {
                        inClass = false;
                        count++;
                    }
                }
                else if (c == '[')
                {
                    inClass = true;
                }
                else if (c is not ('*' or '?'))
                {
                    count++;
                }
            }

            return count;
        }

        private enum Side
        {
            Local,
            Remote,
            Tag,
            Any,
        }

        private enum RuleKind
        {
            Exact,
            Folder,
            Glob,
            HistoryOf,
        }

        private sealed class Rule
        {
            public const int EXACT = int.MaxValue;

            public RuleKind Kind;
            public Side Side;
            public string Full;
            public string Short;
            public string Glob;
            public Regex Matcher;
            public int Precision;
            public FilterMode Mode;
            public bool IsValid = true;

            public static Rule From(HistoryFilter filter)
            {
                if (filter.Mode == FilterMode.None || string.IsNullOrEmpty(filter.Pattern))
                    return null;

                return filter.Type switch
                {
                    FilterType.LocalBranch => Branch(RuleKind.Exact, Side.Local, filter.Pattern, 11, filter.Mode),
                    FilterType.LocalBranchFolder => Branch(RuleKind.Folder, Side.Local, filter.Pattern, 11, filter.Mode),
                    FilterType.RemoteBranch => Branch(RuleKind.Exact, Side.Remote, filter.Pattern, 13, filter.Mode),
                    FilterType.RemoteBranchFolder => Branch(RuleKind.Folder, Side.Remote, filter.Pattern, 13, filter.Mode),
                    FilterType.Tag => new Rule()
                    {
                        Kind = RuleKind.Exact,
                        Side = Side.Tag,
                        Full = $"refs/tags/{filter.Pattern}",
                        Short = filter.Pattern,
                        Precision = EXACT,
                        Mode = filter.Mode,
                    },
                    _ => null,
                };
            }

            public static Rule From(HistoryRule rule)
            {
                if (string.IsNullOrWhiteSpace(rule.Pattern))
                    return null;

                if (rule.Kind == HistoryRuleKind.HistoryOf)
                    return new Rule() { Kind = RuleKind.HistoryOf, Side = Side.Any, Full = rule.Pattern, Short = rule.Pattern, Mode = FilterMode.Excluded };

                if (rule.Mode == FilterMode.None)
                    return null;

                var glob = Normalize(rule.Pattern);
                return new Rule()
                {
                    Kind = RuleKind.Glob,
                    Side = Side.Any,
                    Full = glob,
                    Short = glob,
                    Glob = glob,
                    Matcher = ToRegex(glob),
                    Precision = PrecisionOf(glob),
                    Mode = rule.Mode,
                };
            }

            public bool Matches(Side side, string shortName, string fullName)
            {
                return Kind switch
                {
                    RuleKind.Exact => Side == side && Full.Equals(fullName, StringComparison.Ordinal),
                    RuleKind.Folder => Side == side && shortName.StartsWith($"{Short}/", StringComparison.Ordinal),
                    RuleKind.Glob => side != Side.Tag && Matcher.IsMatch(shortName),
                    _ => false,
                };
            }

            private static Rule Branch(RuleKind kind, Side side, string pattern, int prefix, FilterMode mode)
            {
                if (pattern.Length <= prefix)
                    return null;

                var shortName = pattern.Substring(prefix);
                return new Rule()
                {
                    Kind = kind,
                    Side = side,
                    Full = pattern,
                    Short = shortName,
                    Precision = kind == RuleKind.Exact ? EXACT : shortName.Length + 1,
                    Mode = mode,
                };
            }
        }

        private readonly List<Rule> _rules = [];
        private readonly string _currentBranch;
        private readonly bool _showCurrentBranch;
    }
}
