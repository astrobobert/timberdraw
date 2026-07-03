using Autodesk.AutoCAD.DatabaseServices;
namespace TimberDraw
{
    // Scarf B: the "lower" half of a splayed scarf joint; mates with ScarfA.
    // Required Extra keys: "ScarfLength" (same as ScarfAGenerator).
    // Stub -- full geometry implementation in Phase 2.
    public class ScarfBGenerator : IJointGenerator
    {
        public string[] RequiredExtras => new[] { "ScarfLength" };
        public JointResult Generate(JointParams p)
            => new JointResult { JointId = ObjectId.Null, AdditionalSolids = new System.Collections.Generic.List<ObjectId>(), Pegs = new System.Collections.Generic.List<ObjectId>() };
    }
}
