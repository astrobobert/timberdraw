using System;
using Autodesk.AutoCAD.Windows;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TimberDraw
{
    // The single TimberDraw palette: one PaletteSet, five workflow-ordered tabs
    // (Frame -> Assembly -> Joints -> Browser -> Output), replacing the old
    // per-command PaletteSets (TDraw / TPanel / TBrowse / TBom). The opener
    // commands keep their names and land on their tab; every hosted control is
    // created ONCE and reused for the whole session (their doc-switch event
    // subscriptions rely on that).
    //
    // Sizing: this is a big program -- give it the space it needs. Most users
    // run dual screens and park the palette on display 2, so the minimum is the
    // largest any tab wants (BOM's width, Frame's height), not a squeezed panel.
    public static class Shell
    {
        // The ONLY source of tab indices (matches the Add order in EnsureCreated).
        public enum Tab { Frame = 0, Assembly = 1, Joints = 2, Browser = 3, Output = 4 }

        private static PaletteSet _ps;
        private static FrameTreeControl _frameTree;
        private static TimberPaletteControl _assembly;
        private static JoinPaletteControl _joints;
        private static Browser.FrameBrowserView _browser;
        private static BomGridControl _bom;

        // Show the shell and land on a tab. Frame refreshes its freeze gate on every
        // show (TFreeze may have run since); Browser reloads its list on re-show --
        // both mirror what the old standalone openers did.
        public static void Show(Tab tab)
        {
            bool created = ShowSet();
            _ps.Activate((int)tab);

            if (tab == Tab.Frame) _frameTree.RefreshFrozenState();
            if (tab == Tab.Browser && !created) _browser.Reload();
        }

        // TBom's entry: load the freshly built piece tally into the grid, then land on Output.
        public static void LoadBom(System.Data.DataTable table)
        {
            ShowSet();
            _bom.LoadData(table);
            _ps.Activate((int)Tab.Output);
        }

        // Create-if-needed + show. PaletteSet.Size is a no-op until the palette window
        // exists, so the roomy first-run float is applied right AFTER the first show
        // (MinimumSize, by contrast, is honored pre-show).
        private static bool ShowSet()
        {
            bool created = EnsureCreated();
            _ps.Visible = true;
            if (created) _ps.Size = new System.Drawing.Size(640, 860);
            return created;
        }

        // Create the PaletteSet + all five tabs once. Returns true when this call created it.
        private static bool EnsureCreated()
        {
            if (_ps != null) return false;

            // Fresh GUID on purpose: AutoCAD caches a PaletteSet's title/dock state by GUID,
            // so reusing an old one would resurrect a stale title or dock position.
            _ps = new PaletteSet("TimberDraw", "TimberDraw", new Guid("9D4E1A77-2C3B-4E8F-A1D6-5F0B8C2E7A31"));

            _frameTree = new FrameTreeControl();
            _assembly  = new TimberPaletteControl();
            _joints    = new JoinPaletteControl();
            _browser   = new Browser.FrameBrowserView();
            _bom       = new BomGridControl();

            var browserHost = new System.Windows.Forms.Integration.ElementHost
            {
                Dock = System.Windows.Forms.DockStyle.Fill,
                Child = _browser,
            };

            _ps.Add("Frame",    _frameTree);
            _ps.Add("Assembly", _assembly);
            _ps.Add("Joints",   _joints);
            _ps.Add("Browser",  browserHost);
            _ps.Add("Output",   _bom);

            _ps.MinimumSize = new System.Drawing.Size(560, 680);
            _ps.Style = PaletteSetStyles.ShowCloseButton | PaletteSetStyles.ShowPropertiesMenu
                      | PaletteSetStyles.ShowAutoHideButton;
            return true;
        }
    }
}
