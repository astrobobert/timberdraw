using System.Collections.Generic;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{
    // Computes a convex member profile as the intersection of half-planes, by
    // successively clipping a large starting quad (Sutherland-Hodgman per plane).
    // Result is a CCW closed polygon ready to extrude via Module1.DrawElement.
    public static class Profile
    {
        private const double BIG = 1.0e6;
        private const double EPS = 1.0e-7;

        public static Point3dCollection Intersect(List<HalfPlane> planes)
        {
            var poly = new List<Point3d>
            {
                new Point3d(-BIG, -BIG, 0), new Point3d(BIG, -BIG, 0),
                new Point3d( BIG,  BIG, 0), new Point3d(-BIG, BIG, 0)
            };
            foreach (HalfPlane hp in planes)
            {
                poly = Clip(poly, hp);
                if (poly.Count == 0) break;
            }
            poly = Dedupe(poly);   // drop coincident vertices (e.g. a feathered heel where two
                                   // boundary lines meet at one point) -- they break extrusion.
            var pts = new Point3dCollection();
            foreach (Point3d p in poly) pts.Add(p);
            return pts;
        }

        // Removes consecutive coincident vertices and the wrap-around duplicate, so the polygon
        // has no zero-length edges (which DrawElement rejects as an invalid object).
        private static List<Point3d> Dedupe(List<Point3d> poly)
        {
            var outp = new List<Point3d>();
            foreach (Point3d p in poly)
                if (outp.Count == 0 || !Near(outp[outp.Count - 1], p)) outp.Add(p);
            while (outp.Count >= 2 && Near(outp[0], outp[outp.Count - 1])) outp.RemoveAt(outp.Count - 1);
            return outp;
        }

        private static bool Near(Point3d a, Point3d b)
            => System.Math.Abs(a.X - b.X) < 1.0e-6 && System.Math.Abs(a.Y - b.Y) < 1.0e-6;

        private static List<Point3d> Clip(List<Point3d> poly, HalfPlane hp)
        {
            var outp = new List<Point3d>();
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                Point3d cur = poly[i];
                Point3d nxt = poly[(i + 1) % n];
                double dc = hp.Signed(cur);
                double dn = hp.Signed(nxt);
                bool ci = dc >= -EPS;
                bool ni = dn >= -EPS;
                if (ci) outp.Add(cur);
                if (ci != ni)
                {
                    double t = dc / (dc - dn);
                    outp.Add(new Point3d(
                        cur.X + t * (nxt.X - cur.X),
                        cur.Y + t * (nxt.Y - cur.Y), 0));
                }
            }
            return outp;
        }
    }
}
