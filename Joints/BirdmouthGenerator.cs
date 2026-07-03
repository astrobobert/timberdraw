using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
namespace TimberDraw
{
    // Birdmouth: triangular seat notch cut into a rafter where it bears on a plate or post top.
    // Generates the notch solid (the material to be removed) as a triangular prism, Width wide.
    // Caller: AddJoint(timberId, birdId, Joint.Mortise) to subtract the notch, then DeleteJoint.
    //
    // Frame convention:
    //   Origin     -- notch corner (intersection of plumb cut foot and seat cut)
    //   FaceNormal -- direction of the seat cut, going INTO the rafter (away from bearing surface)
    //                 For a left rafter: (+1, 0, 0); for a right rafter: (-1, 0, 0)
    //   LateralDir -- direction of the plumb cut, going upward: (0, 1, 0)
    //   Width      -- rafter section width (full-Width extrusion, same as housing in TenonGenerator)
    //   Extra["SeatDepth"] -- plumb height of the notch in inches
    //   Extra["Pitch"]     -- roof pitch as rise/run ratio (e.g. 0.5 for 6/12)
    //
    // Notch triangle cross-section:
    //   b0 = Origin                                     (notch corner)
    //   b1 = Origin + LateralDir * SeatDepth            (top of plumb cut)
    //   b2 = Origin + FaceNormal * (SeatDepth / Pitch)  (end of seat cut)
    //
    // The hypotenuse b1->b2 lies along the rafter bottom face (slope = Pitch).
    // Winding fix: reverse {b2,b1,b0} when LateralDir x FaceNormal has negative Z
    // (same convention as TenonGenerator -- ensures CCW for correct +Z extrusion).
    // baseZ mirrors TenonGenerator housing formula (full-Width extrusion from timber base).
    public class BirdmouthGenerator : IJointGenerator
    {
        public string[] RequiredExtras => new[] { "Pitch", "SeatDepth" };

        public JointResult Generate(JointParams p)
        {
            p.Extra.TryGetValue("SeatDepth", out double seatDepth);
            if (seatDepth <= 0) seatDepth = 2.0;
            p.Extra.TryGetValue("Pitch", out double pitch);
            if (pitch <= 0) pitch = 0.5;

            // Winding: reverse when LateralDir x FaceNormal has negative Z.
            bool reverse = p.LateralDir.CrossProduct(p.FaceNormal).Z < 0;

            // baseZ: bottom of the timber in 3D -- mirrors TenonGenerator housing formula.
            double baseZ = Module1.Make3D ? p.Origin.Z - (p.Width - 2.0) / 2.0 : p.Origin.Z;

            Point3d b0 = new(p.Origin.X, p.Origin.Y, baseZ);
            Point3d b1 = b0 + p.LateralDir * seatDepth;
            Point3d b2 = b0 + p.FaceNormal  * (seatDepth / pitch);

            Point3dCollection pts = reverse
                ? new() { b2, b1, b0 }
                : new() { b0, b1, b2 };

            ObjectId birdId = Module1.DrawElement(pts, p.Width, "Tenon", p.BentNumber, p.Designation);

            return new JointResult
            {
                JointId          = birdId,
                AdditionalSolids = new List<ObjectId>(),
                Pegs             = new List<ObjectId>()
            };
        }
    }
}
