using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using DataTable = System.Data.DataTable;    // disambiguate from Autodesk.AutoCAD.DatabaseServices.DataTable
using DataColumn = System.Data.DataColumn;
using DataRow = System.Data.DataRow;

namespace TimberDraw
{
    // Managed-timber BOM. Every managed timber already carries its identity (Type/role, GridLabel, size,
    // frame/bent/bay/wall tags) in the legacy Module1 XData schema, stamped at emit by DrawFramedSolid --
    // so this is a READ-and-TALLY, not a build. Overall length is MEASURED from the finished solid (incl.
    // projecting tenons); joinery counts are DERIVED from the TFrame feature lists + the joint spec. TBom
    // presents the per-timber piece tally in a sortable DataGridView palette (BomGridControl); selecting
    // rows highlights those solids in model space.
    public partial class ManagedCommands
    {
        // Per-timber joinery tally (geometry gives male/female + pegs; the joint spec names the kinds).
        private struct Joinery
        {
            public int Joints, Tenon, Mortise, Housing, Shoulder, Dovetail, Peg, Untyped;
        }

        [CommandMethod("TBom")]
        public static void BuildBom()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            DataTable table = BuildBomTable(doc.Database);
            if (table.Rows.Count == 0)
            {
                ed.WriteMessage("\nNo managed timbers found -- nothing to BOM.");
                return;
            }
            ShowBomPalette(table);
            ed.WriteMessage($"\nBOM: {table.Rows.Count} pieces -- see the Timber BOM palette.");
        }

        // The per-timber PIECE TALLY as a typed DataTable (bound by BomGridControl -> sortable columns). The
        // Handle column resolves back to the solid for highlighting. Reused by the grid's Refresh + Export.
        public static DataTable BuildBomTable(Database db)
        {
            var t = new DataTable("PieceTally");
            t.Columns.Add("Label", typeof(string));
            t.Columns.Add("Type", typeof(string));
            t.Columns.Add("W", typeof(int));
            t.Columns.Add("D", typeof(int));
            t.Columns.Add("Overall (in)", typeof(double));
            t.Columns.Add("Buy (ft)", typeof(int));
            t.Columns.Add("BF", typeof(double));
            t.Columns.Add("Joints", typeof(int));
            t.Columns.Add("Tenon", typeof(int));
            t.Columns.Add("Mortise", typeof(int));
            t.Columns.Add("Housing", typeof(int));
            t.Columns.Add("Shoulder", typeof(int));
            t.Columns.Add("Dovetail", typeof(int));
            t.Columns.Add("Peg", typeof(int));
            t.Columns.Add("Untyped", typeof(int));
            t.Columns.Add("Handle", typeof(string));

            List<ConnectionType> presets = ConnectionType.BuiltIns();   // for preset -> element order
            foreach (ManagedTimber.TimberBom b in ManagedTimber.EnumerateForBom(db))
            {
                string handle = b.Id.Handle.ToString();
                int w = (int)Math.Round(b.F.W);
                int d = (int)Math.Round(b.F.D);
                int buyFt = Module1.BuyLongFeet(b.Overall);   // overall length -> stock to buy
                Joinery j = Classify(b.F, b.Specs, presets);
                t.Rows.Add(
                    FirstNonEmpty(b.Label, b.Designation, handle),
                    FirstNonEmpty(b.Type, "NA"),
                    w, d, Math.Round(b.Overall, 1), buyFt, Math.Round(w * d * buyFt / 12.0, 2),
                    j.Joints, j.Tenon, j.Mortise, j.Housing, j.Shoulder, j.Dovetail, j.Peg, j.Untyped,
                    handle);
            }
            return t;
        }

        // Write the piece tally to a CSV (for the grid's Export button). Columns/order follow the table.
        public static void WritePieceCsv(DataTable t, string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Csv(t.Columns.Cast<DataColumn>().Select(c => (object)c.ColumnName).ToArray()));
            foreach (DataRow r in t.Rows) sb.AppendLine(Csv(r.ItemArray));
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        // ---- BOM palette (dockable, reused across TBom invocations; mirrors the TDraw / TPanel idiom) ----
        internal static PaletteSet _bomPs;
        private static BomGridControl _bomControl;

