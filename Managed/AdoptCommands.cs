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
    // ManagedCommands part: TAdopt -- convert user-modeled 3DSOLIDs into managed timbers, in place.
    public partial class ManagedCommands
    {
        // Adopt user-generated solids into the managed world: measure each solid's timber axes and
        // stock extents, then REPLACE it with a real managed timber of that W x D x L at the same
        // position and orientation (Free marker stamped, layer preserved). From then on it is
        // indistinguishable from a TPlace'd stick -- assign, joinery, BOM, shop maps, scribe.
        // The body must be BOX-LIKE: a solid filling less than 90% of its stock box (an arched
        // timber, hardware) is LEFT AS-IS and reported, so a shaped body is never silently replaced
        // by its bounding stock. Already-managed solids are skipped. UNDO restores the originals.
        [CommandMethod("TAdopt")]
        public static void AdoptSolids()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "3DSOLID") });
            var pso = new PromptSelectionOptions { MessageForAdding = "\nSelect solids to adopt as managed timbers: " };
            PromptSelectionResult sel = ed.GetSelection(pso, filter);
            if (sel.Status != PromptStatus.OK) return;

            string type = GetType(ed);   // one type for the run (default "Timber"); re-type later via TSection
            if (type == null) return;

            // Measure first (reads only), then replace -- a failed measurement never costs the original.
            var adopts = new List<(ObjectId Id, string Layer, Point3d O, Vector3d X, Vector3d Y, Vector3d Z,
                                   double W, double D, double L, double Fill)>();
            int skippedManaged = 0, notBox = 0, failed = 0;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in sel.Value.GetObjectIds())
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Solid3d solid)) continue;
                    if (ManagedTimber.TryReadFrame(db, id, out _)) { skippedManaged++; continue; }   // already managed
                    try
                    {
                        if (!MeasureStockBox(solid, out Point3d o, out Vector3d ax, out Vector3d ay, out Vector3d az,
                                             out double w, out double d, out double l, out double fill))
                        { failed++; continue; }
                        if (fill < 0.90)
                        {
                            notBox++;
                            ed.WriteMessage("\n  " + solid.Handle + ": not box-like (body fills "
                                + (fill * 100.0).ToString("0") + "% of its "
                                + w.ToString("0.###") + "x" + d.ToString("0.###") + " stock) -- left as-is."
                                + " Shaped timbers keep their body; cut the shape INTO a managed timber instead (TProfile).");
                            continue;
                        }
                        adopts.Add((id, solid.Layer, o, ax, ay, az, w, d, l, fill));
                    }
                    catch (System.Exception ex)
                    {
                        failed++;
                        Diag.Warn("TAdopt/Measure", solid.Handle + ": " + ex.Message);
                    }
                }
                tr.Commit();
            }

            // Replace: draw the managed box first, then erase the original (a failed draw costs nothing).
            int adopted = 0;
            foreach (var a in adopts)
            {
                ObjectId nid = ManagedTimber.DrawBox(a.O, a.Z, a.Y, a.X, a.L, a.D, a.W, type, "", "Butt", "Butt");
                if (nid.IsNull) { failed++; continue; }
                if (!string.IsNullOrEmpty(a.Layer))
                {
                    using (doc.LockDocument())
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        if (tr.GetObject(nid, OpenMode.ForWrite) is Entity ne) ne.Layer = a.Layer;
                        tr.Commit();
                    }
                }
                Module1.EraseEntity(a.Id);
                adopted++;
                ed.WriteMessage("\n  adopted " + a.W.ToString("0.###") + "x" + a.D.ToString("0.###") + "x"
                    + a.L.ToString("0.#") + (a.Fill < 0.999 ? " (body filled " + (a.Fill * 100.0).ToString("0") + "% of the stock)" : ""));
            }

            ed.WriteMessage("\nTAdopt: " + adopted + " solid(s) adopted as managed " + type + " timbers"
                + (skippedManaged > 0 ? ", " + skippedManaged + " already managed" : "")
                + (notBox > 0 ? ", " + notBox + " not box-like (left as-is)" : "")
                + (failed > 0 ? ", " + failed + " failed" : "")
                + (adopted > 0 ? " -- TAssign them to address, TJointAll / the Joints pane to cut." : "."));
        }

        // Measure a foreign solid's TIMBER frame from its STRAIGHT Brep edges (the shop-map idiom):
        // the longest straight edge runs the LENGTH; the longest straight edge perpendicular to it
        // runs the section. Exact on a box -- including square sections and mitered ends -- and
        // curved edges (an arch) simply don't vote. Returns the DrawBox frame: O = near-end section
        // center, X = width, Y = depth (up-ish), Z = length, right-handed.
        private static bool MeasureStockBox(Solid3d solid, out Point3d o, out Vector3d ax, out Vector3d ay,
            out Vector3d az, out double w, out double d, out double l, out double fill)
        {
            o = Point3d.Origin; ax = Vector3d.XAxis; ay = Vector3d.YAxis; az = Vector3d.ZAxis;
            w = d = l = fill = 0;

            var edges = new List<(Vector3d Dir, double Len)>();
            using (var brep = new Autodesk.AutoCAD.BoundaryRepresentation.Brep(solid))
            {
                foreach (Autodesk.AutoCAD.BoundaryRepresentation.Edge edge in brep.Edges)
                {
                    try
                    {
                        Curve3d gc = edge.Curve;
                        Interval iv = gc.GetInterval();
                        Point3d a = gc.EvaluatePoint(iv.LowerBound), b = gc.EvaluatePoint(iv.UpperBound);
                        Point3d mid = gc.EvaluatePoint((iv.LowerBound + iv.UpperBound) / 2.0);
                        var lin = new Point3d((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0, (a.Z + b.Z) / 2.0);
                        if (mid.DistanceTo(lin) > 1e-6) continue;   // curved: no vote
                        double len = a.DistanceTo(b);
                        if (len > 1e-6) edges.Add(((b - a).GetNormal(), len));
                    }
                    catch { }   // a hiccup edge is skipped, like the shop maps do
                }
            }

            Vector3d zAxis = default, sa = default;
            double zLen = 0, aLen = 0;
            foreach ((Vector3d Dir, double Len) e in edges)
                if (e.Len > zLen) { zLen = e.Len; zAxis = e.Dir; }
            if (zLen <= 1e-6) return false;   // no straight edge at all (a fully curved body)
            foreach ((Vector3d Dir, double Len) e in edges)
                if (System.Math.Abs(e.Dir.DotProduct(zAxis)) < 0.05 && e.Len > aLen) { aLen = e.Len; sa = e.Dir; }
            if (aLen <= 1e-6) return false;   // nothing perpendicular to seat a section on
            sa = (sa - zAxis * sa.DotProduct(zAxis)).GetNormal();   // exact perpendicular
            Vector3d sb = zAxis.CrossProduct(sa);

            // Measure along the axes; classify depth = the more-vertical section axis.
            Solid3dMassProperties mp = solid.MassProperties;
            Point3d c0 = mp.Centroid;
            ExtentsIn(solid, c0, sa, sb, zAxis, out double[] lo, out double[] hi);
            double da = hi[0] - lo[0], db2 = hi[1] - lo[1];
            l = hi[2] - lo[2];
            Point3d center = c0 + sa * ((lo[0] + hi[0]) / 2.0) + sb * ((lo[1] + hi[1]) / 2.0) + zAxis * ((lo[2] + hi[2]) / 2.0);
            bool aVert = Math.Abs(sa.DotProduct(Vector3d.ZAxis)) >= Math.Abs(sb.DotProduct(Vector3d.ZAxis));
            Vector3d yAxis = aVert ? sa : sb;
            d = aVert ? da : db2;
            w = aVert ? db2 : da;
            if (yAxis.DotProduct(Vector3d.ZAxis) < 0) yAxis = -yAxis;   // depth reads upward when it can

            az = zAxis;
            ay = yAxis;
            ax = ay.CrossProduct(az);   // right-handed: X x Y = Z
            o = center - az * (l / 2.0);   // DrawBox origin = near-end section center
            if (w <= 1e-6 || d <= 1e-6 || l <= 1e-6) return false;
            fill = mp.Volume / (w * d * l);
            return true;
        }

        // A solid's AABB extents in an arbitrary orthonormal frame at basePt: transform a CLONE into
        // that frame and read its bounds (no Brep walk needed).
        private static void ExtentsIn(Solid3d solid, Point3d basePt, Vector3d a, Vector3d b, Vector3d c,
            out double[] lo, out double[] hi)
        {
            using (var clone = (Solid3d)solid.Clone())
            {
                clone.TransformBy(Matrix3d.AlignCoordinateSystem(
                    basePt, a, b, c, Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis));
                Extents3d e = clone.GeometricExtents;
                lo = new[] { e.MinPoint.X, e.MinPoint.Y, e.MinPoint.Z };
                hi = new[] { e.MaxPoint.X, e.MaxPoint.Y, e.MaxPoint.Z };
            }
        }
    }
}
