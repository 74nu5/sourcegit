using System;
using System.Collections.Generic;

using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    /// <summary>
    ///     One section of the left panel, named once so that hiding it, listing it and
    ///     bringing it back all speak of the same thing.
    /// </summary>
    public sealed class SidebarSection : ObservableObject
    {
        public string Key { get; init; } = string.Empty;

        /// <summary>
        ///     What the header says, so the chip that brings it back says the same.
        /// </summary>
        public string Label { get; init; } = string.Empty;

        public Action Restore { get; init; }
    }

    /// <summary>
    ///     The sections a repository currently hides.
    ///
    ///     A hidden section has no header left to right-click, so without this there would be
    ///     no way back to it short of a settings dialog nobody would think to open. The list
    ///     appears only when something is in it, and costs a single row when it does.
    /// </summary>
    public sealed class SidebarSections : ObservableObject
    {
        public AvaloniaList<SidebarSection> Hidden { get; } = [];

        public bool HasHidden
        {
            get => _hasHidden;
            private set => SetProperty(ref _hasHidden, value);
        }

        public void Rebuild(IEnumerable<SidebarSection> hidden)
        {
            Hidden.Clear();
            foreach (var section in hidden)
                Hidden.Add(section);

            HasHidden = Hidden.Count > 0;
        }

        private bool _hasHidden = false;
    }
}
