using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    /// <summary>
    ///     The lane order of a repository and the default one, typed as text: the branches
    ///     that hold the leftmost columns of the graph, in that order.
    /// </summary>
    public class ConfigureLaneOrder : Popup
    {
        public string RepositoryOrder
        {
            get => _repositoryOrder;
            set => SetProperty(ref _repositoryOrder, value);
        }

        public string DefaultOrder
        {
            get => _defaultOrder;
            set => SetProperty(ref _defaultOrder, value);
        }

        public ConfigureLaneOrder(Repository repo)
        {
            _repo = repo;
            _repositoryOrder = repo.HasOwnLaneOrder ? Models.LaneOrder.Format(repo.LaneOrder) : string.Empty;
            _defaultOrder = Models.LaneOrder.Format(Preferences.Instance.DefaultLaneOrder);
        }

        public override Task<bool> Sure()
        {
            var pref = Preferences.Instance;
            pref.DefaultLaneOrder = Models.LaneOrder.Parse(_defaultOrder);
            pref.Save();

            _repo.SetLaneOrder(Models.LaneOrder.Parse(_repositoryOrder));
            return Task.FromResult(true);
        }

        private readonly Repository _repo;
        private string _repositoryOrder;
        private string _defaultOrder;
    }
}
