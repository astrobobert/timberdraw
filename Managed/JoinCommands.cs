using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace TimberDraw
{
    // Shared state between the Joints pane and the TJoinPick / TJoinApply commands: the picked timber pair and
    // the active (edited) connection type. The pane stashes the pair + the live connection type; TJoinApply
    // reads them and cuts the joint INSIDE a command context (which holds the document lock RebuildFromFrame
    // needs -- a palette button handler does not).
    public static class JoinSession
    {
        public static ObjectId AId, BId;
        public static ManagedTimber.TFrame A, B;
        public static bool HasPair;
        public static string AName = "", BName = "";   // picked types ("Brace", "Post"), for the pane's Pick button
        public static ConnectionType Active;     // the connection type shown in the pane (live, edited values)
        public static string LoadedState;        // a picked pair's existing-joint state for the pane to load (else null)
        public static string LastDiag;           // the last apply result / reason, for the pane's status line
        public static bool ReleaseOnApply;       // the pane's APPLY button finalizes: affix the joint, then RELEASE the
                                                 // pair (so type/param changes set up the NEXT joint). Live re-cut
                                                 // edits leave this false and keep the pair. Consumed per apply.
        public static event System.Action Changed;

        public static void SetPair(ObjectId aId, ManagedTimber.TFrame a, ObjectId bId, ManagedTimber.TFrame b, string diag = null)
        { AId = aId; A = a; BId = bId; B = b; HasPair = true; if (diag != null) LastDiag = diag; Changed?.Invoke(); }

        public static void ClearPair() { HasPair = false; Changed?.Invoke(); }

        // Push a status message to the pane WITHOUT touching the pair (a failed cut keeps the pair live so a
        // type / param change can re-cut).
        public static void Report(string diag) { LastDiag = diag; Changed?.Invoke(); }
    }

    public partial class ManagedCommands
    {
        // Pick the two timbers for a joint (a = the member being jointed, b = the host) and stash them for the
        // Joints pane. The pane then renders the active connection type's element stack; Apply cuts it.
        [CommandMethod("TJoinPick")]
        public static void JoinPick()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the FIRST timber (the one being jointed -- strut / brace / rafter): ", out ObjectId aId, out ManagedTimber.TFrame a)) return;
            if (!PickTimber(ed, db, "\nPick the HOST it beds into: ", out ObjectId bId, out ManagedTimber.TFrame b)) return;
            if (aId == bId) { ed.WriteMessage("\nPick two different timbers."); return; }

            // If the pair already carries a joint, load its saved settings so the pane repopulates. Find the joint
            // by its shared id IN THE GEOMETRY (so a deleted joint's stale spec entry is ignored), then read its state.
            string loaded = null;
            int existing = SharedJointId(a, b);
            if (existing != 0)
            {
                Dictionary<int, string> sa = ManagedTimber.ReadJointSpecs(aId);
                if (!sa.TryGetValue(existing, out loaded))
                    ManagedTimber.ReadJointSpecs(bId).TryGetValue(existing, out loaded);
            }
            JoinSession.LoadedState = loaded;
            JoinSession.AName = TypeName(aId);   // read here, in the command context -- the pane
            JoinSession.BName = TypeName(bId);   // shows them on its Pick button
            JoinSession.SetPair(aId, a, bId, b, loaded != null ? "loaded the existing joint's settings" : null);
            // A FRESH pair: cut the configured joint instantly (set up the params, then just pick pair after pair).
            // An existing-joint pair instead loads its saved settings (above) -- don't clobber them with a default cut.
            if (existing == 0) ApplyHeldPair(ed, db);
            else ed.WriteMessage("\nJoints: existing joint loaded -- tweak + Apply to re-cut, or Pick pair for another.");
        }

        // A picked timber's display type from its XData ("Post", "Brace"...), for the pane's Pick button.
        private static string TypeName(ObjectId id)
        {
            try
            {
                string t = Module1.GetXdata(id)?.Type;
                return string.IsNullOrEmpty(t) ? "timber" : t;
            }
            catch { return "timber"; }
        }

        // The joint id present in BOTH frames' GEOMETRY (the joint between this pair), across every feature kind --
        // so it covers box tenons (Features) as well as the polygon/prism joints. 0 = no shared joint.
        private static int SharedJointId(ManagedTimber.TFrame a, ManagedTimber.TFrame b)
        {
            HashSet<int> ids = AllJointIds(a);
            foreach (int id in AllJointIds(b)) if (ids.Contains(id)) return id;
            return 0;
        }

        private static HashSet<int> AllJointIds(ManagedTimber.TFrame f) => ManagedTimber.JointIds(f);

        // Cut the active connection type (set by the pane, with its edited params) onto the picked pair. Runs in
        // a command context so RebuildFromFrame has the document lock. Updates the held ids to the rebuilt solids
        // and clears the pair (re-pick to cut another -- v1 avoids re-reading stale frames).
        [CommandMethod("TJoinApply")]
        public static void JoinApply()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!JoinSession.HasPair) { ed.WriteMessage("\nNo timber pair -- press Pick pair first."); return; }
            if (JoinSession.Active == null) { ed.WriteMessage("\nNo connection type selected."); return; }
            ApplyHeldPair(ed, db);
        }

        // Remove the held pair's joint COMPLETELY -- every feature kind + the persisted per-joint spec -- and
        // rebuild both timbers plain. The pane's one-click CLEAR: the pair STAYS HELD, so Apply right after
        // re-cuts the same connection fresh at the CURRENT contact (the displaced-joint re-snap, without
        // toggling an element off/on to force a re-cut). Runs in a command context for the document lock,
        // like TJoinApply. Same machinery as the all-elements-off delete inside ApplyHeldPair.
        [CommandMethod("TJoinClear")]
        public static void JoinClear()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!JoinSession.HasPair) { ed.WriteMessage("\nNo timber pair -- press Pick pair first."); return; }
            ManagedTimber.TFrame fa = JoinSession.A, fb = JoinSession.B;
            int sid = SharedJointId(fa, fb);
            if (sid == 0)
            {
                ed.WriteMessage("\nNo joint between the held pair -- nothing to clear.");
                JoinSession.Report("no joint between the held pair");
                return;
            }
            fa = CloneFrame(fa); fb = CloneFrame(fb);
            StripJoint(ref fa, sid); StripJoint(ref fb, sid);
            ObjectId na = ManagedTimber.RebuildFromFrame(JoinSession.AId, fa);
            ObjectId nb = ManagedTimber.RebuildFromFrame(JoinSession.BId, fb);
            ManagedTimber.RemoveJointSpec(na, sid);
            ManagedTimber.RemoveJointSpec(nb, sid);
            string gone = "joint cleared -- Apply re-cuts fresh at the current contact";
            ed.WriteMessage("\nJoints: " + gone + ".");
            if (ManagedTimber.TryReadFrame(db, na, out ManagedTimber.TFrame a2) &&
                ManagedTimber.TryReadFrame(db, nb, out ManagedTimber.TFrame b2))
                JoinSession.SetPair(na, a2, nb, b2, gone);
            else { JoinSession.LastDiag = gone + " (pair released)"; JoinSession.ClearPair(); }
        }

        // Cut the active connection type (the dropdown is king -- exactly the type the pane shows) onto the held
        // pair. On success: persist the joint state, then either keep the pair LIVE on the rebuilt solids (a tweak
        // re-cuts in place -- the live-edit path) or, when the pane's APPLY button set ReleaseOnApply, AFFIX and
        // RELEASE the pair (the finalize gesture: the next type/param change sets up the NEXT joint, never re-cuts
        // this one). On failure: report the reason and KEEP the pair so a type / param change can re-cut.
        // Drives TJoinApply (the pane's live edits, Apply, and type switches) and the auto-cut on a fresh TJoinPick.
        private static void ApplyHeldPair(Editor ed, Database db)
        {
            bool release = JoinSession.ReleaseOnApply;
            JoinSession.ReleaseOnApply = false;   // consumed per apply; a failed cut keeps the pair regardless

            // If the pair ALREADY carries a joint, cut on stripped CLONES so a new connection type REPLACES it --
            // even across a different feature kind (box <-> dovetail <-> shoulder ...). Each cutter's own re-cut
            // de-dup only clears ITS OWN kind, so a cross-kind switch would otherwise leave the previous joint's
            // features orphaned (the "could not be removed" case). SharedJointId spans all kinds; StripJoint clears
            // them. Working on clones also means a failed cut never disturbs the live pair.
            ManagedTimber.TFrame fa = JoinSession.A, fb = JoinSession.B;
            int sid = SharedJointId(fa, fb);
            if (sid != 0)
            {
                fa = CloneFrame(fa); fb = CloneFrame(fb);
                StripJoint(ref fa, sid); StripJoint(ref fb, sid);
            }

            ApplyResult r = JoinSession.Active.Apply(db, JoinSession.AId, fa, JoinSession.BId, fb);
            if (!r.Ok)
            {
                // The user EMPTIED the joint (every element unchecked) and the pair already had one -> that's a
                // DELETE, not a no-op. The pre-stripped clones already lack it, so COMMIT them: rebuild both
                // solids without the joint, drop its spec, keep the pair live. (A genuine no-fit -- elements still
                // ON but no contact found -- falls through to the diagnostic and leaves the existing joint alone.)
                if (sid != 0 && JoinSession.Active.Elements.TrueForAll(e => !e.Enabled))
                {
                    ObjectId da = ManagedTimber.RebuildFromFrame(JoinSession.AId, fa);
                    ObjectId dbId = ManagedTimber.RebuildFromFrame(JoinSession.BId, fb);
                    ManagedTimber.RemoveJointSpec(da, sid);
                    ManagedTimber.RemoveJointSpec(dbId, sid);
                    string gone = JoinSession.Active.Name + ": joint removed (all elements off).";
                    ed.WriteMessage("\n" + gone);
                    if (!release &&
                        ManagedTimber.TryReadFrame(db, da, out ManagedTimber.TFrame da2) &&
                        ManagedTimber.TryReadFrame(db, dbId, out ManagedTimber.TFrame db2))
                        JoinSession.SetPair(da, da2, dbId, db2, gone);
                    else { JoinSession.LastDiag = gone + (release ? " Pair released." : ""); JoinSession.ClearPair(); }
                    return;
                }
                string miss = "nothing to cut -- " + r.Diag;
                ed.WriteMessage("\n" + miss + ".");
                JoinSession.Report(miss);
                return;
            }
            // The replacement got a fresh id (the old features were stripped first), so retire the previous joint's
            // persisted spec; then write the new one. Re-picking the pair then repopulates the pane from the new joint.
            if (sid != 0 && sid != r.Jid)
            {
                ManagedTimber.RemoveJointSpec(r.AId, sid);
                ManagedTimber.RemoveJointSpec(r.BId, sid);
            }
            if (r.Jid != 0)
            {
                string state = JoinSession.Active.SerializeState();
                ManagedTimber.WriteJointSpec(r.AId, r.Jid, state);
                ManagedTimber.WriteJointSpec(r.BId, r.Jid, state);
            }
            string diag = JoinSession.Active.Name + ": " + r.Diag;
            if (release)
            {
                // The APPLY finalize: the joint is affixed (cut + spec persisted above) -- release the pair so the
                // pane is free to set up the NEXT joint (a type/param change no longer re-cuts this one).
                ed.WriteMessage("\n" + diag + " -- applied; pair released. Pick pair for the next joint.");
                JoinSession.LastDiag = diag + " -- applied; pair released";
                JoinSession.ClearPair();
                return;
            }
            ed.WriteMessage("\n" + diag + " -- pair kept; tweak to re-cut, or Apply to finish.");
            // Keep the pair LIVE on the REBUILT solids so a param tweak re-cuts WITHOUT re-picking. Re-read both
            // frames from the rebuilt ids; if a solid is gone, drop the pair rather than hold a stale handle.
            if (ManagedTimber.TryReadFrame(db, r.AId, out ManagedTimber.TFrame a2) &&
                ManagedTimber.TryReadFrame(db, r.BId, out ManagedTimber.TFrame b2))
                JoinSession.SetPair(r.AId, a2, r.BId, b2, diag);
            else { JoinSession.LastDiag = diag + " (pair released)"; JoinSession.ClearPair(); }
        }

        // A shallow copy whose feature LISTS are independent (new lists, shared immutable polygon arrays), so the
        // cut + strip below mutate the copy, never the live JoinSession frames. The TFrame value type carries the
        // placement by value already.
        private static ManagedTimber.TFrame CloneFrame(ManagedTimber.TFrame f)
        {
            f.Cuts        = f.Cuts        != null ? new List<(Point3d, Vector3d)>(f.Cuts) : null;
            f.Subtracts   = f.Subtracts   != null ? new List<Point3d[]>(f.Subtracts) : null;
            f.Features    = f.Features    != null ? new List<(Point3d, Point3d, bool, int)>(f.Features) : null;
            f.Pegs        = f.Pegs        != null ? new List<(Point3d, Vector3d, double, double, int)>(f.Pegs) : null;
            f.JointPolys  = f.JointPolys  != null ? new List<(Point3d[], int, bool, double, double)>(f.JointPolys) : null;
            f.JointPolysZ = f.JointPolysZ != null ? new List<(Point3d[], int, bool, double, double)>(f.JointPolysZ) : null;
            f.JointPrisms = f.JointPrisms != null ? new List<(Point3d[], Vector3d, int, bool)>(f.JointPrisms) : null;
            return f;
        }

        // Erase a joint from a frame COMPLETELY -- every feature kind that can carry an id (box features + pegs +
        // the three polygon families). The single definition now lives on ManagedTimber (the regen orphan sweep
        // in EraseFrame shares it); switching a pair's joint to a different KIND still replaces it in place
        // instead of stacking a second one.
        private static void StripJoint(ref ManagedTimber.TFrame f, int id) => ManagedTimber.StripJoint(ref f, id);

        // ---- Factored apply-halves for the facade (each FINDS its own contact, so it runs from a timber pair).
        //      The matching commands route through these too, so command + facade share one path. -------------

        // Rafter HEAD -> king-post side: the shoulder notch (JointPolys via ApplyRafterFoot).
        public static bool ApplyRafterHeadJoint(Database db, ObjectId rId, ref ManagedTimber.TFrame rafter,
            ObjectId pId, ref ManagedTimber.TFrame kingpost, ManagedTimber.RafterHeadSpec spec,
            out ObjectId newRafterId, out ObjectId newPostId, out int jid, out string diag)
        {
            newRafterId = ObjectId.Null; newPostId = ObjectId.Null;
            if (!ComputeRafterHeadJoint(db, ref rafter, ref kingpost, spec, out jid, out diag)) return false;
            newRafterId = ManagedTimber.RebuildFromFrame(rId, rafter);
            newPostId = ManagedTimber.RebuildFromFrame(pId, kingpost);
            return true;
        }

        // Compute-only core (mutate the frames, no DB rebuild) -- shared by ApplyRafterHeadJoint (commit) and the
        // joint PREVIEW (on cloned frames).
        public static bool ComputeRafterHeadJoint(Database db, ref ManagedTimber.TFrame rafter,
            ref ManagedTimber.TFrame kingpost, ManagedTimber.RafterHeadSpec spec, out int jid, out string diag)
        {
            jid = 0;
            if (!FindFootContact(rafter, kingpost, out ManagedTimber.TFace kpFace))
            { diag = "no head-into-side contact -- the rafter's head must die into a king-post side"; return false; }
            if (!ManagedTimber.RafterHeadJoint(rafter, kingpost, kpFace, spec,
                    out List<(Point3d[] Poly, bool OnPost, double Xlo, double Xhi)> polys, out diag))
                return false;
            int reuse = ExistingRafterFootId(rafter, kingpost);
            jid = reuse != 0 ? reuse : NextJointId(db);
            // Replace THIS joint by its id; the geometry-overlap purge applies ONLY to legacy id-0 features.
            // A different identified joint sharing the host (the ridge housing at the king-post apex overlaps
            // the rafter-head notch in elevation bbox) must NEVER be swept up by the overlap net.
            foreach ((Point3d[] Poly, bool OnPost, double Xlo, double Xhi) p in polys)
            {
                if (p.OnPost) kingpost.JointPolys?.RemoveAll(j => (reuse != 0 && j.Joint == reuse) || (j.Joint == 0 && PolysOverlap(j.Poly, p.Poly)));
                else          rafter.JointPolys?.RemoveAll(j => (reuse != 0 && j.Joint == reuse) || (j.Joint == 0 && PolysOverlap(j.Poly, p.Poly)));
            }
            ApplyRafterFoot(ref rafter, ref kingpost, jid, polys);
            return true;
        }

        // Purlin end -> rafter side: housing + dovetail (JointPrisms: rafter subtract, purlin union; id-only re-cut).
        public static bool ApplyPurlinJoint(Database db, ObjectId puId, ref ManagedTimber.TFrame purlin,
            ObjectId rId, ref ManagedTimber.TFrame rafter, ManagedTimber.PurlinRafterSpec spec,
            out ObjectId newPurlinId, out ObjectId newRafterId, out int jid, out string diag)
        {
            newPurlinId = ObjectId.Null; newRafterId = ObjectId.Null;
            if (!ComputePurlinJoint(db, ref purlin, ref rafter, spec, out jid, out diag)) return false;
            newPurlinId = ManagedTimber.RebuildFromFrame(puId, purlin);
            newRafterId = ManagedTimber.RebuildFromFrame(rId, rafter);
            return true;
        }

        // Compute-only core (mutate the frames, no DB rebuild) -- shared by ApplyPurlinJoint (commit) and PREVIEW.
        public static bool ComputePurlinJoint(Database db, ref ManagedTimber.TFrame purlin,
            ref ManagedTimber.TFrame rafter, ManagedTimber.PurlinRafterSpec spec, out int jid, out string diag)
        {
            jid = 0;
            if (!FindFootContact(purlin, rafter, out ManagedTimber.TFace rFace))
            { diag = "no end-into-side contact -- the purlin's end must die into a rafter side"; return false; }
            if (!ManagedTimber.PurlinRafterJoint(purlin, rafter, rFace, spec,
                    out List<(Point3d[] Poly, Vector3d Extrude, bool OnRafter)> prisms, out diag))
                return false;
            int reuse = ExistingRafterFootId(purlin, rafter);
            jid = reuse != 0 ? reuse : NextJointId(db);
            if (purlin.JointPrisms == null) purlin.JointPrisms = new List<(Point3d[], Vector3d, int, bool)>();
            if (rafter.JointPrisms == null) rafter.JointPrisms = new List<(Point3d[], Vector3d, int, bool)>();
            // Replace THIS joint by its id; the geometry-overlap purge applies ONLY to legacy id-0 prisms (a
            // stale un-identified pocket at the same contact). A DIFFERENT identified joint on the same host
            // must never be swept up by the overlap net. Other purlins sit at different stations anyway.
            // PURGE BEFORE ADDING: this joint puts TWO prisms (housing + tongue) on each target list, so the
            // by-id purge must run once up front -- doing it per-prism would delete the housing when the
            // tongue is added (same id, same list), leaving the dovetail with no housing on every re-cut.
            if (reuse != 0)
            {
                purlin.JointPrisms.RemoveAll(j => j.Joint == reuse);
                rafter.JointPrisms.RemoveAll(j => j.Joint == reuse);
            }
            foreach ((Point3d[] Poly, Vector3d Extrude, bool OnRafter) p in prisms)
            {
                var target = p.OnRafter ? rafter.JointPrisms : purlin.JointPrisms;
                target.RemoveAll(j => j.Joint == 0 && PrismPolysOverlap(j.Poly, p.Poly));
                target.Add((p.Poly, p.Extrude, jid, p.OnRafter));
            }
            return true;
        }

        // Common-rafter head -> ridge side: the let-in gain (JointPrisms: ridge subtract, common union; id-only re-cut).
        public static bool ApplyCommonRidgeJoint(Database db, ObjectId cId, ref ManagedTimber.TFrame common,
            ObjectId rId, ref ManagedTimber.TFrame ridge, ManagedTimber.CommonRidgeSpec spec,
            out ObjectId newCommonId, out ObjectId newRidgeId, out int jid, out string diag)
        {
            newCommonId = ObjectId.Null; newRidgeId = ObjectId.Null;
            if (!ComputeCommonRidgeJoint(db, ref common, ref ridge, spec, out jid, out diag)) return false;
            newCommonId = ManagedTimber.RebuildFromFrame(cId, common);
            newRidgeId = ManagedTimber.RebuildFromFrame(rId, ridge);
            return true;
        }

        // Compute-only core (mutate the frames, no DB rebuild) -- shared by ApplyCommonRidgeJoint (commit) and PREVIEW.
        public static bool ComputeCommonRidgeJoint(Database db, ref ManagedTimber.TFrame common,
            ref ManagedTimber.TFrame ridge, ManagedTimber.CommonRidgeSpec spec, out int jid, out string diag)
        {
            jid = 0;
            if (!FindFootContact(common, ridge, out ManagedTimber.TFace rFace))
            { diag = "no end-into-side contact -- the common's head must die into a ridge side"; return false; }
            if (!ManagedTimber.CommonRidgeJoint(common, ridge, rFace, spec,
                    out List<(Point3d[] Poly, Vector3d Extrude, bool OnRidge)> prisms, out diag))
                return false;
            int reuse = ExistingRafterFootId(common, ridge);
            jid = reuse != 0 ? reuse : NextJointId(db);
            if (common.JointPrisms == null) common.JointPrisms = new List<(Point3d[], Vector3d, int, bool)>();
            if (ridge.JointPrisms == null) ridge.JointPrisms = new List<(Point3d[], Vector3d, int, bool)>();
            // Replace THIS joint by its id; the geometry-overlap purge applies ONLY to legacy id-0 prisms (a
            // stale un-identified pocket at the same contact). A DIFFERENT identified joint on the same host
            // must never be swept up by the overlap net. Other commons sit at different stations anyway.
            foreach ((Point3d[] Poly, Vector3d Extrude, bool OnRidge) p in prisms)
            {
                var target = p.OnRidge ? ridge.JointPrisms : common.JointPrisms;
                target.RemoveAll(j => (reuse != 0 && j.Joint == reuse) || (j.Joint == 0 && PrismPolysOverlap(j.Poly, p.Poly)));
                target.Add((p.Poly, p.Extrude, jid, p.OnRidge));
            }
            return true;
        }

        // Common-rafter -> eave-girt birdsmouth: the shared hexagon (rafter notch JointPolys union + girt pocket
        // JointPolysZ subtract; id-only re-cut). CommonEaveJoint finds the girt faces itself -- no contact arg.
        public static bool ApplyCommonEaveJoint(Database db, ObjectId cId, ref ManagedTimber.TFrame common,
            ObjectId gId, ref ManagedTimber.TFrame girt, ManagedTimber.CommonEaveSpec spec,
            out ObjectId newCommonId, out ObjectId newGirtId, out int jid, out string diag)
        {
            newCommonId = ObjectId.Null; newGirtId = ObjectId.Null;
            if (!ComputeCommonEaveJoint(db, ref common, ref girt, spec, out jid, out diag)) return false;
            newCommonId = ManagedTimber.RebuildFromFrame(cId, common);
            newGirtId = ManagedTimber.RebuildFromFrame(gId, girt);
            return true;
        }

        // Compute-only core (mutate the frames, no DB rebuild) -- shared by ApplyCommonEaveJoint (commit) and PREVIEW.
        public static bool ComputeCommonEaveJoint(Database db, ref ManagedTimber.TFrame common,
            ref ManagedTimber.TFrame girt, ManagedTimber.CommonEaveSpec spec, out int jid, out string diag)
        {
            jid = 0;
            if (!ManagedTimber.CommonEaveJoint(common, girt, spec,
                    out Point3d[] rNotch, out double rXlo, out double rXhi,
                    out Point3d[] gPocket, out double gZlo, out double gZhi, out diag))
                return false;
            int reuse = ExistingRafterFootId(common, girt);
            jid = reuse != 0 ? reuse : NextJointId(db);
            if (reuse != 0)
            {
                common.JointPolys?.RemoveAll(j => j.Joint == reuse);
                girt.JointPolysZ?.RemoveAll(j => j.Joint == reuse);
            }
            if (common.JointPolys == null) common.JointPolys = new List<(Point3d[], int, bool, double, double)>();
            if (girt.JointPolysZ == null) girt.JointPolysZ = new List<(Point3d[], int, bool, double, double)>();
            common.JointPolys.Add((rNotch, jid, false, rXlo, rXhi));
            girt.JointPolysZ.Add((gPocket, jid, true, gZlo, gZhi));
            return true;
        }

        // Girt END -> post SIDE box tenon (tenon + housing + pegs). Mirrors TJoint's apply-half faithfully (the
        // bespoke end-cap-mates-a-post-side contact + the re-cut de-dup). TJoint itself is NOT refactored onto
        // this -- it is the most intricate command and stays untouched; the facade duplicates the logic here.
        public static bool ApplyBoxTenonJoint(Database db, ObjectId girtId, ref ManagedTimber.TFrame girt,
            ObjectId postId, ref ManagedTimber.TFrame post, ManagedTimber.JointSpec spec,
            out ObjectId newGirtId, out ObjectId newPostId, out int jid, out string diag)
        {
            newGirtId = ObjectId.Null; newPostId = ObjectId.Null;
            if (!ComputeBoxTenonJoint(db, ref girt, ref post, spec, out jid, out diag)) return false;
            newGirtId = ManagedTimber.RebuildFromFrame(girtId, girt);
            newPostId = ManagedTimber.RebuildFromFrame(postId, post);
            return true;
        }

        // Compute-only core (mutate the frames, no DB rebuild) -- shared by ApplyBoxTenonJoint (commit) and PREVIEW.
        public static bool ComputeBoxTenonJoint(Database db, ref ManagedTimber.TFrame girt,
            ref ManagedTimber.TFrame post, ManagedTimber.JointSpec spec, out int jid, out string diag)
        {
            jid = 0; diag = "";
            // The girt END-cap (Faces 0/1) that mates a post SIDE face (coplanar, opposing, overlapping).
            ManagedTimber.TFace[] gf = ManagedTimber.Faces(girt);
            ManagedTimber.TFace[] pf = ManagedTimber.Faces(post);
            bool found = false; ManagedTimber.TFace gEnd = default;
            for (int gi = 0; gi <= 1 && !found; gi++)
                foreach (ManagedTimber.TFace ps in pf)
                {
                    if (System.Math.Abs(ps.N.DotProduct(post.Z)) >= 0.5) continue;     // post face must be a SIDE
                    if (ManagedTimber.FacesMate(gf[gi], ps, 0.25, out _)) { gEnd = gf[gi]; found = true; break; }
                }
            if (!found) { diag = "no end-into-face contact -- the girt's end must bear on a post side face"; return false; }

            if (!ManagedTimber.GirtPostJoint(girt, post, gEnd, spec,
                    out List<(Point3d Min, Point3d Max, bool Subtract)> features,
                    out List<(Point3d C, Vector3d Axis, double R, double Half)> pegs,
                    out List<(Point3d[] Poly, bool OnPost, double Xlo, double Xhi)> polys))
            { diag = "nothing to cut -- enable a tenon, housing or shoulder (or the tenon collapsed)"; return false; }

            if (girt.Features == null) girt.Features = new List<(Point3d, Point3d, bool, int)>();
            if (post.Features == null) post.Features = new List<(Point3d, Point3d, bool, int)>();

            // Re-cut de-dup: identify the joint at this end (tenon feature, or a shoulder-only shared poly id, or
            // a legacy id-0 cut by geometry) and replace it instead of stacking.
            bool farEnd = gEnd.N.DotProduct(girt.Z) > 0.0;
            double girtL = girt.L;   // a ref parameter cannot be captured inside the lambda below
            int ti = girt.Features.FindIndex(f => !f.Subtract &&
                         (((f.Min.Z + f.Max.Z) / 2.0 > girtL / 2.0) == farEnd));
            int reuseId = ti >= 0 ? girt.Features[ti].Joint : 0;
            if (reuseId == 0) reuseId = ExistingRafterFootId(girt, post);
            if (reuseId != 0)
            {
                girt.Features.RemoveAll(f => f.Joint == reuseId);
                post.Features.RemoveAll(f => f.Joint == reuseId);
                post.Pegs?.RemoveAll(p => p.Joint == reuseId);
                girt.JointPolys?.RemoveAll(j => j.Joint == reuseId);
                post.JointPolys?.RemoveAll(j => j.Joint == reuseId);
                jid = reuseId;
            }
            else if (ti >= 0)
            {
                girt.Features.RemoveAt(ti);
                post.Features.RemoveAll(f => f.Subtract &&
                    features.Exists(nf => nf.Subtract && BoxesOverlap(f.Min, f.Max, nf.Min, nf.Max)));
                jid = NextJointId(db);
            }
            else jid = NextJointId(db);

            ApplyJoint(ref girt, ref post, jid, features, pegs);
            ApplyRafterFoot(ref girt, ref post, jid, polys);   // shoulder triangle polys
            return true;
        }
    }
}
