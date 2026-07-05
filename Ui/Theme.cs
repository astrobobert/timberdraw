using System;
using System.Drawing;
using System.Windows.Forms;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TimberDraw
{
    // The ONE visual system for every TimberDraw UI surface. All colors, fonts, metrics, and
    // control factories route through here -- no surface declares its own palette. Colorblind
    // rule (durable): NEVER green as an indicator; the accent is blue, notices are blue.
    //
    // Theme follows AutoCAD's COLORTHEME sysvar (0 = dark, 1 = light), read ONCE, lazily, the
    // first time any Theme member is touched. Changing the AutoCAD theme takes effect the next
    // session -- there is no live re-theme.
    public static class Theme
    {
        private static bool _init;
        private static bool _dark;

        // ---- palette -------------------------------------------------------------------

        public static Color Bg         { get { EnsureInit(); return _dark ? Color.FromArgb(45, 45, 48)    : SystemColors.Control; } }
        public static Color Surface    { get { EnsureInit(); return _dark ? Color.FromArgb(55, 55, 60)    : SystemColors.Window; } }
        public static Color Fg         { get { EnsureInit(); return _dark ? Color.FromArgb(240, 240, 240) : SystemColors.ControlText; } }
        public static Color SubtleFg   { get { EnsureInit(); return _dark ? Color.FromArgb(160, 160, 165) : SystemColors.GrayText; } }
        public static Color Border     { get { EnsureInit(); return _dark ? Color.FromArgb(70, 70, 75)    : SystemColors.ControlDark; } }
        public static Color HeaderBack { get { EnsureInit(); return _dark ? Color.FromArgb(62, 62, 68)    : SystemColors.ControlLight; } }

        // The one blue. Dark mode lightens it for contrast -- still blue, never green.
        public static Color Accent     { get { EnsureInit(); return _dark ? Color.FromArgb(80, 140, 255)  : Color.FromArgb(0, 70, 200); } }

        public static bool IsDark      { get { EnsureInit(); return _dark; } }

        // ---- type ----------------------------------------------------------------------

        private static Font _base, _header;
        public static Font Base   { get { if (_base   == null) _base   = new Font("Segoe UI", 9f); return _base; } }
        public static Font Header { get { if (_header == null) _header = new Font("Segoe UI", 9f, FontStyle.Bold); return _header; } }

        // ---- metrics (unifies PropertyPane's 150 vs JoinPaletteControl's 124) -----------

        public const int LabelW = 150;   // property-row label column width
        public const int RowH   = 23;    // property/editor row height
        public const int Pad    = 4;     // standard inner padding

        // ---- factories (replace the per-file Btn/Cap/Chk/MakeButton helpers) -------------

        public static Button Button(string text, EventHandler onClick = null)
        {
            var b = new Button
            {
                Text = text,
                Height = 26,
                FlatStyle = IsDark ? FlatStyle.Flat : FlatStyle.System,
                Font = Base,
            };
            if (IsDark)
            {
                b.BackColor = Color.FromArgb(60, 60, 65);
                b.ForeColor = Fg;
                b.FlatAppearance.BorderColor = Border;
            }
            if (onClick != null) b.Click += onClick;
            return b;
        }

        public static Label Caption(string text) => new Label
        {
            Text = text, AutoSize = true, Font = Base, ForeColor = Fg, Padding = new Padding(0, Pad, 0, 0),
        };

        public static CheckBox Check(string text) => new CheckBox
        {
            Text = text, AutoSize = true, Font = Base, ForeColor = Fg,
        };

        public static Label HeaderLabel(string text) => new Label
        {
            Text = text, AutoSize = false, Height = 20, Dock = DockStyle.Top,
            Font = Header, BackColor = HeaderBack, ForeColor = Fg,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(Pad, 0, 0, 0),
        };

        // Walk a control tree and apply the palette. Only INPUT controls, buttons, and grids are
        // touched explicitly -- labels, checkboxes, group boxes, and containers inherit the root's
        // ambient Bg/Fg, so a control that set its own colors (e.g. a HeaderBack header) keeps them.
        // Call at the END of a control's init (after all children exist).
        public static void Apply(Control root)
        {
            if (root == null) return;
            root.BackColor = Bg;
            root.ForeColor = Fg;
            ApplyChildren(root);
        }

        private static void ApplyChildren(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                switch (c)
                {
                    case DataGridView g: ApplyGrid(g); continue;
                    case TextBox t: t.BackColor = Surface; t.ForeColor = Fg; break;
                    case ComboBox cb: cb.BackColor = Surface; cb.ForeColor = Fg; break;
                    case ListBox lb: lb.BackColor = Surface; lb.ForeColor = Fg; break;
                    case TreeView tv: tv.BackColor = Surface; tv.ForeColor = Fg; break;
                    // A GroupBox CAPTION only honors ForeColor when it is set LOCALLY (the visual-
                    // styles renderer ignores ambient inheritance), so dark mode must set it or the
                    // captions paint navy-on-near-black. Light mode keeps the stock themed caption.
                    case GroupBox gb: if (IsDark) gb.ForeColor = Fg; break;
                    case Button b:
                        if (IsDark)
                        {
                            b.FlatStyle = FlatStyle.Flat;
                            b.BackColor = Color.FromArgb(60, 60, 65);
                            b.ForeColor = Fg;
                            b.FlatAppearance.BorderColor = Border;
                        }
                        break;
                }
                if (c.HasChildren) ApplyChildren(c);
            }
        }

        public static void ApplyGrid(DataGridView g)
        {
            g.EnableHeadersVisualStyles = false;
            g.BackgroundColor = Bg;
            g.GridColor = Border;
            g.DefaultCellStyle.BackColor = Surface;
            g.DefaultCellStyle.ForeColor = Fg;
            g.DefaultCellStyle.SelectionBackColor = Accent;
            g.DefaultCellStyle.SelectionForeColor = IsDark ? Color.Black : Color.White;
            g.ColumnHeadersDefaultCellStyle.BackColor = HeaderBack;
            g.ColumnHeadersDefaultCellStyle.ForeColor = Fg;
            g.ColumnHeadersDefaultCellStyle.Font = Header;
            g.RowHeadersDefaultCellStyle.BackColor = HeaderBack;
        }

        // Status-line convention (codifies the Joints pane rule): notices are Accent blue,
        // everything else is plain Fg. Never green.
        public static void SetStatus(Label status, string msg, bool notice)
        {
            if (status == null) return;
            status.Text = msg ?? "";
            status.ForeColor = notice ? Accent : Fg;
        }

        // ---- init ------------------------------------------------------------------------

        private static void EnsureInit()
        {
            if (_init) return;
            _init = true;
            try
            {
                // COLORTHEME: 0 = dark UI, 1 = light UI. Absent/odd values fall back to light.
                object v = AcadApp.GetSystemVariable("COLORTHEME");
                _dark = Convert.ToInt32(v) == 0;
            }
            catch { _dark = false; }
        }
    }
}
