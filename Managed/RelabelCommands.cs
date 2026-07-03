using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

namespace TimberDraw
{
    // TRelabel: bring an existing frame's grid labels up to the CURRENT conventions IN PLACE -- no
    // regenerate, so hand-cut joinery is untouched. Two passes, both idempotent, both stamped by
    // the emitter itself on any NEW emit:
    //   1) Dn/Up LEVEL qualifier -- a floor girt and its tie girt sharing one digit-first span
    //      label (two "1AE") become "1AE-Dn" (lower) / "1AE-Up" (upper).
    //   2) FAMILY PREFIX (type-first, 2026-07-03) -- bare digit-first bent labels gain their
    //      family code from the timber's role: "1A" -> "P-1A", "1AE-Up" -> "TG-1AE-Up".
    // Letter-first wall/bay labels, brace symbols, and owner-addressed free timbers pass through.
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

            // FAMILY PREFIX pass. Re-enumerate first: the level pass above just rewrote its pairs'
            // labels, and prefixing a stale snapshot would drop the fresh -Dn/-Up. A label still
            // digit-first here is the bare pre-convention form; prefixed ones start with a letter
            // and pass through untouched (idempotent).
            int prefixed = 0;
            foreach (var t in ManagedTimber.EnumerateForShop(db))
            {
                string label = t.GridLabel ?? "";
                if (label.Length == 0 || !char.IsDigit(label[0])) continue;
                string fam = BentFamily(t.Role, t.Designation);
                if (fam.Length == 0) continue;
                prefixed += Apply(t, fam + "-" + label);
            }

            ed.WriteMessage($"\nTRelabel: {renamed} level label(s) + {prefixed} family prefix(es) updated"
                + (ambiguous > 0 ? $", {ambiguous} group(s) skipped" : "") + ".");
        }

        // The bent family code for the prefix pass: the shared editor table for the roles it lists,
        // with the girt split by designation (floor girt FG vs tie/other TG) -- mirrors the
        // generator's BentFamilyCode. Unknown roles return "" (label left bare).
        private static string BentFamily(string role, string desig)
        {
            if (string.IsNullOrEmpty(role)) return "";
            if (role.Equals("Girt", StringComparison.OrdinalIgnoreCase))
                return string.Equals(desig, "FG", StringComparison.OrdinalIgnoreCase) ? "FG" : "TG";
            return BentLabelFamilies.TryGetValue(role, out string f) ? f : "";
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
