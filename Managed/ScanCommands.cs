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
    // ManagedCommands part: TScan, TPickFace, and the UCS presets.
    public partial class ManagedCommands
    {
        // Rescan managed timbers for coincident (mating) faces and mark the derived nodes. Nodes are
        // NOT stored -- each scan recomputes them; separate the timbers and rescan -> the node is gone.
        [CommandMethod("TScan")]
        public static void Scan()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            int count = ManagedTimber.Enumerate(db).Count;
            var nodes = ManagedTimber.ComputeNodes(db);

            DrawNodeMarkers(doc, db, nodes);
            ed.WriteMessage("\nTScan: " + count + " managed timbers, " + nodes.Count + " node(s).");
            foreach (Point3d n in nodes)
                ed.WriteMessage("\n  node @ " + n.X.ToString("0.#") + "," + n.Y.ToString("0.#") + "," + n.Z.ToString("0.#"));
        }

        // SPIKE (A0): confirm interactive face picking + the BRep read. Run TPickFace, pick a face on a
        // managed timber -> it highlights natively; prints the matched analytic face's centre + normal.
        // (Throwaway; no geometry created.)
        [CommandMethod("TPickFace")]
        public static void PickFaceSpike()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!ManagedTimber.PickFace(ed, db, "\nPick a timber FACE: ", out ObjectId id, out ManagedTimber.TFace f))
            { ed.WriteMessage("\nTPickFace: no face picked."); return; }

            ed.WriteMessage("\nTPickFace: timber " + id.Handle +
                "\n  centre " + f.C.X.ToString("0.#") + "," + f.C.Y.ToString("0.#") + "," + f.C.Z.ToString("0.#") +
                "\n  normal " + f.N.X.ToString("0.##") + "," + f.N.Y.ToString("0.##") + "," + f.N.Z.ToString("0.##") +
                "\n  extents " + f.UHalf.ToString("0.#") + " x " + f.VHalf.ToString("0.#"));
        }

        // (TMove / TRotate removed: a ManagedTransformOverrule on Solid3d now keeps each timber's stored
        // frame + scarf node in lockstep through NATIVE MOVE / ROTATE / MIRROR / ALIGN, so no special
        // commands are needed. See Managed/ManagedTransformOverrule.cs and ApplyManagedTransform above.)

        // ---- orientation presets (palette) ------------------------------------------------
        // The managed verbs read the section roll -- and, for TPlace, the extrusion direction --
        // from the current UCS. These three presets set the standard orthographic UCSs so the
        // user doesn't hand-roll the UCS per member. In the model basis the BENTS lie in the
        // world YZ plane and the WALLS in the world XZ plane, so (Robert's mapping, 2026-07-07 --
        // the two were reversed before):
        //   Plan (Top)  -> X = world +X, Y = world +Y : Z = world +Z (posts / verticals)
        //   Bent        -> X = world +Y, Y = world +Z : Z = world +X (the bent cross-plane;
        //                  TPlace extrudes along the building)
        //   Wall        -> X = world +X, Y = world +Z : Z = world -Y (the wall elevation;
        //                  TPlace extrudes across the span, into the screen)
        // TSpan / TJoin take their direction from the picked faces, so for them the UCS only sets
        // the section roll.

        [CommandMethod("TUcsPlan")]
        public static void UcsPlan()
            => SetUcs(Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis, "Plan (Top)");

        [CommandMethod("TUcsBent")]
        public static void UcsBent()
            => SetUcs(Vector3d.YAxis, Vector3d.ZAxis, Vector3d.XAxis, "Bent elevation");

        [CommandMethod("TUcsWall")]
        public static void UcsWall()
            => SetUcs(Vector3d.XAxis, Vector3d.ZAxis, Vector3d.YAxis.Negate(), "Wall elevation");

        private static void SetUcs(Vector3d x, Vector3d y, Vector3d z, string label)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            ed.CurrentUserCoordinateSystem = Matrix3d.AlignCoordinateSystem(
                Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                Point3d.Origin, x, y, z);
            ed.WriteMessage("\nUCS: " + label + ".");
        }
    }
}
