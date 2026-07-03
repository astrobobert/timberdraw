using Autodesk.AutoCAD.DatabaseServices;
namespace TimberDraw
{
    // Dovetail tenon: trapezoidal cross-section, tapered at ~1:8 on both sides.
    // Required Extra keys:
    //   "TaperAngle" -- taper ratio (default 0.125 = 1:8)
    //   "TenonLength" -- projection in inches (default 4.0)
    // Stub -- full geometry implementation in Phase 2.
    public class DovetailGenerator : IJointGenerator
    {
        public string[] RequiredExtras => new[] { "TaperAngle", "TenonLength" };
        public JointResult Generate(JointParams p)
            => new JointResult { JointId = ObjectId.Null, AdditionalSolids = new System.Collections.Generic.List<ObjectId>(), Pegs = new System.Collections.Generic.List<ObjectId>() };
    }
}
