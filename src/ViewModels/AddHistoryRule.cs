using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    /// <summary>
    ///     Shows or hides, in the graph, every branch whose name matches a wildcard pattern:
    ///     backup/*, *-avant-rebase-*, 74nu5-*. The branches an agent creates by the dozen
    ///     are not always in a folder one could filter instead.
    /// </summary>
    public class AddHistoryRule : Popup
    {
        [Required(ErrorMessage = "Pattern is required!")]
        public string Pattern
        {
            get => _pattern;
            set
            {
                if (SetProperty(ref _pattern, value, true))
                    OnPropertyChanged(nameof(Preview));
            }
        }

        public bool IsShow
        {
            get => _isShow;
            set => SetProperty(ref _isShow, value);
        }

        /// <summary>
        ///     How many branches the pattern matches right now, so a typo shows before the
        ///     graph empties.
        /// </summary>
        public string Preview
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_pattern))
                    return string.Empty;

                var count = 0;
                foreach (var branch in _repo.Branches)
                {
                    if (!branch.IsDetachedHead && Models.HistoryRuleSet.Matches(_pattern, branch.FriendlyName))
                        count++;
                }

                return App.Text("HistoryRules.Matching", count);
            }
        }

        public AddHistoryRule(Repository repo, Models.FilterMode mode)
        {
            _repo = repo;
            _isShow = mode == Models.FilterMode.Included;
        }

        public override Task<bool> Sure()
        {
            _repo.AddHistoryPatternRule(_pattern, _isShow ? Models.FilterMode.Included : Models.FilterMode.Excluded);
            return Task.FromResult(true);
        }

        private readonly Repository _repo;
        private string _pattern = string.Empty;
        private bool _isShow;
    }
}
