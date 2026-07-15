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
    // ManagedCommands part: TCopy -- copy managed timbers as NEW sticks (address freed, joints re-keyed).
    public partial class ManagedCommands
    {
        // Copy managed timbers the managed way. Plain COPY deep-clones IDENTITY: the copy arrives
        // claiming the original's grid address, its production number (duplicate cut-marks in the
        // BOM), and its joint ids (a THIRD timber on a pairwise id -- confuses re-cut and sync).
        // TCopy instead makes every copy a NEW stick:
        //   - grid assignment CLEARED (frame / bent / wall / bay / floor tags + label) -- TAssign
        //     re-addresses it; it lands on the CURRENT layer like a fresh TPlace;
        //   - production number fresh (minted like any new timber's);
        //   - Free marker set (a regenerate never erases it);
        //   - SHAPE kept (miters, scarfed ends, TProfile arches);
        //   - JOINERY kept but RE-KEYED: every joint id re-mints CONSISTENTLY ACROSS THE PLACEMENT,
        //     so a copied jointed pair keeps its mutual joint intact and paired, while a joint to a
        //     timber left behind becomes an orphan half under a fresh id -- exactly what TJointSync
        //     re-attaches at the new location (TJointDel / the pane's Clear drops it).
        // Base point + repeated destinations, like COPY; Enter finishes.
        [CommandMethod("TCopy")]
        public static void CopyTimbers()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "3DSOLID") });
            var pso = new PromptSelectionOptions { MessageForAdding = "\nSelect timbers to copy: " };
            PromptSelectionResult sel = ed.GetSelection(pso, filter);
            if (sel.Status != PromptStatus.OK) return;

            // Capture each source ONCE (frame + the identity a copy keeps: type/designation/end cuts
            // + its persisted joint recipes). Only managed timbers copy.
            var sources = new List<(ManagedTimber.TFrame F, string Type, string Desig, string Near, string Far,
                                    Dictionary<int, string> Specs)>();
            int skipped = 0;
            foreach (ObjectId id in sel.Value.GetObjectIds())
            {
                if (id.IsNull || id.IsErased || !ManagedTimber.TryReadFrame(db, id, out ManagedTimber.TFrame f))
                { skipped++; continue; }
                Module1.DataStructure xd = Module1.GetXdata(id);
                sources.Add((f,
                    string.IsNullOrEmpty(xd?.Type) ? "Timber" : xd.Type,
                    xd?.Designation ?? "",
                    string.IsNullOrEmpty(xd?.JointNear) ? "Butt" : xd.JointNear,
                    string.IsNullOrEmpty(xd?.JointFar) ? "Butt" : xd.JointFar,
                    ManagedTimber.ReadJointSpecs(id)));
            }
            if (sources.Count == 0) { ed.WriteMessage("\nNo managed timbers in the selection."); return; }
            if (skipped > 0) ed.WriteMessage("\n" + skipped + " non-managed solid(s) skipped (TAdopt them first).");

            PromptPointResult bp = ed.GetPoint("\nBase point: ");
            if (bp.Status != PromptStatus.OK) return;
            Matrix3d ucs = ed.CurrentUserCoordinateSystem;
            Point3d baseWcs = bp.Value.TransformBy(ucs);

            int placements = 0, made = 0, rekeyed = 0;
            for (; ; )
            {
                var ppo = new PromptPointOptions("\nSecond point (Enter to finish): ")
                { UseBasePoint = true, BasePoint = baseWcs, AllowNone = true };
                PromptPointResult pr = ed.GetPoint(ppo);
                if (pr.Status == PromptStatus.None) break;        // Enter: done
                if (pr.Status != PromptStatus.OK) break;          // Esc: done
                Vector3d delta = pr.Value.TransformBy(ucs) - baseWcs;
                Matrix3d move = Matrix3d.Displacement(delta);

                // One id map PER PLACEMENT: a joint shared by two copied timbers keeps ONE fresh id
                // on both halves; the previous placement's copies are already in the DB, so the next
                // NextJointId seed never collides.
                var map = new Dictionary<int, int>();
                int next = NextJointId(db);
                foreach ((ManagedTimber.TFrame F, string Type, string Desig, string Near, string Far,
                          Dictionary<int, string> Specs) s in sources)
                {
                    ManagedTimber.TFrame nf = ManagedTimber.TransformFrame(s.F, move);
                    nf = RekeyJoints(nf, map, ref next);
                    ObjectId nid = ManagedTimber.DrawFramedSolid(nf, s.Type, s.Desig, s.Near, s.Far);
                    if (nid.IsNull) continue;
                    Module1.DataStructure nd = Module1.GetXdata(nid);
                    if (nd != null) { nd.Free = "1"; Module1.SetXdata(nid, nd); }   // hand-placed: survives regen
                    foreach (KeyValuePair<int, string> kv in s.Specs)
                        if (kv.Key != 0 && map.TryGetValue(kv.Key, out int nk))
                            ManagedTimber.WriteJointSpec(nid, nk, kv.Value);
                    made++;
                }
                rekeyed += map.Count;
                placements++;
            }

            // Copied braces join their size+shape groups immediately (symbols are model-wide).
            if (made > 0 && sources.Exists(s => string.Equals(s.Type, "Brace", StringComparison.OrdinalIgnoreCase)))
                RelabelBraces(db);

            ed.WriteMessage("\nTCopy: " + made + " new timber(s) across " + placements + " placement(s)"
                + (rekeyed > 0 ? ", " + rekeyed + " joint id(s) re-keyed" : "")
                + " -- unassigned + free. TAssign to address; TJointSync to re-attach joints at the new location.");
        }

        // Rebuild the five joinery lists with FRESH joint ids (0 = legacy unkeyed stays 0). The map is
        // shared across one placement so a joint spanning two copied timbers stays paired.
        private static ManagedTimber.TFrame RekeyJoints(ManagedTimber.TFrame f, Dictionary<int, int> map, ref int next)
        {
            int n = next;
            int Key(int j)
            {
                if (j == 0) return 0;
                if (!map.TryGetValue(j, out int k)) { k = n++; map[j] = k; }
                return k;
            }
            if (f.Features != null)
            {
                var l = new List<(Point3d, Point3d, bool, int)>(f.Features.Count);
                foreach ((Point3d Min, Point3d Max, bool Subtract, int Joint) x in f.Features)
                    l.Add((x.Min, x.Max, x.Subtract, Key(x.Joint)));
                f.Features = l;
            }
            if (f.Pegs != null)
            {
                var l = new List<(Point3d, Vector3d, double, double, int)>(f.Pegs.Count);
                foreach ((Point3d C, Vector3d Axis, double R, double Half, int Joint) x in f.Pegs)
                    l.Add((x.C, x.Axis, x.R, x.Half, Key(x.Joint)));
                f.Pegs = l;
            }
            if (f.JointPolys != null)
            {
                var l = new List<(Point3d[], int, bool, double, double)>(f.JointPolys.Count);
                foreach ((Point3d[] Poly, int Joint, bool Subtract, double Xlo, double Xhi) x in f.JointPolys)
                    l.Add((x.Poly, Key(x.Joint), x.Subtract, x.Xlo, x.Xhi));
                f.JointPolys = l;
            }
            if (f.JointPolysZ != null)
            {
                var l = new List<(Point3d[], int, bool, double, double)>(f.JointPolysZ.Count);
                foreach ((Point3d[] Poly, int Joint, bool Subtract, double Xlo, double Xhi) x in f.JointPolysZ)
                    l.Add((x.Poly, Key(x.Joint), x.Subtract, x.Xlo, x.Xhi));
                f.JointPolysZ = l;
            }
            if (f.JointPrisms != null)
            {
                var l = new List<(Point3d[], Vector3d, int, bool)>(f.JointPrisms.Count);
                foreach ((Point3d[] Poly, Vector3d Extrude, int Joint, bool Subtract) x in f.JointPrisms)
                    l.Add((x.Poly, x.Extrude, Key(x.Joint), x.Subtract));
                f.JointPrisms = l;
            }
            next = n;
            return f;
        }
    }
}
