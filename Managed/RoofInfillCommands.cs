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
    // ManagedCommands part: roof infill joints -- TPurlin, TCommonRidge, TCommonEave
    // (+ deletes and review loops).
    public partial class ManagedCommands
    {
        // Cut a PURLIN housed into a RAFTER as a let-in DOVETAIL. Pick the PURLIN then the RAFTER: the rafter
        // gets the dovetail pocket (subtract) and the purlin's end grows the matching tongue (union). The recipe
        // (seat / height / width / flare) is reviewed; the cut is an id-keyed JointPrism on each timber (re-cut
        // REPLACES; TPurlinDel removes) and rides MOVE / SAVE. Needs a rafter SIDE face the purlin dies into.
        [CommandMethod("TPurlin")]
        public static void Purlin()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the PURLIN: ", out ObjectId puId, out ManagedTimber.TFrame purlin)) return;
            if (!PickTimber(ed, db, "\nPick the RAFTER: ", out ObjectId rId, out ManagedTimber.TFrame rafter)) return;
            if (puId == rId) { ed.WriteMessage("\nPick two different timbers."); return; }

            if (!ReviewPurlin(ed)) return;

            // Cut via the factored apply-half (the same path the ConnectionType facade drives; it finds the contact).
            if (!ApplyPurlinJoint(db, puId, ref purlin, rId, ref rafter, _purlin,
                    out ObjectId npu, out ObjectId nr, out int jid, out string diag))
            { ed.WriteMessage("\nNothing to cut -- " + diag + "."); return; }
            StampJoint(npu, nr, jid, ConnectionType.HousedDovetail(_purlin));
            ed.WriteMessage("\n[diag] " + diag);
            ed.WriteMessage("\nTPurlin: joint #" + jid + " cut -- " + diag +
                            " (purlin " + npu.Handle + ", rafter " + nr.Handle + ").");
        }

        // Remove a purlin dovetail: pick the purlin + rafter, drop the prisms sharing their joint id, rebuild.
        [CommandMethod("TPurlinDel")]
        public static void PurlinDelete()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the PURLIN: ", out ObjectId puId, out ManagedTimber.TFrame purlin)) return;
            if (!PickTimber(ed, db, "\nPick the RAFTER: ", out ObjectId rId, out ManagedTimber.TFrame rafter)) return;
            if (puId == rId) { ed.WriteMessage("\nPick two different timbers."); return; }

            int id = ExistingRafterFootId(purlin, rafter);
            if (id == 0) { ed.WriteMessage("\nNo purlin joint between those two timbers."); return; }
            purlin.JointPrisms?.RemoveAll(j => j.Joint == id);
            rafter.JointPrisms?.RemoveAll(j => j.Joint == id);

            ObjectId npu = ManagedTimber.RebuildFromFrame(puId, purlin);
            ObjectId nr  = ManagedTimber.RebuildFromFrame(rId, rafter);
            ed.WriteMessage("\nTPurlinDel: joint #" + id + " removed (purlin " + npu.Handle + ", rafter " + nr.Handle + ").");
        }

        // The purlin-dovetail recipe sub-menu: edit the sticky _purlin (housing seat / tongue length / base
        // width / band depth / taper angle). Enter / "Cut" proceeds.
        private static bool ReviewPurlin(Editor ed)
        {
            while (true)
            {
                var pko = new PromptKeywordOptions(
                    "\nHoused dovetail -- Seat=" + _purlin.Seat + " Length=" + _purlin.Length +
                    " Width=" + _purlin.Width + " Depth=" + _purlin.Depth + " Angle=" + _purlin.Angle + ". ")
                { AllowNone = true };
                pko.Keywords.Add("Cut");
                pko.Keywords.Add("Seat");
                pko.Keywords.Add("Length");
                pko.Keywords.Add("Width");
                pko.Keywords.Add("Depth");
                pko.Keywords.Add("Angle");
                pko.Keywords.Default = "Cut";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return false;
                string kw = kr.Status == PromptStatus.None ? "Cut" : kr.StringResult;
                if (kw == "Cut") return true;
                switch (kw)
                {
                    case "Seat":   if (GetPositive(ed, "Full-section housing depth into the rafter", _purlin.Seat, out double sv)) _purlin.Seat = sv; break;
                    case "Length": if (GetPositive(ed, "Dovetail tongue length past the housing", _purlin.Length, out double lv)) _purlin.Length = lv; break;
                    case "Width":  if (GetPositive(ed, "Dovetail base width", _purlin.Width, out double wv)) _purlin.Width = wv; break;
                    case "Depth":  if (GetPositive(ed, "Dovetail band depth (flush with the top face)", _purlin.Depth, out double dv)) _purlin.Depth = dv; break;
                    case "Angle":  if (GetDouble  (ed, "Dovetail taper half-angle (degrees)", _purlin.Angle, false, out double av)) _purlin.Angle = av; break;
                }
            }
        }

        // Cut a COMMON RAFTER's head into a RIDGE as a let-in HOUSING. Pick the COMMON then the RIDGE: the ridge
        // gets the full-section gain (subtract) and the common's head fills it (union, same shape). The seat is
        // reviewed; the cut is an id-keyed JointPrism on each timber (re-cut REPLACES; TCommonRidgeDel removes)
        // and rides MOVE / SAVE. Needs a ridge SIDE face the common's head dies into.
        [CommandMethod("TCommonRidge")]
        public static void CommonRidge()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the COMMON rafter: ", out ObjectId cId, out ManagedTimber.TFrame common)) return;
            if (!PickTimber(ed, db, "\nPick the RIDGE: ", out ObjectId rId, out ManagedTimber.TFrame ridge)) return;
            if (cId == rId) { ed.WriteMessage("\nPick two different timbers."); return; }

            if (!ReviewCommonRidge(ed)) return;

            // Cut via the factored apply-half (the same path the ConnectionType facade drives; it finds the contact).
            if (!ApplyCommonRidgeJoint(db, cId, ref common, rId, ref ridge, _comridge,
                    out ObjectId nc, out ObjectId nr, out int jid, out string diag))
            { ed.WriteMessage("\nNothing to cut -- " + diag + "."); return; }
            StampJoint(nc, nr, jid, ConnectionType.CommonRidge(_comridge));
            ed.WriteMessage("\n[diag] " + diag);
            ed.WriteMessage("\nTCommonRidge: joint #" + jid + " cut -- " + diag +
                            " (common " + nc.Handle + ", ridge " + nr.Handle + ").");
        }

        // Remove a common -> ridge housing: pick the common + ridge, drop the prisms sharing their joint id, rebuild.
        [CommandMethod("TCommonRidgeDel")]
        public static void CommonRidgeDelete()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the COMMON rafter: ", out ObjectId cId, out ManagedTimber.TFrame common)) return;
            if (!PickTimber(ed, db, "\nPick the RIDGE: ", out ObjectId rId, out ManagedTimber.TFrame ridge)) return;
            if (cId == rId) { ed.WriteMessage("\nPick two different timbers."); return; }

            int id = ExistingRafterFootId(common, ridge);
            if (id == 0) { ed.WriteMessage("\nNo common -> ridge joint between those two timbers."); return; }
            common.JointPrisms?.RemoveAll(j => j.Joint == id);
            ridge.JointPrisms?.RemoveAll(j => j.Joint == id);

            ObjectId nc = ManagedTimber.RebuildFromFrame(cId, common);
            ObjectId nr = ManagedTimber.RebuildFromFrame(rId, ridge);
            ed.WriteMessage("\nTCommonRidgeDel: joint #" + id + " removed (common " + nc.Handle + ", ridge " + nr.Handle + ").");
        }

        // The common->ridge housing sub-menu: edit the sticky _comridge (the let-in seat). Enter / "Cut" proceeds.
        private static bool ReviewCommonRidge(Editor ed)
        {
            while (true)
            {
                var pko = new PromptKeywordOptions(
                    "\nCommon->ridge housing -- Seat=" + _comridge.Seat + ". ") { AllowNone = true };
                pko.Keywords.Add("Cut");
                pko.Keywords.Add("Seat");
                pko.Keywords.Default = "Cut";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return false;
                string kw = kr.Status == PromptStatus.None ? "Cut" : kr.StringResult;
                if (kw == "Cut") return true;
                if (kw == "Seat" && GetPositive(ed, "Let-in housing depth into the ridge face", _comridge.Seat, out double sv))
                    _comridge.Seat = sv;
            }
        }

        // Cut a HOUSED COMMON RAFTER -> EAVE GIRT birdsmouth. Pick the COMMON then the GIRT: the rafter is
        // notched (seat let-in below the girt top + heel let-in inside the heel face) and the girt gets the
        // matching top pocket -- BOTH are cut, sharing one joint id (re-cut REPLACES; TCommonEaveDel removes).
        // The recipe (the two let-ins) is reviewed; the cuts ride MOVE / SAVE.
        [CommandMethod("TCommonEave")]
        public static void CommonEave()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the COMMON rafter: ", out ObjectId cId, out ManagedTimber.TFrame common)) return;
            if (!PickTimber(ed, db, "\nPick the EAVE GIRT: ", out ObjectId gId, out ManagedTimber.TFrame girt)) return;
            if (cId == gId) { ed.WriteMessage("\nPick two different timbers."); return; }

            if (!ReviewCommonEave(ed)) return;

            // Cut via the factored apply-half (the same path the ConnectionType facade drives).
            if (!ApplyCommonEaveJoint(db, cId, ref common, gId, ref girt, _comeave,
                    out ObjectId nc, out ObjectId ng, out int jid, out string diag))
            { ed.WriteMessage("\nNothing to cut -- " + diag + "."); return; }
            StampJoint(nc, ng, jid, ConnectionType.Birdsmouth(_comeave));
            ed.WriteMessage("\n[diag] " + diag);
            ed.WriteMessage("\nTCommonEave: joint #" + jid + " cut -- " + diag +
                            " (common " + nc.Handle + ", girt " + ng.Handle + ").");
        }

        // Remove a housed common -> eave-girt birdsmouth: pick the common + girt, drop the rafter notch + girt
        // pocket sharing their joint id, rebuild both.
        [CommandMethod("TCommonEaveDel")]
        public static void CommonEaveDelete()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the COMMON rafter: ", out ObjectId cId, out ManagedTimber.TFrame common)) return;
            if (!PickTimber(ed, db, "\nPick the EAVE GIRT: ", out ObjectId gId, out ManagedTimber.TFrame girt)) return;
            if (cId == gId) { ed.WriteMessage("\nPick two different timbers."); return; }

            int id = ExistingRafterFootId(common, girt);
            if (id == 0) { ed.WriteMessage("\nNo birdsmouth between those two timbers."); return; }
            common.JointPolys?.RemoveAll(j => j.Joint == id);
            girt.JointPolysZ?.RemoveAll(j => j.Joint == id);

            ObjectId nc = ManagedTimber.RebuildFromFrame(cId, common);
            ObjectId ng = ManagedTimber.RebuildFromFrame(gId, girt);
            ed.WriteMessage("\nTCommonEaveDel: birdsmouth #" + id + " removed (common " + nc.Handle + ", girt " + ng.Handle + ").");
        }

        // The housed-birdsmouth sub-menu: edit the sticky _comeave (the seat + heel let-ins). Enter / "Cut" proceeds.
        private static bool ReviewCommonEave(Editor ed)
        {
            while (true)
            {
                var pko = new PromptKeywordOptions(
                    "\nHoused birdsmouth -- Seat=" + _comeave.Seat + " Heel=" + _comeave.Heel + ". ")
                { AllowNone = true };
                pko.Keywords.Add("Cut");
                pko.Keywords.Add("Seat");
                pko.Keywords.Add("Heel");
                pko.Keywords.Default = "Cut";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return false;
                string kw = kr.Status == PromptStatus.None ? "Cut" : kr.StringResult;
                if (kw == "Cut") return true;
                switch (kw)
                {
                    case "Seat": if (GetPositive(ed, "Seat let-in below the girt top", _comeave.Seat, out double sv)) _comeave.Seat = sv; break;
                    case "Heel": if (GetPositive(ed, "Heel let-in inside the heel face", _comeave.Heel, out double hv)) _comeave.Heel = hv; break;
                }
            }
        }
    }
}
