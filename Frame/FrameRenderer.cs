using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{
    // Renders a FrameGraph as plain solid-box bodies, one per edge -- role-agnostic.
    //
    // In-bent members: body profile = convex intersection of the edge's half-planes
    // (XY), extruded by Width in Z, placed at Z = nodeA.Z + ZOffset (bent position +
    // back/center/front placement).
    //
    // Bay members (Longitudinal): a Width x Depth cross-section in XY at nodeA, extruded
    // along Z by the bay length (nodeB.Z - nodeA.Z).
    //
    // NO joinery: jointNear/jointFar = "Butt"; JointFactory / AddJoint never called.
    public static class FrameRenderer
    {
        private const string BentNumber = "1";

        public static void Draw(FrameGraph g)
        {
            foreach (FrameEdge e in g.Edges)
            {
                Point3d a = g.Node(e.NodeA).Pos;
                Point3d b = g.Node(e.NodeB).Pos;

                if (e.Longitudinal) { DrawLongitudinal(e, a, b); continue; }

                Point3dCollection raw;
                if (e.CustomProfile != null && e.CustomProfile.Length >= 3)
                {
                    raw = new Point3dCollection();
                    foreach (Point3d q in e.CustomProfile) raw.Add(q);
                    // CustomProfiles can be wound either way (e.g. mirrored left/right
                    // commons). DrawElement extrudes per winding, so normalize to CCW for a
                    // consistent +Z extrusion that matches the half-plane members.
                    if (SignedArea(raw) < 0) ReverseInPlace(raw);
                }
                else
                {
                    raw = Profile.Intersect(e.Planes);
                }
                if (raw.Count < 3) continue;

                double zBase = a.Z + e.ZOffset;
                var pts = new Point3dCollection();
                foreach (Point3d q in raw) pts.Add(new Point3d(q.X, q.Y, zBase));

                string size = (int)e.Width + "x" + (int)e.Depth + "x"
                            + Module1.BuyLongFeet(a.DistanceTo(b));
                Module1.DrawElement(pts, e.Width, e.Role, BentNumber, e.Designation, size,
                    jointNear: "Butt", jointFar: "Butt");
            }
        }

        // Bay member: cross-section (Width x Depth) centered on nodeA's XY, extruded
        // along Z to fill the clear gap between bent faces. The Z span is the bent
        // center-to-center spacing (nodeB.Z - nodeA.Z) minus the bent thickness
        // (LongInset), starting at the near bent's far face (nodeA.Z + LongInset).
        private static void DrawLongitudinal(FrameEdge e, Point3d a, Point3d b)
        {
            double z   = a.Z + e.LongInset;
            double len = (b.Z - a.Z) - e.LongInset;
            if (len <= 0) return;

            Point3dCollection cross;
            if (e.Planes.Count >= 3)
            {
                // Custom cross-section (e.g. chamfered ridge) via half-plane clip.
                Point3dCollection raw = Profile.Intersect(e.Planes);
                if (raw.Count < 3) return;
                cross = new Point3dCollection();
                foreach (Point3d q in raw) cross.Add(new Point3d(q.X, q.Y, z));
            }
            else
            {
                double w = e.Width, d = e.Depth;
                cross = new Point3dCollection
                {
                    new Point3d(a.X - w / 2, a.Y - d / 2, z),
                    new Point3d(a.X + w / 2, a.Y - d / 2, z),
                    new Point3d(a.X + w / 2, a.Y + d / 2, z),
                    new Point3d(a.X - w / 2, a.Y + d / 2, z)
                };
            }
            string size = (int)e.Width + "x" + (int)e.Depth + "x" + Module1.BuyLongFeet(len);
            Module1.DrawElement(cross, len, e.Role, BentNumber, e.Designation, size,
                jointNear: "Butt", jointFar: "Butt");
        }

        // Shoelace signed area in the XY plane. Positive = CCW winding.
        private static double SignedArea(Point3dCollection pts)
        {
            double a = 0;
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                Point3d p = pts[i];
                Point3d q = pts[(i + 1) % n];
                a += p.X * q.Y - q.X * p.Y;
            }
            return a * 0.5;
        }

        // Reverses the vertex order of a Point3dCollection in place.
        private static void ReverseInPlace(Point3dCollection pts)
        {
            var tmp = new System.Collections.Generic.List<Point3d>();
            foreach (Point3d p in pts) tmp.Add(p);
            tmp.Reverse();
            pts.Clear();
            foreach (Point3d p in tmp) pts.Add(p);
        }
    }
}
