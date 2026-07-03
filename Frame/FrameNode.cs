using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{
    // A node in the structural frame graph: a point where members meet.
    // Pos is a bent-plane reference point (XY; Z = bent offset, 0 for a single bent).
    // Role is a human-readable tag (e.g. "PostBaseL", "EaveL", "Apex") used for
    // debugging and, later, for attaching joinery at the node.
    public class FrameNode
    {
        public int Id;
        public Point3d Pos;
        public string Role;
    }
}
