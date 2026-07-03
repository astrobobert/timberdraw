using Autodesk.AutoCAD.DatabaseServices;
namespace TimberDraw
{
    // Birdmouth housing: the receiving notch in the post/plate top that matches the rafter's birdmouth.
    // Required Extra keys: "Pitch", "SeatDepth" (same as BirdmouthGenerator).
    // Stub -- full geometry implementation in Phase 2.
    public class BirdmouthHousingGenerator : IJointGenerator
    {
        public string[] RequiredExtras => new[] { "Pitch", "SeatDepth" };
        public JointResult Generate(JointParams p)
            => new JointResult { JointId = ObjectId.Null, AdditionalSolids = new System.Collections.Generic.List<ObjectId>(), Pegs = new System.Collections.Generic.List<ObjectId>() };
    }
}
