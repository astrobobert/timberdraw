using System.Collections.Generic;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{
    // An edge in the structural frame graph: one timber member spanning two nodes.
    // The body is the convex intersection of Planes (two long faces + neighbour cuts).
    // Width = section width (Z extrusion); Depth = section depth (for sizing/labels).
    // No joinery is stored here -- joints will be a later layer hung off the nodes.
    public class FrameEdge
    {
        public int Id;
        public int NodeA;     // reference-line start node id
        public int NodeB;     // reference-line end node id
        public double Width;
        public double Depth;
        public string Role;   // "Post" | "Girt" | "Rafter" | "KingPost" | "Ridge" | "Plate"
        public string Designation;
        public double ZOffset;       // in-bent member: placement (back/center/front) offset
        public bool   Longitudinal;  // true = bay member: cross-section extruded ALONG Z A->B
        public double LongInset;     // bay member: Z inset at the A end (= bent thickness)
        public double BayOffset;     // roof-member position metadata: commons = centerline
                                     // offset from the near bent (nodeA) along Z; purlins =
                                     // station offset from the eave girt up the rake.
        public Point3d[] CustomProfile; // explicit XY cross-section (concave OK, e.g. birdsmouth);
                                        // when set, the renderer extrudes it instead of Planes
        public bool   SquareTail;    // common rafter: cut the TAIL (foot) end square (perpendicular to the
                                     // rafter) instead of plumb (vertical fascia). Only when a tail exists.
        public bool   FreeBox;       // true = a straight box member between NodeA..NodeB in ANY
                                     // orientation (e.g. a wall-plane bay brace). The emitter builds a
                                     // plain W x D x L box from the two node points (butt ends, no
                                     // half-planes) -- the universal-timber path.
        // Organization tags (the grouping layer): which bent / bay this edge belongs to, so the
        // emitted managed timber can be tagged + grouped + per-frame redrawn. Set by the
        // Build(FrameSpec) walk (Arabic bent number "1"..; Roman bay numeral "I"..). Empty when the
        // graph was built from a single bent (Build(KPBentParams)); the emitter then falls back.
        public string BentTag = "";
        public string BayTag = "";
        public List<HalfPlane> Planes = new List<HalfPlane>();
    }
}
