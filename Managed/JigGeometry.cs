using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;

namespace TimberDraw
{
    // Shared ghost drawing for the managed placement jigs. Draws a wireframe box from a near
    // end-face centre, extruded `length` along lengthAxis, with a `depth` x `width` cross-section
    // along depthAxis / widthAxis. When length is ~0 only the near rectangle is drawn (e.g. the
    // TPlace roll phase, where the length isn't set yet).
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

        // Wireframe of a MITERED box (the TJoin knee-brace ghost) from its two end-cap faces
        // (Faces(frame)[0]/[1]: centre + in-plane axes, VHalf already tilt-corrected). The long
        // edges must connect CORRESPONDING corners -- the toe side of one cap to the toe side of
        // the other. Each cap's V axis is tilted in its own miter plane, so the caps cannot be
        // aligned against EACH OTHER (at a 45-degree brace the two Vs are exactly perpendicular
        // and the pairing flipped: heel joined toe -- Robert's catch); each cap is aligned to the
        // FRAME's own width/depth axes (refU/refV) instead, whose signs are end-independent.
        public static void DrawMiteredWire(WorldDraw draw, ManagedTimber.TFace near, ManagedTimber.TFace far,
            Vector3d refU, Vector3d refV, short aci)
        {
            draw.SubEntityTraits.Color = aci;
            double nsu = near.U.DotProduct(refU) >= 0.0 ? 1.0 : -1.0;
            double nsv = near.V.DotProduct(refV) >= 0.0 ? 1.0 : -1.0;
            double fsu = far.U.DotProduct(refU) >= 0.0 ? 1.0 : -1.0;
            double fsv = far.V.DotProduct(refV) >= 0.0 ? 1.0 : -1.0;
            Point3d n0 = near.C + near.U * (nsu * near.UHalf) + near.V * (nsv * near.VHalf);
            Point3d n1 = near.C + near.U * (nsu * near.UHalf) - near.V * (nsv * near.VHalf);
            Point3d n2 = near.C - near.U * (nsu * near.UHalf) - near.V * (nsv * near.VHalf);
            Point3d n3 = near.C - near.U * (nsu * near.UHalf) + near.V * (nsv * near.VHalf);
            Point3d f0 = far.C + far.U * (fsu * far.UHalf) + far.V * (fsv * far.VHalf);
            Point3d f1 = far.C + far.U * (fsu * far.UHalf) - far.V * (fsv * far.VHalf);
            Point3d f2 = far.C - far.U * (fsu * far.UHalf) - far.V * (fsv * far.VHalf);
            Point3d f3 = far.C - far.U * (fsu * far.UHalf) + far.V * (fsv * far.VHalf);
            Rect(draw, n0, n1, n2, n3);
            Rect(draw, f0, f1, f2, f3);
            draw.Geometry.WorldLine(n0, f0);
            draw.Geometry.WorldLine(n1, f1);
            draw.Geometry.WorldLine(n2, f2);
            draw.Geometry.WorldLine(n3, f3);
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
