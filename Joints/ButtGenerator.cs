using Autodesk.AutoCAD.DatabaseServices;
namespace TimberDraw
{
    // Butt joint: bearing face only, no protruding geometry, no pegs.
    // Returns ObjectId.Null -- the parent timber is untouched at this end.
    // Posts default to Butt at both ends and can be changed to any other
    // end-condition type via the TimberTag joint-type selector.
    public class ButtGenerator : IJointGenerator
    {
        public string[] RequiredExtras => System.Array.Empty<string>();
        public JointResult Generate(JointParams p)
            => new()
            {   JointId = ObjectId.Null,
                AdditionalSolids = new System.Collections.Generic.List<ObjectId>(),
                Pegs = new System.Collections.Generic.List<ObjectId>()
            };
    }
}
