using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{
    // Managed timber -> per-face scribe frames for the TimberScribe laser head (.tsj export).
    //
    // The scribe head rides the four LONG faces (RS1-RS4); the two ends (5-6) are transferred by
    // hand. Face coordinates follow the framer convention shared with TimberTag/TimberScribe:
    //   origin = anchor end + datum edge (upper-left), X along the length from the anchor end,
    //   Y toward the framer (datum edge = far edge, y = 0), units inches.
    //
    // DATUM RULE (geometric, from role -- user-approved 2026-07-02):
    //   RS1    = the most UP-facing side face (beams/girts/rafters/braces), or for near-vertical
    //            timbers (posts) the most OUTWARD face, judged from the frame's plan centroid.
    //   anchor = the lower end (bottom of a post, foot of a rafter/brace); for level timbers the
    //            end toward the lower grid address (toward Bent 1 / Wall A).
    //   roll   = RS2 is the face a framer sees rising when the stick rolls AWAY from them
    //            (n2 = A x n1, A = anchor->far). Each face's datum edge (y = 0) is then the arris
    //            it shares with the PREVIOUS face, so the machine's datum arm stays on the far
    //            edge through the whole roll sequence. RS3 is opposite RS1.
    //
    // PROJECTION: the actual linework comes from AutoCAD's SOLPROF hidden-line engine
    // (ScribeSolprof) -- one profile per face, visible (PV) = burn path, hidden (PH) = preview
    // only. This file owns the face FRAMES (where each face's coordinate system sits in the
    // world) and the shared models; it computes no visibility itself.
    public static class ScribeFaces
    {
        internal const double EdgeTol = 0.02;   // arris / stock-end proximity (in)

        public enum MarkKind { Line, Poly, Circle, Arc }

        public sealed class Mark
        {
            public MarkKind Kind;
            public bool Visible;                       // true = CUT line (burned, drawn black)
            public bool Boundary;                      // true = stock OUTLINE (gray reference, not burned)
            public List<Point2d> Pts;                  // Line (2 pts) / Poly (n pts), face coords
            public Point2d Center; public double R;    // Circle / Arc
            public double StartDeg, EndDeg;            // Arc (CCW in face coords)
        }

        public sealed class Face
        {
            public int Number;                         // 1..4 = RS1..RS4
            public double LengthIn;                    // overall stick length (same on all faces)
            public double WidthIn;                     // across this face
            public double ThickIn;                     // the perpendicular section dimension
            public List<Mark> Marks = new List<Mark>();
            public int VisibleCount;
            public int DimCount;                       // burned dimension annotations (text)
        }

        // One RS face's mapping frame: world -> face coords (x = station from the anchor end, y =
        // from the datum edge toward the framer). Shared by ScribeSolprof (UCS construction) and
        // ScribeAnnotate (text placement) so linework and text land in the same coordinates.
        public struct FaceFrame
        {
            public int Number;          // 1..4
            public Vector3d A;          // length direction, anchor -> far
            public Vector3d N;          // outward face normal
            public Vector3d V;          // in-face, v grows toward the framer
            public Point3d C;           // face center (nominal plane)
            public double HalfV;        // half-width across the face
            public double HalfN;        // half-thickness along the normal
            public double UMin;         // world station of x = 0 (overall stick min along A)
            public double Overall;      // overall stick length
            public double FaceW;        // face width (2 * HalfV)

            public Point2d Map(Point3d p) => new Point2d(
                p.GetAsVector().DotProduct(A) - UMin,
                (p - C).DotProduct(V) + HalfV);
        }

        public sealed class Sheet
        {
            public string Id = "";
            public string Description = "";
            public Face[] Faces = new Face[4];
        }

        // Datum rule + face frames for one managed timber. `frameCenter` is the plan centroid of
        // the WHOLE frame (drives the post outward-face rule). Returns the sheet shell (identity)
        // with the four FaceFrames; null when the solid is unavailable.
        public static Sheet Frames(Database db, ManagedTimber.ShopInfo t, Point3d frameCenter,
                                   out FaceFrame[] frames)
        {
            frames = null;
            ManagedTimber.TFrame f = t.F;
            Vector3d ax = f.X.GetNormal(), ay = f.Y.GetNormal(), az = f.Z.GetNormal();
            Point3d mid = f.O + az * (f.L / 2.0);

            // ---- anchor end / length direction A (anchor -> far) --------------------------------
            Vector3d A;
            if (Math.Abs(az.Z) > 0.1) A = az.Z >= 0 ? az : -az;                      // sloped/vertical: foot end
            else if (Math.Abs(az.X) >= Math.Abs(az.Y)) A = az.X >= 0 ? az : -az;     // level, runs the length
            else A = az.Y >= 0 ? az : -az;                                           // level, runs across

            // ---- RS1 normal ----------------------------------------------------------------------
            bool vertical = Math.Abs(az.Z) > 0.85;   // 45-deg braces stay on the beam rule
            Vector3d pref;
            if (vertical)
            {
                var outp = new Vector3d(mid.X - frameCenter.X, mid.Y - frameCenter.Y, 0.0);
                pref = outp.Length > 1e-6 ? outp.GetNormal() : Vector3d.XAxis;
            }
            else pref = Vector3d.ZAxis;
            Vector3d[] cand = { ax, -ax, ay, -ay };
            Vector3d n1 = cand.OrderByDescending(n =>
                n.DotProduct(pref) + 1e-6 * n.X + 1e-9 * n.Y).First();   // tiny world-axis tie-break

            Vector3d n2 = A.CrossProduct(n1).GetNormal();
            Vector3d[] ns = { n1, n2, -n1, -n2 };                        // RS1..RS4 (roll-away order)

            // ---- overall extent along A (tenon stubs included): clone-and-measure ----------------
            double umin, umax;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                if (!(tr.GetObject(t.Id, OpenMode.ForRead) is Solid3d sol)) return null;
                try
                {
                    using (Entity clone = (Entity)sol.Clone())
                    {
                        clone.TransformBy(Matrix3d.AlignCoordinateSystem(
                            Point3d.Origin, A, n1, A.CrossProduct(n1),
                            Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis));
                        Extents3d b = clone.Bounds ?? throw new InvalidOperationException("no bounds");
                        umin = b.MinPoint.X;
                        umax = b.MaxPoint.X;
                    }
                }
                catch
                {
                    // nominal fallback: the frame's own length span
                    double u0 = f.O.GetAsVector().DotProduct(A);
                    double u1 = (f.O + az * f.L).GetAsVector().DotProduct(A);
                    umin = Math.Min(u0, u1); umax = Math.Max(u0, u1);
                }
                tr.Commit();
            }
            double overall = Math.Max(umax - umin, 0.001);

            double hW = f.W / 2.0, hD = f.D / 2.0;
            frames = new FaceFrame[4];
            for (int i = 0; i < 4; i++)
            {
                Vector3d nk = ns[i];
                double halfN = Math.Abs(nk.DotProduct(ax)) * hW + Math.Abs(nk.DotProduct(ay)) * hD;
                Vector3d vk = A.CrossProduct(nk).GetNormal();             // v grows toward the framer
                double halfV = Math.Abs(vk.DotProduct(ax)) * hW + Math.Abs(vk.DotProduct(ay)) * hD;
                frames[i] = new FaceFrame
                {
                    Number = i + 1, A = A, N = nk, V = vk,
                    C = mid + nk * halfN,                                 // face center (nominal plane)
                    HalfV = halfV, HalfN = halfN,
                    UMin = umin, Overall = overall, FaceW = 2.0 * halfV
                };
            }

            return new Sheet
            {
                Id = FirstNonEmpty(t.GridLabel, t.Designation, t.Role, "timber"),
                Description = (t.Role + " " + f.W.ToString("0.##") + "x" + f.D.ToString("0.##")
                               + (string.IsNullOrEmpty(t.GridLabel) ? "" : "  " + t.GridLabel)).Trim()
            };
        }

        // Plan centroid of the whole managed frame (timber midpoints) -- the post outward-face judge.
        public static Point3d FrameCenter(List<ManagedTimber.ShopInfo> all)
        {
            if (all == null || all.Count == 0) return Point3d.Origin;
            double x = 0, y = 0, z = 0;
            foreach (var t in all)
            {
                Point3d m = t.F.O + t.F.Z.GetNormal() * (t.F.L / 2.0);
                x += m.X; y += m.Y; z += m.Z;
            }
            return new Point3d(x / all.Count, y / all.Count, z / all.Count);
        }

        // CUT-TO-LENGTH lines: EVERY face gets a full-width burned line at BOTH end stations
        // (x = 0 and x = Overall) so the framer can square the stock to length before laying out
        // joinery. SOLPROF only yields an end line incidentally -- a tenoned end shows just the
        // tip's width, and side views can miss the end entirely -- so these are emitted
        // EXPLICITLY, never trusted to the profile. Any existing burned line already sitting on
        // an end station is removed first (the full-width line supersedes it -- keeps the burn
        // single-pass instead of re-burning the tenon tip under the new line).
        internal const double EndStationTol = 0.05;   // "sits on the end station" (in)

        internal static void AddEndCutLines(FaceFrame ff, List<Mark> marks)
        {
            foreach (double x in new[] { 0.0, ff.Overall })
            {
                marks.RemoveAll(m =>
                    m.Visible && m.Kind == MarkKind.Line && m.Pts != null && m.Pts.Count == 2 &&
                    Math.Abs(m.Pts[0].X - x) < EndStationTol &&
                    Math.Abs(m.Pts[1].X - x) < EndStationTol);
                marks.Add(new Mark
                {
                    Kind = MarkKind.Line,
                    Visible = true,
                    Pts = new List<Point2d> { new Point2d(x, 0.0), new Point2d(x, ff.FaceW) }
                });
            }
        }

        // A straight visible line is a LONG ARRIS of the stock -- the timber's own long edge, running
        // the length along the datum/near edge (y ~ 0 or y ~ faceW). Those are existing faces of the
        // stick, not cuts, so they are dropped from the burn. Everything else is KEPT, including the
        // full-width END CUTS (constant-station lines at the stock extremes): the framer saws those to
        // length, so they belong in the burn. Oblique silhouette lines (brace miters, plumb cuts) and
        // the interior cut lines are kept too.
        internal static bool IsLongArris(Point2d a, Point2d b, double faceW)
        {
            bool alongSideA = a.Y < EdgeTol && b.Y < EdgeTol;
            bool alongSideB = a.Y > faceW - EdgeTol && b.Y > faceW - EdgeTol;
            return (alongSideA || alongSideB) && Math.Abs(a.Y - b.Y) < EdgeTol;
        }

        private static string FirstNonEmpty(params string[] xs)
            => xs.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "";
    }
}
