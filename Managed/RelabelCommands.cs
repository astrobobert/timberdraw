using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

namespace TimberDraw
{
    // TRelabel: apply the Dn/Up LEVEL qualifier to grid labels IN PLACE -- no regenerate, so
    // hand-cut joinery is untouched. The emitter now stamps "-Dn"/"-Up" itself at Draw time
    // (FrameGrid.LabelForEdge); this command retrofits frames emitted BEFORE that fix, where a
    // floor girt and its tie girt share one digit-first span label (two "1AE" -> the LOWER one
    // "1AE-Dn", the UPPER "1AE-Up"). Only exact-duplicate digit-first labels are touched:
    // letter-first wall/bay labels, brace symbols, owner-addressed free timbers, and unique bent
    // labels all pass through. Idempotent -- existing -Dn/-Up strip and re-derive by elevation.
    public partial class ManagedCommands
    {
        [CommandMethod("TRelabel")]
        public static void RelabelLevels()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            List<ManagedTimber.ShopInfo> all = ManagedTimber.EnumerateForShop(db);
            var groups = new Dictionary<string, List<ManagedTimber.ShopInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in all)
            {
                string label = t.GridLabel ?? "";
                if (label.Length == 0 || !char.IsDigit(label[0])) continue;   // digit-first bent labels only
                string baseLabel = StripLevel(label);
                if (!groups.TryGetValue(baseLabel, out var list))
                    groups[baseLabel] = list = new List<ManagedTimber.ShopInfo>();
                list.Add(t);
            }

            int renamed = 0, ambiguous = 0;
            foreach (var kv in groups)
            {
                if (kv.Value.Count < 2) continue;                 // unique label (or lone tie girt): leave it
                if (kv.Value.Count > 2)
                {
                    ambiguous++;
                    ed.WriteMessage($"\n  {kv.Key}: {kv.Value.Count} timbers share this label -- skipped (expected a floor/tie girt pair).");
                    continue;
                }
                var pair = kv.Value.OrderBy(t => MidZ(t.F)).ToList();   // WCS Z-up: [0] = lower
                renamed += Apply(pair[0], kv.Key + "-Dn");
                renamed += Apply(pair[1], kv.Key + "-Up");
            }

            ed.WriteMessage($"\nTRelabel: {renamed} label(s) updated"
                + (ambiguous > 0 ? $", {ambiguous} group(s) skipped" : "") + ".");
        }

        private static double MidZ(ManagedTimber.TFrame f) => (f.O + f.Z * (f.L / 2.0)).Z;

        private static string StripLevel(string label)
            => label.EndsWith("-Dn", StringComparison.OrdinalIgnoreCase) ||
               label.EndsWith("-Up", StringComparison.OrdinalIgnoreCase)
                ? label.Substring(0, label.Length - 3)
                : label;

        private static int Apply(ManagedTimber.ShopInfo t, string newLabel)
        {
            if (string.Equals(t.GridLabel, newLabel, StringComparison.Ordinal)) return 0;
            var xd = Module1.GetXdata(t.Id);
            if (xd == null) return 0;
            xd.GridLabel = newLabel;
            Module1.SetXdata(t.Id, xd);
            return 1;
        }
    }
}
