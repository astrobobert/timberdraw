using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using BRep = Autodesk.AutoCAD.BoundaryRepresentation;

namespace TimberDraw
{
    // Burned dimension text as a SURFACE MODEL, judged FROM THE VIEWER (user model, 2026-07-03).
    // SOLPROF already draws the visible linework from each RS viewpoint; labels follow the same
    // rule: cast rays DOWN THE VIEW AXIS -- the first surface a ray hits is the surface you see
    // there, and it gets ONE label placed at the center of its ray-visible area. Implemented as a
    // sample grid over each candidate surface's projected footprint (a ray survives if it reaches
    // the surface's plane depth without passing through wood), which is the same semantics as
    // point-in-SOLPROF-polygon without reconstructing the polygons.
    //
    // This replaces a zoo of surface-side heuristics (front-facing, occlusion steps, outward
    // orientation, open-at-face, meets-face, clear-ray, pocket-undercut) that each fixed one
    // complaint and caused the next: probes shot down peg bores, normals flipped in end notches,
    // end shoulders labeled on every face they touched. Ray-from-viewer is orientation-blind
    // (undercut pocket walls are hit from the void side and label correctly) and covered surfaces
    // (pierced mortise cheeks behind an intact face) simply never win a ray.
    //   - a surface TILTED relative to the RS face -> its bevel ANGLE (deg, F2, acute);
    //   - a FLAT surface below the RS face -> its DEPTH of cut (fraction inches);
    //   - the RS face itself (flush), edge-on faces, and surfaces no ray reaches -> nothing.
    //
    // The surfaces are the solid's real Brep faces (not the TFrame feature lists), so a tenon's
    // shoulder is ONE face -> ONE label. SOLPROF still draws the burn LINEWORK; this only produces
    // the text laid over it.
    public static class ScribeAnnotate
    {
        private const double RecessTol  = 1.0 / 32.0;  // min depth below the face worth printing (in)
        private const double FlatTolDeg = 0.75;        // within this of parallel = flat; of perp = edge-on
        private const double CoplanarTol = 0.03;       // planarity test for a Brep face (in)
        private const double DimTextH   = 0.50;        // FIXED label height (user call: 1/2" always,
                                                       // whether it fits the surface footprint or not)
        private const double ThinMin    = 0.25;        // skip a surface whose projected area is thinner
        private const double RayStep    = 0.30;        // ray march step (in); < any wood wall thickness
                                                       // we care about (cheeks 1"+, overhang tips excepted
                                                       // -- the 2-sample threshold absorbs edge grazes)

        // A planar boundary face of the solid, in WCS, with its per-RS ray visibility. NOut is the
        // PLANE normal -- its sign carries no meaning (ray visibility is orientation-blind; the
        // bevel uses the acute |dot|). Seen[k] = ray-visible sample count from RS face k; At[k] =
        // label anchor (mean of the visible samples, face coords); Total[k] = samples cast.
        public struct SolidFace
        {
            public Vector3d NOut;
            public Point3d Centroid;
            public Point3d[] Bpts;
            public int[] Seen;
            public int[] Total;
            public Point2d[] At;
        }

        private sealed class Ann
        {
            public string Text; public Point2d At; public double H; public double W;
        }

        // ---- Brep face extraction + ray visibility (once per timber) -------------------------------

