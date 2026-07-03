using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
namespace TimberDraw
{
    // Result of a joint generator: the joint solid plus any pegs it produced.
    // JointId is ObjectId.Null for no-geometry joints (e.g. Butt).
    // Pegs is never null (empty list when the generator drew no pegs).
    public struct JointResult
    {
        public ObjectId JointId;
        // Shoulder pads and other full-Width additive solids. Each is AddJoint(Tenon)'d
        // into the girt body, then DeleteJoint'd (transitional: when posts are migrated
        // these will be subtracted from the post instead via orchestrator AddMortise calls).
        public List<ObjectId> AdditionalSolids;
        public List<ObjectId> Pegs;
    }
}
