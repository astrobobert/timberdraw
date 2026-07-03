using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
namespace TimberDraw
{
    // Shoulder: right-triangle bearing notch at the rafter-to-kingpost peak connection.
    // Generates a 3-point right-triangle prism extruded along the timber Width (Z axis).
    //   (a) BoolUnited INTO the rafter body (the triangle protrudes from the plumb face)
    //   (b) Subtracted from the kingpost via AddMortise to cut the matching notch.
    //
    // Frame convention:
    //   Origin      -- bearing corner: bottom of the plumb face (at rafter bottom level)
    //   FaceNormal  -- rafter axis direction (along the rafter bottom face)
    //                  Left rafter:  (cos Beta, sin Beta, 0)
    //                  Right rafter: (-cos Beta, sin Beta, 0)
    //   LateralDir  -- up the plumb face: (0, 1, 0)
    //   Width       -- member section width (full-Width extrusion along Z)
    //   Depth       -- leg c on plumb face = sitdepth / sin(Beta)  (caller computes this)
    //   ShoulderDepth -- leg a along rafter axis = sitdepth (user-adjustable, default 3")
    //
    // Triangle cross-section -- right angle at s2 (end of leg a):
    //   s0 = Origin                              (bearing corner on plumb face)
    //   s1 = s0 + LateralDir * Depth             (end of leg c, on plumb face above s0)
    //   s2 = s0 + FaceNormal * ShoulderDepth     (right-angle corner, sitdepth along rafter axis)
    //
    //   a = s0->s2: along rafter axis,        length = sitdepth         (fixed, user-adjustable)
    //   b = s2->s1: perp to rafter axis,      length = sitdepth*cot(B)  (varies with pitch)
    //   c = s1->s0: on plumb face (vertical), length = sitdepth/sin(B)  (varies with pitch)
    //   Right angle at s2. Legs a==b only at 45-deg pitch (cot 45 = 1).
    //
    // sitdepth is stored in ShoulderGenerator.RequiredExtras as "sitdepth" so TimberTag
    // shows a numeric field for it. The caller computes Depth = sin(Beta)*sitdepth.
    //
    // Winding fix: reverse {s2,s1,s0} when LateralDir x FaceNormal has negative Z.
    //   Left rafter  (FaceNormal = cos,sin,0): Z = -cos Beta < 0 -> reverse=true  -> {s2,s1,s0}
    //   Right rafter (FaceNormal = -cos,sin,0): Z = +cos Beta > 0 -> reverse=false -> {s0,s1,s2}
    // baseZ: mirrors TenonGenerator housing formula for consistent 3D placement.
    public class ShoulderGenerator : IJointGenerator
    {
        public string[] RequiredExtras => new[] { "sitdepth" };

        public JointResult Generate(JointParams p)
        {
            bool reverse = p.LateralDir.CrossProduct(p.FaceNormal).Z < 0;

            // baseZ: bottom of timber in 3D -- same formula as TenonGenerator housing.
            double baseZ = Module1.Make3D ? p.Origin.Z - (p.Width - 2.0) / 2.0 : p.Origin.Z;

            Point3d s0 = new(p.Origin.X, p.Origin.Y, baseZ);
            Point3d s1 = s0 + p.LateralDir * p.Depth;              // face top (height = Depth)
            Point3d s2 = s0 + p.FaceNormal  * p.ShoulderDepth;     // horizontal bearing corner

            Point3dCollection pts = reverse
                ? new() { s2, s1, s0 }
                : new() { s0, s1, s2 };

            ObjectId shoulderId = Module1.DrawElement(pts, p.Width, "Tenon", p.BentNumber, p.Designation);

            return new JointResult
            {
                JointId          = shoulderId,
                AdditionalSolids = new List<ObjectId>(),
                Pegs             = new List<ObjectId>()
            };
        }
    }
}
