using System.Drawing;
using System.Windows.Forms;

namespace TimberDraw
{
    // The ONE property-row idiom, shared by PropertyPane (the Frame tab's grid) and the Joints
    // pane's union grid: a 2-column TableLayoutPanel docked to the top of an auto-scrolling host,
    // accent header rows, label + typed-editor rows at Theme.LabelW/RowH, and
    // painted separators. Each consumer keeps its own binding/commit semantics -- this is layout
    // and styling only, so the two grids can never drift apart visually again. Section headers
    // are ACCENT TEXT with no filled band (the one header idiom, every tab).
    internal static class PaneRows
    {
        // The 2-col grid, ready to dock into a scroll host.
        public static TableLayoutPanel MakeGrid()
        {
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows,
                Padding = new Padding(0, 0, 0, Theme.Pad),
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.LabelW));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            return grid;
        }

        // A bold section-header LABEL (PropertyPane's category rows + the Assembly tab's flat
        // sections). Accent text on the plain background -- NO filled band, so a header can never
        // be mistaken for a button (Robert's Assembly-tab call; matches the Browser's group headers).
        public static Label HeaderCell(string text) => new Label
        {
            Text = text, Dock = DockStyle.Fill, Height = 22,
            BackColor = Theme.Bg, ForeColor = Theme.Accent, Font = Theme.Header,
            TextAlign = ContentAlignment.BottomLeft,
            Padding = new Padding(Theme.Pad, 0, 0, 2), Margin = new Padding(0),
        };

        // A bold section-header CHECKBOX (the Joints pane's element-enable rows) -- same accent
        // no-band look as HeaderCell; the checkbox glyph is what says "clickable". Disabled rows
        // gray out via the system renderer as before.
        public static CheckBox HeaderCheck(string text) => new CheckBox
        {
            Text = text, Dock = DockStyle.Fill, Height = 22,
            BackColor = Theme.Bg, ForeColor = Theme.Accent, Font = Theme.Header,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(2, 0, 0, 0), Margin = new Padding(0),
        };

        // A row label. leftPad distinguishes top-level rows (6) from indented param rows (8).
        public static Label RowLabel(string text, int leftPad, bool subtle = false) => new Label
        {
            Text = text, Dock = DockStyle.Fill, Height = Theme.RowH, AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = subtle ? Theme.SubtleFg : Theme.Fg,
            Padding = new Padding(leftPad, 0, 0, 0), Margin = new Padding(0),
        };

        public static void AddFullRow(TableLayoutPanel grid, Control c)
        {
            int r = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(c, 0, r);
            grid.SetColumnSpan(c, 2);
        }

        public static void AddParamRow(TableLayoutPanel grid, Control label, Control editor)
        {
            int r = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(label, 0, r);
            grid.Controls.Add(editor, 1, r);
        }

        public static void AddSeparator(TableLayoutPanel grid)
        {
            var sep = new Panel { Dock = DockStyle.Fill, Height = 7, Margin = new Padding(0) };
            sep.Paint += (s, e) =>
            {
                int y = sep.Height / 2;
                using (var p = new Pen(Theme.Border))
                    e.Graphics.DrawLine(p, 2, y, sep.Width - 2, y);
            };
            AddFullRow(grid, sep);
        }
    }
}