        private static void ShowBomPalette(DataTable table)
        {
            if (_bomPs == null)
            {
                _bomPs = new PaletteSet("Timber BOM", "TimberBom",
                    new Guid("A7C3F210-9E44-4B6D-8F12-6D5E4C3B2A10"));
                _bomControl = new BomGridControl();
                _bomPs.Add("BOM", _bomControl);
                _bomPs.MinimumSize = new System.Drawing.Size(560, 400);
                _bomPs.Style = PaletteSetStyles.ShowCloseButton | PaletteSetStyles.ShowAutoHideButton
                             | PaletteSetStyles.ShowPropertiesMenu;
            }
            _bomControl.LoadData(table);
            _bomPs.Visible = true;
        }

        // Classify this timber's joinery. Geometry gives, per joint id, whether the timber has a male (union)
        // or female (subtract) feature + the peg-bore total. The joint spec (when present) names the enabled
        // element KINDS; each is attributed to a column by the timber's side. No spec => Untyped.
        private static Joinery Classify(ManagedTimber.TFrame f, Dictionary<int, string> specs,
                                        List<ConnectionType> presets)
        {
            var j = new Joinery { Peg = f.Pegs?.Count ?? 0 };

            // Per joint id on this timber: does it carry a union (male) feature and/or a subtract (female) one?
            var sides = new Dictionary<int, (bool union, bool sub)>();
            void Mark(int id, bool sub)
            {
                if (id == 0) return;
                sides.TryGetValue(id, out var s);
                if (sub) s.sub = true; else s.union = true;
                sides[id] = s;
            }
            if (f.Features   != null) foreach (var x in f.Features)   Mark(x.Joint, x.Subtract);
            if (f.JointPolys != null) foreach (var x in f.JointPolys) Mark(x.Joint, x.Subtract);
            if (f.JointPolysZ!= null) foreach (var x in f.JointPolysZ)Mark(x.Joint, x.Subtract);
            if (f.JointPrisms!= null) foreach (var x in f.JointPrisms)Mark(x.Joint, x.Subtract);
            if (f.Pegs       != null) foreach (var x in f.Pegs)       Mark(x.Joint, true);   // pegs bore the host

            j.Joints = sides.Count;

            foreach (KeyValuePair<int, (bool union, bool sub)> kv in sides)
            {
                bool male = kv.Value.union;   // a union feature => this timber is the male / tongue side
                HashSet<ElementKind> kinds =
                    (specs != null && specs.TryGetValue(kv.Key, out string state)) ? EnabledKinds(state, presets) : null;
                if (kinds == null) { j.Untyped++; continue; }
                if (kinds.Contains(ElementKind.Tenon)) { if (male) j.Tenon++; else j.Mortise++; }
                if (kinds.Contains(ElementKind.Housing) && !male) j.Housing++;   // the pocket is on the host
                if (kinds.Contains(ElementKind.Shoulder)) j.Shoulder++;
                if (kinds.Contains(ElementKind.Dovetail)) j.Dovetail++;
                // Pegs are counted from geometry (j.Peg) -- they bore the host cheeks only.
            }
            return j;
        }

        // The enabled element kinds in a stored joint state "<Name>|<e0flag,params>;<e1...>;..." -- a lenient
        // parse that needs only the preset (element order, by Name in BuiltIns) + each segment's leading flag,
        // not the param values. Null when the preset name isn't recognized.
        private static HashSet<ElementKind> EnabledKinds(string state, List<ConnectionType> presets)
        {
            if (string.IsNullOrEmpty(state)) return null;
            int bar = state.IndexOf('|');
            string name = bar < 0 ? state : state.Substring(0, bar);
            ConnectionType preset = presets.Find(c => c.Name == name);
            if (preset == null) return null;

            var set = new HashSet<ElementKind>();
            if (bar < 0)   // name only -> assume the preset's full element set
            {
                foreach (JointElement el in preset.Elements) set.Add(el.Kind);
                return set;
            }
            string[] segs = state.Substring(bar + 1).Split(';');
            for (int i = 0; i < preset.Elements.Count && i < segs.Length; i++)
            {
                int comma = segs[i].IndexOf(',');
                string flag = (comma < 0 ? segs[i] : segs[i].Substring(0, comma)).Trim();
                if (flag == "1") set.Add(preset.Elements[i].Kind);
            }
            return set;
        }

        private static string FirstNonEmpty(params string[] vals)
        {
            foreach (string v in vals) if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            return "";
        }

        // One CSV row: comma-joined, RFC-4180 quoting only where a cell needs it.
        private static string Csv(params object[] cells) => string.Join(",", cells.Select(CsvField));

        private static string CsvField(object o)
        {
            string s = o?.ToString() ?? "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
                s = "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
