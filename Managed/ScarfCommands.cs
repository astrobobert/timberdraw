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
    // ManagedCommands part: TScarf (scarf-splice a timber into two pieces).
    public partial class ManagedCommands
    {
        // Apply a keyed hook scarf to ONE full-length beam at a picked point, splitting it into two
        // interlocking managed timbers (the shop joint from drawscarf.lsp). Stage A: the splayed/squinted
        // BLADE only -- tables + key come in Stages B/C. Each piece overlaps the other by the scarf
        // length, so the assembled run stays the beam's drawn length while each piece's stored Size is its
        // FULL physical length including the scarf tongue (printed for manufacturing). TScan nodes at the
        // scarf centre.
        [CommandMethod("TScarf")]
        public static void ScarfSplice()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the beam to scarf: ", out ObjectId beamId, out ManagedTimber.TFrame f)) return;

            double l = 3.0 * f.D;
            if (l + 1.0 > f.L)
            { ed.WriteMessage("\nThe beam is too short for a " + l.ToString("0.#") + "\" scarf."); return; }

            // Ghost the scarf footprint sliding along the beam; the cursor projects onto the beam axis
            // (clamped to fit), and the Length keyword resizes the region live. A pick commits.
            var jig = new ScarfJig(f, l);
            while (true)
            {
                PromptResult dr = ed.Drag(jig);
                if (dr.Status == PromptStatus.Keyword)
                {
                    if (dr.StringResult == "Length")
                    {
                        var lopts = new PromptDistanceOptions("\nScarf length: ")
                        { DefaultValue = jig.Len, UseDefaultValue = true, AllowNegative = false, AllowZero = false };
                        PromptDoubleResult lr = ed.GetDistance(lopts);
                        if (lr.Status == PromptStatus.OK)
                        {
                            if (lr.Value + 1.0 > f.L)
                                ed.WriteMessage("\nThe beam is too short for a " + lr.Value.ToString("0.#") + "\" scarf.");
                            else jig.SetLength(lr.Value);
                        }
                    }
                    continue;
                }
                if (dr.Status != PromptStatus.OK) return;
                break;
            }
            double xc = jig.Xc;
            l = jig.Len;
            double h = l / 2.0;
            if (xc - h < 0.5 || xc + h > f.L - 0.5)
            { ed.WriteMessage("\nThe scarf (" + l.ToString("0.#") + "\") doesn't fit within the beam at that point."); return; }

            Module1.DataStructure od = Module1.GetXdata(beamId);
            string type = string.IsNullOrEmpty(od?.Type) ? "Timber" : od.Type;
            string desg = od?.Designation ?? "";
            Point3d cs = f.O + f.Z * xc;

            // Piece frames: piece1 = [0, xc+h], piece2 = [xc-h, L] along Z -- they overlap over the scarf.
            ManagedTimber.TFrame f1 = f; f1.L = xc + h; f1.NearN = f.Z.Negate(); f1.FarN = f.Z;
            ManagedTimber.TFrame f2 = f; f2.O = f.O + f.Z * (xc - h); f2.L = f.L - (xc - h); f2.NearN = f.Z.Negate(); f2.FarN = f.Z;

            ObjectId nP1, nP2;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                // Tongue1 (left piece) + tongue2 = 180-degree point reflection about the scarf centre.
                ObjectId t1id = ManagedTimber.BuildScarfTongue(tr, btr, f, xc, l);
                var tongue1 = (Solid3d)tr.GetObject(t1id, OpenMode.ForWrite);
                var tongue2 = (Solid3d)tongue1.Clone();
                btr.AppendEntity(tongue2); tr.AddNewlyCreatedDBObject(tongue2, true);
                tongue2.TransformBy(Matrix3d.Rotation(Math.PI, f.X, cs));   // reflect about the WIDTH axis

                // Un-scarfed stubs, then union each piece's tongue onto its stub.
                Solid3d leftStub = ManagedTimber.MakeBoxSolid(f, 0, xc - h);
                Solid3d rightStub = ManagedTimber.MakeBoxSolid(f, xc + h, f.L);
                nP1 = btr.AppendEntity(leftStub); tr.AddNewlyCreatedDBObject(leftStub, true);
                nP2 = btr.AppendEntity(rightStub); tr.AddNewlyCreatedDBObject(rightStub, true);
                leftStub.BooleanOperation(BooleanOperationType.BoolUnite, tongue1);
                rightStub.BooleanOperation(BooleanOperationType.BoolUnite, tongue2);

                // Stage B: bearing tables (locks). Each 1.5x2 abutment notch is filled across the OUTER
                // width bands [+1,+W/2] and [-W/2,-1] ((W-2)/2 each); the middle 2" band [-1,+1] stays
                // open as the key slot (Stage C). Bottom-right abutment -> piece2; the 180-rotated
                // top-left abutment -> piece1. Locks overrun Mx into their stub for a clean union.
                double D = f.D, W = f.W, Mx = 0.25;
                // bottom-right (piece2 / rightStub)
                UnionBox(tr, btr, rightStub, f, xc + h - 1.5, xc + h + Mx, -D / 2.0, -D / 2.0 + 2.0, 1.0, W / 2.0);
                UnionBox(tr, btr, rightStub, f, xc + h - 1.5, xc + h + Mx, -D / 2.0, -D / 2.0 + 2.0, -W / 2.0, -1.0);
                // top-left (piece1 / leftStub)
                UnionBox(tr, btr, leftStub, f, xc - h - Mx, xc - h + 1.5, D / 2.0 - 2.0, D / 2.0, 1.0, W / 2.0);
                UnionBox(tr, btr, leftStub, f, xc - h - Mx, xc - h + 1.5, D / 2.0 - 2.0, D / 2.0, -W / 2.0, -1.0);

                // Stage C: the key band -- the middle 2" width band [-1,+1] of each abutment, drawn as the
                // ALTERNATING piece's material (per drawscarf.lsp): bottom-right -> piece1, top-left ->
                // piece2. It interlocks through the other piece's tables (integral, not a loose wedge).
                // It runs flush to the abutment face (no overrun into the mating piece) and Mx into its
                // own tongue for a clean union.
                UnionBox(tr, btr, leftStub, f, xc + h - 1.5 - Mx, xc + h, -D / 2.0, -D / 2.0 + 2.0, -1.0, 1.0);
                UnionBox(tr, btr, rightStub, f, xc - h, xc - h + 1.5 + Mx, D / 2.0 - 2.0, D / 2.0, -1.0, 1.0);

                // Drop boolean OPERATION HISTORY so the scarf pieces save cleanly (see BuildFramedSolid).
                try { leftStub.RecordHistory = false; leftStub.ShowHistory = false;
                      rightStub.RecordHistory = false; rightStub.ShowHistory = false; } catch { }

                ManagedTimber.WriteFrameXrecord(tr, leftStub, f1);
                ManagedTimber.WriteFrameXrecord(tr, rightStub, f2);

                // The key is INTEGRAL: Stage C above unions the middle width band as the alternating
                // piece's material (per drwscarf.lsp's scarfkey1/scarfkey2), so the two halves interlock
                // through each other's tables. There is no loose key wedge -- and the keyway HOOK + VOID
                // are formed by the tongue profile (BuildScarfTongue) + its 180 rotation, not by a cut.

                ((Entity)tr.GetObject(beamId, OpenMode.ForWrite)).Erase();
                tr.Commit();
            }

            // XData (outside the txn, like the other commands) + the explicit splice node.
            string sz1 = (int)Math.Round(f.W) + "x" + (int)Math.Round(f.D) + "x" + Module1.BuyLongFeet(f1.L);
            string sz2 = (int)Math.Round(f.W) + "x" + (int)Math.Round(f.D) + "x" + Module1.BuyLongFeet(f2.L);
            // Pieces inherit the parent's free-assembly origin: scarfed FREE timbers stay regen-proof;
            // scarfed SKELETON halves stay skeleton (a regenerate replaces the unsplit member).
            var xd1 = new Module1.DataStructure(type, "", desg, sz1, "0", 0, 0, 0, f.W, f.D, f1.L, "scarf", "scarf", false);
            var xd2 = new Module1.DataStructure(type, "", desg, sz2, "0", 0, 0, 0, f.W, f.D, f2.L, "scarf", "scarf", false);
            xd1.Free = od?.Free ?? "";
            xd2.Free = od?.Free ?? "";
            Module1.SetXdata(nP1, xd1);
            Module1.SetXdata(nP2, xd2);
            ManagedTimber.WriteScarfNode(db, nP1, cs);
            ManagedTimber.WriteScarfNode(db, nP2, cs);

            ed.WriteMessage("\nTScarf: l=" + l.ToString("0.#") +
                            "  piece1 overall " + f1.L.ToString("0.#") + "\" (" + nP1.Handle + ")" +
                            ",  piece2 overall " + f2.L.ToString("0.#") + "\" (" + nP2.Handle + ").");
        }
    }
}
