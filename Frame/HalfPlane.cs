using System;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{
    // A 2D half-plane in the bent (XY) plane. "Inside" = the kept side:
    // points where (X - P) . N >= 0, with N the inward normal.
    //
    // A member body is the convex intersection of its half-planes: the two long
    // faces (from the section) plus the cut planes contributed by its neighbours.
    // This replaces per-member trig -- every junction is just another cut plane.
    public struct HalfPlane
    {
        public Point3d  P;   // a point on the boundary line
        public Vector3d N;   // inward normal (kept side)

        public HalfPlane(Point3d p, Vector3d n) { P = p; N = n; }

        // Signed distance along N; >= 0 means the point is on the kept side.
        public double Signed(Point3d x) => (x.X - P.X) * N.X + (x.Y - P.Y) * N.Y;

        // Axis-aligned helpers (vertical / horizontal cut lines).
        public static HalfPlane KeepRightOfX(double x) => new HalfPlane(new Point3d(x, 0, 0), new Vector3d( 1, 0, 0));
        public static HalfPlane KeepLeftOfX(double x)  => new HalfPlane(new Point3d(x, 0, 0), new Vector3d(-1, 0, 0));
        public static HalfPlane KeepAboveY(double y)   => new HalfPlane(new Point3d(0, y, 0), new Vector3d(0,  1, 0));
        public static HalfPlane KeepBelowY(double y)   => new HalfPlane(new Point3d(0, y, 0), new Vector3d(0, -1, 0));

        // General: line through p along dir; keep the side that contains keepToward.
        // Used by diagonal members (braces, struts) whose faces are neither vertical
        // nor horizontal -- works at any orientation, unlike the below/above helpers.
        public static HalfPlane Through(Point3d p, Vector3d dir, Point3d keepToward)
        {
            Vector3d n = Norm(new Vector3d(-dir.Y, dir.X, 0));
            double d = (keepToward.X - p.X) * n.X + (keepToward.Y - p.Y) * n.Y;
            if (d < 0) n = n.Negate();
            return new HalfPlane(p, n);
        }

        // Sloped line through p with direction dir; keep the lower-y or higher-y side.
        public static HalfPlane KeepBelowLine(Point3d p, Vector3d dir)
        {
            Vector3d n = new Vector3d(dir.Y, -dir.X, 0);
            if (n.Y > 0) n = n.Negate();
            return new HalfPlane(p, Norm(n));
        }
        public static HalfPlane KeepAboveLine(Point3d p, Vector3d dir)
        {
            Vector3d n = new Vector3d(dir.Y, -dir.X, 0);
            if (n.Y < 0) n = n.Negate();
            return new HalfPlane(p, Norm(n));
        }

        private static Vector3d Norm(Vector3d v)
        {
            double l = Math.Sqrt(v.X * v.X + v.Y * v.Y);
            return l > 1e-12 ? new Vector3d(v.X / l, v.Y / l, 0) : v;
        }
    }
}
