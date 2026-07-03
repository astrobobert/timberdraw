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
            DataContext = _vm;
        }

        public void Reload() => _vm.Refresh();
    }
}
