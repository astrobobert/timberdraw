using System.Windows.Controls;

namespace TimberDraw.Browser
{
    // Code-behind for the Frame Browser WPF pilot. Owns its view-model; Reload() lets the host
    // refresh the list when the palette is re-shown.
    public partial class FrameBrowserView : UserControl
    {
        private readonly FrameBrowserViewModel _vm = new();

        public FrameBrowserView()
        {
            InitializeComponent();
            ThemeWpf.Apply(this);   // feed the shared Theme palette into the Td* XAML resources
            DataContext = _vm;
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
