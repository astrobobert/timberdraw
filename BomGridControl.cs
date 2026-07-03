using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Windows.Forms;
using Autodesk.AutoCAD.DatabaseServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using DataTable = System.Data.DataTable;    // disambiguate from Autodesk.AutoCAD.DatabaseServices.DataTable

namespace TimberDraw
{
    // The BOM palette control: a sortable DataGridView of the per-timber piece tally (TBom -> BuildBomTable).
    // Rows are read-only + multi-select; selecting rows HIGHLIGHTS the matching solids in model space by their
    // entity handle (Entity.Highlight under a document lock -- the proven from-palette pattern). Refresh
    // re-reads the model; Export writes the tally to CSV.
    public class BomGridControl : UserControl
    {
        private readonly DataGridView _grid;
        private readonly List<ObjectId> _highlighted = new List<ObjectId>();
        private bool _loading;

        // Palette / dark-mode colors (match ApplyDarkMode elsewhere; blue selection -- never green).
        private static readonly Color Bg     = Color.FromArgb(45, 45, 48);
        private static readonly Color Input  = Color.FromArgb(55, 55, 60);
        private static readonly Color Fg     = Color.FromArgb(240, 240, 240);
        private static readonly Color Accent = Color.FromArgb(0, 70, 200);   // blue (colorblind-safe)

        public BomGridControl()
        {
            BackColor = Bg;
            ForeColor = Fg;

            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 30,
                FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight,
                Padding = new Padding(4, 3, 4, 3),
                BackColor = Bg
            };
            bar.Controls.Add(MakeButton("Refresh", Refresh_Click));
            bar.Controls.Add(MakeButton("Export CSV", Export_Click));

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
                AutoGenerateColumns = true,
                BackgroundColor = Input,
                BorderStyle = BorderStyle.None,
                EnableHeadersVisualStyles = false
            };
            _grid.DefaultCellStyle.BackColor = Input;
            _grid.DefaultCellStyle.ForeColor = Fg;
            _grid.DefaultCellStyle.SelectionBackColor = Accent;
            _grid.DefaultCellStyle.SelectionForeColor = Fg;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Bg;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Fg;
            _grid.GridColor = Color.FromArgb(70, 70, 75);
            _grid.SelectionChanged += Grid_SelectionChanged;

            Controls.Add(_grid);
            Controls.Add(bar);
        }

        private Button MakeButton(string text, EventHandler onClick)
        {
            var b = new Button
            {
                Text = text,
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(60, 60, 65),
                ForeColor = Fg,
                Margin = new Padding(2, 0, 2, 0)
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 75);
            b.Click += onClick;
            return b;
        }

        // Bind a freshly built table. Clears any existing highlight and the initial auto-selection.
        public void LoadData(DataTable table)
        {
            _loading = true;
            ClearHighlight();
            _grid.DataSource = null;
            _grid.DataSource = table;

            if (_grid.Columns.Contains("Overall (in)")) _grid.Columns["Overall (in)"].DefaultCellStyle.Format = "0.0";
            if (_grid.Columns.Contains("BF")) _grid.Columns["BF"].DefaultCellStyle.Format = "0.00";
            foreach (DataGridViewColumn c in _grid.Columns)
                if (c.ValueType == typeof(int) || c.ValueType == typeof(double))
                    c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

            _grid.ClearSelection();
            _loading = false;
        }

        private void Refresh_Click(object sender, EventArgs e)
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            try
            {
                using (doc.LockDocument())
                    LoadData(ManagedCommands.BuildBomTable(doc.Database));
            }
            catch (Exception ex) { MessageBox.Show("BOM refresh failed: " + ex.Message); }
        }

        private void Export_Click(object sender, EventArgs e)
        {
            if (!(_grid.DataSource is DataTable t) || t.Rows.Count == 0) return;
            using (var dlg = new SaveFileDialog
            {
                Title = "Export BOM",
                Filter = "CSV files (*.csv)|*.csv",
                DefaultExt = "csv",
                FileName = "Timber-BOM.csv",
                OverwritePrompt = true
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                try { ManagedCommands.WritePieceCsv(t, dlg.FileName); }
                catch (Exception ex) { MessageBox.Show("Export failed: " + ex.Message); }
            }
        }

        private void Grid_SelectionChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            var handles = new List<string>();
            foreach (DataGridViewRow row in _grid.SelectedRows)
            {
                if (!_grid.Columns.Contains("Handle")) break;
                object h = row.Cells["Handle"].Value;
                if (h != null) handles.Add(h.ToString());
            }
            UpdateHighlight(handles);
        }

        private void ClearHighlight() => UpdateHighlight(new List<string>());

        // Un-highlight the previously highlighted solids, then highlight the ones named by `handles`. One
        // document lock + transaction (runs on the UI thread -- the proven TimberTag from-palette pattern).
        private void UpdateHighlight(List<string> handles)
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            try
            {
                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in _highlighted)
                    {
                        if (id.IsNull || id.IsErased) continue;
                        if (tr.GetObject(id, OpenMode.ForRead, false, true) is Entity eo) eo.Unhighlight();
                    }
                    _highlighted.Clear();

                    foreach (string h in handles)
                    {
                        if (!TryHandle(db, h, out ObjectId id) || id.IsNull || id.IsErased) continue;
                        if (tr.GetObject(id, OpenMode.ForRead, false, true) is Entity en)
                        {
                            en.Highlight();
                            _highlighted.Add(id);
                        }
                    }
                    tr.Commit();
                }
                AcApp.UpdateScreen();
            }
            catch { /* editor busy / a command is active -- skip this highlight pass */ }
        }

        private static bool TryHandle(Database db, string hex, out ObjectId id)
        {
            id = ObjectId.Null;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            try { return db.TryGetObjectId(new Handle(Convert.ToInt64(hex, 16)), out id); }
            catch { return false; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { try { ClearHighlight(); } catch { } }
            base.Dispose(disposing);
        }
    }
}
