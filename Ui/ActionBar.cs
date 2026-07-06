using System.Windows.Forms;

namespace TimberDraw
{
    // The ONE bottom-action idiom, modeled on the Joints tab's full-span buttons (the look
    // Robert picked): every tab's command buttons live in one of these, docked at the bottom --
    // full-width rows of equal columns, a hero action on a row of its own. Never scatter action
    // buttons through the content again.
    internal static class ActionBar
    {
        public const int RowH = 32;
        public const int CapH = 18;

        // A small group caption above a row -- subtle grey, no fill, no border: a third visual
        // tier below section headers (accent) and buttons (bordered fill), never mistaken for
        // either. Pass to Build interleaved with the rows it names.
        public static Label Caption(string text) => new Label
        {
            Text = text, Dock = DockStyle.Top, Height = CapH,
            ForeColor = Theme.SubtleFg, BackColor = Theme.Bg, Font = Theme.Base,
            TextAlign = System.Drawing.ContentAlignment.BottomLeft,
            Padding = new Padding(Theme.Pad, 0, 0, 1), Margin = new Padding(0),
        };

        // One full-span row of equal-width buttons.
        public static TableLayoutPanel Row(params Button[] buttons)
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = RowH,
                ColumnCount = buttons.Length,
                RowCount = 1,
                Margin = new Padding(0),
                BackColor = Theme.Bg,
            };
            for (int i = 0; i < buttons.Length; i++)
            {
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / buttons.Length));
                buttons[i].Dock = DockStyle.Fill;
                buttons[i].Margin = new Padding(2);
                row.Controls.Add(buttons[i], i, 0);
            }
            return row;
        }

        // The bottom bar: rows stacked top-down, docked to the host's bottom edge. Pass rows in
        // VISUAL order (first argument renders topmost).
        public static Panel Build(params Control[] rows)
        {
            var bar = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 0,   // grows with each row below
                BackColor = Theme.Bg,
                Padding = new Padding(Theme.Pad, Theme.Pad, Theme.Pad, Theme.Pad),
            };
            // Dock=Top stacking renders in REVERSE add order, so add last row first.
            for (int i = rows.Length - 1; i >= 0; i--)
            {
                bar.Controls.Add(rows[i]);
                bar.Height += rows[i].Height;
            }
            bar.Height += 2 * Theme.Pad;
            return bar;
        }
    }
}
