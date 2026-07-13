using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;

namespace TimberDraw
{
    // ManagedTimber part: PERSISTENCE -- the frame xrecord (de)serialization, joint-spec
    // map, and scarf/seat node stores. (Verbatim moves; see CLAUDE.md.)
    public static partial class ManagedTimber
    {
        public static Dictionary<int, string> ReadJointSpecs(ObjectId id)
        {
            var map = new Dictionary<int, string>();
            if (id.IsNull) return map;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return map;
            using Transaction tr = doc.Database.TransactionManager.StartTransaction();
            if (tr.GetObject(id, OpenMode.ForRead) is Entity ent && !ent.ExtensionDictionary.IsNull)
            {
                var dict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, OpenMode.ForRead);
                if (dict.Contains(JointSpecsKey)
                    && tr.GetObject(dict.GetAt(JointSpecsKey), OpenMode.ForRead) is Xrecord xr && xr.Data != null)
                {
                    TypedValue[] arr = xr.Data.AsArray();
                    for (int i = 0; i + 1 < arr.Length; i += 2)
                    {
                        int jid = (int)System.Math.Round(Convert.ToDouble(arr[i].Value));
                        string state = arr[i + 1].Value?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(state)) map[jid] = state;
                    }
                }
            }
            tr.Commit();
            return map;
        }

        // Transaction-scoped read of the joint-spec map from an already-open entity (for one-pass gathers
        // like EnumerateForBom). Mirrors the public ReadJointSpecs(ObjectId) reader.
        private static Dictionary<int, string> ReadJointSpecs(Transaction tr, Entity ent)
        {
            var map = new Dictionary<int, string>();
            if (ent.ExtensionDictionary.IsNull) return map;
            var dict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, OpenMode.ForRead);
            if (dict.Contains(JointSpecsKey)
                && tr.GetObject(dict.GetAt(JointSpecsKey), OpenMode.ForRead) is Xrecord xr && xr.Data != null)
            {
                TypedValue[] arr = xr.Data.AsArray();
                for (int i = 0; i + 1 < arr.Length; i += 2)
                {
                    int jid = (int)System.Math.Round(Convert.ToDouble(arr[i].Value));
                    string state = arr[i + 1].Value?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(state)) map[jid] = state;
                }
            }
            return map;
        }

        public static void WriteJointSpec(ObjectId id, int jid, string state)
        {
            Dictionary<int, string> map = ReadJointSpecs(id);
            map[jid] = state ?? "";
            WriteJointSpecsMap(id, map);
        }

        public static void RemoveJointSpec(ObjectId id, int jid)
        {
            Dictionary<int, string> map = ReadJointSpecs(id);
            if (map.Remove(jid)) WriteJointSpecsMap(id, map);
        }

        private static void WriteJointSpecsMap(ObjectId id, Dictionary<int, string> map)
        {
            if (id.IsNull) return;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            using (doc.LockDocument())
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                if (!(tr.GetObject(id, OpenMode.ForWrite) is Entity ent)) { tr.Commit(); return; }
                if (ent.ExtensionDictionary.IsNull) ent.CreateExtensionDictionary();
                var dict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, OpenMode.ForWrite);
                if (map.Count == 0)
                {
                    if (dict.Contains(JointSpecsKey)) dict.Remove(JointSpecsKey);
                    tr.Commit();
                    return;
                }
                var rb = new ResultBuffer();
                foreach (KeyValuePair<int, string> kv in map)
                {
                    rb.Add(new TypedValue((int)DxfCode.Real, (double)kv.Key));
                    rb.Add(new TypedValue((int)DxfCode.Text, kv.Value ?? ""));
                }
                if (dict.Contains(JointSpecsKey))
                {
                    var xr = (Xrecord)tr.GetObject(dict.GetAt(JointSpecsKey), OpenMode.ForWrite);
                    xr.Data = rb;
                }
                else
                {
                    var xr = new Xrecord { Data = rb };
                    dict.SetAt(JointSpecsKey, xr);
                    tr.AddNewlyCreatedDBObject(xr, true);
                }
                tr.Commit();
            }
        }

        // ---- frame storage ----------------------------------------------------------------

        private static ResultBuffer FrameToBuffer(TFrame f)
        {
            double[] v = { f.O.X, f.O.Y, f.O.Z, f.X.X, f.X.Y, f.X.Z, f.Y.X, f.Y.Y, f.Y.Z,
                           f.Z.X, f.Z.Y, f.Z.Z, f.L, f.D, f.W,
                           f.NearN.X, f.NearN.Y, f.NearN.Z, f.FarN.X, f.FarN.Y, f.FarN.Z };
            var rb = new ResultBuffer();
            foreach (double d in v) rb.Add(new TypedValue((int)DxfCode.Real, d));
            // Optional trailer: cut count, then 6 reals per cut (P.xyz, N.xyz). Absent for plain boxes.
            int cutCount = f.Cuts?.Count ?? 0;
            rb.Add(new TypedValue((int)DxfCode.Real, cutCount));
            if (f.Cuts != null)
                foreach ((Point3d P, Vector3d N) c in f.Cuts)
                    foreach (double d in new[] { c.P.X, c.P.Y, c.P.Z, c.N.X, c.N.Y, c.N.Z })
                        rb.Add(new TypedValue((int)DxfCode.Real, d));
            // Second trailer: subtract count, then per polygon (point count, then 2 reals per pt:
            // localLength, localDepth). Always written (even 0) once cuts are present, so the reader
            // can tell a cut-only solid (no second trailer) from one carrying subtracts.
            int subCount = f.Subtracts?.Count ?? 0;
            rb.Add(new TypedValue((int)DxfCode.Real, subCount));
            if (f.Subtracts != null)
                foreach (Point3d[] poly in f.Subtracts)
                {
                    rb.Add(new TypedValue((int)DxfCode.Real, poly.Length));
                    foreach (Point3d p in poly)
                    {
                        rb.Add(new TypedValue((int)DxfCode.Real, p.X));
                        rb.Add(new TypedValue((int)DxfCode.Real, p.Y));
                    }
                }
            // Third trailer: joinery feature count, then 8 reals per feature (Min.xyz, Max.xyz,
            // subtract flag 1/0, joint id). Always written (even 0) so it sits sequentially after the
            // subtracts. Legacy solids wrote 7 reals/feature (no joint id) -- the reader detects width.
            int featCount = f.Features?.Count ?? 0;
            rb.Add(new TypedValue((int)DxfCode.Real, featCount));
            if (f.Features != null)
                foreach ((Point3d Min, Point3d Max, bool Subtract, int Joint) ft in f.Features)
                    foreach (double d in new[] { ft.Min.X, ft.Min.Y, ft.Min.Z, ft.Max.X, ft.Max.Y, ft.Max.Z, ft.Subtract ? 1.0 : 0.0, (double)ft.Joint })
                        rb.Add(new TypedValue((int)DxfCode.Real, d));
            // Fourth trailer: peg count, then 9 reals per peg (C.xyz, Axis.xyz, R, Half, joint id). Absent
            // on pre-peg solids; the reader only reads it when reals remain after the features.
            int pegCount = f.Pegs?.Count ?? 0;
            rb.Add(new TypedValue((int)DxfCode.Real, pegCount));
            if (f.Pegs != null)
                foreach ((Point3d C, Vector3d Axis, double R, double Half, int Joint) pg in f.Pegs)
                    foreach (double d in new[] { pg.C.X, pg.C.Y, pg.C.Z, pg.Axis.X, pg.Axis.Y, pg.Axis.Z, pg.R, pg.Half, (double)pg.Joint })
                        rb.Add(new TypedValue((int)DxfCode.Real, d));
            // Fifth trailer: joint-polygon count, then per poly (point count, joint id, subtract flag 1/0,
            // width band Xlo, Xhi, then 2 reals/pt: localLength, localDepth). Absent on solids written before
            // joint polygons existed; the reader only reads it when reals remain after the pegs. Additive.
            int jsCount = f.JointPolys?.Count ?? 0;
            rb.Add(new TypedValue((int)DxfCode.Real, jsCount));
            if (f.JointPolys != null)
                foreach ((Point3d[] Poly, int Joint, bool Subtract, double Xlo, double Xhi) jp in f.JointPolys)
                {
                    rb.Add(new TypedValue((int)DxfCode.Real, jp.Poly.Length));
                    rb.Add(new TypedValue((int)DxfCode.Real, (double)jp.Joint));
                    rb.Add(new TypedValue((int)DxfCode.Real, jp.Subtract ? 1.0 : 0.0));
                    rb.Add(new TypedValue((int)DxfCode.Real, jp.Xlo));
                    rb.Add(new TypedValue((int)DxfCode.Real, jp.Xhi));
                    foreach (Point3d p in jp.Poly)
                    {
                        rb.Add(new TypedValue((int)DxfCode.Real, p.X));
                        rb.Add(new TypedValue((int)DxfCode.Real, p.Y));
                    }
                }
            // 6th trailer: JointPolysZ (Z-extruded section polygons -- the ridge tongue). Same layout as the
            // 5th; additive (absent on older solids, read only if reals remain).
            int jzCount = f.JointPolysZ?.Count ?? 0;
            rb.Add(new TypedValue((int)DxfCode.Real, jzCount));
            if (f.JointPolysZ != null)
                foreach ((Point3d[] Poly, int Joint, bool Subtract, double Xlo, double Xhi) jp in f.JointPolysZ)
                {
                    rb.Add(new TypedValue((int)DxfCode.Real, jp.Poly.Length));
                    rb.Add(new TypedValue((int)DxfCode.Real, (double)jp.Joint));
                    rb.Add(new TypedValue((int)DxfCode.Real, jp.Subtract ? 1.0 : 0.0));
                    rb.Add(new TypedValue((int)DxfCode.Real, jp.Xlo));
                    rb.Add(new TypedValue((int)DxfCode.Real, jp.Xhi));
                    foreach (Point3d p in jp.Poly)
                    {
                        rb.Add(new TypedValue((int)DxfCode.Real, p.X));
                        rb.Add(new TypedValue((int)DxfCode.Real, p.Y));
                    }
                }
            // 7th trailer: JointPrisms (general oriented prisms -- the purlin dovetail). Per prism: ptCount,
            // joint, subtract, Extrude.xyz, then 3 reals/pt (local x,y,z). Additive (absent on older solids).
            int jpmCount = f.JointPrisms?.Count ?? 0;
            rb.Add(new TypedValue((int)DxfCode.Real, jpmCount));
            if (f.JointPrisms != null)
                foreach ((Point3d[] Poly, Vector3d Extrude, int Joint, bool Subtract) jp in f.JointPrisms)
                {
                    rb.Add(new TypedValue((int)DxfCode.Real, jp.Poly.Length));
                    rb.Add(new TypedValue((int)DxfCode.Real, (double)jp.Joint));
                    rb.Add(new TypedValue((int)DxfCode.Real, jp.Subtract ? 1.0 : 0.0));
                    rb.Add(new TypedValue((int)DxfCode.Real, jp.Extrude.X));
                    rb.Add(new TypedValue((int)DxfCode.Real, jp.Extrude.Y));
                    rb.Add(new TypedValue((int)DxfCode.Real, jp.Extrude.Z));
                    foreach (Point3d p in jp.Poly)
                    {
                        rb.Add(new TypedValue((int)DxfCode.Real, p.X));
                        rb.Add(new TypedValue((int)DxfCode.Real, p.Y));
                        rb.Add(new TypedValue((int)DxfCode.Real, p.Z));
                    }
                }
            return rb;
        }

        private static bool TryReadFrame(Transaction tr, Entity ent, out TFrame f)
        {
            f = default;
            if (ent.ExtensionDictionary.IsNull) return false;
            var dict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, OpenMode.ForRead);
            if (!dict.Contains(FrameKey)) return false;
            var xr = (Xrecord)tr.GetObject(dict.GetAt(FrameKey), OpenMode.ForRead);
            var a = new List<double>();
            foreach (TypedValue tv in xr.Data.AsArray()) a.Add(Convert.ToDouble(tv.Value));
            if (a.Count < 15) return false;
            f = new TFrame
            {
                O = new Point3d(a[0], a[1], a[2]),
                X = new Vector3d(a[3], a[4], a[5]),
                Y = new Vector3d(a[6], a[7], a[8]),
                Z = new Vector3d(a[9], a[10], a[11]),
                L = a[12], D = a[13], W = a[14],
                // End-cut normals: present (21 reals) for mitered braces; default to square ends along
                // the LENGTH (Z) axis for legacy solids that only stored 15.
                NearN = a.Count >= 21 ? new Vector3d(a[15], a[16], a[17]) : new Vector3d(a[9], a[10], a[11]).Negate(),
                FarN = a.Count >= 21 ? new Vector3d(a[18], a[19], a[20]) : new Vector3d(a[9], a[10], a[11])
            };
            // Optional trailers (sequential): [cutCount, 6 reals/cut] then [subCount, per poly: ptCount,
            // 2 reals/pt]. Both absent on legacy solids; the second absent on cut-only (2a) solids.
            int idx = 21;
            if (idx < a.Count)
            {
                int cutCount = (int)System.Math.Round(a[idx++]);
                var cuts = new List<(Point3d, Vector3d)>();
                for (int i = 0; i < cutCount && idx + 5 < a.Count; i++, idx += 6)
                    cuts.Add((new Point3d(a[idx], a[idx + 1], a[idx + 2]),
                              new Vector3d(a[idx + 3], a[idx + 4], a[idx + 5])));
                if (cuts.Count > 0) f.Cuts = cuts;
            }
            if (idx < a.Count)
            {
                int subCount = (int)System.Math.Round(a[idx++]);
                var subs = new List<Point3d[]>();
                for (int i = 0; i < subCount && idx < a.Count; i++)
                {
                    int ptCount = (int)System.Math.Round(a[idx++]);
                    var poly = new List<Point3d>();
                    for (int k = 0; k < ptCount && idx + 1 < a.Count; k++, idx += 2)
                        poly.Add(new Point3d(a[idx], a[idx + 1], 0));
                    if (poly.Count >= 3) subs.Add(poly.ToArray());
                }
                if (subs.Count > 0) f.Subtracts = subs;
            }
            if (idx < a.Count)
            {
                int featCount = (int)System.Math.Round(a[idx++]);
                // Legacy 7-real features (no joint id) wrote NOTHING after, so they fit the remaining reals
                // exactly; everything since writes 8-real features followed by a peg trailer (>= 1 real), so
                // the remainder can never equal featCount*7. Distinguishes legacy (-> Joint 0) from keyed.
                int perFeat = (featCount > 0 && (a.Count - idx) == featCount * 7) ? 7 : 8;
                var feats = new List<(Point3d, Point3d, bool, int)>();
                for (int i = 0; i < featCount && idx + 6 < a.Count; i++, idx += perFeat)
                    feats.Add((new Point3d(a[idx], a[idx + 1], a[idx + 2]),
                               new Point3d(a[idx + 3], a[idx + 4], a[idx + 5]),
                               a[idx + 6] != 0.0,
                               perFeat >= 8 ? (int)System.Math.Round(a[idx + 7]) : 0));
                if (feats.Count > 0) f.Features = feats;
            }
            if (idx < a.Count)   // fourth trailer: pegs (absent on pre-peg solids)
            {
                int pegCount = (int)System.Math.Round(a[idx++]);
                var pegs = new List<(Point3d, Vector3d, double, double, int)>();
                for (int i = 0; i < pegCount && idx + 8 < a.Count; i++, idx += 9)
                    pegs.Add((new Point3d(a[idx], a[idx + 1], a[idx + 2]),
                              new Vector3d(a[idx + 3], a[idx + 4], a[idx + 5]),
                              a[idx + 6], a[idx + 7],
                              (int)System.Math.Round(a[idx + 8])));
                if (pegs.Count > 0) f.Pegs = pegs;
            }
            if (idx < a.Count)   // fifth trailer: joint polygons (absent on pre-joint-polygon solids)
            {
                int jsCount = (int)System.Math.Round(a[idx++]);
                var js = new List<(Point3d[], int, bool, double, double)>();
                for (int i = 0; i < jsCount && idx + 4 < a.Count; i++)
                {
                    int ptCount = (int)System.Math.Round(a[idx++]);
                    int joint = (int)System.Math.Round(a[idx++]);
                    bool subtract = a[idx++] != 0.0;
                    double xlo = a[idx++], xhi = a[idx++];
                    var poly = new List<Point3d>();
                    for (int k = 0; k < ptCount && idx + 1 < a.Count; k++, idx += 2)
                        poly.Add(new Point3d(a[idx], a[idx + 1], 0));
                    if (poly.Count >= 3) js.Add((poly.ToArray(), joint, subtract, xlo, xhi));
                }
                if (js.Count > 0) f.JointPolys = js;
            }
            if (idx < a.Count)   // sixth trailer: Z-extruded joint polygons (the ridge tongue)
            {
                int jzCount = (int)System.Math.Round(a[idx++]);
                var jz = new List<(Point3d[], int, bool, double, double)>();
                for (int i = 0; i < jzCount && idx + 4 < a.Count; i++)
                {
                    int ptCount = (int)System.Math.Round(a[idx++]);
                    int joint = (int)System.Math.Round(a[idx++]);
                    bool subtract = a[idx++] != 0.0;
                    double xlo = a[idx++], xhi = a[idx++];
                    var poly = new List<Point3d>();
                    for (int k = 0; k < ptCount && idx + 1 < a.Count; k++, idx += 2)
                        poly.Add(new Point3d(a[idx], a[idx + 1], 0));
                    if (poly.Count >= 3) jz.Add((poly.ToArray(), joint, subtract, xlo, xhi));
                }
                if (jz.Count > 0) f.JointPolysZ = jz;
            }
            if (idx < a.Count)   // seventh trailer: general oriented prisms (the purlin dovetail)
            {
                int jpmCount = (int)System.Math.Round(a[idx++]);
                var jpm = new List<(Point3d[], Vector3d, int, bool)>();
                for (int i = 0; i < jpmCount && idx + 5 < a.Count; i++)
                {
                    int ptCount = (int)System.Math.Round(a[idx++]);
                    int joint = (int)System.Math.Round(a[idx++]);
                    bool subtract = a[idx++] != 0.0;
                    Vector3d ext = new Vector3d(a[idx], a[idx + 1], a[idx + 2]); idx += 3;
                    var poly = new List<Point3d>();
                    for (int k = 0; k < ptCount && idx + 2 < a.Count; k++, idx += 3)
                        poly.Add(new Point3d(a[idx], a[idx + 1], a[idx + 2]));
                    if (poly.Count >= 3) jpm.Add((poly.ToArray(), ext, joint, subtract));
                }
                if (jpm.Count > 0) f.JointPrisms = jpm;
            }
            return true;
        }

        public static bool TryReadFrame(Database db, ObjectId id, out TFrame f)
        {
            using Transaction tr = db.TransactionManager.StartTransaction();
            f = default;
            if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) return false;
            bool ok = TryReadFrame(tr, ent, out f);
            tr.Commit();
            return ok;
        }

        // Write the TMFrame xrecord on a solid already resident in the current transaction.
        public static void WriteFrameXrecord(Transaction tr, Solid3d solid, TFrame f)
        {
            solid.CreateExtensionDictionary();
            var dict = (DBDictionary)tr.GetObject(solid.ExtensionDictionary, OpenMode.ForWrite);
            var xr = new Xrecord { Data = FrameToBuffer(f) };
            dict.SetAt(FrameKey, xr);
            tr.AddNewlyCreatedDBObject(xr, true);
        }

        // Store the splice point this timber participates in (so TScan can show the node even though the
        // halved interface faces aren't analytic). Transient by construction: erase the timber, node gone.
        public static void WriteScarfNode(Database db, ObjectId id, Point3d cs)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                if (!(tr.GetObject(id, OpenMode.ForWrite) is Entity ent)) { tr.Commit(); return; }
                if (ent.ExtensionDictionary.IsNull) ent.CreateExtensionDictionary();
                var dict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, OpenMode.ForWrite);
                var rb = new ResultBuffer(
                    new TypedValue((int)DxfCode.Real, cs.X),
                    new TypedValue((int)DxfCode.Real, cs.Y),
                    new TypedValue((int)DxfCode.Real, cs.Z));
                var xr = new Xrecord { Data = rb };
                dict.SetAt(ScarfKey, xr);
                tr.AddNewlyCreatedDBObject(xr, true);
                tr.Commit();
            }
        }

        // All scarf splice points stored on managed timbers (deduped -- both halves store the same point).
        public static List<Point3d> EnumerateScarfNodes(Database db)
        {
            var pts = new List<Point3d>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
                    if (ent.ExtensionDictionary.IsNull) continue;
                    var dict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, OpenMode.ForRead);
                    if (!dict.Contains(ScarfKey)) continue;
                    var xr = (Xrecord)tr.GetObject(dict.GetAt(ScarfKey), OpenMode.ForRead);
                    TypedValue[] a = xr.Data.AsArray();
                    if (a.Length < 3) continue;
                    var p = new Point3d(Convert.ToDouble(a[0].Value), Convert.ToDouble(a[1].Value), Convert.ToDouble(a[2].Value));
                    bool dup = false;
                    foreach (Point3d q in pts) if (q.DistanceTo(p) < 0.25) { dup = true; break; }
                    if (!dup) pts.Add(p);
                }
                tr.Commit();
            }
            return pts;
        }

        // Stamp a timber with explicit SEAT nodes (WCS). A bay brace seats into the post and girt along
        // OBLIQUE member-face cut planes, not its perpendicular end caps -- so the analytic Faces() (the
        // nominal box) never mates at the seat and TScan would miss it. The emitter writes the two seat
        // points (centerline x member plane) here so ComputeNodes can surface them, exactly as for scarf.
        public static void WriteSeatNodes(ObjectId id, IList<Point3d> pts)
        {
            if (pts == null || pts.Count == 0) return;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            using (doc.LockDocument())
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                if (!(tr.GetObject(id, OpenMode.ForWrite) is Entity ent)) { tr.Commit(); return; }
                if (ent.ExtensionDictionary.IsNull) ent.CreateExtensionDictionary();
                var dict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, OpenMode.ForWrite);
                var rb = new ResultBuffer();
                foreach (Point3d p in pts)
                {
                    rb.Add(new TypedValue((int)DxfCode.Real, p.X));
                    rb.Add(new TypedValue((int)DxfCode.Real, p.Y));
                    rb.Add(new TypedValue((int)DxfCode.Real, p.Z));
                }
                var xr = new Xrecord { Data = rb };
                dict.SetAt(SeatKey, xr);
                tr.AddNewlyCreatedDBObject(xr, true);
                tr.Commit();
            }
        }

        // All explicit seat points stored on managed timbers (deduped against each other).
        public static List<Point3d> EnumerateSeatNodes(Database db)
        {
            var pts = new List<Point3d>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
                    if (ent.ExtensionDictionary.IsNull) continue;
                    var dict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, OpenMode.ForRead);
                    if (!dict.Contains(SeatKey)) continue;
                    var xr = (Xrecord)tr.GetObject(dict.GetAt(SeatKey), OpenMode.ForRead);
                    TypedValue[] a = xr.Data.AsArray();
                    for (int i = 0; i + 2 < a.Length; i += 3)
                    {
                        var p = new Point3d(Convert.ToDouble(a[i].Value), Convert.ToDouble(a[i + 1].Value), Convert.ToDouble(a[i + 2].Value));
                        bool dup = false;
                        foreach (Point3d q in pts) if (q.DistanceTo(p) < 0.25) { dup = true; break; }
                        if (!dup) pts.Add(p);
                    }
                }
                tr.Commit();
            }
            return pts;
        }
    }
}
