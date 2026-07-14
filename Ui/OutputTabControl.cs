using System;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TimberDraw
{
    // The Output tab: everything that leaves the model in one place -- the BOM grid over one
    // bottom action bar (the BOM's Refresh / Export CSV plus the Shop and Scribe commands,
    // fired like every other palette verb via SendStringToExecute). The grid auto-loads on
    // first activation (Shell.EnsureBomLoaded), so the tab never opens empty.
    internal class OutputTabControl : UserControl
    {
        private readonly BomGridControl _bom = new BomGridControl();
        private readonly ToolTip _tip = new ToolTip { AutoPopDelay = 20000, InitialDelay = 400, ReshowDelay = 100, ShowAlways = true };

        // The hosted grid, for Shell.LoadBom.
        public BomGridControl Bom => _bom;

        public OutputTabControl()
        {
            Panel bar = ActionBar.Build(
                ActionBar.Row(Tool("Refresh", "Reload the cut list from the model.", (s, e) => _bom.RefreshFromModel()),
                              Tool("Export CSV", "Write the cut list to a CSV file.", (s, e) => _bom.ExportCsv())),
                ActionBar.Row(Tool("Shop", "TShop: build the shop-map layouts -- per bent, per wall, and floor plans.", (s, e) => Send("TShop")),
                              Tool("Shop Clear", "TShopClear: remove the drawn shop maps.", (s, e) => Send("TShopClear")),
                              Tool("Scribe", "TScribe: export .tsj laser scribe files for SELECTED timbers.", (s, e) => Send("TScribe")),
                              Tool("Scribe All", "TScribeAll: export .tsj scribe files for every timber (identical sticks share one file).", (s, e) => Send("TScribeAll"))));

            _bom.Dock = DockStyle.Fill;
            Controls.Add(_bom);
            Controls.Add(bar);
            _bom.BringToFront();   // fill takes what the bottom bar leaves
            Theme.Apply(this);
        }

        private Button Tool(string text, string tip, EventHandler onClick)
        {
            Button b = Theme.Button(text);
            b.Click += onClick;
            _tip.SetToolTip(b, tip);
            return b;
        }

        // Shop/scribe run interactive prompts + DB writes -- command context required.
        private static void Send(string command)
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            doc?.SendStringToExecute(command + " ", true, false, true);
        }
    }
}
