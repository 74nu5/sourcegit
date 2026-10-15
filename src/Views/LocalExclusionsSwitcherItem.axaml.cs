using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    /// <summary>
    ///     The "Excluded Locally" entry of a repository's page bar, after upstream's three.
    ///
    ///     A control of its own so that upstream's page bar takes one line for it. It is shown
    ///     only while something is excluded, or when the preference keeps it -- with neither,
    ///     the bar is exactly upstream's.
    ///
    ///     That preference has to be reachable while the entry is hidden, so its menu belongs
    ///     to the whole bar: right-clicking any page there offers it.
    /// </summary>
    public partial class LocalExclusionsSwitcherItem : ListBoxItem
    {
        public static readonly FuncMultiValueConverter<object, bool> IsShown =
            new(values =>
            {
                var list = new List<object>(values);
                if (list.Count < 3 || list[0] is not bool isBare || isBare)
                    return false;

                var always = list[1] is true;
                var count = list[2] is int n ? n : 0;
                return always || count > 0;
            });

        protected override Type StyleKeyOverride => typeof(ListBoxItem);

        public LocalExclusionsSwitcherItem()
        {
            InitializeComponent();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            _bar = this.FindAncestorOfType<ListBox>();
            if (_bar != null)
                _bar.ContextRequested += OnBarContextRequested;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (_bar != null)
                _bar.ContextRequested -= OnBarContextRequested;
            _bar = null;

            base.OnDetachedFromVisualTree(e);
        }

        private void OnBarContextRequested(object sender, ContextRequestedEventArgs e)
        {
            if (DataContext is not ViewModels.Repository { IsBare: false } repo)
                return;

            var pref = ViewModels.Preferences.Instance;

            var always = new MenuItem();
            always.Header = App.Text("LocalExclusions.AlwaysShow");
            if (pref.ShowLocalExclusionsPage)
                always.Icon = this.CreateMenuIcon("Icons.Check");
            always.Click += (_, ev) =>
            {
                pref.ShowLocalExclusionsPage = !pref.ShowLocalExclusionsPage;

                // Turned off while on the page with nothing on it: the entry disappears, and
                // the page with it.
                if (!pref.ShowLocalExclusionsPage && repo.LocalExclusionsCount == 0 && repo.IsLocalExclusionsVisible)
                    repo.SelectedViewIndex = 1;

                ev.Handled = true;
            };

            var menu = new ContextMenu();
            menu.Items.Add(always);
            menu.Open(sender as Control);
            e.Handled = true;
        }

        private ListBox _bar = null;
    }
}
