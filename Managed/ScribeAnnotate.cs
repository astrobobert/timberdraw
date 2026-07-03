using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using BRep = Autodesk.AutoCAD.BoundaryRepresentation;

namespace TimberDraw
{
    // Burned dimension text as a SURFACE MODEL (user model, 2026-07-02). Looking straight at a
    // reference (RS) face, every surface of the solid that is VISIBLE from that viewpoint gets ONE
    // label, centered in the surface's projected area:
    //   - a surface TILTED relative to the RS face -> its bevel ANGLE (deg, F2);
    //   - a FLAT surface (parallel to RS) sitting BELOW the RS face -> its DEPTH of cut (fraction");
    //   - the RS face itself (depth 0), edge-on (perpendicular), and back-facing surfaces -> nothing.
    //
    // The surfaces are the solid's real Brep faces (not the TFrame feature lists), so a tenon's
    // shoulder is ONE face -> ONE label, not one-per-polygon-edge. SOLPROF still draws the burn
    // LINEWORK; this only produces the text laid over it.
    public static class ScribeAnnotate
    {
        private const double RecessTol  = 1.0 / 32.0;  // min depth below the face worth printing (in)
        private const double FlatTolDeg = 0.75;        // within this of parallel = flat; of perp = edge-on
        private const double CoplanarTol = 0.03;       // planarity test for a Brep face (in)
        private const double DimTextH   = 0.75;        // label height (in)
        private const double MinTextH   = 0.30;
        private const double ThinMin    = 0.25;        // skip a surface whose projected area is thinner

        // A planar boundary face of the solid, in WCS: outward normal, centroid, boundary vertices,
        // and per-RS visibility (Vis[k] = visible looking down RS face k's normal: front-facing AND
        // not buried behind wood).
        public struct SolidFace
        {
            public Vector3d NOut;
            public Point3d Centroid;
            public Point3d[] Bpts;
            public bool[] Vis;
        }

        private sealed class Ann
        {
            public string Text; public Point2d At; public double H; public double W;
        }

        // ---- Brep face extraction (once per timber) ------------------------------------------------

