using System;
using System.Drawing;
using System.Windows.Forms;

namespace TimberDraw
{
    // The ONE visual system for every TimberDraw UI surface. All colors, fonts, metrics, and
    // control factories route through here -- no surface declares its own palette. Colorblind
    // rule (durable): NEVER green as an indicator; the accent is blue, notices are blue.
    //
    // The palette is ALWAYS DARK (Robert's call after the Phase B walk-through) -- it does not
    // follow AutoCAD's COLORTHEME. IsDark stays as the single switch so a future light theme
    // is a one-line change. Two durable WinForms rules learned the hard way:
    //  - NEVER rely on ambient color inheritance: the AutoCAD palette host repaints hosted
    //    controls, and any surface without an explicitly pinned BackColor collapses to light.
    //  - GroupBox cannot be themed (the visual-styles renderer paints its frame/caption via
    //    UxTheme, ignoring control colors) -- use flat header sections (PaneRows.HeaderCell).
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
                FlatStyle = FlatStyle.Flat,
                Font = Base,
                BackColor = Color.FromArgb(60, 60, 65),
                ForeColor = Fg,
            };
            b.FlatAppearance.BorderColor = Border;
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

        // (No HeaderLabel factory here: PaneRows.HeaderCell is the ONE section-header idiom --
        // accent text, no band -- so headers can never drift apart or get mistaken for buttons.)

        // Walk a control tree and apply the palette. Everything gets an EXPLICIT color -- never
        // rely on ambient inheritance (the AutoCAD palette host repaints hosted controls, and an
        // un-pinned surface collapses to light). Inputs get flat single-line borders: the default
        // Fixed3D bevel is painted by Windows in the non-client area and ignores BackColor (the
        // "white outline" look). A control that set its own colors first (e.g. an accent section
        // header) keeps them -- labels/checkboxes are left to inherit from their PINNED parent.
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
                    case TextBox t:
                        t.BackColor = Surface; t.ForeColor = Fg;
                        t.BorderStyle = BorderStyle.FixedSingle;
                        break;
                    case ComboBox cb:
                        cb.BackColor = Surface; cb.ForeColor = Fg;
                        cb.FlatStyle = FlatStyle.Flat;
                        break;
                    case ListBox lb:
                        lb.BackColor = Surface; lb.ForeColor = Fg;
                        lb.BorderStyle = BorderStyle.FixedSingle;
                        break;
                    case TreeView tv:
                        tv.BackColor = Surface; tv.ForeColor = Fg;
                        tv.BorderStyle = BorderStyle.FixedSingle;
                        break;
                    // GroupBox is a legacy straggler (new UI uses flat header sections): pin its
                    // interior + set ForeColor LOCALLY (the visual-styles caption renderer ignores
                    // ambient inheritance). Its etched frame stays system-drawn -- don't use it.
                    case GroupBox gb: gb.BackColor = Bg; gb.ForeColor = Fg; break;
                    case Button b:
                        b.FlatStyle = FlatStyle.Flat;
                        b.BackColor = Color.FromArgb(60, 60, 65);
                        b.ForeColor = Fg;
                        b.FlatAppearance.BorderColor = Border;
                        break;
                    case SplitContainer sc: sc.BackColor = Bg; break;
                    // Pin every container explicitly (Panel covers FlowLayoutPanel and
                    // TableLayoutPanel too; the PaneRows separator panel repaints its line anyway).
                    case Panel p: p.BackColor = Bg; break;
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
            _dark = true;   // always dark; flip here (or read COLORTHEME) to bring light back
        }
    }
}
