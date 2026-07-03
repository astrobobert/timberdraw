using System.Globalization;
using System.Text;
using System.Text.Json;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{
    // Serializes a FrameGraph to / from JSON -- the "database" the frame is recreated
    // from. The stored graph (nodes + edges + half-planes) is the source of truth:
    // FrameRenderer.Draw(FromJson(text)) reproduces the drawing with no params or
    // generator involved. Manual JSON (matches the codebase style; avoids serializer
    // field-inclusion quirks on net48).
    public static class FrameStore
    {
        public static string ToJson(FrameGraph g)
        {
            var ic = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("{\"nodes\":[");
            for (int i = 0; i < g.Nodes.Count; i++)
            {
                FrameNode n = g.Nodes[i];
                if (i > 0) sb.Append(',');
                sb.AppendFormat(ic, "{{\"id\":{0},\"role\":\"{1}\",\"x\":{2},\"y\":{3},\"z\":{4}}}",
                    n.Id, Esc(n.Role), n.Pos.X, n.Pos.Y, n.Pos.Z);
            }
            sb.Append("],\"edges\":[");
            for (int i = 0; i < g.Edges.Count; i++)
            {
                FrameEdge e = g.Edges[i];
                if (i > 0) sb.Append(',');
                sb.AppendFormat(ic,
                    "{{\"id\":{0},\"a\":{1},\"b\":{2},\"w\":{3},\"d\":{4},\"role\":\"{5}\",\"desig\":\"{6}\",\"z\":{7},\"lng\":{8},\"ins\":{9},\"off\":{10},\"planes\":[",
                    e.Id, e.NodeA, e.NodeB, e.Width, e.Depth, Esc(e.Role), Esc(e.Designation), e.ZOffset,
                    e.Longitudinal ? "true" : "false", e.LongInset, e.BayOffset);
                for (int j = 0; j < e.Planes.Count; j++)
                {
                    HalfPlane h = e.Planes[j];
                    if (j > 0) sb.Append(',');
                    sb.AppendFormat(ic, "{{\"px\":{0},\"py\":{1},\"pz\":{2},\"nx\":{3},\"ny\":{4},\"nz\":{5}}}",
                        h.P.X, h.P.Y, h.P.Z, h.N.X, h.N.Y, h.N.Z);
                }
                sb.Append("],\"cp\":[");
                if (e.CustomProfile != null)
                    for (int j = 0; j < e.CustomProfile.Length; j++)
                    {
                        if (j > 0) sb.Append(',');
                        sb.AppendFormat(ic, "{{\"x\":{0},\"y\":{1}}}",
                            e.CustomProfile[j].X, e.CustomProfile[j].Y);
                    }
                sb.Append("]}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        public static FrameGraph FromJson(string json)
        {
            var g = new FrameGraph();
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement r = doc.RootElement;

            foreach (JsonElement n in r.GetProperty("nodes").EnumerateArray())
            {
                g.Nodes.Add(new FrameNode
                {
                    Id   = n.GetProperty("id").GetInt32(),
                    Role = n.GetProperty("role").GetString(),
                    Pos  = new Point3d(n.GetProperty("x").GetDouble(),
                                       n.GetProperty("y").GetDouble(),
                                       n.GetProperty("z").GetDouble())
                });
            }
            foreach (JsonElement e in r.GetProperty("edges").EnumerateArray())
            {
                var fe = new FrameEdge
                {
                    Id          = e.GetProperty("id").GetInt32(),
                    NodeA       = e.GetProperty("a").GetInt32(),
                    NodeB       = e.GetProperty("b").GetInt32(),
                    Width       = e.GetProperty("w").GetDouble(),
                    Depth       = e.GetProperty("d").GetDouble(),
                    Role        = e.GetProperty("role").GetString(),
                    Designation = e.GetProperty("desig").GetString(),
                    ZOffset     = e.TryGetProperty("z", out JsonElement zEl) ? zEl.GetDouble() : 0.0,
                    Longitudinal = e.TryGetProperty("lng", out JsonElement lEl) && lEl.GetBoolean(),
                    LongInset   = e.TryGetProperty("ins", out JsonElement insEl) ? insEl.GetDouble() : 0.0,
                    BayOffset   = e.TryGetProperty("off", out JsonElement offEl) ? offEl.GetDouble() : 0.0
                };
                foreach (JsonElement h in e.GetProperty("planes").EnumerateArray())
                {
                    fe.Planes.Add(new HalfPlane(
                        new Point3d(h.GetProperty("px").GetDouble(),
                                    h.GetProperty("py").GetDouble(),
                                    h.GetProperty("pz").GetDouble()),
                        new Vector3d(h.GetProperty("nx").GetDouble(),
                                     h.GetProperty("ny").GetDouble(),
                                     h.GetProperty("nz").GetDouble())));
                }
                if (e.TryGetProperty("cp", out JsonElement cpEl) && cpEl.GetArrayLength() >= 3)
                {
                    var prof = new System.Collections.Generic.List<Point3d>();
                    foreach (JsonElement q in cpEl.EnumerateArray())
                        prof.Add(new Point3d(q.GetProperty("x").GetDouble(),
                                             q.GetProperty("y").GetDouble(), 0));
                    fe.CustomProfile = prof.ToArray();
                }
                g.Edges.Add(fe);
            }
            return g;
        }

        private static string Esc(string s)
            => string.IsNullOrEmpty(s) ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
