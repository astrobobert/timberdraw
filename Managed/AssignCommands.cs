using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;

namespace TimberDraw
{
    // ManagedCommands part: TAssign + the owner-label minting helpers.
    public partial class ManagedCommands
    {
        // Assign freely-placed timbers to the frame's organization HIERARCHY:
        //   Frame -> Bent N -> members | Wall X -> Bay -> members | Floor N -> members
        // Pick one or more managed timbers, then say which frame + owner -- or set the target on the
        // TPanel Assembly group first and the command runs promptless (ManagedAssembly). Floor is the
        // owner of floor-system members (joists, summers), numbered bottom-up (1 = first). Writes the grouping
        // tags as an IN-PLACE XData patch -- the solid is NOT rebuilt (no erase/redraw, same handle)
        // -- so the Browser regroups them on Refresh. A wall/floor exists the moment a timber claims
        // it (implicit organizational labels). Repetitive free families (Joist, Summer) also get their
        // owner-addressed GridLabel minted here (J-<floor>-1..n in run order, continuing after the
        // group's existing numbering) -- the label conventions' free-timber scheme.
        [CommandMethod("TAssign")]
        public static void AssignToFrame()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            // Targets: a pane handoff (the Frame Browser's selected rows, consumed ONCE) or an
            // interactive selection (honors a pickfirst set). Either way only MANAGED timbers
            // (frame xrecord) survive; stale browser rows (erased handles) are skipped.
            List<ObjectId> picked = ManagedAssembly.TakeIds();
            if (picked == null)
            {
                var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "3DSOLID") });
                var pso = new PromptSelectionOptions { MessageForAdding = "\nSelect timbers to assign: " };
                PromptSelectionResult sel = ed.GetSelection(pso, filter);
                if (sel.Status != PromptStatus.OK) return;
                picked = new List<ObjectId>(sel.Value.GetObjectIds());
            }

            var ids = new List<ObjectId>();
            var frames = new Dictionary<ObjectId, ManagedTimber.TFrame>();
            int skipped = 0;
            foreach (ObjectId id in picked)
            {
                if (!id.IsNull && !id.IsErased
                    && ManagedTimber.TryReadFrame(db, id, out ManagedTimber.TFrame f)) { ids.Add(id); frames[id] = f; }
                else skipped++;
            }
            if (ids.Count == 0) { ed.WriteMessage("\nNo managed timbers in the selection."); return; }

            // The palette's Assembly pane supplies the target SILENTLY when it is set (the pane is
            // the command's visibility -- the ManagedSection pattern); the command-line prompts
            // below remain for a console-driven assign or an unparseable pane value.
            string frame = null;
            string bentTag = "", wallTag = "", bayTag = "", floorTag = "", colTag = "";
            if (ManagedAssembly.HasCurrent)
            {
                string owner = ManagedAssembly.Owner;
                switch (ManagedAssembly.Kind)
                {
                    case "Bent":
                        // Owner box = the bent number ("2", or typed intersection "2C"); the pane's
                        // second coordinate box supplies the column letter -- together the grid
                        // intersection a free post stands on.
                        SplitIntersection(owner, out string pb, out string pc);
                        if (pb.Length > 0)
                        {
                            bentTag = pb;
                            colTag = pc.Length > 0 ? pc : LettersOnly(ManagedAssembly.Bay);
                        }
                        break;
                    case "Wall":
                        if (owner.Length > 0 && char.IsLetter(owner[0]))
                        { wallTag = owner.ToUpperInvariant(); bayTag = ManagedAssembly.Bay.ToUpperInvariant(); }
                        break;
                    case "Floor":
                        // Floor members carry a BAY designation too (Robert's call, 2026-07-16):
                        // the browser groups a floor's joists per bay and the shop maps partition
                        // on it. The label grammar is unchanged (J-<floor>-n).
                        if (int.TryParse(owner, out int fn) && fn > 0)
                        { floorTag = fn.ToString(); bayTag = ManagedAssembly.Bay.ToUpperInvariant(); }
                        break;
                }
                if (bentTag.Length + wallTag.Length + floorTag.Length > 0) frame = ManagedAssembly.FrameTag;
            }

            if (frame == null)
            {
                // Frame tag: default to the first selected timber's existing frame, else "A".
                string defFrame = "A";
                Module1.DataStructure first = Module1.GetXdata(ids[0]);
                if (first != null && !string.IsNullOrEmpty(first.FrameTag)) defFrame = first.FrameTag;
                PromptResult fr = ed.GetString(
                    new PromptStringOptions("\nFrame tag: ") { DefaultValue = defFrame, AllowSpaces = false });
                if (fr.Status != PromptStatus.OK) return;
                frame = string.IsNullOrWhiteSpace(fr.StringResult) ? defFrame : fr.StringResult.Trim();

                // Owner kind: a numbered bent, a lettered wall (-> optional bay), or a numbered floor.
                var pko = new PromptKeywordOptions("\nAssign as");   // API appends "[Bent/Wall/Floor] <Bent>:"
                pko.Keywords.Add("Bent");
                pko.Keywords.Add("Wall");
                pko.Keywords.Add("Floor");
                pko.Keywords.Default = "Bent";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK) return;

                if (kr.StringResult == "Bent")
                {
                    // A bare number owns the timber ("2"); a grid INTERSECTION ("2C") also stamps the
                    // post-style GridLabel -- how a free post joins the frame at bent 2 x wall C.
                    PromptResult br = ed.GetString(new PromptStringOptions(
                        "\nBent number (or intersection, e.g. 2C): ") { DefaultValue = "1", AllowSpaces = false });
                    if (br.Status != PromptStatus.OK) return;
                    SplitIntersection(br.StringResult, out bentTag, out colTag);
                    if (bentTag.Length == 0) { ed.WriteMessage("\nTAssign: a bent must lead with its number."); return; }
                }
                else if (kr.StringResult == "Wall")
                {
                    PromptResult wr = ed.GetString(
                        new PromptStringOptions("\nWall letter: ") { DefaultValue = "A", AllowSpaces = false });
                    if (wr.Status != PromptStatus.OK) return;
                    wallTag = (string.IsNullOrWhiteSpace(wr.StringResult) ? "A" : wr.StringResult.Trim()).ToUpperInvariant();

                    PromptResult yr = ed.GetString(
                        new PromptStringOptions("\nBay (Roman numeral, blank for none): ") { AllowSpaces = false });
                    if (yr.Status != PromptStatus.OK) return;
                    bayTag = (yr.StringResult ?? "").Trim().ToUpperInvariant();
                }
                else   // Floor: floor-system members (joists, summers), level numbered bottom-up
                {
                    PromptIntegerResult lr = ed.GetInteger(new PromptIntegerOptions("\nFloor number (1 = first): ")
                    { DefaultValue = 1, LowerLimit = 1, AllowNegative = false, AllowZero = false });
                    if (lr.Status != PromptStatus.OK) return;
                    floorTag = lr.Value.ToString();

                    // A floor member's bay designation (Robert's call, 2026-07-16) -- optional,
                    // like a wall's; the label grammar stays J-<floor>-n.
                    PromptResult fyr = ed.GetString(
                        new PromptStringOptions("\nBay (Roman numeral, blank for none): ") { AllowSpaces = false });
                    if (fyr.Status != PromptStatus.OK) return;
                    bayTag = (fyr.StringResult ?? "").Trim().ToUpperInvariant();
                }
            }

            // Owner-addressed labels (FAM-owner-seq, e.g. J-1-1 / P-A-1): minted in run order before
            // the patch so the loop below writes tags + label in one SetXdata. A grid INTERSECTION
            // assign skips the mint -- the intersection itself is the address (P-2C below).
            Dictionary<ObjectId, string> mint = colTag.Length > 0
                ? new Dictionary<ObjectId, string>()
                : MintOwnerLabels(db, ids, frames, bentTag, wallTag, bayTag, floorTag);

            // In-place XData patch (no rebuild): set the grouping tags on each timber + move it to the
            // group layer so it isolates with its bent/wall/floor.
            string groupLayer = ManagedTimber.GroupLayer(frame, bentTag, wallTag, floorTag);
            int n = 0;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectId lid = ManagedTimber.EnsureFrameLayer(tr, db, groupLayer);
                foreach (ObjectId id in ids)
                    if (tr.GetObject(id, OpenMode.ForWrite) is Entity ent) ent.LayerId = lid;
                tr.Commit();
            }
            foreach (ObjectId id in ids)
            {
                Module1.DataStructure xd = Module1.GetXdata(id);
                if (xd == null) continue;
                // A FULLY unassigned timber cannot be a skeleton member (the emitter stamps every
                // emit), so its first assignment also marks it FREE -- hand-placed timbers from
                // before the marker existed gain regenerate protection the moment they're assigned.
                // An already-assigned timber is left as-is (it could be a skeleton member re-owned).
                if (string.IsNullOrEmpty(xd.FrameTag) && string.IsNullOrEmpty(xd.BentNumber)
                    && string.IsNullOrEmpty(xd.WallTag) && string.IsNullOrEmpty(xd.BayTag)
                    && string.IsNullOrEmpty(xd.FloorTag) && string.IsNullOrEmpty(xd.GridLabel))
                    xd.Free = "1";
                xd.FrameTag = frame;
                xd.BentNumber = bentTag;
                xd.WallTag = wallTag;
                xd.BayTag = bayTag;
                xd.FloorTag = floorTag;
                if (mint.TryGetValue(id, out string label)) xd.GridLabel = label;
                else if (colTag.Length > 0)
                {
                    // Grid intersection, TYPE-FIRST like the emitter (P-2C beside the skeleton's
                    // KP-2C -- distinct labels, so the shop-map dedup labels both). FamilyFor
                    // guarantees the prefix (unknown types use their initial).
                    xd.GridLabel = FamilyFor(xd.Type) + "-" + bentTag + colTag;
                }
                Module1.SetXdata(id, xd);
                n++;
            }

            string target = bentTag.Length > 0
                ? "Bent " + bentTag + (colTag.Length > 0 ? " (grid " + bentTag + colTag + ")" : "")
                : wallTag.Length > 0 ? "Wall " + wallTag + (bayTag.Length > 0 ? " / Bay " + bayTag : "")
                : "Floor " + floorTag + (bayTag.Length > 0 ? " / Bay " + bayTag : "");
            // Braces are label-by-symbol, skipped by the owner-seq mint above -- re-derive their
            // *, ** grouping from the whole model so a just-assigned brace shows its symbol, not a blank.
            RelabelBraces(db);

            ed.WriteMessage("\nTAssign: " + n + " timber(s) -> Frame " + frame + " / " + target
                + (mint.Count > 0 ? " (" + mint.Count + " label(s) minted)" : "")
                + (skipped > 0 ? " (skipped " + skipped + " non-managed)" : "") + ".");
            ManagedAssembly.RaiseApplied();   // an open Frame Browser refreshes its rows
        }

        // The free families TAssign mints owner-addressed labels for (label grammar FAM-OWNER-SEQ;
        // hierarchy: floor-system members belong to their FLOOR -> J-1-1..n). Editor-local map -- the
        // generator's FamilyCode table stays on its side of the boundary.
        private static readonly Dictionary<string, string> OwnerLabelFamilies =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { { "Joist", "J" }, { "Summer", "SB" } };

        // Bent family codes for the grid-intersection mint (P-2C, KP-2C) -- the editor-local mirror of
        // the generator's BentFamilyCode (the boundary keeps the tables separate on purpose). Unknown
        // roles mint the bare anchor.
        private static readonly Dictionary<string, string> BentLabelFamilies =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { { "Post", "P" }, { "KingPost", "KP" }, { "QueenPost", "QP" }, { "Rafter", "RF" },
              { "Strut", "ST" }, { "VStrut", "VS" }, { "HBeam", "HB" }, { "HPost", "HP" },
              { "Girt", "G" }, { "Brace", "B" }, { "Ridge", "RG" }, { "Common", "C" },
              { "Purlin", "PU" }, { "EaveGirt", "EG" }, { "FloorGirt", "FG" } };

        // The label family for ANY type: the owner table, the bent table, else the type's initial
        // letter uppercased -- every TAssign label carries a family prefix (Robert's call; the bare
        // anchor read as unlabeled).
        private static string FamilyFor(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return "T";
            if (OwnerLabelFamilies.TryGetValue(type, out string f)) return f;
            if (BentLabelFamilies.TryGetValue(type, out f)) return f;
            return char.ToUpperInvariant(type.Trim()[0]).ToString();
        }

        // Mint FAM-<owner>-<seq> GridLabels for the selected family members. Owner preference: floor,
        // else bay, else wall, else bent. Sequence runs ALONG THE ROW (midpoints sorted on the
        // horizontal direction across the members, oriented +X/+Y so numbering is reproducible) and
        // continues after the highest existing sequence in the same group among UNSELECTED timbers --
        // so adding a second field to a floor extends the count, while re-assigning the whole field
        // renumbers it.
        private static Dictionary<ObjectId, string> MintOwnerLabels(Database db, List<ObjectId> ids,
            Dictionary<ObjectId, ManagedTimber.TFrame> frames, string bentTag, string wallTag, string bayTag,
            string floorTag)
        {
            var mint = new Dictionary<ObjectId, string>();
            string owner = floorTag.Length > 0 ? floorTag
                : bayTag.Length > 0 ? bayTag : wallTag.Length > 0 ? wallTag : bentTag;
            if (owner.Length == 0) return mint;

            var byFam = new Dictionary<string, List<ObjectId>>(StringComparer.Ordinal);
            foreach (ObjectId id in ids)
            {
                Module1.DataStructure xd = Module1.GetXdata(id);
                if (xd == null || string.IsNullOrEmpty(xd.Type)) continue;
                // Braces are the group-symbol family (*, **, ...): they are NEVER owner-seq numbered.
                // RelabelBraces (called after the assign) re-derives their symbol by size+shape; minting
                // B-owner-seq here would clobber it (B-3-1 instead of *).
                if (string.Equals(xd.Type, "Brace", StringComparison.OrdinalIgnoreCase)) continue;
                string fam = FamilyFor(xd.Type);   // EVERY other type mints FAM-owner-seq
                if (!byFam.TryGetValue(fam, out var list)) byFam[fam] = list = new List<ObjectId>();
                list.Add(id);
            }
            if (byFam.Count == 0) return mint;

            List<ManagedTimber.ShopInfo> all = ManagedTimber.EnumerateForShop(db);
            var selected = new HashSet<ObjectId>(ids);
            foreach (var kv in byFam)
            {
                string prefix = kv.Key + "-" + owner + "-";
                int next = 1;
                foreach (var t in all)
                {
                    if (selected.Contains(t.Id)) continue;
                    string gl = t.GridLabel ?? "";
                    if (!gl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    if (int.TryParse(gl.Substring(prefix.Length), out int seq) && seq >= next) next = seq + 1;
                }

                Vector3d run = Vector3d.ZAxis.CrossProduct(frames[kv.Value[0]].Z);
                if (run.Length < 1e-6) run = Vector3d.XAxis;         // vertical member: any stable order
                run = run.GetNormal();
                if (run.X < -1e-9 || (Math.Abs(run.X) < 1e-9 && run.Y < 0)) run = -run;
                kv.Value.Sort((a, b) => MidAlong(frames[a], run).CompareTo(MidAlong(frames[b], run)));
                foreach (ObjectId id in kv.Value) mint[id] = prefix + (next++);
            }
            return mint;
        }

        private static double MidAlong(ManagedTimber.TFrame f, Vector3d dir)
            => (f.O + f.Z * (f.L / 2.0)).GetAsVector().DotProduct(dir);

        // A 1-2 letter column string, uppercased; anything else -> "".
        private static string LettersOnly(string s)
        {
            s = (s ?? "").Trim().ToUpperInvariant();
            if (s.Length == 0 || s.Length > 2) return "";
            foreach (char c in s) if (!char.IsLetter(c)) return "";
            return s;
        }

        // "2C" -> bent "2" + column "C"; "2" -> bent "2", no column; "1.1B" -> "1.1" + "B" (sub-bent
        // lines carry dots). Digits lead, a 1-2 letter column may trail; anything else parses empty.
        private static void SplitIntersection(string s, out string bent, out string col)
        {
            s = (s ?? "").Trim().ToUpperInvariant();
            int i = 0;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
            bent = s.Substring(0, i);
            col = s.Substring(i);
            foreach (char c in col)
                if (!char.IsLetter(c)) { col = ""; break; }
            if (col.Length > 2 || bent.Length == 0) col = "";
        }
    }
}
