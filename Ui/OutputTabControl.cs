using System;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TimberDraw
{
    // The Output tab: everything that leaves the model in one place -- one verb toolbar (the
    // BOM's Refresh / Export CSV plus the Shop and Scribe commands, fired like every other
    // palette verb via SendStringToExecute) over the BOM grid.
    internal class OutputTabControl : UserControl
    {
        private readonly BomGridControl _bom = new BomGridControl();

        // The hosted grid, for Shell.LoadBom.
        public BomGridControl Bom => _bom;

        public OutputTabControl()
        {
            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 32,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(4, 3, 4, 3),
            };
            bar.Controls.Add(Tool("Refresh",    (s, e) => _bom.RefreshFromModel()));
            bar.Controls.Add(Tool("Export CSV", (s, e) => _bom.ExportCsv()));
            bar.Controls.Add(Tool("Shop",       (s, e) => Send("TShop")));
            bar.Controls.Add(Tool("Shop Clear", (s, e) => Send("TShopClear")));
            bar.Controls.Add(Tool("Scribe",     (s, e) => Send("TScribe")));
            bar.Controls.Add(Tool("Scribe All", (s, e) => Send("TScribeAll")));

            _bom.Dock = DockStyle.Fill;
            Controls.Add(_bom);
            Controls.Add(bar);
            Theme.Apply(this);
        }

        private static Button Tool(string text, EventHandler onClick)
        {
            Button b = Theme.Button(text);
            b.AutoSize = true;
            b.Margin = new Padding(2, 0, 2, 0);
            b.Click += onClick;
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
