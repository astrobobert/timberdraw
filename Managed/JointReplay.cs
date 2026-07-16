using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{
    // The regen JOINT LEDGER (Robert's ask, 2026-07-15: hours of joinery must survive a skeleton
    // re-Generate). EraseFrame harvests one entry per joint id losing at least one side to the
    // erase; ReplayJoints re-cuts them onto the fresh skeleton after Emit. Identity is GEOMETRIC
    // (role + frame midpoint + length) -- grid labels renumber when a bent is inserted, midpoints
    // don't move; and NO ObjectIds ride in the ledger (the orphan sweep and the replay itself
    // rebuild entities, so ids captured at harvest go stale -- a survivor simply re-matches itself
    // at distance ~0).

    // One side of a harvested joint.
    public struct JointLedgerSide
    {
        public string Role;      // xdata "Type" at harvest
        public string Label;     // xdata "GridLabel" at harvest ("P-2A", "*"; may be blank)
        public Point3d Mid;      // f.O + f.Z * (f.L / 2)
        public double Len;       // f.L
        public bool Erased;      // skeleton side (died) vs survivor (kept, features stripped)
        public bool Male;        // carried a UNION feature under OldJid (tenon/tongue side)
    }

    // A joint that lost at least one side, with its stored recipe.
    public sealed class JointLedgerEntry
    {
        public int OldJid;
        public string State;                 // ConnectionType.SerializeState(); null = no recipe
        public List<JointLedgerSide> Sides;  // expected 2; anything else is reported, not cut
    }

    // ManagedCommands part: the replay op. A partial so it shares the private spine --
    // StampJoint (Core), SharedJointId (JoinCommands), and the remap idiom.
    public partial class ManagedCommands
    {
        // Matching tolerances. A same-spec regen re-emits identical geometry (and a survivor IS its
        // own match), so real matches land at ~0; MatchTol is how much midpoint drift a param tweak
        // may bridge; inside TieBand of each other two candidates are separated by length, else the
        // entry is AMBIGUOUS and skipped -- a report, never a wrong cut.
        private const double ReplayMatchTol = 24.0;
        private const double ReplayExactTol = 0.5;
        private const double ReplayTieBand = 6.0;
        private const int ReplayDetailCap = 20;   // max per-joint detail lines echoed

        // Replay the harvested ledger onto the fresh skeleton: match each side to a current managed
        // timber by role + nearest midpoint, then cut the stored recipe via the universal
        // ConnectionType.Apply (male-first, both orders -- Compute* fails WITHOUT mutating on a
        // wrong order/no contact, the TJointSync contract). Restoration only: a pair already
        // sharing a joint id is skipped, and nothing is ever cut that wasn't in the ledger.
        //
        // `labelFallback` (Robert's defect 2026-07-15: an eave-height change RELOCATED the skeleton
        // past the midpoint cap and its joinery dropped): when the geometric match fails, re-pair by
        // GRID LABEL -- labels are stable across a pure PARAM change (same member count -> same
        // positional labels), which is exactly the case where members relocate. The caller arms it
        // only when erased count == emitted count; on an INSERT/REMOVE labels renumber (and the
        // geometry does NOT move), so the geometric match keeps working there and label matching
        // stays OFF. Geometric always runs FIRST -- label is the rescue, never the primary.
        // Returns a one-line tally (null for an empty ledger); per-failure details are echoed here.
        public static string ReplayJoints(Database db, List<JointLedgerEntry> ledger, bool labelFallback)
        {
            if (ledger == null || ledger.Count == 0) return null;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return null;
            Editor ed = doc.Editor;
            int replayed = 0, reattached = 0, already = 0, noContact = 0,
                unmatched = 0, ambiguous = 0, noRecipe = 0, labelMatched = 0, details = 0;

            void Detail(string msg)
            {
                details++;
                if (details == ReplayDetailCap + 1) { ed.WriteMessage("\n  regen joinery: ... (more suppressed; see TDiag)"); return; }
                if (details <= ReplayDetailCap) ed.WriteMessage("\n  " + msg);
                Diag.Warn("ReplayJoints", msg);
            }

            try
            {
                List<ConnectionType> presets = ConnectionType.BuiltIns();

                // Candidate indexes over the post-Emit drawing, by role AND by grid label. Frames
                // don't MOVE during replay (RebuildFromFrame keeps placement), so midpoints stay
                // valid; ObjectIds don't -- they are chased through the remap at use time.
                var byRole = new Dictionary<string, List<(ObjectId Id, Point3d Mid, double Len, string Role)>>();
                var byLabel = new Dictionary<string, List<(ObjectId Id, Point3d Mid, double Len, string Role)>>();
                foreach (ManagedTimber.ShopInfo t in ManagedTimber.EnumerateForShop(db))
                {
                    (ObjectId, Point3d, double, string) rec =
                        (t.Id, t.F.O + t.F.Z * (t.F.L / 2.0), t.F.L, t.Role ?? "");
                    string role = t.Role ?? "";
                    if (!byRole.TryGetValue(role, out List<(ObjectId Id, Point3d Mid, double Len, string Role)> rl))
                        byRole[role] = rl = new List<(ObjectId Id, Point3d Mid, double Len, string Role)>();
                    rl.Add(rec);
                    string label = t.GridLabel ?? "";
                    if (label.Length > 0)
                    {
                        if (!byLabel.TryGetValue(label, out List<(ObjectId Id, Point3d Mid, double Len, string Role)> ll))
                            byLabel[label] = ll = new List<(ObjectId Id, Point3d Mid, double Len, string Role)>();
                        ll.Add(rec);
                    }
                }

                var remap = new Dictionary<ObjectId, ObjectId>();
                ObjectId Cur(ObjectId id) { while (!id.IsNull && remap.TryGetValue(id, out ObjectId nx)) id = nx; return id; }
                void Map(ObjectId from, ObjectId to) { if (!from.IsNull && !to.IsNull && from != to) remap[from] = to; }

                // Nearest candidate to the side's harvested midpoint. 0 = matched, 1 = none in
                // range, 2 = ambiguous (two in the tie band the length tiebreak can't separate).
                int Nearest(List<(ObjectId Id, Point3d Mid, double Len, string Role)> cands,
                            JointLedgerSide s, double cap, out ObjectId id)
                {
                    id = ObjectId.Null;
                    if (cands == null || cands.Count == 0) return 1;
                    int bi = -1; double bd = double.MaxValue, sd = double.MaxValue; int si = -1;
                    for (int i = 0; i < cands.Count; i++)
                    {
                        double dist = cands[i].Mid.DistanceTo(s.Mid);
                        if (dist < bd) { sd = bd; si = bi; bd = dist; bi = i; }
                        else if (dist < sd) { sd = dist; si = i; }
                    }
                    if (bd > cap) return 1;
                    if (bd > ReplayExactTol && si >= 0 && sd < bd + ReplayTieBand)
                    {
                        // Two candidates in the tie band: separate by length, else refuse.
                        double lb = System.Math.Abs(cands[bi].Len - s.Len);
                        double ls = System.Math.Abs(cands[si].Len - s.Len);
                        if (System.Math.Abs(lb - ls) <= 6.0) return 2;
                        if (ls < lb) bi = si;
                    }
                    id = cands[bi].Id;
                    return 0;
                }

                // Geometric first (survives inserts/removes, where labels renumber); the LABEL
                // rescue catches relocated members after a pure param change. Non-unique labels
                // (brace group symbols) narrow to the label set, then nearest-midpoint picks
                // within it -- same-symbol braces sit a bay apart, so nearest is decisive; NO
                // distance cap inside a label set (relocation can be arbitrarily large; the
                // AABB touch gate still protects the pair).
                int MatchSide(JointLedgerSide s, out ObjectId id, out bool viaLabel)
                {
                    viaLabel = false;
                    byRole.TryGetValue(s.Role ?? "", out List<(ObjectId Id, Point3d Mid, double Len, string Role)> rc);
                    int g = Nearest(rc, s, ReplayMatchTol, out id);
                    if (g == 0) return 0;
                    if (labelFallback && !string.IsNullOrEmpty(s.Label)
                        && byLabel.TryGetValue(s.Label, out List<(ObjectId Id, Point3d Mid, double Len, string Role)> lc))
                    {
                        List<(ObjectId Id, Point3d Mid, double Len, string Role)> same =
                            lc.FindAll(c => c.Role == (s.Role ?? ""));
                        int l = Nearest(same, s, double.MaxValue, out ObjectId lid);
                        if (l == 0) { id = lid; viaLabel = true; return 0; }
                        if (l == 2) return 2;
                    }
                    return g;
                }

                ledger.Sort((x, y) => x.OldJid.CompareTo(y.OldJid));   // deterministic order

                foreach (JointLedgerEntry e in ledger)
                {
                    try
                    {
                        ConnectionType ct = e.State != null ? ConnectionType.FromState(presets, e.State) : null;
                        if (ct == null)
                        { noRecipe++; Detail("regen joint #" + e.OldJid + ": no stored recipe -- re-cut via TJointAll / the Joints pane."); continue; }
                        if (e.Sides == null || e.Sides.Count != 2)
                        {
                            unmatched++;
                            Detail("regen joint #" + e.OldJid + " (" + ct.DisplayName + "): expected 2 carriers, found "
                                + (e.Sides?.Count ?? 0) + " -- skipped.");
                            continue;
                        }

                        JointLedgerSide sa = e.Sides[0], sb = e.Sides[1];
                        // Male-first ordering hint: Apply's Compute* cores expect the MALE (end)
                        // timber first; when exactly one side carried the union feature, lead with it.
                        if (sb.Male && !sa.Male) { JointLedgerSide t = sa; sa = sb; sb = t; }

                        int ma = MatchSide(sa, out ObjectId aId, out bool aViaLabel);
                        int mb = MatchSide(sb, out ObjectId bId, out bool bViaLabel);
                        if (ma == 2 || mb == 2)
                        { ambiguous++; Detail("regen joint #" + e.OldJid + " (" + ct.DisplayName + "): two equally near candidates -- re-cut manually."); continue; }
                        if (ma != 0 || mb != 0 || aId == bId)
                        { unmatched++; Detail("regen joint #" + e.OldJid + " (" + ct.DisplayName + "): no matching timber on the new skeleton."); continue; }

                        aId = Cur(aId); bId = Cur(bId);
                        if (!ManagedTimber.TryReadFrame(db, aId, out ManagedTimber.TFrame fa)
                            || !ManagedTimber.TryReadFrame(db, bId, out ManagedTimber.TFrame fb))
                        { unmatched++; Detail("regen joint #" + e.OldJid + " (" + ct.DisplayName + "): a matched timber went missing -- skipped."); continue; }

                        // Same-pair-two-joints (a girt tenoning the same post at both ends): the
                        // second entry must not silently REPLACE the first -- Apply has no end hint.
                        if (SharedJointId(fa, fb) != 0)
                        { already++; Detail("regen joint #" + e.OldJid + " (" + ct.DisplayName + "): pair already jointed (second joint between the same two timbers) -- re-cut manually."); continue; }

                        // Conservative touch gate: the direction-only cutters (rafter head, purlin,
                        // common ridge, strut engine) would cut FLOATING joinery for a matched-but-
                        // drifted-apart pair. Expanded nominal boxes must overlap.
                        if (!FramesTouch(fa, fb, 1.0))
                        { noContact++; Detail("regen joint #" + e.OldJid + " (" + ct.DisplayName + "): matched pair no longer touches -- skipped."); continue; }

                        ApplyResult r = ct.Apply(db, aId, fa, bId, fb);
                        if (r.Ok) { Map(aId, r.AId); Map(bId, r.BId); }
                        else
                        {
                            r = ct.Apply(db, bId, fb, aId, fa);
                            if (r.Ok) { Map(bId, r.AId); Map(aId, r.BId); }
                        }
                        if (!r.Ok)
                        { noContact++; Detail("regen joint #" + e.OldJid + " (" + ct.DisplayName + "): could not re-cut -- " + r.Diag); continue; }

                        StampJoint(r.AId, r.BId, r.Jid, ct);
                        // Stale-stamp hygiene: a surviving side kept its OldJid stamp through the
                        // orphan sweep. Remove it ONLY when the id is absent from that entity's
                        // current geometry -- a freshly minted r.Jid can COLLIDE with an old ledger
                        // id (freed ids re-mint), and the live stamp must not be deleted.
                        if (e.OldJid != r.Jid)
                        {
                            foreach (ObjectId nid in new[] { r.AId, r.BId })
                                if (ManagedTimber.TryReadFrame(db, nid, out ManagedTimber.TFrame nf)
                                    && !ManagedTimber.JointIds(nf).Contains(e.OldJid))
                                    ManagedTimber.RemoveJointSpec(nid, e.OldJid);
                        }
                        replayed++;
                        if (sa.Erased != sb.Erased) reattached++;   // exactly one side was a survivor
                        if (aViaLabel || bViaLabel) labelMatched++; // relocated member re-paired by label
                    }
                    catch (System.Exception ex)
                    {
                        noContact++;
                        Detail("regen joint #" + e.OldJid + ": replay failed -- " + ex.Message);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Diag.Warn("ReplayJoints", "replay aborted: " + ex.Message);
                return "regen joinery: replay aborted -- " + ex.Message + " (joints re-cuttable via TJointSync / TJointAll).";
            }

            var parts = new List<string>();
            parts.Add(replayed + " replayed" + (reattached > 0 ? " (" + reattached + " survivor re-attach)" : ""));
            if (labelMatched > 0) parts.Add(labelMatched + " relocated (re-paired by label)");
            if (already > 0) parts.Add(already + " already-jointed pair(s) skipped");
            if (noContact > 0) parts.Add(noContact + " no-contact");
            if (unmatched > 0) parts.Add(unmatched + " unmatched");
            if (ambiguous > 0) parts.Add(ambiguous + " ambiguous");
            if (noRecipe > 0) parts.Add(noRecipe + " no-recipe");
            return "regen joinery: " + string.Join(", ", parts) + ".";
        }

        // Expanded nominal-box overlap: WCS AABBs of the two frames (8 corners each: the section
        // centered on O, extruded L along Z), each padded by `pad`. Conservative -- a true contact
        // always passes; a floating pair fails.
        private static bool FramesTouch(ManagedTimber.TFrame a, ManagedTimber.TFrame b, double pad)
        {
            FrameAabb(a, out Point3d amin, out Point3d amax);
            FrameAabb(b, out Point3d bmin, out Point3d bmax);
            return amin.X - pad <= bmax.X && bmin.X - pad <= amax.X
                && amin.Y - pad <= bmax.Y && bmin.Y - pad <= amax.Y
                && amin.Z - pad <= bmax.Z && bmin.Z - pad <= amax.Z;
        }

        private static void FrameAabb(ManagedTimber.TFrame f, out Point3d min, out Point3d max)
        {
            double lox = double.MaxValue, loy = double.MaxValue, loz = double.MaxValue;
            double hix = double.MinValue, hiy = double.MinValue, hiz = double.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Point3d c = f.O + f.Z * ((i & 1) == 0 ? 0.0 : f.L)
                                + f.X * (((i & 2) == 0 ? -1.0 : 1.0) * f.W / 2.0)
                                + f.Y * (((i & 4) == 0 ? -1.0 : 1.0) * f.D / 2.0);
                if (c.X < lox) lox = c.X; if (c.X > hix) hix = c.X;
                if (c.Y < loy) loy = c.Y; if (c.Y > hiy) hiy = c.Y;
                if (c.Z < loz) loz = c.Z; if (c.Z > hiz) hiz = c.Z;
            }
            min = new Point3d(lox, loy, loz);
            max = new Point3d(hix, hiy, hiz);
        }
    }
}
