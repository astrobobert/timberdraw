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
    //
    // Drawing switches: the shell owns doc-switch for the Browser and Output tabs
    // (re-list / re-tally against the new document); the Frame tab subscribes on
    // its own and resets to the empty start.
    public static class Shell
    {
        // The ONLY source of tab indices (matches the Add order in EnsureCreated).
        public enum Tab { Frame = 0, Assembly = 1, Joints = 2, Browser = 3, Output = 4 }

        private static PaletteSet _ps;
        private static FrameTreeControl _frameTree;
        private static TimberPaletteControl _assembly;
        private static JoinPaletteControl _joints;
        private static Browser.FrameBrowserView _browser;
        private static OutputTabControl _output;

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
            _output.Bom.LoadData(table);
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
            _output    = new OutputTabControl();

            var browserHost = new System.Windows.Forms.Integration.ElementHost
            {
                Dock = System.Windows.Forms.DockStyle.Fill,
                Child = _browser,
            };

            _ps.Add("Frame",    _frameTree);
            _ps.Add("Assembly", _assembly);
            _ps.Add("Joints",   _joints);
            _ps.Add("Browser",  browserHost);
            _ps.Add("Output",   _output);

            _ps.MinimumSize = new System.Drawing.Size(560, 680);
            _ps.Style = PaletteSetStyles.ShowCloseButton | PaletteSetStyles.ShowPropertiesMenu
                      | PaletteSetStyles.ShowAutoHideButton;

            // The Output tab never opens empty: first activation with no tally auto-loads the
            // BOM from the model ("what happened to BOM?" -- it was an empty grid). The Browser
            // re-lists on EVERY activation -- its list was built when the palette was created,
            // which could be before any frame existed (the empty-tab-on-entry bug).
            _ps.PaletteActivated += (s, e) =>
            {
                try
                {
                    if (e.Activated == null) return;
                    if (e.Activated.Name == "Output" && !_output.Bom.HasData)
                        _output.Bom.RefreshFromModel();
                    if (e.Activated.Name == "Browser")
                        _browser.Reload();
                }
                catch { /* best-effort; a busy editor just leaves the grid for manual Refresh */ }
            };

            // Drawing switch: the shell re-syncs the Browser and Output tabs to the newly active
            // document (the Frame tab handles its own reset -- do not double-reset it). Subscribed
            // once for the session, matching the controls' create-once lifetime. Best-effort:
            // activation can fire mid-init, same guard as FrameTreeControl.OnDocumentActivated.
            AcadApp.DocumentManager.DocumentActivated += (s, e) =>
            {
                try { OnDocSwitch(e.Document != null); } catch { }
            };
            return true;
        }

        // Re-sync the doc-reading tabs to the newly active drawing. Browser: Reload reads the
        // ACTIVE document (empty list when there is none). Output: drop the old drawing's
        // highlight ids and rebuild the tally (or clear it) -- the PaletteActivated auto-load
        // only fires on tab changes, so it can't cover a switch while Output is visible.
        private static void OnDocSwitch(bool hasDoc)
        {
            _browser.Reload();
            _output.Bom.OnDocSwitch(hasDoc);
        }
    }
}
