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
    // ManagedCommands part: TProfile -- cut a drawn profile curve through a managed timber.
    public partial class ManagedCommands
    {
        // Cut a CLOSED drawn curve (polyline / circle / spline) through a managed timber, straight
        // through its width -- the arched-timber verb: draw the arch profile on the timber's
        // elevation, TProfile it, and the shape is CARRIED IN THE TIMBER'S OWN RECIPE (a Subtracts
        // polygon), so it survives moves and re-cuts, reads in the shop maps, scribes (TScribe draws
        // the real cut edges), and takes joinery like any other stick (joints cut on the nominal
        // stock faces). The curve is faceted (arcs ~2 degree chords), projected onto the elevation
        // plane (length x depth), and subtracted. A shape cut is body work like TScarf -- there is
        // no ...Del; UNDO restores, or re-place the timber. The drawn curve is left in the drawing.
        [CommandMethod("TProfile")]
        public static void ProfileCut()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the timber to profile-cut: ", out ObjectId tid, out ManagedTimber.TFrame f)) return;

            var peo = new PromptEntityOptions("\nSelect the CLOSED profile curve (polyline / circle / spline): ");
            peo.SetRejectMessage("\nThat is not a curve.");
            peo.AddAllowedClass(typeof(Curve), false);
            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            List<Point3d> world;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var cv = tr.GetObject(per.ObjectId, OpenMode.ForRead) as Curve;
                if (cv == null || !cv.Closed)
                { ed.WriteMessage("\nThe profile must be a CLOSED curve (close the polyline / use a circle)."); return; }
                world = SampleClosedCurve(cv);
                tr.Commit();
            }
            if (world == null || world.Count < 3) { ed.WriteMessage("\nCould not sample that curve."); return; }

            // Project onto the timber's ELEVATION plane (length u along f.Z, depth v along f.Y, both
            // from the near-end section center O) -- the width component is dropped, so the cut runs
            // straight through. Warn when the curve plane is visibly skewed to that plane.
            Vector3d n = NewellNormal(world);
            if (n.Length > 1e-9 && System.Math.Abs(n.GetNormal().DotProduct(f.X)) < 0.7)
                ed.WriteMessage("\nNote: the profile plane is skewed to the timber's elevation -- it is projected straight through the width.");

            var poly = new List<Point3d>(world.Count);
            double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
            foreach (Point3d p in world)
            {
                Vector3d rel = p - f.O;
                double u = rel.DotProduct(f.Z), v = rel.DotProduct(f.Y);
                if (poly.Count > 0)
                {
                    Point3d last = poly[poly.Count - 1];
                    if (System.Math.Abs(last.X - u) < 1e-6 && System.Math.Abs(last.Y - v) < 1e-6) continue;
                }
                poly.Add(new Point3d(u, v, 0.0));
                if (u < minU) minU = u; if (u > maxU) maxU = u;
                if (v < minV) minV = v; if (v > maxV) maxV = v;
            }
            // A closed curve's last sample can duplicate the first; the builder closes the loop itself.
            if (poly.Count > 1)
            {
                Point3d a = poly[0], z = poly[poly.Count - 1];
                if (System.Math.Abs(a.X - z.X) < 1e-6 && System.Math.Abs(a.Y - z.Y) < 1e-6) poly.RemoveAt(poly.Count - 1);
            }
            if (poly.Count < 3) { ed.WriteMessage("\nThe profile collapses on the elevation plane."); return; }
            if (maxU < 0 || minU > f.L || maxV < -f.D / 2.0 || minV > f.D / 2.0)
            { ed.WriteMessage("\nThe profile does not touch the timber's elevation -- nothing to cut."); return; }

            if (f.Subtracts == null) f.Subtracts = new List<Point3d[]>();
            f.Subtracts.Add(poly.ToArray());
            ObjectId nid = ManagedTimber.RebuildFromFrame(tid, f);
            ed.WriteMessage(nid.IsNull
                ? "\nTProfile: rebuild failed -- nothing changed."
                : "\nTProfile: profile cut through the width (" + poly.Count + " points, carried in the timber's recipe)."
                  + " The drawn curve is left in the drawing; UNDO restores the timber.");
        }

        // Facet a closed curve into WCS points: a polyline walks its own vertices (straight segments
        // exact, bulge arcs at ~2 degree chords); any other closed curve (circle / ellipse / spline)
        // samples uniformly by parameter. Null on a geometry hiccup.
        private static List<Point3d> SampleClosedCurve(Curve cv)
        {
            try
            {
                var pts = new List<Point3d>();
                if (cv is Autodesk.AutoCAD.DatabaseServices.Polyline pl)
                {
                    int nv = pl.NumberOfVertices;
                    for (int i = 0; i < nv; i++)
                    {
                        pts.Add(pl.GetPoint3dAt(i));
                        double bulge = pl.GetBulgeAt(i);
                        if (System.Math.Abs(bulge) > 1e-9)
                        {
                            double sweep = System.Math.Abs(4.0 * System.Math.Atan(bulge));   // included arc angle
                            int segs = System.Math.Max(4, (int)System.Math.Ceiling(sweep / (System.Math.PI / 90.0)));
                            for (int k = 1; k < segs; k++)
                                pts.Add(pl.GetPointAtParameter(i + (double)k / segs));
                        }
                    }
                }
                else
                {
                    const int N = 96;
                    double p0 = cv.StartParam, p1 = cv.EndParam;
                    for (int k = 0; k < N; k++)
                        pts.Add(cv.GetPointAtParameter(p0 + (p1 - p0) * k / N));
                }
                return pts;
            }
            catch { return null; }
        }

        // Newell's method: a robust polygon normal from ordered vertices (planar or nearly so).
        private static Vector3d NewellNormal(List<Point3d> pts)
        {
            double nx = 0, ny = 0, nz = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                Point3d a = pts[i], b = pts[(i + 1) % pts.Count];
                nx += (a.Y - b.Y) * (a.Z + b.Z);
                ny += (a.Z - b.Z) * (a.X + b.X);
                nz += (a.X - b.X) * (a.Y + b.Y);
            }
            return new Vector3d(nx, ny, nz);
        }
    }
}
