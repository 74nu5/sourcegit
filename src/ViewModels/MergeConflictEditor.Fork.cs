using System;
using System.ComponentModel;
using System.Text;

namespace SourceGit.ViewModels
{
    /// <summary>
    ///     Typing in the Result panel.
    ///
    ///     Upstream resolves a conflict by choosing, block by block: mine, theirs, both.
    ///     What it never lets anyone do is type -- and a merge almost always ends with a
    ///     touch-up neither side describes: a brace too many, two imports to fold into
    ///     one. Until now that meant leaving for an external tool and coming back to stage.
    ///
    ///     Once every block is settled there is nothing left to choose, so the panel stops
    ///     being a rendering of those choices and becomes the file itself.
    /// </summary>
    public partial class MergeConflictEditor
    {
        /// <summary>
        ///     True while the Result panel accepts the keyboard.
        ///
        ///     Not simply "nothing left to resolve": a binary file, a missing file, or one
        ///     with no marker at all also counts zero -- and for those `SaveAndStageAsync`
        ///     returns without writing anything, so whatever was typed would go silently in
        ///     the bin. Hence the second test.
        /// </summary>
        public bool IsResultEditable
        {
            get => _unsolvedCount == 0 && _conflictRegions.Count > 0 && !_backToBlocks;
        }

        /// <summary>
        ///     Whether the switch between the two modes is worth offering at all.
        /// </summary>
        public bool CanSwitchResultMode
        {
            get => _unsolvedCount == 0 && _conflictRegions.Count > 0;
        }

        /// <summary>
        ///     Whether anything was actually typed. Nothing is lost, and nothing has to be
        ///     asked, as long as this stays false.
        /// </summary>
        public bool HasResultEdits
        {
            get => _hasResultEdits;
            private set => SetProperty(ref _hasResultEdits, value);
        }

        /// <summary>
        ///     Where the saved text comes from once the user has typed.
        ///
        ///     Installed by the view at the first real keystroke and cleared when the panel
        ///     goes back to blocks, so a file nobody retouched is still written by the
        ///     upstream path, byte for byte.
        /// </summary>
        public Func<string> ResultTextProvider
        {
            get;
            set;
        }

        /// <summary>
        ///     Result scrolls on its own once it is editable.
        ///
        ///     The three panels share <see cref="ScrollOffset"/>, which is exactly what the
        ///     padding is for. Take the padding away and the alignment is a lie; worse,
        ///     bringing the caret into view would drag Mine and Theirs, which clamp at
        ///     their own length and drag the caret back.
        /// </summary>
        public Avalonia.Vector ResultScrollOffset
        {
            get => _resultScrollOffset;
            set => SetProperty(ref _resultScrollOffset, value);
        }

        /// <summary>
        ///     The text the Result panel starts from when it becomes editable.
        ///
        ///     Built from what is on screen rather than rebuilt from the regions, on
        ///     purpose: what the user sees is then exactly what gets saved. One kind of line
        ///     is dropped on the way -- the padding the display inserts to keep the three
        ///     panels aligned, which is the only thing in the Result panel carrying
        ///     <see cref="Models.ConflictLineType.None"/>. Contents come from
        ///     <see cref="Models.ConflictLine"/>, never from the document, so a line longer
        ///     than the 1000 characters the display trims comes back whole.
        /// </summary>
        public string BuildEditableSeed()
        {
            var builder = new StringBuilder();
            var first = true;

            foreach (var line in _resultLines)
            {
                if (line.Type == Models.ConflictLineType.None)
                    continue;

                if (!first)
                    builder.Append('\n');

                builder.Append(line.Content);
                first = false;
            }

            return builder.ToString();
        }

        public void MarkResultEdited()
        {
            HasResultEdits = true;
        }

        /// <summary>
        ///     Leave the typing behind and go back to choosing blocks.
        ///
        ///     Free retouches cannot be put back into a per-block model, so the caller has
        ///     already asked. The order matters: the view has to be out of edit mode before
        ///     <c>RefreshDisplayData</c> reassigns `ResultLines`, or the guard in
        ///     `UpdateContent` would still be closed and the panel would keep the old text.
        /// </summary>
        public void SwitchToBlocks()
        {
            if (_backToBlocks)
                return;

            ResultTextProvider = null;
            HasResultEdits = false;
            _backToBlocks = true;

            OnPropertyChanged(nameof(IsResultEditable));
            RefreshDisplayData();
        }

        /// <summary>
        ///     Come back to typing after a detour through the blocks. The panel is reseeded
        ///     from the regions: a clean state, never half of one.
        /// </summary>
        public void SwitchToEditing()
        {
            if (!_backToBlocks || !CanSwitchResultMode)
                return;

            _backToBlocks = false;
            OnPropertyChanged(nameof(IsResultEditable));
        }

        /// <summary>
        ///     What `SaveAndStageAsync` writes. Upstream rebuilds the file from the regions,
        ///     and that stays the answer until someone has actually typed.
        /// </summary>
        private string PickContentToWrite(string rebuilt)
        {
            return ResultTextProvider?.Invoke() ?? rebuilt;
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);

            if (!string.Equals(e.PropertyName, nameof(UnsolvedCount), StringComparison.Ordinal))
                return;

            // Undoing a resolution reopens the block panel for good: the text that was typed
            // no longer describes the file the regions now produce.
            if (_unsolvedCount > 0)
            {
                _backToBlocks = false;
                ResultTextProvider = null;
                HasResultEdits = false;
            }

            OnPropertyChanged(nameof(IsResultEditable));
            OnPropertyChanged(nameof(CanSwitchResultMode));
        }

        private bool _backToBlocks = false;
        private bool _hasResultEdits = false;
        private Avalonia.Vector _resultScrollOffset = Avalonia.Vector.Zero;
    }
}
