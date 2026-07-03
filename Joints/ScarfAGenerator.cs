using Autodesk.AutoCAD.DatabaseServices;
namespace TimberDraw
{
    // Scarf A: the "upper" half of a splayed scarf joint (long-axis splice).
    // Required Extra keys:
    //   "ScarfLength" -- total splice length in inches (typically 3x section Depth)
    // Stub -- full geometry implementation in Phase 2.
    public class ScarfAGenerator : IJointGenerator
    {
        public string[] RequiredExtras => new[] { "ScarfLength" };
        public JointResult Generate(JointParams p)
            => new JointResult { JointId = ObjectId.Null, AdditionalSolids = new System.Collections.Generic.List<ObjectId>(), Pegs = new System.Collections.Generic.List<ObjectId>() };
    }
}
