using System.Collections.Generic;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{
    // A structural frame as a graph of nodes (junctions) and edges (members).
    // Pure data container -- no AutoCAD entity dependency, so it can later be
    // serialized to / rebuilt from a database (the "recreate from a few params" goal).
    public class FrameGraph
    {
        public List<FrameNode> Nodes = new List<FrameNode>();
        public List<FrameEdge> Edges = new List<FrameEdge>();

        // Add a node, returns its id (= index).
        public int AddNode(string role, Point3d pos)
        {
            int id = Nodes.Count;
            Nodes.Add(new FrameNode { Id = id, Role = role, Pos = pos });
            return id;
        }

        // Add an edge between two existing node ids; returns it so the caller can
        // populate its half-planes.
        public FrameEdge AddEdge(string role, int nodeA, int nodeB,
            double width, double depth, string designation)
        {
            var e = new FrameEdge
            {
                Id          = Edges.Count,
                NodeA       = nodeA,
                NodeB       = nodeB,
                Width       = width,
                Depth       = depth,
                Role        = role,
                Designation = designation
            };
            Edges.Add(e);
            return e;
        }

        public FrameNode Node(int id) => Nodes[id];
    }
}
