namespace SourceGit.ViewModels
{
    /// <summary>
    ///     Whether the graph leaves this branch out, so the sidebar can say so.
    ///
    ///     The filter icon beside a branch only shows the filter set on that branch itself.
    ///     A branch inside a filtered folder, or matched by a pattern, carried no mark at
    ///     all, and nothing said which branches the graph was actually showing.
    /// </summary>
    public partial class BranchTreeNode
    {
        public bool IsHiddenInGraph
        {
            get => _isHiddenInGraph;
            set
            {
                if (SetProperty(ref _isHiddenInGraph, value))
                    OnPropertyChanged(nameof(NameOpacity));
            }
        }

        public double NameOpacity
        {
            get => _isHiddenInGraph ? 0.45 : 1.0;
        }

        private bool _isHiddenInGraph = false;
    }
}
