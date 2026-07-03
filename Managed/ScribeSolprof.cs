using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TimberDraw
{
    // The TScribe projection engine: AutoCAD's own SOLPROF hidden-line command, one profile per
    // timber face. Ported from TimberTag's proven scribe extraction (UserControl1.cs) with two
    // upgrades: TScribe runs inside a [CommandMethod], so SOLPROF executes SYNCHRONOUSLY via
    // Editor.Command (no SendStringToExecute/CommandEnded state machine), and the run cleans up
    // after itself (TimberTag leaked the temp layout, PV/PH layers, and exploded geometry).
    //
    // SOLPROF requirements (the recipe):
    //  - must run in MODEL space through a PAPER-SPACE viewport: temp layout + its floating
    //    viewport, CVPORT set to the viewport number;
    //  - the viewport must LOOK at the face: UCSFOLLOW=1 + set the face UCS + REGEN re-plans the
    //    view down the UCS Z axis;
    //  - output lands in model space as BlockReferences on layers PV-{vpHandle} (visible) and
    //    PH-{vpHandle} (hidden). Same viewport all run -> same layer names: inserts are erased
    //    after EVERY face so nothing bleeds into the next collection.
    //
    // UCS per face: origin at the face's (u=0, v=0) corner, X = A (station axis), Z = N (outward
    // normal), Y = N x A = -V. Face coords are then exactly (x_ucs, -y_ucs) -- the same FaceFrame
    // coordinates ScribeAnnotate uses, so burned text lands with the linework by construction.
    internal static class ScribeSolprof
    {
        private const string TempLayout = "TS TEMP";   // layout names must be colon-free

        internal sealed class State
        {
            public string PrevLayout;
            public Matrix3d PrevUcs;
            public object PrevUcsFollow;
            public HashSet<string> ProfileLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        // Save the session state and stand up the temp layout + viewport SOLPROF needs.
        public static State Prepare(Document doc)
        {
            Editor ed = doc.Editor;
            var st = new State
            {
                PrevLayout = LayoutManager.Current.CurrentLayout,
                PrevUcs = ed.CurrentUserCoordinateSystem,
                PrevUcsFollow = AcApp.GetSystemVariable("UCSFOLLOW")
            };

            LayoutManager lm = LayoutManager.Current;
            if (lm.LayoutExists(TempLayout))
            {
                if (lm.CurrentLayout == TempLayout) lm.CurrentLayout = st.PrevLayout;
                lm.DeleteLayout(TempLayout);
            }
            ObjectId layoutId = lm.CreateLayout(TempLayout);
            lm.CurrentLayout = TempLayout;
            ed.SwitchToPaperSpace();

            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                var layout = (Layout)tr.GetObject(layoutId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is Viewport vp && vp.Number != 1)
                    {
                        ed.SwitchToModelSpace();
                        AcApp.SetSystemVariable("CVPORT", vp.Number);
                        break;
                    }
                }
                tr.Commit();
            }

            AcApp.SetSystemVariable("UCSFOLLOW", 1);
            return st;
        }

        // Profile one timber face. Returns the face's marks: SOLPROF visible layer -> burn path
        // (minus the stick's own outline), hidden layer -> preview-only. Empty list on failure.
        public static List<ScribeFaces.Mark> ProfileFace(Document doc, ObjectId solidId,
                                                         ScribeFaces.FaceFrame ff, State st)
        {
            var marks = new List<ScribeFaces.Mark>();
            Editor ed = doc.Editor;

            // face UCS: origin at (u=0, v=0) on the face plane, X = A, Z = N, Y = N x A = -V
            Point3d p00 = ff.C + ff.A * (ff.UMin - ff.C.GetAsVector().DotProduct(ff.A))
                               - ff.V * ff.HalfV;
            Vector3d uy = ff.N.CrossProduct(ff.A);
            Matrix3d ucs = Matrix3d.AlignCoordinateSystem(
                Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                p00, ff.A, uy, ff.N);

            try
            {
                ed.CurrentUserCoordinateSystem = ucs;
                ed.Command("_.REGEN");                       // UCSFOLLOW re-plans the viewport
                Handle vpHandle = ed.CurrentViewportObjectId.Handle;
                st.ProfileLayers.Add("PV-" + vpHandle);
                st.ProfileLayers.Add("PH-" + vpHandle);

                ed.SetImpliedSelection(new[] { solidId });
                // Previous selection, end selection, then Y Y Y: hidden-lines-on-separate-layer,
                // project onto a plane, delete tangential edges (TimberTag's "p  y y y").
                ed.Command("_.SOLPROF", "_P", "", "_Y", "_Y", "_Y");

                // Only the VISIBLE profile (PV) is collected -- the framer doesn't want hidden lines.
                // The PH layer still gets registered above so Cleanup purges it.
                Collect(doc, "PV-" + vpHandle, ucs, ff, true, marks);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n  SOLPROF face {ff.Number} failed: {ex.Message}");
            }
            return marks;
        }

        // Gather the profile block inserts on `layer`, explode IN MEMORY (children never enter the
        // DB), convert each entity to a face-coordinate mark, and erase the inserts.
        private static void Collect(Document doc, string layer, Matrix3d ucs,
                                    ScribeFaces.FaceFrame ff, bool visible,
                                    List<ScribeFaces.Mark> marks)
        {
            Matrix3d w2u = ucs.Inverse();
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                // collect first, then explode/erase -- never mutate the BTR mid-enumeration
                var inserts = new List<ObjectId>();
                foreach (ObjectId entId in ms)
                {
                    if (tr.GetObject(entId, OpenMode.ForRead) is BlockReference br &&
                        string.Equals(br.Layer, layer, StringComparison.OrdinalIgnoreCase))
                        inserts.Add(entId);
                }

                foreach (ObjectId brId in inserts)
                {
                    var br = (BlockReference)tr.GetObject(brId, OpenMode.ForWrite);
                    var exploded = new DBObjectCollection();
                    try { br.Explode(exploded); } catch { }
                    foreach (DBObject obj in exploded)
                    {
                        using (obj)
                        {
                            if (!(obj is Entity ent)) continue;
                            try
                            {
                                ent.TransformBy(w2u);
                                Convert(ent, ff, visible, marks);
                            }
                            catch { }
                        }
                    }
                    br.Erase();
                }
                tr.Commit();
            }
        }

        // UCS entity -> face-coordinate mark. Face coords = (x, -y): the UCS Y axis is -V, and the
        // mirror flips arc orientation, so a CCW arc [s, e] becomes CCW [-e, -s].
        private static void Convert(Entity ent, ScribeFaces.FaceFrame ff, bool visible,
                                    List<ScribeFaces.Mark> marks)
        {
            switch (ent)
            {
                case Line ln:
                {
                    var a = new Point2d(ln.StartPoint.X, -ln.StartPoint.Y);
                    var b = new Point2d(ln.EndPoint.X, -ln.EndPoint.Y);
                    if (a.GetDistanceTo(b) < 1e-6) return;
                    // A long arris is the stock's own uncut edge -> gray BOUNDARY (not burned). It gives
                    // the framer the piece outline for context. Everything else (end cuts, joinery,
                    // oblique cut edges) is a CUT -> burned + drawn black.
                    bool boundary = visible && ScribeFaces.IsLongArris(a, b, ff.FaceW);
                    marks.Add(new ScribeFaces.Mark
                    {
                        Kind = ScribeFaces.MarkKind.Line,
                        Visible = visible && !boundary,
                        Boundary = boundary,
                        Pts = new List<Point2d> { a, b }
                    });
                    return;
                }
                case Circle c:
                    marks.Add(new ScribeFaces.Mark
                    {
                        Kind = ScribeFaces.MarkKind.Circle, Visible = visible,
                        Center = new Point2d(c.Center.X, -c.Center.Y), R = c.Radius
                    });
                    return;
                case Arc arc:
                {
                    double sDeg = arc.StartAngle * 180.0 / Math.PI;
                    double eDeg = arc.EndAngle * 180.0 / Math.PI;
                    marks.Add(new ScribeFaces.Mark
                    {
                        Kind = ScribeFaces.MarkKind.Arc, Visible = visible,
                        Center = new Point2d(arc.Center.X, -arc.Center.Y), R = arc.Radius,
                        StartDeg = Norm360(-eDeg), EndDeg = Norm360(-sDeg)
                    });
                    return;
                }
                case Curve cv:
                {
                    // anything else (rare polyline/spline) -> sampled poly
                    var pts = new List<Point2d>();
                    try
                    {
                        double t0 = cv.StartParam, t1 = cv.EndParam;
                        const int n = 24;
                        for (int k = 0; k <= n; k++)
                        {
                            Point3d p = cv.GetPointAtParameter(t0 + (t1 - t0) * k / n);
                            pts.Add(new Point2d(p.X, -p.Y));
                        }
                    }
                    catch { return; }
                    marks.Add(new ScribeFaces.Mark
                    {
                        Kind = ScribeFaces.MarkKind.Poly, Visible = visible, Pts = pts
                    });
                    return;
                }
            }
        }

        private static double Norm360(double deg)
        {
            deg %= 360.0;
            return deg < 0 ? deg + 360.0 : deg;
        }

        // Tear everything down: leftover profile geometry, the PV/PH layers, the temp layout, and
        // the saved UCS / UCSFOLLOW / layout. Best-effort throughout -- runs in a finally.
        public static void Cleanup(Document doc, State st)
        {
            Editor ed = doc.Editor;
            Database db = doc.Database;

            // leftover inserts/entities on the profile layers (only present after a mid-run error)
            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                    foreach (ObjectId id in ms)
                    {
                        if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
                        if (!st.ProfileLayers.Contains(ent.Layer)) continue;
                        ent.UpgradeOpen();
                        ent.Erase();
                    }
                    tr.Commit();
                }
            }
            catch { }

            // the PV/PH layers themselves
            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    foreach (string name in st.ProfileLayers)
                    {
                        if (!lt.Has(name)) continue;
                        try
                        {
                            var ltr = (LayerTableRecord)tr.GetObject(lt[name], OpenMode.ForWrite);
                            ltr.Erase();
                        }
                        catch { }
                    }
                    tr.Commit();
                }
            }
            catch { }

            // anonymous profile block definitions SOLPROF minted (best-effort purge)
            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var candidates = new ObjectIdCollection();
                    foreach (ObjectId id in bt)
                    {
                        var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                        if (btr.IsAnonymous) candidates.Add(id);
                    }
                    db.Purge(candidates);
                    foreach (ObjectId id in candidates)
                    {
                        var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForWrite);
                        btr.Erase();
                    }
                    tr.Commit();
                }
            }
            catch { }

            // restore the session
            try { AcApp.SetSystemVariable("UCSFOLLOW", System.Convert.ToInt16(st.PrevUcsFollow)); } catch { }
            try { ed.CurrentUserCoordinateSystem = st.PrevUcs; } catch { }
            try
            {
                LayoutManager lm = LayoutManager.Current;
                if (!string.IsNullOrEmpty(st.PrevLayout) && lm.LayoutExists(st.PrevLayout))
                    lm.CurrentLayout = st.PrevLayout;
                if (lm.LayoutExists(TempLayout)) lm.DeleteLayout(TempLayout);
            }
            catch { }
        }
    }
}
