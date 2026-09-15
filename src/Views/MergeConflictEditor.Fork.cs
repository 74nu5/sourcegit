using System;
using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class MergeConflictTextPresenter
    {
        /// <summary>
        ///     While this is true the document is the file, not a rendering of the block
        ///     choices -- so nothing may overwrite it. Read by `UpdateContent`, which is the
        ///     one path that would silently throw the typing away.
        /// </summary>
        public bool IsEditingResult
        {
            get => _isEditingResult;
        }

        /// <summary>
        ///     Hand the panel over to the keyboard.
        ///
        ///     Everything taken apart here is indexed by document line number into `Lines`:
        ///     one inserted line and it all points at the wrong rows. Removing it is honest;
        ///     leaving it in place and hoping is not.
        /// </summary>
        public void EnterEditMode(ViewModels.MergeConflictEditor vm)
        {
            if (_isEditingResult || vm == null)
                return;

            _isEditingResult = true;

            // Assigning Text clears the undo stack and resets the caret. That is wanted: a
            // Ctrl+Z reaching back to the padded, trimmed rendering would hand the user a
            // corrupt text with a save button next to it.
            Text = vm.BuildEditableSeed();

            for (var i = TextArea.TextView.BackgroundRenderers.Count - 1; i >= 0; i--)
            {
                if (TextArea.TextView.BackgroundRenderers[i] is LineBackgroundRenderer)
                    TextArea.TextView.BackgroundRenderers.RemoveAt(i);
            }

            for (var i = TextArea.TextView.LineTransformers.Count - 1; i >= 0; i--)
            {
                if (TextArea.TextView.LineTransformers[i] is ConflictMarkerTransformer)
                    TextArea.TextView.LineTransformers.RemoveAt(i);
            }

            // Our gutter numbers the lines of the original file and leaves the padding
            // blank. AvaloniaEdit's own margin counts the document, which is now exactly
            // what the file is about to become.
            TextArea.LeftMargins.Clear();
            ShowLineNumbers = true;

            // Hovering reports a block by comparing a document line index against a line
            // index in the original file. Without the padding that mapping is a lie, and
            // `Undo` would land on a different region than the one under the pointer.
            TextArea.TextView.PointerEntered -= OnTextViewPointerChanged;
            TextArea.TextView.PointerMoved -= OnTextViewPointerChanged;
            TextArea.TextView.PointerWheelChanged -= OnTextViewPointerWheelChanged;
            vm.SelectedChunk = null;

            // Result scrolls alone from here: bringing the caret into view would otherwise
            // drag Mine and Theirs, which clamp at their own length and drag it back.
            if (_scrollViewer != null)
            {
                _scrollViewer.ScrollChanged -= OnTextViewScrollChanged;
                vm.ResultScrollOffset = _scrollViewer.Offset;
                _scrollViewer.Bind(ScrollViewer.OffsetProperty, new Binding("ResultScrollOffset", BindingMode.TwoWay));
            }

            IsReadOnly = false;

            // Subscribed after the seed, so seeding is never mistaken for a keystroke.
            TextChanged += OnResultTextChanged;
        }

        /// <summary>
        ///     Give the panel back to the blocks, exactly as it was found.
        /// </summary>
        public void LeaveEditMode(ViewModels.MergeConflictEditor vm)
        {
            if (!_isEditingResult)
                return;

            TextChanged -= OnResultTextChanged;
            IsReadOnly = true;
            _isEditingResult = false;

            ShowLineNumbers = false;
            TextArea.LeftMargins.Clear();
            TextArea.LeftMargins.Add(new LineNumberMargin(this));
            TextArea.LeftMargins.Add(new VerticalSeparatorMargin());

            TextArea.TextView.BackgroundRenderers.Add(new LineBackgroundRenderer(this));
            TextArea.TextView.LineTransformers.Add(new ConflictMarkerTransformer(this));

            TextArea.TextView.PointerEntered += OnTextViewPointerChanged;
            TextArea.TextView.PointerMoved += OnTextViewPointerChanged;
            TextArea.TextView.PointerWheelChanged += OnTextViewPointerWheelChanged;

            if (_scrollViewer != null)
            {
                _scrollViewer.Bind(ScrollViewer.OffsetProperty, new Binding("ScrollOffset", BindingMode.OneWay));
                _scrollViewer.ScrollChanged += OnTextViewScrollChanged;
            }

            if (vm != null)
                vm.SelectedChunk = null;

            // A rendering again: put back whatever the regions currently say.
            UpdateContent();
        }

        private void OnResultTextChanged(object sender, EventArgs e)
        {
            if (DataContext is not ViewModels.MergeConflictEditor vm)
                return;

            vm.MarkResultEdited();
            vm.ResultTextProvider ??= () => Text;
        }

        private bool _isEditingResult = false;
    }

    public partial class MergeConflictEditor
    {
        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);

            if (_watched != null)
            {
                _watched.PropertyChanged -= OnViewModelPropertyChanged;
                _watched = null;
            }

            if (DataContext is ViewModels.MergeConflictEditor vm)
            {
                _watched = vm;
                _watched.PropertyChanged += OnViewModelPropertyChanged;
            }

            ApplyResultMode();
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);
            ApplyResultMode();
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (string.Equals(e.PropertyName, nameof(ViewModels.MergeConflictEditor.IsResultEditable), StringComparison.Ordinal))
                ApplyResultMode();
        }

        private void ApplyResultMode()
        {
            if (ResultPresenter == null || DataContext is not ViewModels.MergeConflictEditor vm)
                return;

            if (vm.IsResultEditable)
                ResultPresenter.EnterEditMode(vm);
            else
                ResultPresenter.LeaveEditMode(vm);
        }

        /// <summary>
        ///     The one way back out of typing -- and the one way back in.
        ///
        ///     It is not the activation button that was ruled out: the nominal path stays
        ///     free of it, since resolving the last block opens the keyboard on its own.
        ///     This exists so that opening it is not a one-way door. Without it, someone who
        ///     picked `Use Mine` on the last block and meant `Use Theirs` would have no way
        ///     back: once every block is resolved, hovering Mine or Theirs reports nothing,
        ///     and the Result popup that carried `Undo` is detached while typing.
        /// </summary>
        private async void OnSwitchResultMode(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            if (DataContext is not ViewModels.MergeConflictEditor vm)
                return;

            if (!vm.IsResultEditable)
            {
                vm.SwitchToEditing();
                return;
            }

            if (vm.HasResultEdits)
            {
                var confirm = new Confirm();
                confirm.SetData(App.Text("MergeConflictEditor.DiscardEdits"), Models.ConfirmButtonType.YesNo);

                var sure = await confirm.ShowDialog<bool>(this);
                if (!sure)
                    return;
            }

            vm.SwitchToBlocks();
        }

        private ViewModels.MergeConflictEditor _watched = null;
    }
}