        // The solid's planar boundary faces + per-RS ray visibility. One short read transaction;
        // safe to call before SOLPROF. `ffs` are the 4 RS FaceFrames (the rays run down each face's
        // view axis). Empty on any Brep hiccup -> no dimension text (linework is independent).
        public static List<SolidFace> BuildSolidFaces(Database db, ObjectId id, ScribeFaces.FaceFrame[] ffs)
        {
            var result = new List<SolidFace>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is Solid3d sol)) { tr.Commit(); return result; }
                try
                {
                    using (var brep = new BRep.Brep(sol))
                    {
                        foreach (BRep.Face face in brep.Faces)
                        {
                            List<Point3d> verts = FaceVertices(face, out int curvedE, out int straightE);
                            // Dominantly curved = bore disc/wall -> never labeled. A planar face
                            // merely PIERCED by a bore (arc notch in the boundary) stays.
                            if (curvedE >= straightE || verts.Count < 3) continue;
                            if (!PlaneFit(verts, out Vector3d n, out Point3d c)) continue;

                            var sf = new SolidFace
                            {
                                NOut = n, Centroid = c, Bpts = verts.ToArray(),
                                Seen = new int[ffs.Length], Total = new int[ffs.Length],
                                At = new Point2d[ffs.Length]
                            };
                            for (int k = 0; k < ffs.Length; k++)
                            {
                                SampleVisibility(brep, n, c, sf.Bpts, ffs[k],
                                    out sf.Seen[k], out sf.Total[k], out sf.At[k], out _);
                            }
                            result.Add(sf);
                        }
                    }
                }
                catch { }
                tr.Commit();
            }
            return result;
        }

        // Ray visibility of one surface from RS face `ff`: sample a grid over the surface's
        // projected footprint; each sample casts a ray from the face plane down the view axis and
        // survives if it reaches the surface's plane depth without passing through wood. `gate`
        // names the cheap pre-filter that skipped sampling (edge-on / sliver / full-length), null
        // when sampling ran. Callers label when Seen clears the threshold in LabelWorthy.
        private static void SampleVisibility(BRep.Brep brep, Vector3d n, Point3d c, Point3d[] bpts,
            ScribeFaces.FaceFrame ff, out int seen, out int total, out Point2d at, out string gate)
        {
            seen = 0; total = 0; at = new Point2d(); gate = null;

            double nd = n.DotProduct(ff.N);
            if (Math.Abs(nd) < 0.013) { gate = "edge-on"; return; }        // ~> 89.25 deg to the view

            double umin = double.MaxValue, umax = double.MinValue;         // projected footprint
            double vmin = double.MaxValue, vmax = double.MinValue;
            foreach (Point3d p in bpts)
            {
                Point2d m = ff.Map(p);
                if (m.X < umin) umin = m.X; if (m.X > umax) umax = m.X;
                if (m.Y < vmin) vmin = m.Y; if (m.Y > vmax) vmax = m.Y;
            }
            if (umax - umin < ThinMin || vmax - vmin < ThinMin) { gate = "sliver"; return; }
            // A face spanning most of the length is the timber's own shape (side face, full-length
            // chamfer), not localized joinery.
            if (umax - umin > 0.6 * ff.Overall) { gate = "full-length"; return; }

            int nu = Math.Max(2, Math.Min(5, (int)Math.Round((umax - umin) / 1.5)));
            int nv = Math.Max(2, Math.Min(4, (int)Math.Round((vmax - vmin) / 1.0)));
            double thick = 2.0 * ff.HalfN;
            double su = 0, sv = 0;

            for (int i = 0; i < nu; i++)
                for (int j = 0; j < nv; j++)
                {
                    double u = umin + (umax - umin) * (0.15 + 0.70 * i / (nu - 1));
                    double v = vmin + (vmax - vmin) * (0.15 + 0.70 * j / (nv - 1));
                    if (u < 0.1 || u > ff.Overall - 0.1 || v < 0.1 || v > ff.FaceW - 0.1) continue;

                    Point3d p0 = PlanePoint(ff, u, v);                     // ray origin on the face plane
                    double t = (p0 - c).DotProduct(n) / nd;                // ray depth to this surface's plane
                    if (t < -0.03 || t > thick + 0.5) continue;            // above the plane / absurd
                    total++;

                    if (!RayClear(brep, ff, u, v, c, n, nd)) continue;
                    // A surviving ray can be a fluke down a PEG BORE (the surface is "visible"
                    // through a 1" hole -- phantom depth labels at pegs). Demand a patch wider
                    // than a bore: at least 2 of 4 neighbor rays 0.6" away (> bore radius) must
                    // also reach this surface.
                    int wide = 0;
                    if (RayClear(brep, ff, u + 0.6, v, c, n, nd)) wide++;
                    if (RayClear(brep, ff, u - 0.6, v, c, n, nd)) wide++;
                    if (wide < 2 && RayClear(brep, ff, u, v + 0.6, c, n, nd)) wide++;
                    if (wide < 2 && RayClear(brep, ff, u, v - 0.6, c, n, nd)) wide++;
                    if (wide < 2) continue;
                    seen++; su += u; sv += v;
                }

            if (seen > 0) at = new Point2d(su / seen, sv / seen);
        }

        // One viewer ray at face coords (u, v): does it LAND ON surface (c, n)? Three conditions:
        // in bounds, unblocked down to the surface's plane, and the plane crossing is a REAL hit --
        // wood on exactly one side of it. The last test rejects rays that cross the plane in
        // MID-AIR (through a housing void, past the stock at an end): those produced stray
        // depth/bevel labels because the plane extends beyond the actual face. An undercut wall
        // still passes (void above the crossing, its own wood below).
        private static bool RayClear(BRep.Brep brep, ScribeFaces.FaceFrame ff,
                                     double u, double v, Point3d c, Vector3d n, double nd)
        {
            if (u < 0.1 || u > ff.Overall - 0.1 || v < 0.1 || v > ff.FaceW - 0.1) return false;
            Point3d p0 = PlanePoint(ff, u, v);
            double t = (p0 - c).DotProduct(n) / nd;
            if (t < -0.03 || t > 2.0 * ff.HalfN + 0.5) return false;
            try
            {
                for (double s = 0.06; s < t - 0.12; s += RayStep)
                    if (Inside(brep, p0 - ff.N * s)) return false;
                Point3d hit = p0 - ff.N * t;
                return Inside(brep, hit + n * 0.08) != Inside(brep, hit - n * 0.08);
            }
            catch { return false; }
        }

        // Enough ray-visible samples to deserve a label: two, or the lone sample of a small patch.
        private static bool LabelWorthy(int seen, int total)
            => seen >= 2 || (seen == 1 && total <= 4);

        // The world point ON RS face ff's nominal plane at face coords (u, v) -- Map's inverse.
        private static Point3d PlanePoint(ScribeFaces.FaceFrame ff, double u, double v)
            => ff.C + ff.A * (u + ff.UMin - ff.C.GetAsVector().DotProduct(ff.A))
                    + ff.V * (v - ff.HalfV);

        private static bool Inside(BRep.Brep brep, Point3d p)
        {
            BRep.PointContainment pc;
            using (var e = brep.GetPointContainment(p, out pc))
                return pc == BRep.PointContainment.Inside;
        }

        // Deduped WCS vertices of a face + its straight/curved edge counts. The caller uses the
        // ratio to tell a bore disc/wall (dominantly curved) from a planar face merely pierced by
        // a bore (dominantly straight, keeps its label).
        private static List<Point3d> FaceVertices(BRep.Face face, out int curvedE, out int straightE)
        {
            var pts = new List<Point3d>();
            curvedE = 0; straightE = 0;
            try
            {
                foreach (BRep.BoundaryLoop loop in face.Loops)
                    foreach (BRep.Edge e in loop.Edges)
                    {
                        if (IsStraight(e)) straightE++; else curvedE++;
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
            int k = ff.Number - 1;
            foreach (SolidFace sf in faces)
            {
                if (sf.Seen == null || k >= sf.Seen.Length) continue;
                if (!LabelWorthy(sf.Seen[k], sf.Total[k])) continue;       // no ray reaches it

                double ad = Math.Min(1.0, Math.Abs(sf.NOut.DotProduct(ff.N)));
                double bevel = Math.Acos(ad) * 180.0 / Math.PI;            // acute plane-to-plane angle

                string text;
                if (bevel <= FlatTolDeg)                                   // flat, parallel to RS
                {
                    double depth = Math.Abs((sf.Centroid - ff.C).DotProduct(ff.N));
                    if (depth <= RecessTol) continue;                      // the RS face itself / flush
                    text = ScribeFont.FormatInches(depth);
                }
                else if (bevel < 90.0 - FlatTolDeg)
                    text = ScribeFont.FormatDegrees(bevel);
                else continue;                                             // edge-on (gate safety net)

                // Fixed 1/2" text, never shrunk to the footprint (user call: readability over fit;
                // Clamp keeps it on the stock and Decollide spreads pile-ups). Anchored at the
                // center of the RAY-VISIBLE area, so it lands in the exposed region, not under an
                // overhang or off the visible part of an L-shaped surface.
                var a = new Ann
                {
                    Text = text, H = DimTextH, W = ScribeFont.Width(text, DimTextH),
                    At = sf.At[k]
                };
                Clamp(a, ff);
                anns.Add(a);
            }
        }

        // ---- blind-peg marks -----------------------------------------------------------------------

        // Burn a 'B' beside the rim of every BLIND peg bore entering this face (user call): the
        // framer needs to know a bore stops short. Works from the timber's own peg records (local
        // cylinders on the TFrame), not the Brep: entry end ON this face plane + other end short of
        // the opposite face = blind here. Through bores (exit at the far face) get nothing.
        public static int EmitBlindPegMarks(ManagedTimber.TFrame f, ScribeFaces.FaceFrame ff,
                                            List<ScribeFaces.Mark> marks)
        {
            if (f.Pegs == null) return 0;
            int n = 0;
            foreach ((Point3d C, Vector3d Axis, double R, double Half, int Joint) pg in f.Pegs)
            {
                if (pg.R <= 1e-6 || pg.Half <= 1e-6) continue;
                Point3d cw = f.O + f.X * pg.C.X + f.Y * pg.C.Y + f.Z * pg.C.Z;
                Vector3d aw = f.X * pg.Axis.X + f.Y * pg.Axis.Y + f.Z * pg.Axis.Z;
                if (aw.Length < 1e-9) continue;
                aw = aw.GetNormal();
                if (Math.Abs(aw.DotProduct(ff.N)) < 0.9) continue;        // no rim on this view

                Point3d e1 = cw + aw * pg.Half, e2 = cw - aw * pg.Half;
                double d1 = (e1 - ff.C).DotProduct(ff.N);                 // signed height vs face plane
                double d2 = (e2 - ff.C).DotProduct(ff.N);
                // Entry end = the one at/above this face (cylinders are often built PROUD of the
                // entry face for a clean boolean -- an exact on-plane test misses them). Blind =
                // the other end stops genuinely inside, short of the far face.
                Point3d entry; double otherDepth;
                if (d1 >= d2) { entry = e1 - ff.N * d1; otherDepth = -d2; }
                else          { entry = e2 - ff.N * d2; otherDepth = -d1; }
                if (Math.Max(d1, d2) < -0.15) continue;                   // never reaches this face
                if (otherDepth < 0.15) continue;                          // barely dents it: not a bore here
                if (otherDepth >= 2.0 * ff.HalfN - 0.15) continue;        // exits the far face: through

                Point2d m = ff.Map(entry);
                if (m.Y < 0.1 || m.Y > ff.FaceW - 0.1) continue;          // rim off this face
                // Centered IN the bore (user call): the drill removes the burn, nothing to sand.
                foreach (List<Point2d> stroke in ScribeFont.Layout("B", DimTextH, m))
                    marks.Add(new ScribeFaces.Mark
                    {
                        Kind = ScribeFaces.MarkKind.Poly, Visible = true, Pts = stroke
                    });
                n++;
            }
            return n;
        }

        // ---- diagnostics ---------------------------------------------------------------------------

        // TScribeProbe engine: walk the solid's Brep exactly like BuildSolidFaces and PRINT each
        // planar face's per-RS ray verdict (gate name, hidden, or the label it would get with its
        // visible/total sample counts). For chasing missing/extra labels with facts.
        public static void DebugProbe(Autodesk.AutoCAD.EditorInput.Editor ed, Database db,
                                      ObjectId id, ScribeFaces.FaceFrame[] ffs)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is Solid3d sol))
                { ed.WriteMessage("\n  not a solid."); tr.Commit(); return; }
                try
                {
                    using (var brep = new BRep.Brep(sol))
                    {
                        int idx = 0, planar = 0;
                        foreach (BRep.Face face in brep.Faces)
                        {
                            idx++;
                            List<Point3d> verts = FaceVertices(face, out int curvedE, out int straightE);
                            if (curvedE >= straightE || verts.Count < 3)
                            {
                                Point3d cm = verts.Count > 0 ? Mean(verts) : Point3d.Origin;
                                ed.WriteMessage($"\n f{idx:00} @({cm.X:0.0},{cm.Y:0.0},{cm.Z:0.0})" +
                                                $" SKIP curved-dominant ({curvedE}c/{straightE}s)");
                                continue;
                            }
                            if (!PlaneFit(verts, out Vector3d n, out Point3d c))
                            {
                                ed.WriteMessage($"\n f{idx:00} SKIP non-planar");
                                continue;
                            }
                            planar++;
                            Point3d[] bpts = verts.ToArray();

                            var sb = new System.Text.StringBuilder();
                            Point2d m0 = ffs[0].Map(c);
                            sb.Append($"\n f{idx:00} u~{m0.X:0.0} n=({n.X:0.00},{n.Y:0.00},{n.Z:0.00})");
                            for (int k = 0; k < ffs.Length; k++)
                            {
                                SampleVisibility(brep, n, c, bpts, ffs[k],
                                    out int seen, out int total, out Point2d at, out string gate);
                                string v;
                                if (gate != null) v = gate;
                                else if (seen == 0) v = "hidden";
                                else if (!LabelWorthy(seen, total)) v = $"graze {seen}/{total}";
                                else
                                {
                                    double ad = Math.Min(1.0, Math.Abs(n.DotProduct(ffs[k].N)));
                                    double bevel = Math.Acos(ad) * 180.0 / Math.PI;
                                    double depth = Math.Abs((c - ffs[k].C).DotProduct(ffs[k].N));
                                    v = bevel <= FlatTolDeg
                                        ? (depth <= RecessTol ? "flush"
                                           : $"DEPTH {ScribeFont.FormatInches(depth)} ({seen}/{total})")
                                        : $"BEVEL {bevel:0.00} ({seen}/{total})";
                                }
                                sb.Append($" | RS{k + 1}:{v}");
                            }
                            ed.WriteMessage(sb.ToString());
                        }
                        ed.WriteMessage($"\n  ({planar} planar faces probed)");
                    }
                }
                catch (System.Exception ex) { ed.WriteMessage($"\n  probe failed: {ex.Message}"); }
                tr.Commit();
            }
        }

        private static Point3d Mean(List<Point3d> v)
        {
            double x = 0, y = 0, z = 0;
            foreach (Point3d p in v) { x += p.X; y += p.Y; z += p.Z; }
            return new Point3d(x / v.Count, y / v.Count, z / v.Count);
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
