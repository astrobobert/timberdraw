using System.Windows.Controls;

namespace TimberDraw.Browser
{
    // Code-behind for the Frame Browser WPF pilot. Owns its view-model; Reload() lets the host
    // refresh the list when the palette is re-shown.
    public partial class FrameBrowserView : UserControl
    {
        private readonly FrameBrowserViewModel _vm = new();
        private bool _stale;          // a joinery re-cut replaced entities while this tab was hidden
        private bool _reloadQueued;   // debounce: a batch cut (TJointAll) fires Rebuilt per timber

        public FrameBrowserView()
        {
            InitializeComponent();
            ThemeWpf.Apply(this);   // feed the shared Theme palette into the Td* XAML resources
            DataContext = _vm;

            // Joinery re-cuts ERASE + REDRAW the solid (fresh ObjectId), so held rows go stale and
            // selection stops highlighting. Re-list when it happens: now if visible (deferred past
            // the cutting command), on next show otherwise. One shell instance lives the whole
            // session, so the static subscription is intentional.
            ManagedTimber.Rebuilt += OnTimberRebuilt;
            IsVisibleChanged += (s, e) =>
            {
                if (IsVisible && _stale) { _stale = false; _vm.Refresh(); }
            };
        }

        private void OnTimberRebuilt()
        {
            if (!IsVisible) { _stale = true; return; }
            if (_reloadQueued) return;
            _reloadQueued = true;
            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                _reloadQueued = false;
                try { _vm.Refresh(); } catch { /* editor busy -- the next show re-lists */ }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        public void Reload() => _vm.Refresh();

        // SelectedItems isn't bindable -- push the full multi-selection into the view-model (it
        // grip-highlights the timbers and feeds Assign; the view never moves on select).
        private void TimberList_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => _vm.SetSelectedMany(TimberList.SelectedItems);

        // Zoom is on demand only: double-click frames the clicked row's timber.
        private void TimberList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => _vm.ZoomSelected();
    }
}
