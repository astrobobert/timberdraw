using Autodesk.AutoCAD.DatabaseServices;
namespace TimberDraw
{
    // Contract for all joint geometry generators.
    // Each JointType has exactly one registered generator.
    // New joint types are added by implementing this interface and calling
    // JointFactory.Register() -- no changes to existing code required.
    public interface IJointGenerator
    {
        // Draws the joint solid and returns its ObjectId.
        // Returns ObjectId.Null for no-geometry joints (e.g. Butt).
        JointResult Generate(JointParams p);

        // Keys that this generator reads from JointParams.Extra.
        // TimberTag uses this list to show the correct input fields when the
        // user changes a timber's JointNear or JointFar type.
        string[] RequiredExtras { get; }
    }
}