        // The solid's planar boundary faces + their per-RS visibility. One short read transaction;
        // safe to call before SOLPROF. `rsN` are the 4 RS face normals (visibility is judged per RS).
        // Empty on any Brep hiccup -> no dimension text (linework + burn set are independent).
        public static List<SolidFace> BuildSolidFaces(Database db, ObjectId id, Vector3d[] rsN)
        {
            var result = new List<SolidFace>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is Solid3d sol)) { tr.Commit(); return result; }
                try
                {
                    using (var brep = new BRep.Brep(sol))
                    {
                        // pass 1: gather planar faces (raw normal, centroid, boundary)
                        var raw = new List<(Vector3d n, Point3d c, Point3d[] b)>();
                        foreach (BRep.Face face in brep.Faces)
                        {
                            List<Point3d> verts = FaceVertices(face, out bool curved);
                            if (curved || verts.Count < 3) continue;                 // bore disc/wall, etc.
                            if (!PlaneFit(verts, out Vector3d n, out Point3d c)) continue;
                            raw.Add((n, c, verts.ToArray()));
                        }
                        if (raw.Count == 0) { tr.Commit(); return result; }

                        // solid center = mean of face centroids -> orient every normal OUTWARD (away
                        // from center). Robust vs GetPointContainment, which mis-oriented some far
                        // faces and made them read as front-facing at mid-span.
                        double cx = 0, cy = 0, cz = 0;
                        foreach (var f in raw) { cx += f.c.X; cy += f.c.Y; cz += f.c.Z; }
                        Point3d ctr = new Point3d(cx / raw.Count, cy / raw.Count, cz / raw.Count);

                        // pass 2: orient + per-RS visibility (front-facing AND not buried)
                        foreach (var f in raw)
                        {
                            Vector3d n = (f.c - ctr).DotProduct(f.n) >= 0 ? f.n : f.n.Negate();
                            var vis = new bool[rsN.Length];
                            for (int k = 0; k < rsN.Length; k++)
                                vis[k] = n.DotProduct(rsN[k]) > 1e-4 && !Occluded(brep, f.c, rsN[k]);
                            result.Add(new SolidFace { NOut = n, Centroid = f.c, Bpts = f.b, Vis = vis });
                        }
                    }
                }
                catch { }
                tr.Commit();
            }
            return result;
        }

        // Is the surface at `c` hidden behind wood when viewed down +viewN? Step just off the surface
        // toward the viewer; if that lands INSIDE the solid, something is in front -> occluded. (A
        // mortise wall that faces the viewer but sits behind the intact face fails here; a pocket
        // bottom that opens toward the viewer passes.)
        private static bool Occluded(BRep.Brep brep, Point3d c, Vector3d viewN)
        {
            try
            {
                Point3d probe = c + viewN.GetNormal() * 0.05;
                BRep.PointContainment pc;
                using (var e = brep.GetPointContainment(probe, out pc))
                    return pc == BRep.PointContainment.Inside;
            }
            catch { return false; }   // unknown -> assume visible (don't drop a real label)
        }

        // Deduped WCS vertices of a face; `curved` = the face has any non-straight (arc/circle) edge,
        // i.e. it is a bore disc/wall or other rounded surface -> caller skips it (no bevel/depth).
        private static List<Point3d> FaceVertices(BRep.Face face, out bool curved)
        {
            var pts = new List<Point3d>();
            curved = false;
            try
            {
                foreach (BRep.BoundaryLoop loop in face.Loops)
                    foreach (BRep.Edge e in loop.Edges)
                    {
                        if (!IsStraight(e)) curved = true;
                        Add(pts, e.Vertex1.Point);
                        Add(pts, e.Vertex2.Point);
                    }
            }
            catch { }
            return pts;
            void Add(List<Point3d> l, Point3d p)
            {
                foreach (Point3d q in l) if (q.DistanceTo(p) < 1e-6) return;
                l.Add(p);
            }
        }

        // An edge is straight when its midpoint lies on the chord between its ends.
        private static bool IsStraight(BRep.Edge e)
        {
            try
            {
                Curve3d gc = e.Curve;
                Interval iv = gc.GetInterval();
                Point3d a = gc.EvaluatePoint(iv.LowerBound), b = gc.EvaluatePoint(iv.UpperBound);
                Point3d m = gc.EvaluatePoint((iv.LowerBound + iv.UpperBound) / 2.0);
                Point3d lin = new Point3d((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0, (a.Z + b.Z) / 2.0);
                return m.DistanceTo(lin) < 1e-4;
            }
            catch { return true; }
        }

        // Normal from the first non-collinear triple; centroid = vertex average; verify all coplanar.
        private static bool PlaneFit(List<Point3d> v, out Vector3d n, out Point3d c)
        {
            n = Vector3d.ZAxis;
            double cx = 0, cy = 0, cz = 0;
            foreach (Point3d p in v) { cx += p.X; cy += p.Y; cz += p.Z; }
            c = new Point3d(cx / v.Count, cy / v.Count, cz / v.Count);

            Vector3d best = new Vector3d();
            for (int i = 1; i < v.Count - 1 && best.Length < 1e-9; i++)
            {
                Vector3d a = v[i] - v[0], b = v[i + 1] - v[0];
                Vector3d x = a.CrossProduct(b);
                if (x.Length > 1e-6) best = x;
            }
            if (best.Length < 1e-9) return false;
            n = best.GetNormal();
            foreach (Point3d p in v)
                if (Math.Abs((p - c).DotProduct(n)) > CoplanarTol) return false;   // not planar
            return true;
        }

        // ---- the surface annotator -----------------------------------------------------------------

        public static int Emit(ScribeFaces.FaceFrame ff, List<ScribeFaces.Mark> marks,
                               List<SolidFace> faces)
        {
            var anns = new List<Ann>();
            if (faces != null) AnnotateSurfaces(faces, ff, anns);

            Decollide(anns, ff);
            foreach (Ann a in anns)
                foreach (List<Point2d> stroke in ScribeFont.Layout(a.Text, a.H, a.At))
                    marks.Add(new ScribeFaces.Mark
                    {
                        Kind = ScribeFaces.MarkKind.Poly, Visible = true, Pts = stroke
                    });
            return anns.Count;
        }

        private static void AnnotateSurfaces(List<SolidFace> faces, ScribeFaces.FaceFrame ff, List<Ann> anns)
        {
            foreach (SolidFace sf in faces)
            {
                if (sf.Vis == null || ff.Number - 1 >= sf.Vis.Length || !sf.Vis[ff.Number - 1])
                    continue;                                             // not visible from this RS face
                double d = Math.Max(-1.0, Math.Min(1.0, sf.NOut.DotProduct(ff.N)));
                double bevel = Math.Acos(d) * 180.0 / Math.PI;
                double depth = -(sf.Centroid - ff.C).DotProduct(ff.N);    // >0 = below the RS face

                string text;
                if (bevel <= FlatTolDeg)                                  // flat, parallel to RS
                {
                    if (depth <= RecessTol) continue;                    // the RS face itself / flush
                    text = ScribeFont.FormatInches(depth);
                }
                else if (bevel < 90.0 - FlatTolDeg)                      // genuinely tilted
                    text = ScribeFont.FormatDegrees(bevel);
                else continue;                                           // perpendicular sliver

                // projected footprint: bbox + centroid
                double umin = double.MaxValue, umax = double.MinValue;
                double vmin = double.MaxValue, vmax = double.MinValue;
                double su = 0, sv = 0;
                foreach (Point3d p in sf.Bpts)
                {
                    Point2d m = ff.Map(p);
                    if (m.X < umin) umin = m.X; if (m.X > umax) umax = m.X;
                    if (m.Y < vmin) vmin = m.Y; if (m.Y > vmax) vmax = m.Y;
                    su += m.X; sv += m.Y;
                }
                if (umax - umin < ThinMin || vmax - vmin < ThinMin) continue;   // sliver
                // A face spanning most of the length is the timber's OWN shape (a side/far face or a
                // full-length chamfer), not localized joinery -- its label floats at mid-span far from
                // any cut. Only localized cuts (mortise/tenon/housing/end cap) get a label.
                if (umax - umin > 0.6 * ff.Overall) continue;

                // fit text inside the footprint
                double h = DimTextH;
                double availV = (vmax - vmin) * 0.8;
                if (availV > 1e-6) h = Math.Min(h, availV);
                double availU = (umax - umin) * 0.9;
                double w0 = ScribeFont.Width(text, h);
                if (availU > 1e-6 && w0 > availU) h *= availU / w0;
                h = Math.Max(h, MinTextH);

                var a = new Ann
                {
                    Text = text, H = h, W = ScribeFont.Width(text, h),
                    At = new Point2d(su / sf.Bpts.Length, sv / sf.Bpts.Length)   // projected centroid
                };
                Clamp(a, ff);
                anns.Add(a);
            }
        }

        // ---- placement -----------------------------------------------------------------------------

        private static void Clamp(Ann a, ScribeFaces.FaceFrame ff)
        {
            double x = a.At.X, y = a.At.Y, m = 0.1;
            double xlo = a.W / 2.0 + m, xhi = ff.Overall - a.W / 2.0 - m;
            double ylo = a.H / 2.0 + m, yhi = ff.FaceW - a.H / 2.0 - m;
            if (xlo < xhi) x = Math.Max(xlo, Math.Min(xhi, x));
            if (ylo < yhi) y = Math.Max(ylo, Math.Min(yhi, y));
            a.At = new Point2d(x, y);
        }

        // Nudge overlapping labels apart (two nested surfaces can share a centroid); two gentle passes.
        private static void Decollide(List<Ann> anns, ScribeFaces.FaceFrame ff)
        {
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < anns.Count; i++)
                    for (int j = i + 1; j < anns.Count; j++)
                    {
                        Ann a = anns[i], b = anns[j];
                        double du = Math.Abs(a.At.X - b.At.X), dv = Math.Abs(a.At.Y - b.At.Y);
                        if (du < (a.W + b.W) / 2.0 * 0.9 && dv < (a.H + b.H) / 2.0 * 1.1)
                        {
                            b.At = new Point2d(b.At.X, b.At.Y + (a.H + b.H) * 0.75);
                            Clamp(b, ff);
                        }
                    }
        }
    }
}
