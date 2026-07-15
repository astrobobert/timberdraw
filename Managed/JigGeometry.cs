using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;

namespace TimberDraw
{
    // Wire-box drawing for the ScarfJig REGION ghost -- the one remaining DrawJig. The scarf ghost
    // marks a cut REGION on an existing timber (a solid there would bury the beam), so it stays a
    // wire box; the free-TIMBER placement previews (TPlace/TSpan/TJoin) are real db solids now
    // (SolidGhost -- Robert's call, 2026-07-15). Draws from a near end-face centre, extruded
    // `length` along lengthAxis, with a `depth` x `width` cross-section.
    internal static class JigGeometry
    {
        public static void DrawBoxWire(WorldDraw draw, Point3d nearCenter,
            Vector3d lengthAxis, double length, Vector3d depthAxis, double depth,
            Vector3d widthAxis, double width, short aci)
        {
            draw.SubEntityTraits.Color = aci;
            double hd = depth / 2.0, hw = width / 2.0;

            Point3d n0 = nearCenter + depthAxis * hd + widthAxis * hw;
            Point3d n1 = nearCenter + depthAxis * hd - widthAxis * hw;
            Point3d n2 = nearCenter - depthAxis * hd - widthAxis * hw;
            Point3d n3 = nearCenter - depthAxis * hd + widthAxis * hw;
            Rect(draw, n0, n1, n2, n3);

            if (length > 1e-6)
            {
                Vector3d ext = lengthAxis * length;
                Point3d f0 = n0 + ext, f1 = n1 + ext, f2 = n2 + ext, f3 = n3 + ext;
                Rect(draw, f0, f1, f2, f3);
                draw.Geometry.WorldLine(n0, f0);
                draw.Geometry.WorldLine(n1, f1);
                draw.Geometry.WorldLine(n2, f2);
                draw.Geometry.WorldLine(n3, f3);
            }
        }

        private static void Rect(WorldDraw draw, Point3d a, Point3d b, Point3d c, Point3d d)
        {
            draw.Geometry.WorldLine(a, b);
            draw.Geometry.WorldLine(b, c);
            draw.Geometry.WorldLine(c, d);
            draw.Geometry.WorldLine(d, a);
        }
    }
}
