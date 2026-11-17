using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    /// <summary>
    ///     Keeps the history filters as they are under a name, to come back to them in one
    ///     click. A view of the same name is replaced.
    /// </summary>
    public class SaveHistoryFilterView : Popup
    {
        [Required(ErrorMessage = "Name is required!")]
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value, true);
        }

        public SaveHistoryFilterView(Repository repo)
        {
            _repo = repo;
        }

        public override Task<bool> Sure()
        {
            _repo.SaveHistoryFilterView(_name);
            return Task.FromResult(true);
        }

        private readonly Repository _repo;
        private string _name = string.Empty;
    }
}
