using System.Windows;
using System.Windows.Media;

namespace TimberDraw
{
    // Bridges Theme into WPF: stuffs the shared palette into an element's Resources as frozen
    // brushes, so XAML references {DynamicResource Td*} and the WPF surface follows the same
    // COLORTHEME-driven look as the WinForms surfaces. Call after InitializeComponent.
    internal static class ThemeWpf
    {
        public static void Apply(FrameworkElement root)
        {
            root.Resources["TdBg"]      = Brush(Theme.Bg);
            root.Resources["TdSurface"] = Brush(Theme.Surface);
            root.Resources["TdFg"]      = Brush(Theme.Fg);
            root.Resources["TdSubtle"]  = Brush(Theme.SubtleFg);
            root.Resources["TdBorder"]  = Brush(Theme.Border);
            root.Resources["TdAccent"]  = Brush(Theme.Accent);
            root.Resources["TdButton"]  = Brush(Theme.ButtonBack);
            root.Resources["TdHeader"]  = Brush(Theme.HeaderBack);
        }

        private static SolidColorBrush Brush(System.Drawing.Color c)
        {
            var b = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }
    }
}
