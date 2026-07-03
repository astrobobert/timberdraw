using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
namespace TimberDraw
{
    // Extrudes an arbitrary polygon profile supplied via JointParams.CustomPts.
    //
    // This generator is the escape hatch for joint shapes that the standard
    // parameterised generators (TenonGenerator, ShoulderGenerator, etc.) cannot
    // express -- principally oblique tenons on sloped members where the cross-
    // section is a non-rectangular quadrilateral computed with PolarPoint calls.
    //
    // Design contract:
    //   p.CustomPts -- 3+ points in bent-local coordinates (same convention as
    //                  Origin in all other JointParams users).  Module1.DrawElement
    //                  adds the bent-insertion Module1.StartPoint offset, so callers
    //                  capture the local pts at RegisterEdge time exactly as they
    //                  were computed during Draw().
    //   p.Width     -- extrusion depth (timber section width).
    //
    // All other JointParams fields are ignored.
    //
    // Registration: JointFactory.RegisterDefaults() → JointType.Polygon.
    public class PolygonGenerator : IJointGenerator
    {
        public string[] RequiredExtras => System.Array.Empty<string>();

        public JointResult Generate(JointParams p)
        {
            var empty = new JointResult
            {
                JointId          = ObjectId.Null,
                AdditionalSolids = new List<ObjectId>(),
                Pegs             = new List<ObjectId>()
            };

            if (p.CustomPts == null || p.CustomPts.Length < 3)
                return empty;

            var col = new Point3dCollection(p.CustomPts);
            ObjectId id = Module1.DrawElement(
                col, p.Width, "Tenon", p.BentNumber, p.Designation);

            return new JointResult
            {
                JointId          = id,
                AdditionalSolids = new List<ObjectId>(),
                Pegs             = new List<ObjectId>()
            };
        }
    }
}
