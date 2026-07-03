using Autodesk.AutoCAD.DatabaseServices;
namespace TimberDraw
{
    // Dovetail housing: trapezoidal void matching the paired dovetail tenon.
    // Required Extra keys: "TaperAngle", "TenonLength" (same as DovetailGenerator).
    // Stub -- full geometry implementation in Phase 2.
    public class DovetailHousingGenerator : IJointGenerator
    {
        public string[] RequiredExtras => new[] { "TaperAngle", "TenonLength" };
        public JointResult Generate(JointParams p)
            => new JointResult { JointId = ObjectId.Null, AdditionalSolids = new System.Collections.Generic.List<ObjectId>(), Pegs = new System.Collections.Generic.List<ObjectId>() };
    }
}
