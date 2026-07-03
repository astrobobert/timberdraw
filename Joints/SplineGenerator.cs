using Autodesk.AutoCAD.DatabaseServices;
namespace TimberDraw
{
    // Spline tenon: loose spline (floating tenon) inserted into paired grooves.
    // No Required Extra keys (spline dimensions derived from TenonWidth and Depth).
    // Stub -- full geometry implementation in Phase 2.
    public class SplineGenerator : IJointGenerator
    {
        public string[] RequiredExtras => System.Array.Empty<string>();
        public JointResult Generate(JointParams p)
            => new JointResult { JointId = ObjectId.Null, AdditionalSolids = new System.Collections.Generic.List<ObjectId>(), Pegs = new System.Collections.Generic.List<ObjectId>() };
    }
}
