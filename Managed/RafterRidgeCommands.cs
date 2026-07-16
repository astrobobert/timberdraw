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
    // ManagedCommands part: principal rafter + ridge joints -- TRafterFoot/TRafterHead,
    // TRidge, TRidgeRafter (+ Apply/Compute halves and deletes).
    public partial class ManagedCommands
    {
        // Cut a principal-rafter FOOT housed into a post SIDE -- a girt-at-a-pitch HOUSING (+ optional TENON).
        // Pick the RAFTER then the POST: the post gets the wedge pocket/mortise and the rafter foot is grown
        // into it (housed stub + tenon tongue) -- a level seat with a pitch-matched top. The recipe (housing
        // depth + tenon thickness/length/shoulders/offset) is reviewed; the cuts are id-keyed polygons (re-cut
        // REPLACES; TRafterFootDel removes) and ride MOVE / TFit + SAVE. Needs a post DEPTH (+/-Y) face.
        [CommandMethod("TRafterFoot")]
        public static void RafterFoot()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the RAFTER: ", out ObjectId rId, out ManagedTimber.TFrame rafter)) return;
            if (!PickTimber(ed, db, "\nPick the POST: ", out ObjectId pId, out ManagedTimber.TFrame post)) return;
            if (rId == pId) { ed.WriteMessage("\nPick two different timbers."); return; }

            if (!ReviewRafterFoot(ed)) return;

            // Cut via the factored apply-half (the same path the ConnectionType facade drives; it finds the contact).
            if (!ApplyRafterFootJoint(db, rId, ref rafter, pId, ref post, _rfoot,
                    out ObjectId nr, out ObjectId np, out int jid, out string diag))
            { ed.WriteMessage("\nNothing to cut -- " + diag + "."); return; }
            StampJoint(nr, np, jid, ConnectionType.RafterFoot(_rfoot));
            ed.WriteMessage("\n[diag] " + diag);
            ed.WriteMessage("\nTRafterFoot: joint #" + jid + " cut -- " + diag +
                            " (rafter " + nr.Handle + ", post " + np.Handle + ").");
        }

        // The apply-half of TRafterFoot, factored for the ConnectionType facade: FINDS the post-side contact
        // itself, cuts the sloped housing (+ optional tenon), shares/mints the joint id (dropping overlapping
        // polys on a re-cut), rebuilds both. False + diag = no contact or nothing enabled.
        public static bool ApplyRafterFootJoint(Database db, ObjectId rId, ref ManagedTimber.TFrame rafter,
            ObjectId pId, ref ManagedTimber.TFrame post, ManagedTimber.RafterFootSpec spec,
            out ObjectId newRafterId, out ObjectId newPostId, out int jid, out string diag)
        {
            newRafterId = ObjectId.Null; newPostId = ObjectId.Null;
            if (!ComputeRafterFootJoint(db, ref rafter, ref post, spec, out jid, out diag)) return false;
            newRafterId = ManagedTimber.RebuildFromFrame(rId, rafter);
            newPostId = ManagedTimber.RebuildFromFrame(pId, post);
            return true;
        }

        // Compute-only core of the rafter-foot joint (housing + optional tenon): find the post-side contact, cut
        // the wedge, share/mint the id and route the polys onto the two frames WITHOUT the DB rebuild. Driven by
        // ApplyRafterFootJoint (commit) and the joint PREVIEW (on cloned frames). False + diag = nothing cut.
        public static bool ComputeRafterFootJoint(Database db, ref ManagedTimber.TFrame rafter,
            ref ManagedTimber.TFrame post, ManagedTimber.RafterFootSpec spec, out int jid, out string diag)
        {
            jid = 0;
            if (!FindFootContact(rafter, post, out ManagedTimber.TFace pFace))
            { diag = "no foot-into-side contact -- the rafter's foot must die into a post side"; return false; }
            if (!ManagedTimber.RafterFootJoint(rafter, post, pFace, spec,
                    out List<(Point3d[] Poly, bool OnPost, double Xlo, double Xhi)> polys,
                    out List<(Point3d C, Vector3d Axis, double R, double Half)> postPegs, out diag))
                return false;
            int reuse = ExistingRafterFootId(rafter, post);
            jid = reuse != 0 ? reuse : NextJointId(db);
            if (reuse != 0) post.Pegs?.RemoveAll(p => p.Joint == reuse);   // old peg bores go with the re-cut joint
            // Replace THIS joint by its id; the geometry-overlap purge applies ONLY to legacy id-0 features --
            // a DIFFERENT identified joint sharing the host must never be swept up by the overlap net.
            foreach ((Point3d[] Poly, bool OnPost, double Xlo, double Xhi) p in polys)
            {
                if (p.OnPost) post.JointPolys?.RemoveAll(j => (reuse != 0 && j.Joint == reuse) || (j.Joint == 0 && PolysOverlap(j.Poly, p.Poly)));
                else          rafter.JointPolys?.RemoveAll(j => (reuse != 0 && j.Joint == reuse) || (j.Joint == 0 && PolysOverlap(j.Poly, p.Poly)));
            }
            ApplyRafterFoot(ref rafter, ref post, jid, polys);
            if (postPegs.Count > 0)
            {
                if (post.Pegs == null) post.Pegs = new List<(Point3d, Vector3d, double, double, int)>();
                foreach ((Point3d C, Vector3d Axis, double R, double Half) pg in postPegs)
                    post.Pegs.Add((pg.C, pg.Axis, pg.R, pg.Half, jid));     // BORE the post cheeks
            }
            return true;
        }

        // Remove a rafter-foot joint: pick the rafter + post, drop the polygons sharing their joint id from
        // both, rebuild. (Parallels TJointDel.)
        [CommandMethod("TRafterFootDel")]
        public static void RafterFootDelete()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the RAFTER: ", out ObjectId rId, out ManagedTimber.TFrame rafter)) return;
            if (!PickTimber(ed, db, "\nPick the POST: ", out ObjectId pId, out ManagedTimber.TFrame post)) return;
            if (rId == pId) { ed.WriteMessage("\nPick two different timbers."); return; }

            int id = ExistingRafterFootId(rafter, post);
            if (id == 0) { ed.WriteMessage("\nNo rafter-foot joint between those two timbers."); return; }
            rafter.JointPolys?.RemoveAll(j => j.Joint == id);
            post.JointPolys?.RemoveAll(j => j.Joint == id);
            post.Pegs?.RemoveAll(p => p.Joint == id);

            ObjectId nr = ManagedTimber.RebuildFromFrame(rId, rafter);
            ObjectId np = ManagedTimber.RebuildFromFrame(pId, post);
            ManagedTimber.RemoveJointSpec(nr, id);   // the recipe goes with the joint (else TJointSync resurrects)
            ManagedTimber.RemoveJointSpec(np, id);
            ed.WriteMessage("\nTRafterFootDel: joint #" + id + " removed (rafter " + nr.Handle + ", post " + np.Handle + ").");
        }

        // Cut a principal-rafter HEAD bearing on a KING POST side -- the legacy "shoulder" notch only. Pick
        // the RAFTER then the KING POST: the king post gets the bearing notch and the rafter head grows the
        // matching tongue. Seat is reviewed; the cut is an id-keyed polygon (re-cut REPLACES;
        // TRafterHeadDel removes) and rides MOVE / TFit + SAVE. Needs a king-post DEPTH (+/-Y) face.
        [CommandMethod("TRafterHead")]
        public static void RafterHead()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the RAFTER: ", out ObjectId rId, out ManagedTimber.TFrame rafter)) return;
            if (!PickTimber(ed, db, "\nPick the KING POST: ", out ObjectId pId, out ManagedTimber.TFrame kingpost)) return;
            if (rId == pId) { ed.WriteMessage("\nPick two different timbers."); return; }

            if (!ReviewRafterHead(ed)) return;

            // Cut via the factored apply-half (the same path the ConnectionType facade drives; it finds the contact).
            if (!ApplyRafterHeadJoint(db, rId, ref rafter, pId, ref kingpost, _rhead,
                    out ObjectId nr, out ObjectId np, out int jid, out string diag))
            { ed.WriteMessage("\nNothing to cut -- " + diag + "."); return; }
            StampJoint(nr, np, jid, ConnectionType.RafterHead(_rhead));
            ed.WriteMessage("\n[diag] " + diag);
            ed.WriteMessage("\nTRafterHead: joint #" + jid + " cut -- " + diag +
                            " (rafter " + nr.Handle + ", king post " + np.Handle + ").");
        }

        // Remove a rafter-head joint: pick the rafter + king post, drop the polygons sharing their joint id
        // from both, rebuild. (Parallels TRafterFootDel.)
        [CommandMethod("TRafterHeadDel")]
        public static void RafterHeadDelete()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the RAFTER: ", out ObjectId rId, out ManagedTimber.TFrame rafter)) return;
            if (!PickTimber(ed, db, "\nPick the KING POST: ", out ObjectId pId, out ManagedTimber.TFrame kingpost)) return;
            if (rId == pId) { ed.WriteMessage("\nPick two different timbers."); return; }

            int id = ExistingRafterFootId(rafter, kingpost);
            if (id == 0) { ed.WriteMessage("\nNo rafter-head joint between those two timbers."); return; }
            rafter.JointPolys?.RemoveAll(j => j.Joint == id);
            kingpost.JointPolys?.RemoveAll(j => j.Joint == id);

            ObjectId nr = ManagedTimber.RebuildFromFrame(rId, rafter);
            ObjectId np = ManagedTimber.RebuildFromFrame(pId, kingpost);
            ManagedTimber.RemoveJointSpec(nr, id);   // the recipe goes with the joint
            ManagedTimber.RemoveJointSpec(np, id);
            ed.WriteMessage("\nTRafterHeadDel: joint #" + id + " removed (rafter " + nr.Handle + ", king post " + np.Handle + ").");
        }

        // The rafter-head shoulder sub-menu: edit the sticky _rhead (just the bearing-seat depth). Enter /
        // "Cut" proceeds.
        private static bool ReviewRafterHead(Editor ed)
        {
            while (true)
            {
                var pko = new PromptKeywordOptions(
                    "\nRafter head shoulder -- Seat=" + _rhead.Seat + " (On=" + (_rhead.On ? "Yes" : "No") + "). ")
                { AllowNone = true };
                pko.Keywords.Add("Cut");
                pko.Keywords.Add("Seat");
                pko.Keywords.Add("On");
                pko.Keywords.Default = "Cut";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return false;
                string kw = kr.Status == PromptStatus.None ? "Cut" : kr.StringResult;
                if (kw == "Cut") return true;
                switch (kw)
                {
                    case "Seat": if (GetPositive(ed, "Shoulder seat depth into the king post", _rhead.Seat, out double sv)) _rhead.Seat = sv; break;
                    case "On":       _rhead.On = !_rhead.On; break;
                }
            }
        }

        // Cut a RIDGE -> KING POST drop-in housing -- the king post top is cut to the ridge's cross-section
        // (incl. its chamfered top) so the ridge lowers straight in. Pick the RIDGE then the KING POST: only
        // the king post is cut (an id-keyed polygon; re-cut REPLACES, TRidgeDel removes) and it rides MOVE /
        // TFit + SAVE. The ridge must run along the king-post width.
        [CommandMethod("TRidge")]
        public static void Ridge()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the RIDGE: ", out ObjectId rId, out ManagedTimber.TFrame ridge)) return;
            if (!PickTimber(ed, db, "\nPick the KING POST: ", out ObjectId pId, out ManagedTimber.TFrame kingpost)) return;
            if (rId == pId) { ed.WriteMessage("\nPick two different timbers."); return; }

            if (!ReviewRidge(ed)) return;

            if (!ManagedTimber.RidgeKpostJoint(ridge, kingpost, _ridge,
                    out List<(Point3d[] Poly, bool OnPost, double Xlo, double Xhi)> polys,
                    out List<(Point3d[] Poly, bool Subtract, double Xlo, double Xhi)> ridgeZPolys, out string diag))
            { ed.WriteMessage("\nNothing to cut -- " + diag + "."); return; }
            ed.WriteMessage("\n[diag] " + diag);

            int reuse = ExistingRafterFootId(ridge, kingpost);
            int jid = reuse != 0 ? reuse : NextJointId(db);
            // Replace THIS joint by its id; the overlap purge applies ONLY to legacy id-0 features -- the
            // rafter-HEAD notches share the king-post apex and must never be swept up by the overlap net.
            foreach ((Point3d[] Poly, bool OnPost, double Xlo, double Xhi) p in polys)   // king post pocket
                if (p.OnPost) kingpost.JointPolys?.RemoveAll(j => (reuse != 0 && j.Joint == reuse) || (j.Joint == 0 && PolysOverlap(j.Poly, p.Poly)));
            if (reuse != 0) ridge.JointPolysZ?.RemoveAll(j => j.Joint == reuse);   // old tongue (re-cut)

            ApplyRafterFoot(ref ridge, ref kingpost, jid, polys);   // king post pocket subtract (shared id)
            if (ridgeZPolys.Count > 0)   // ridge TONGUE (chamfered, Z-extruded into the pocket)
            {
                if (ridge.JointPolysZ == null) ridge.JointPolysZ = new List<(Point3d[], int, bool, double, double)>();
                foreach ((Point3d[] Poly, bool Subtract, double Xlo, double Xhi) z in ridgeZPolys)
                    ridge.JointPolysZ.Add((z.Poly, jid, z.Subtract, z.Xlo, z.Xhi));
            }

            ObjectId nr = ManagedTimber.RebuildFromFrame(rId, ridge);
            ObjectId np = ManagedTimber.RebuildFromFrame(pId, kingpost);
            StampJoint(nr, np, jid, ConnectionType.RidgeHousing(_ridge));
            ed.WriteMessage("\nTRidge: joint #" + jid + " cut -- " + diag +
                            " (ridge " + nr.Handle + ", king post " + np.Handle + ").");
        }

        // Remove a ridge -> king post joint: pick the ridge + king post, drop the polygons sharing their joint
        // id from both, rebuild. (Parallels TRafterHeadDel.)
        [CommandMethod("TRidgeDel")]
        public static void RidgeDelete()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the RIDGE: ", out ObjectId rId, out ManagedTimber.TFrame ridge)) return;
            if (!PickTimber(ed, db, "\nPick the KING POST: ", out ObjectId pId, out ManagedTimber.TFrame kingpost)) return;
            if (rId == pId) { ed.WriteMessage("\nPick two different timbers."); return; }

            int id = ExistingRafterFootId(ridge, kingpost);
            if (id == 0) { ed.WriteMessage("\nNo ridge -> king post joint between those two timbers."); return; }
            ridge.JointPolys?.RemoveAll(j => j.Joint == id);
            kingpost.JointPolys?.RemoveAll(j => j.Joint == id);
            ridge.JointPolysZ?.RemoveAll(j => j.Joint == id);   // the chamfered tongue

            ObjectId nr = ManagedTimber.RebuildFromFrame(rId, ridge);
            ObjectId np = ManagedTimber.RebuildFromFrame(pId, kingpost);
            ManagedTimber.RemoveJointSpec(nr, id);   // the recipe goes with the joint
            ManagedTimber.RemoveJointSpec(np, id);
            ed.WriteMessage("\nTRidgeDel: joint #" + id + " removed (ridge " + nr.Handle + ", king post " + np.Handle + ").");
        }

        // The ridge-housing sub-menu: edit the sticky _ridge (the bearing-seat depth). Enter / "Cut" proceeds.
        private static bool ReviewRidge(Editor ed)
        {
            while (true)
            {
                var pko = new PromptKeywordOptions(
                    "\nRidge housing -- Seat=" + _ridge.Seat + " ShoulderBottom=" + _ridge.ShoulderBottom +
                    " (On=" + (_ridge.On ? "Yes" : "No") + "). ")
                { AllowNone = true };
                pko.Keywords.Add("Cut");
                pko.Keywords.Add("Seat");
                pko.Keywords.Add("ShoulderBottom");
                pko.Keywords.Add("On");
                pko.Keywords.Default = "Cut";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return false;
                string kw = kr.Status == PromptStatus.None ? "Cut" : kr.StringResult;
                if (kw == "Cut") return true;
                switch (kw)
                {
                    case "Seat": if (GetPositive(ed, "Housing seat depth", _ridge.Seat, out double sv)) _ridge.Seat = sv; break;
                    case "ShoulderBottom": if (GetDouble(ed, "Bottom shoulder -- the ridge's lower N inches stay full and bear (0 = full drop-in)", _ridge.ShoulderBottom, false, out double bv)) _ridge.ShoulderBottom = bv; break;
                    case "On":   _ridge.On = !_ridge.On; break;
                }
            }
        }

        // Cut a RIDGE -> PRINCIPAL RAFTER drop-in housing -- the SAME geometry as TRidge (the rafter head is
        // cut to the ridge's chamfered cross-section risen to the apex, so the ridge lowers straight in), but
        // the host is a sloped principal rafter instead of a king post. This is the king-post-LESS bent: the
        // two rafters carry the ridge themselves, so the ridge is housed into BOTH rafter heads (run this once
        // per rafter). Reuses RidgeKpostJoint verbatim -- the pocket maps to the rafter's local Z x Y and
        // extrudes across the rafter WIDTH (= the ridge axis), independent of the rafter pitch. Pick the RIDGE
        // then the RAFTER. Only the rafter is cut (an id-keyed pocket) + a chamfered tongue on the ridge; re-cut
        // REPLACES, TRidgeRafterDel removes, and it rides MOVE / TFit + SAVE.
        [CommandMethod("TRidgeRafter")]
        public static void RidgeRafter()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the RIDGE: ", out ObjectId rId, out ManagedTimber.TFrame ridge)) return;
            if (!PickTimber(ed, db, "\nPick the RAFTER: ", out ObjectId fId, out ManagedTimber.TFrame rafter)) return;
            if (rId == fId) { ed.WriteMessage("\nPick two different timbers."); return; }

            if (!ReviewRidgeRafter(ed)) return;

            // Cut via the factored apply-half (host-neutral; the same path the ConnectionType facade drives).
            if (!ApplyRidgeHousingJoint(db, rId, ref ridge, fId, ref rafter, _ridgeRafter,
                    out ObjectId nr, out ObjectId nf, out int jid, out string diag))
            { ed.WriteMessage("\nNothing to cut -- " + diag + "."); return; }
            StampJoint(nr, nf, jid, ConnectionType.RidgeHousing(_ridgeRafter));
            ed.WriteMessage("\n[diag] " + diag);
            ed.WriteMessage("\nTRidgeRafter: joint #" + jid + " cut -- " + diag +
                            " (ridge " + nr.Handle + ", rafter " + nf.Handle + ").");
        }

        // The apply-half of TRidgeRafter, factored for the ConnectionType facade. HOST-NEUTRAL: the drop-in
        // pocket + chamfered tongue cut identically into a king post OR a principal rafter (RidgeKpostJoint maps
        // to the host's local frame). Id-only removal so a two-bay host keeps the other bay's pocket. False +
        // diag = nothing cut (e.g. the ridge does not run along the host width).
        public static bool ApplyRidgeHousingJoint(Database db, ObjectId rId, ref ManagedTimber.TFrame ridge,
            ObjectId hId, ref ManagedTimber.TFrame host, ManagedTimber.RidgeHousingSpec spec,
            out ObjectId newRidgeId, out ObjectId newHostId, out int jid, out string diag)
        {
            newRidgeId = ObjectId.Null; newHostId = ObjectId.Null;
            if (!ComputeRidgeHousingJoint(db, ref ridge, ref host, spec, out jid, out diag)) return false;
            newRidgeId = ManagedTimber.RebuildFromFrame(rId, ridge);
            newHostId = ManagedTimber.RebuildFromFrame(hId, host);
            return true;
        }

        // Compute-only core of the ridge housing (host pocket SUBTRACT + ridge TONGUE union, host-neutral king-post
        // OR rafter) WITHOUT the DB rebuild. Driven by ApplyRidgeHousingJoint (commit) and the joint PREVIEW (on
        // cloned frames). False + diag = nothing cut.
        public static bool ComputeRidgeHousingJoint(Database db, ref ManagedTimber.TFrame ridge,
            ref ManagedTimber.TFrame host, ManagedTimber.RidgeHousingSpec spec, out int jid, out string diag)
        {
            jid = 0;
            if (!ManagedTimber.RidgeKpostJoint(ridge, host, spec,
                    out List<(Point3d[] Poly, bool OnPost, double Xlo, double Xhi)> polys,
                    out List<(Point3d[] Poly, bool Subtract, double Xlo, double Xhi)> ridgeZPolys, out diag))
                return false;
            int reuse = ExistingRafterFootId(ridge, host);
            jid = reuse != 0 ? reuse : NextJointId(db);
            if (reuse != 0)
            {
                host.JointPolys?.RemoveAll(j => j.Joint == reuse);
                ridge.JointPolysZ?.RemoveAll(j => j.Joint == reuse);
            }
            ApplyRafterFoot(ref ridge, ref host, jid, polys);   // host pocket subtract (OnPost), shared id
            if (ridgeZPolys.Count > 0)   // ridge TONGUE (chamfered, Z-extruded into the pocket)
            {
                if (ridge.JointPolysZ == null) ridge.JointPolysZ = new List<(Point3d[], int, bool, double, double)>();
                foreach ((Point3d[] Poly, bool Subtract, double Xlo, double Xhi) z in ridgeZPolys)
                    ridge.JointPolysZ.Add((z.Poly, jid, z.Subtract, z.Xlo, z.Xhi));
            }
            return true;
        }

        // Remove a ridge -> principal-rafter housing: pick the ridge + rafter, drop the pocket + tongue sharing
        // their joint id from both, rebuild. (Parallels TRidgeDel.)
        [CommandMethod("TRidgeRafterDel")]
        public static void RidgeRafterDelete()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the RIDGE: ", out ObjectId rId, out ManagedTimber.TFrame ridge)) return;
            if (!PickTimber(ed, db, "\nPick the RAFTER: ", out ObjectId fId, out ManagedTimber.TFrame rafter)) return;
            if (rId == fId) { ed.WriteMessage("\nPick two different timbers."); return; }

            int id = ExistingRafterFootId(ridge, rafter);
            if (id == 0) { ed.WriteMessage("\nNo ridge -> rafter joint between those two timbers."); return; }
            ridge.JointPolys?.RemoveAll(j => j.Joint == id);
            rafter.JointPolys?.RemoveAll(j => j.Joint == id);
            ridge.JointPolysZ?.RemoveAll(j => j.Joint == id);   // the chamfered tongue

            ObjectId nr = ManagedTimber.RebuildFromFrame(rId, ridge);
            ObjectId nf = ManagedTimber.RebuildFromFrame(fId, rafter);
            ManagedTimber.RemoveJointSpec(nr, id);   // the recipe goes with the joint
            ManagedTimber.RemoveJointSpec(nf, id);
            ed.WriteMessage("\nTRidgeRafterDel: joint #" + id + " removed (ridge " + nr.Handle + ", rafter " + nf.Handle + ").");
        }

        // The ridge -> rafter housing sub-menu: edit the sticky _ridgeRafter (the bearing-seat depth). Enter / "Cut" proceeds.
        private static bool ReviewRidgeRafter(Editor ed)
        {
            while (true)
            {
                var pko = new PromptKeywordOptions(
                    "\nRidge->rafter housing -- Seat=" + _ridgeRafter.Seat + " ShoulderBottom=" + _ridgeRafter.ShoulderBottom +
                    " (On=" + (_ridgeRafter.On ? "Yes" : "No") + "). ")
                { AllowNone = true };
                pko.Keywords.Add("Cut");
                pko.Keywords.Add("Seat");
                pko.Keywords.Add("ShoulderBottom");
                pko.Keywords.Add("On");
                pko.Keywords.Default = "Cut";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return false;
                string kw = kr.Status == PromptStatus.None ? "Cut" : kr.StringResult;
                if (kw == "Cut") return true;
                switch (kw)
                {
                    case "Seat": if (GetPositive(ed, "Housing seat depth", _ridgeRafter.Seat, out double sv)) _ridgeRafter.Seat = sv; break;
                    case "ShoulderBottom": if (GetDouble(ed, "Bottom shoulder -- the ridge's lower N inches stay full and bear (0 = full drop-in)", _ridgeRafter.ShoulderBottom, false, out double bv)) _ridgeRafter.ShoulderBottom = bv; break;
                    case "On":   _ridgeRafter.On = !_ridgeRafter.On; break;
                }
            }
        }
    }
}
