using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{
    // JointEdge: one timber-to-timber joint relationship in a bent.
    //
    // The giver's end contributes joint geometry (Tenon, Shoulder, ...) that is
    // BoolUnited into the giver body and BoolSubtracted into the receiver.
    // JointType = "Butt" means no geometry is generated for this edge.
    //
    // Params holds the full geometric and scalar data needed to call
    // JointFactory.Create(Params.JointType, Params) and reproduce the exact solid.
    public struct JointEdge
    {
        public Handle      GiverHandle;      // timber that gives the joint
        public short       GiverEnd;         // Module1.End.Near/Far/Body
        public Handle      ReceiverHandle;   // timber that receives the mortise
        public short       ReceiverEnd;      // Module1.End.Body in most cases
        public string      JointType;        // "Tenon" | "Shoulder" | "Butt" | ...
        public JointParams Params;           // geometric + scalar params for JointFactory
    }

    // BentNetwork: document-level joint edge registry.
    //
    // Each TDraw call draws one bent.  At draw time the orchestrator (KPBent,
    // QPBent, etc.) registers every JF-based joint connection and commits them
    // to the AutoCAD Named Object Dictionary.  Regenerate() reads incoming edges
    // for a freshly drawn receiver to re-cut all its mortises exactly.
    //
    // Storage key in NOD:  "BentNetworkV1_<bentNumber>"  (one xrecord per bent)
    // Block size:          EdgeEntrySize = 20 TypedValues per edge
    //
    // Usage in orchestrator Draw():
    //   BentNetwork.BeginBent(bentNumber)
    //   BentNetwork.RegisterEdge(giver, giverEnd, receiver, receiverEnd, type, jp)
    //   ...
    //   BentNetwork.EndBent()   // writes to NOD in one transaction
    //
    // Usage in Regenerate:
    //   JointEdge[] incoming = BentNetwork.GetIncomingEdges(receiverHandle);
    public static class BentNetwork
    {
        // Number of TypedValues in each edge block (fixed, no sentinel needed).
        private const int EdgeEntrySize = 20;
        private const string NomPrefix  = "BentNetworkV1_";

        // In-memory staging list populated between BeginBent / EndBent.
        private static readonly List<JointEdge> _pending = new List<JointEdge>();
        private static string _currentBent = "";

        // -----------------------------------------------------------------------
        // Write path  (called by orchestrator Draw())
        // -----------------------------------------------------------------------

        // Call at the start of the orchestrator's Draw() before any RegisterEdge.
        public static void BeginBent(string bentNumber)
        {
            _currentBent = bentNumber ?? "";
            _pending.Clear();
        }

        // Accumulate one JF-based joint connection into the pending list.
        // Called alongside PrepareIncomingJointRecord for each JF connection.
        // Edges with JointType = "Butt" are stored but skipped during re-cut.
        public static void RegisterEdge(Handle giverHandle, short giverEnd,
            Handle receiverHandle, short receiverEnd,
            string jointType, JointParams jp)
        {
            // Strip GeneratePegs flag -- pegs are never re-generated during re-cut.
            var stored     = jp;
            stored.GeneratePegs = false;
            _pending.Add(new JointEdge
            {
                GiverHandle    = giverHandle,
                GiverEnd       = giverEnd,
                ReceiverHandle = receiverHandle,
                ReceiverEnd    = receiverEnd,
                JointType      = jointType ?? "Butt",
                Params         = stored
            });
        }

        // Call at the end of orchestrator Draw() to persist all registered edges.
        // Overwrites any previous network for this bent number.
        public static void EndBent()
        {
            if (string.IsNullOrEmpty(_currentBent) || _pending.Count == 0)
            {
                _pending.Clear();
                _currentBent = "";
                return;
            }
            WriteEdges(_currentBent, _pending);
            _pending.Clear();
            _currentBent = "";
        }

        // -----------------------------------------------------------------------
        // Read path  (called by TimberFactory during Regenerate)
        // -----------------------------------------------------------------------

        // Returns all edges registered for the given bent number.
        public static JointEdge[] GetEdges(string bentNumber)
        {
            return ReadEdges(bentNumber);
        }

        // Returns all edges where ReceiverHandle == receiverHandle, across all bents.
        // Used by NetworkManager.ReapplyIncoming to re-cut incoming mortises into a
        // freshly drawn receiver without needing the giver to redraw.
        public static JointEdge[] GetIncomingEdges(Handle receiverHandle)
        {
            var result = new List<JointEdge>();
            Database db = HostApplicationServices.WorkingDatabase;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return result.ToArray();
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DBDictionary nod = (DBDictionary)tr.GetObject(
                    db.NamedObjectsDictionaryId,
                    Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
                foreach (DBDictionaryEntry entry in nod)
                {
                    if (!entry.Key.StartsWith(NomPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                    Xrecord xrec = tr.GetObject(entry.Value,
                        Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, false) as Xrecord;
                    if (xrec == null) continue;
                    ParseEdges(xrec.Data.AsArray(), result, receiverHandle);
                }
                tr.Commit();
            }
            return result.ToArray();
        }

        // Returns the single edge for the given giver handle + end (for UpdateEdge / Phase C).
        public static JointEdge FindEdge(Handle giverHandle, short giverEnd)
        {
            Database db = HostApplicationServices.WorkingDatabase;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return default;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DBDictionary nod = (DBDictionary)tr.GetObject(
                    db.NamedObjectsDictionaryId,
                    Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
                foreach (DBDictionaryEntry entry in nod)
                {
                    if (!entry.Key.StartsWith(NomPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                    Xrecord xrec = tr.GetObject(entry.Value,
                        Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, false) as Xrecord;
                    if (xrec == null) continue;
                    var matches = new List<JointEdge>();
                    ParseEdges(xrec.Data.AsArray(), matches, null, giverHandle, giverEnd);
                    if (matches.Count > 0) { tr.Commit(); return matches[0]; }
                }
                tr.Commit();
            }
            return default;
        }

        // Replace every ReceiverHandle == oldHandle with newHandle across all bent records.
        // Called after a receiver timber is regenerated so that the next ReapplyIncoming
        // call for that timber finds its incoming edges under the new entity handle.
        public static void UpdateReceiverHandle(Handle oldHandle, Handle newHandle)
        {
            if (oldHandle == newHandle) return;
            string oldStr = oldHandle.ToString();
            Database db = HostApplicationServices.WorkingDatabase;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DBDictionary nod = (DBDictionary)tr.GetObject(
                    db.NamedObjectsDictionaryId,
                    Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
                foreach (DBDictionaryEntry entry in nod)
                {
                    if (!entry.Key.StartsWith(NomPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                    Xrecord xrec = tr.GetObject(entry.Value,
                        Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite, false) as Xrecord;
                    if (xrec == null) continue;
                    TypedValue[] vals = xrec.Data.AsArray();
                    bool changed = false;
                    for (int i = 0; i + EdgeEntrySize <= vals.Length; i += EdgeEntrySize)
                    {
                        // ReceiverHandle is at offset +2 in each 20-value block.
                        if (vals[i + 2].TypeCode == (int)DxfCode.Handle &&
                            vals[i + 2].Value?.ToString() == oldStr)
                        {
                            vals[i + 2] = new TypedValue((int)DxfCode.Handle, newHandle);
                            changed = true;
                        }
                    }
                    if (changed) xrec.Data = new ResultBuffer(vals);
                }
                tr.Commit();
            }
        }

        // Update an edge's JointType and Params in place (Phase C -- UpdateEdge path).
        // Rewrites the entire bent xrecord with the updated edge.
        public static void UpdateEdge(Handle giverHandle, short giverEnd,
            string newJointType, JointParams newParams)
        {
            Database db = HostApplicationServices.WorkingDatabase;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DBDictionary nod = (DBDictionary)tr.GetObject(
                    db.NamedObjectsDictionaryId,
                    Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
                foreach (DBDictionaryEntry entry in nod)
                {
                    if (!entry.Key.StartsWith(NomPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                    Xrecord xrec = tr.GetObject(entry.Value,
                        Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite, false) as Xrecord;
                    if (xrec == null) continue;
                    var edges = new List<JointEdge>();
                    ParseEdges(xrec.Data.AsArray(), edges, null);
                    bool changed = false;
                    for (int i = 0; i < edges.Count; i++)
                    {
                        var e = edges[i];
                        if (e.GiverHandle == giverHandle && e.GiverEnd == giverEnd)
                        {
                            e.JointType = newJointType ?? "Butt";
                            e.Params    = newParams;
                            e.Params.GeneratePegs = false;
                            edges[i] = e;
                            changed = true;
                        }
                    }
                    if (changed)
                    {
                        var vals = new List<TypedValue>(edges.Count * EdgeEntrySize);
                        foreach (var e in edges)
                            AppendEdgeValues(vals, e);
                        xrec.Data = new ResultBuffer(vals.ToArray());
                    }
                }
                tr.Commit();
            }
        }

        // -----------------------------------------------------------------------
        // Serialization helpers
        // -----------------------------------------------------------------------

        private static void WriteEdges(string bentNumber, List<JointEdge> edges)
        {
            Database db = HostApplicationServices.WorkingDatabase;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            string nomKey = NomPrefix + bentNumber;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DBDictionary nod = (DBDictionary)tr.GetObject(
                    db.NamedObjectsDictionaryId,
                    Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite);
                Xrecord xrec;
                if (nod.Contains(nomKey))
                {
                    xrec = (Xrecord)tr.GetObject(nod.GetAt(nomKey),
                        Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite);
                }
                else
                {
                    xrec = new Xrecord();
                    nod.SetAt(nomKey, xrec);
                    tr.AddNewlyCreatedDBObject(xrec, true);
                }
                var vals = new List<TypedValue>(edges.Count * EdgeEntrySize);
                foreach (JointEdge e in edges)
                    AppendEdgeValues(vals, e);
                xrec.Data = new ResultBuffer(vals.ToArray());
                tr.Commit();
            }
        }

        private static JointEdge[] ReadEdges(string bentNumber)
        {
            string nomKey = NomPrefix + bentNumber;
            Database db = HostApplicationServices.WorkingDatabase;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return Array.Empty<JointEdge>();
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DBDictionary nod = (DBDictionary)tr.GetObject(
                    db.NamedObjectsDictionaryId,
                    Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
                if (!nod.Contains(nomKey)) { tr.Commit(); return Array.Empty<JointEdge>(); }
                Xrecord xrec = (Xrecord)tr.GetObject(nod.GetAt(nomKey),
                    Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
                var result = new List<JointEdge>();
                ParseEdges(xrec.Data.AsArray(), result, null);
                tr.Commit();
                return result.ToArray();
            }
        }

        // Serialize one JointEdge to the 20-value block format.
        //
        // CustomPts (Polygon joints): encoded into the JointType text field as
        //   "Polygon:x0,y0,z0;x1,y1,z1;..."
        // All other joints write the plain type string ("Tenon", "Shoulder", etc.).
        // The 20-value block size is unchanged -- backward compatible with V1 readers.
        private static void AppendEdgeValues(List<TypedValue> vals, JointEdge e)
        {
            JointParams jp = e.Params;
            var ic = System.Globalization.CultureInfo.InvariantCulture;

            // Encode CustomPts into the JointType text field when present.
            string jtText;
            if (jp.CustomPts != null && jp.CustomPts.Length >= 3)
            {
                var sb = new System.Text.StringBuilder("Polygon:");
                for (int pi = 0; pi < jp.CustomPts.Length; pi++)
                {
                    if (pi > 0) sb.Append(';');
                    var pt = jp.CustomPts[pi];
                    sb.AppendFormat(ic, "{0:G10},{1:G10},{2:G10}", pt.X, pt.Y, pt.Z);
                }
                jtText = sb.ToString();
            }
            else
            {
                jtText = e.JointType ?? "Butt";
            }

            vals.Add(new TypedValue((int)DxfCode.Handle, e.GiverHandle));
            vals.Add(new TypedValue(70, e.GiverEnd));
            vals.Add(new TypedValue((int)DxfCode.Handle, e.ReceiverHandle));
            vals.Add(new TypedValue(70, e.ReceiverEnd));
            vals.Add(new TypedValue((int)DxfCode.Text,   jtText));
            vals.Add(new TypedValue(70, (short)jp.JointType));
            vals.Add(new TypedValue(10, jp.Origin));
            vals.Add(new TypedValue(40, jp.FaceNormal.X));
            vals.Add(new TypedValue(40, jp.FaceNormal.Y));
            vals.Add(new TypedValue(40, jp.FaceNormal.Z));
            vals.Add(new TypedValue(40, jp.LateralDir.X));
            vals.Add(new TypedValue(40, jp.LateralDir.Y));
            vals.Add(new TypedValue(40, jp.LateralDir.Z));
            vals.Add(new TypedValue(40, jp.Width));
            vals.Add(new TypedValue(40, jp.Depth));
            vals.Add(new TypedValue(40, jp.TenonWidth));
            vals.Add(new TypedValue(40, jp.TopRelish));
            vals.Add(new TypedValue(40, jp.ShoulderDepth));
            vals.Add(new TypedValue(40, jp.HousingDepth));
            vals.Add(new TypedValue(40, jp.Pitch));
        }

        // Deserialize edge blocks.  Filter by receiverHandle or giverHandle+giverEnd
        // when those arguments are provided (non-null / non-default).
        //
        // Polygon edges: JointType text field carries "Polygon:x0,y0,z0;x1,y1,z1;..."
        // -- decoded back to JointParams.CustomPts.  All other edges parse as before.
        private static void ParseEdges(TypedValue[] vals, List<JointEdge> result,
            Handle? receiverFilter,
            Handle? giverFilter = null, short giverEndFilter = -1)
        {
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            for (int i = 0; i + EdgeEntrySize <= vals.Length; i += EdgeEntrySize)
            {
                JointEdge e;
                try
                {
                    e.GiverHandle    = Module1.StringToHandle((string)vals[i +  0].Value);
                    e.GiverEnd       = (short)vals[i +  1].Value;
                    e.ReceiverHandle = Module1.StringToHandle((string)vals[i +  2].Value);
                    e.ReceiverEnd    = (short)vals[i +  3].Value;
                    string jtText    = (string)vals[i +  4].Value;
                    var jt           = (Module1.JointType)(short)vals[i +  5].Value;
                    var org          = (Point3d)vals[i +  6].Value;
                    double fnX = (double)vals[i + 7].Value, fnY = (double)vals[i +  8].Value,
                           fnZ = (double)vals[i + 9].Value;
                    double ldX = (double)vals[i +10].Value, ldY = (double)vals[i + 11].Value,
                           ldZ = (double)vals[i +12].Value;
                    double w   = (double)vals[i +13].Value, d   = (double)vals[i + 14].Value;
                    double tw  = (double)vals[i +15].Value, rel = (double)vals[i + 16].Value;
                    double sh  = (double)vals[i +17].Value, hd  = (double)vals[i + 18].Value;
                    double pit = (double)vals[i +19].Value;

                    e.Params = new JointParams(jt, org,
                        new Vector3d(fnX, fnY, fnZ), new Vector3d(ldX, ldY, ldZ),
                        w, d, tw, "", "", rel, sh, false, hd, pit);

                    // Decode CustomPts when the text field starts with "Polygon:".
                    if (jtText.StartsWith("Polygon:", StringComparison.OrdinalIgnoreCase))
                    {
                        string[] ptStrs = jtText.Substring(8).Split(';');
                        var customPts = new Point3d[ptStrs.Length];
                        for (int pi = 0; pi < ptStrs.Length; pi++)
                        {
                            string[] xyz = ptStrs[pi].Split(',');
                            customPts[pi] = new Point3d(
                                double.Parse(xyz[0], ic),
                                double.Parse(xyz[1], ic),
                                double.Parse(xyz[2], ic));
                        }
                        e.Params.CustomPts = customPts;
                        e.JointType = "Polygon";
                    }
                    else
                    {
                        e.JointType = jtText;
                    }
                }
                catch { continue; }

                if (receiverFilter.HasValue && e.ReceiverHandle != receiverFilter.Value) continue;
                if (giverFilter.HasValue &&
                    (e.GiverHandle != giverFilter.Value || e.GiverEnd != giverEndFilter)) continue;
                result.Add(e);
            }
        }
    }
}
