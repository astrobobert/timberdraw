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
    // ManagedTimber part: QUERIES -- drawing enumeration (BOM/shop info), node computation,
    // and interactive face picking. (Verbatim moves; see CLAUDE.md.)
    public static partial class ManagedTimber
    {
        // ---- joint-id primitives over the five feature kinds (box features + pegs + the three polygon
        //      families): "which joints does this timber carry" and "remove this joint from this timber".
        //      The single definitions -- shared by the Joints pane, TJointSync, and the regen orphan sweep
        //      in EraseFrame. -----------------------------------------------------------------------------

        // Every joint id present in the frame's GEOMETRY (id 0 = legacy unidentified cuts, excluded).
        public static HashSet<int> JointIds(TFrame f)
        {
            var s = new HashSet<int>();
            if (f.Features    != null) foreach (var x in f.Features)    s.Add(x.Joint);
            if (f.Pegs        != null) foreach (var x in f.Pegs)        s.Add(x.Joint);
            if (f.JointPolys  != null) foreach (var x in f.JointPolys)  s.Add(x.Joint);
            if (f.JointPolysZ != null) foreach (var x in f.JointPolysZ) s.Add(x.Joint);
            if (f.JointPrisms != null) foreach (var x in f.JointPrisms) s.Add(x.Joint);
            s.Remove(0);
            return s;
        }

        // Erase a joint from a frame COMPLETELY -- every feature kind that can carry an id.
        public static void StripJoint(ref TFrame f, int id)
        {
            if (id == 0) return;
            f.Features?.RemoveAll(x => x.Joint == id);
            f.Pegs?.RemoveAll(x => x.Joint == id);
            f.JointPolys?.RemoveAll(x => x.Joint == id);
            f.JointPolysZ?.RemoveAll(x => x.Joint == id);
            f.JointPrisms?.RemoveAll(x => x.Joint == id);
        }

        // WCS frames of every managed timber stamped with `frameTag` (null = all managed timbers).
        // Includes both parametric-emitted timbers and sub timbers assigned via TAssign -- the source
        // for the drawing-derived structural grid (so adding/removing a sub shifts the numbering).
        public static List<TFrame> EnumerateFrameFrames(Database db, string frameTag)
        {
            var list = new List<TFrame>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
                    if (!TryReadFrame(tr, ent, out TFrame f)) continue;
                    if (frameTag != null && ReadXTextField(tr, ent, "FrameTag") != frameTag) continue;
                    list.Add(f);
                }
                tr.Commit();
            }
            return list;
        }

        // All managed timbers in the current space (those carrying a frame xrecord).
        public static List<(ObjectId Id, TFrame F)> Enumerate(Database db)
        {
            var list = new List<(ObjectId, TFrame)>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
                    if (TryReadFrame(tr, ent, out TFrame f)) list.Add((id, f));
                }
                tr.Commit();
            }
            return list;
        }

        // Like Enumerate, but also reads each timber's ROLE (the XData "Type" field) so node detection
        // can reject role pairs that are never a direct joint (e.g. a principal rafter and the ridge --
        // their nominal boxes coincide at the king-post shoulder, but the rafter joints to the king post).
        public static List<(ObjectId Id, TFrame F, string Role)> EnumerateWithRole(Database db)
        {
            var list = new List<(ObjectId, TFrame, string)>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
                    if (TryReadFrame(tr, ent, out TFrame f)) list.Add((id, f, ReadXTextField(tr, ent, "Type")));
                }
                tr.Commit();
            }
            return list;
        }

        // A managed timber's data assembled for TBom: identity + frame + the MEASURED overall length along
        // the length axis. Overall INCLUDES projecting tenons and reflects brace / miter / chamfer trims,
        // unlike the nominal F.L (shoulder-to-shoulder box, over-long on FreeBox braces).
        public sealed class TimberBom
        {
            public ObjectId Id;
            public TFrame F;
            public string Type;
            public string Label;
            public string Designation;
            public double Overall;
            public Dictionary<int, string> Specs;  // joint id -> ConnectionType state (Join-pane / stamped cuts)
        }

        // One-pass gather for TBom (one transaction): per managed timber, its frame + identity (role /
        // grid label / designation) + the overall length, measured as the finished solid's extent along
        // its own length axis F.Z.
        public static List<TimberBom> EnumerateForBom(Database db)
        {
            var list = new List<TimberBom>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
                    if (!TryReadFrame(tr, ent, out TFrame f)) continue;
                    list.Add(new TimberBom
                    {
                        Id          = id,
                        F           = f,
                        Type        = ReadXTextField(tr, ent, "Type"),
                        Label       = ReadXTextField(tr, ent, "GridLabel"),
                        Designation = ReadXTextField(tr, ent, "Designation"),
                        Overall     = MeasureAlongZ(ent, f),
                        Specs       = ReadJointSpecs(tr, ent)
                    });
                }
                tr.Commit();
            }
            return list;
        }

        // A managed timber's data assembled for TShop (the 2D assembly maps): frame + identity +
        // grouping tags (bent / wall / bay). Mirrors EnumerateForBom but reads the grouping keys the
        // shop maps partition on (BentNumber / WallTag / BayTag) rather than the measured length + specs.
        public sealed class ShopInfo
        {
            public ObjectId Id;
            public TFrame F;
            public string Role;        // XData "Type"
            public string GridLabel;   // installer label ("1A", "EG-A-I", "*")
            public string BentTag;     // XData "BentNumber" (Arabic; blank on free timbers)
            public string WallTag;     // XData "WallTag" (letter; blank on cross members)
            public string BayTag;      // XData "BayTag" (Roman)
            public string FloorTag;    // XData "FloorTag" (digits, floor level bottom-up; joists/summers)
            public string Designation; // generic role designation (fallback label)
            public string Layer;       // the solid's group layer (TM_<frame>_Bent<n> / _Wall<x>) -- the
                                       // emitter's own bent/wall grouping (authoritative; bay braces too)
        }

        // One-pass gather for TShop (one transaction): per managed timber, its frame + identity +
        // the bent/wall/bay grouping tags. The shop-map builder groups these into bent / wall / plan maps.
        public static List<ShopInfo> EnumerateForShop(Database db)
        {
            var list = new List<ShopInfo>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
                    if (!TryReadFrame(tr, ent, out TFrame f)) continue;
                    list.Add(new ShopInfo
                    {
                        Id          = id,
                        F           = f,
                        Role        = ReadXTextField(tr, ent, "Type"),
                        GridLabel   = ReadXTextField(tr, ent, "GridLabel"),
                        BentTag     = ReadXTextField(tr, ent, "BentNumber"),
                        WallTag     = ReadXTextField(tr, ent, "WallTag"),
                        BayTag      = ReadXTextField(tr, ent, "BayTag"),
                        FloorTag    = ReadXTextField(tr, ent, "FloorTag"),
                        Designation = ReadXTextField(tr, ent, "Designation"),
                        Layer       = ent.Layer
                    });
                }
                tr.Commit();
            }
            return list;
        }

        // Overall length of a finished solid along its length axis F.Z: clone the solid, align F.Z to world
        // Z, and read the clone's Z-extent (the clone+TransformBy+Bounds pattern from TimberTag.MinBoundBox).
        // This captures tenon stubs projecting past the ends AND the seat/miter trims that shorten a brace.
        // Falls back to the nominal F.L on any failure.
        private static double MeasureAlongZ(Entity ent, TFrame f)
        {
            try
            {
                using (Entity clone = (Entity)ent.Clone())
                {
                    Matrix3d m = Matrix3d.AlignCoordinateSystem(
                        f.O, f.X, f.Y, f.Z,
                        Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis);
                    clone.TransformBy(m);
                    Extents3d? b = clone.Bounds;
                    if (b.HasValue) return b.Value.MaxPoint.Z - b.Value.MinPoint.Z;
                }
            }
            catch { }
            return f.L;
        }

        // Unordered role pairs that must NEVER produce a coincidence node: their solids touch only as a
        // bounding-box artifact, not a real joint. Rafter|Ridge -- the principal rafter butts the king
        // post (its real joint) while the ridge is carried on the king-post tops; with ridge width ~
        // king-post depth their faces fall in the same plane and would otherwise node spuriously.
        private static readonly HashSet<string> NonJointRolePairs = new HashSet<string> { "Rafter|Ridge" };

        private static bool RolePairExcluded(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            string key = string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;
            return NonJointRolePairs.Contains(key);
        }

        // Derive the coincidence nodes of all managed timbers: every opposing+coplanar+overlapping
        // face pair (O(N^2)) plus the explicit scarf-splice nodes. Pure read -- no markers drawn.
        // Shared by TScan and the Frame Browser (Phase 3).
        public static List<Point3d> ComputeNodes(Database db)
        {
            const double tol = 0.05;
            const double dedup = 0.25;   // collapse coincident nodes (same predicate as scarf/seat)
            var t = EnumerateWithRole(db);
            var nodes = new List<Point3d>();
            void AddNode(Point3d p)
            {
                foreach (Point3d q in nodes) if (q.DistanceTo(p) < dedup) return;
                nodes.Add(p);
            }
            for (int i = 0; i < t.Count; i++)
                for (int j = i + 1; j < t.Count; j++)
                {
                    if (RolePairExcluded(t[i].Role, t[j].Role)) continue;   // not a real joint (bbox artifact)
                    var fa = Faces(t[i].F);
                    var fb = Faces(t[j].F);
                    foreach (var a in fa)
                        foreach (var b in fb)
                            if (FacesMate(a, b, tol, out Point3d at)) AddNode(at);
                }
            foreach (Point3d p in EnumerateScarfNodes(db)) AddNode(p);   // scarf interface isn't analytic; stored explicitly
            foreach (Point3d p in EnumerateSeatNodes(db)) AddNode(p);    // brace seats (oblique member cuts) aren't analytic faces either
            return nodes;
        }

        // ---- interactive face pick (subentity) --------------------------------------------

        // Interactively pick ONE face of a managed timber. `ForceSubSelections` makes GetSelection
        // pick a face (AutoCAD highlights it natively); we read its FullSubentityPath, find the face
        // centroid via a Brep over that path, and return the matching ANALYTIC TFace (clean extents +
        // outward normal). Returns the timber id + face.
        public static bool PickFace(Editor ed, Database db, string msg, out ObjectId id, out TFace face)
            => PickFaceKeyword(ed, db, msg, null, out id, out face) == 1;

        // PickFace with an optional escape KEYWORD (TJoin's Modify -- Robert's options-over-verbs
        // call): 1 = face picked, 0 = the keyword was typed, -1 = cancelled/failed. GetSelection has
        // no Keyword status, so the keyword THROWS from KeywordInput (the canonical pattern) and is
        // caught right here -- the ErrorStatus.OK carrier never surfaces. Include the keyword in
        // `msg` yourself (selection prompts don't render the bracket list).
        public static int PickFaceKeyword(Editor ed, Database db, string msg, string keyword,
            out ObjectId id, out TFace face)
        {
            id = ObjectId.Null; face = default;
            var pso = new PromptSelectionOptions
            { MessageForAdding = msg, SingleOnly = true, ForceSubSelections = true, SinglePickInSpace = true };
            if (!string.IsNullOrEmpty(keyword))
            {
                pso.Keywords.Add(keyword);
                pso.KeywordInput += (s, e) => throw new Autodesk.AutoCAD.Runtime.Exception(
                    Autodesk.AutoCAD.Runtime.ErrorStatus.OK, e.Input);
            }
            // Filter the pick to 3DSOLIDs only -- managed timbers are solids, so this hides every other
            // entity (lines, dims, text, blocks, scarf debris) from the selection. A stray non-managed
            // solid still falls through to the TryReadFrame check below.
            var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "3DSOLID") });
            PromptSelectionResult psr;
            try { psr = ed.GetSelection(pso, filter); }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
                when (ex.ErrorStatus == Autodesk.AutoCAD.Runtime.ErrorStatus.OK && ex.Message == keyword)
            { return 0; }
            if (psr.Status != PromptStatus.OK || psr.Value == null || psr.Value.Count == 0) return -1;

            SelectedObject so = psr.Value[0];
            if (so == null) return -1;
            id = so.ObjectId;
            if (!TryReadFrame(db, id, out TFrame frame)) { ed.WriteMessage("\nNot a managed timber."); return -1; }

            SelectedSubObject[] subs = so.GetSubentities();
            if (subs == null || subs.Length == 0) { ed.WriteMessage("\nNo face captured."); return -1; }
            FullSubentityPath path = subs[0].FullSubentityPath;
            if (path.SubentId.Type != SubentityType.Face) { ed.WriteMessage("\nPick a FACE (not an edge/vertex)."); return -1; }

            if (!FaceCentroid(db, id, path, out Point3d c)) { ed.WriteMessage("\nCouldn't read the face geometry."); return -1; }
            face = NearestFaceByCenter(frame, c);
            return 1;
        }

        // Centre of a picked face. `Entity.GetSubentity` returns a copy of the face geometry; our faces
        // are rectangles, so its WCS bounding-box centre == the face centre (a rectangle's AABB is
        // centred on it). Wrapped so a geometry hiccup degrades to false rather than crashing.
        private static bool FaceCentroid(Database db, ObjectId entId, FullSubentityPath path, out Point3d centroid)
        {
            centroid = default;
            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    if (!(tr.GetObject(entId, OpenMode.ForRead) is Entity ent)) { tr.Commit(); return false; }
                    using (Entity sub = ent.GetSubentity(path))
                    {
                        if (sub == null) { tr.Commit(); return false; }
                        Extents3d e = sub.GeometricExtents;
                        centroid = new Point3d((e.MinPoint.X + e.MaxPoint.X) / 2.0,
                                               (e.MinPoint.Y + e.MaxPoint.Y) / 2.0,
                                               (e.MinPoint.Z + e.MaxPoint.Z) / 2.0);
                    }
                    tr.Commit();
                }
                return true;
            }
            catch { return false; }
        }

        // The analytic face whose centre is nearest the given point (identifies the picked face).
        public static TFace NearestFaceByCenter(TFrame f, Point3d c)
        {
            TFace best = default; double bestD = double.MaxValue;
            foreach (TFace fc in Faces(f))
            {
                double d = (fc.C - c).Length;
                if (d < bestD) { bestD = d; best = fc; }
            }
            return best;
        }
    }
}
