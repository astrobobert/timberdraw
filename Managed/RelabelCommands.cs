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

            // Brace symbols (*, **, ...) re-derived from the current model too -- so re-sectioned or
            // hand-placed braces pick up the size+shape grouping the same as the emitted ones.
            int braces = RelabelBraces(db);

            ed.WriteMessage($"\nTRelabel: {renamed} level label(s) + {prefixed} family prefix(es)"
                + $" + {braces} brace symbol(s) updated"
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

        // TRelabelBraces -- re-derive every brace's group symbol from the CURRENT model. A brace carries
        // only a symbol (*, **, ...), shared by every brace of the same SIZE + SHAPE. Run it after
        // re-sectioning / hand-placing / assigning braces (the emitter stamps a provisional symbol at
        // draw, but that never revisits post-emit edits, which is why a re-sized brace kept reading '*').
        [CommandMethod("TRelabelBraces")]
        public static void RelabelBracesCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            int n = RelabelBraces(doc.Database);
            doc.Editor.WriteMessage("\nTRelabelBraces: " + n + " brace symbol(s) updated.");
        }

        // Model-wide brace symbols: every Brace-role timber gets a group symbol (*, **, ...) by SIZE +
        // SHAPE -- cross-section (W x D, quarter-inch buckets) plus the brace's angle-from-horizontal and
        // its true (trimmed) length. Overall (the finished solid's extent along its axis) is used for
        // length, so a bent brace's centerline and a wall brace's OVER-LONG box collapse to the same
        // stick -- identical braces share a symbol, genuinely different ones split. Symbols are assigned
        // in a stable numeric order (section, then angle, then length ascending), so the mapping is
        // reproducible across runs. Returns the number of GridLabels changed. The single authority --
        // the emitter's FrameGrid.BraceLabel is a provisional stamp this supersedes.
        public static int RelabelBraces(Database db)
        {
            var braces = ManagedTimber.EnumerateForBom(db)
                .Where(t => string.Equals(t.Type, "Brace", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (braces.Count == 0) return 0;

            (int W, int D, int A, int L) Key(ManagedTimber.TimberBom t) =>
                (QInt(t.F.W), QInt(t.F.D), (int)Math.Round(BraceAngleDeg(t.F)), (int)Math.Round(t.Overall));

            var order = braces.Select(Key).Distinct()
                .OrderBy(k => k.W).ThenBy(k => k.D).ThenBy(k => k.A).ThenBy(k => k.L)
                .ToList();
            var symbol = new Dictionary<(int, int, int, int), string>();
            for (int i = 0; i < order.Count; i++) symbol[order[i]] = new string('*', i + 1);

            int changed = 0;
            foreach (var t in braces)
            {
                string lbl = symbol[Key(t)];
                if (string.Equals(t.Label, lbl, StringComparison.Ordinal)) continue;
                var xd = Module1.GetXdata(t.Id);
                if (xd == null) continue;
                xd.GridLabel = lbl;
                Module1.SetXdata(t.Id, xd);
                changed++;
            }
            return changed;
        }

        private static int QInt(double v) => (int)Math.Round(v * 4.0);   // quarter-inch section bucket

        // Brace angle from horizontal (0 = flat, 90 = plumb), from the world-Z rise of the length axis --
        // storage-independent (a brace's box length varies by build path; its direction does not).
        private static double BraceAngleDeg(ManagedTimber.TFrame f)
        {
            double zx = f.Z.X, zy = f.Z.Y, zz = f.Z.Z;
            double h = Math.Sqrt(zx * zx + zy * zy);
            return Math.Atan2(Math.Abs(zz), h) * 180.0 / Math.PI;
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
