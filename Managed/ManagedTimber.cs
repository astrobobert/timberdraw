using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;

[assembly: CommandClass(typeof(TimberDraw.ManagedCommands))]
namespace TimberDraw
{
    // Managed-timber assembly model (NEW direction, additive -- does not touch the TDraw/TFrame
    // pipeline). A timber is a managed solid: its SHAPE comes from attributes (W x D x L, type,
    // per-end cut), drawn as a box placed freely in WCS. Each managed timber also stores its
    // placement FRAME (origin + axes + L/D/W) in an xrecord, so its 6 faces can be computed
    // analytically -- no BRep. Nodes are NOT stored: they are derived from face coincidence by a
    // rescan (TScan). WCS is the source of truth; UCS is only an input convenience.
    public static class ManagedTimber
    {
        public const string FrameKey = "TMFrame";   // xrecord on the solid's extension dictionary
        public const string NodeLayer = "TM_NODES"; // transient coincidence markers
        public const string ScarfKey = "TMScarf";   // xrecord: a scarf splice point this timber carries
        public const string SeatKey  = "TMSeat";    // xrecord: explicit seat nodes a timber carries (brace seats -- oblique member cuts aren't analytic faces)
        public const string GridKey  = "TMGrid";    // xrecord on a structural-grid entity: its FrameTag
        public const string JointSpecsKey = "TMJointSpecs"; // xrecord: per joint id, the editor's OPAQUE spec state string (jid:Real, state:Text)*
        public const string GridLayer = "TM_GRID";  // layer for the drawn structural grid (lines + bubbles)
        public const string GridTempLayer = "TM_GRID_TEMP";  // provisional grid lines for Free Assembly elements
        public const string ShopKey     = "TMShop";      // xrecord on a shop-drawing entity (outline / label / context)
        public const string ShopLayer   = "TM_SHOP";     // layer for the drawn 2D assembly maps (primary outlines + labels)
        public const string ShopCtxLayer = "TM_SHOP_CTX"; // faded context (connected neighbors, arris sections, direction marks)
        public const string GroupPrefix = "TM_";     // per-group timber layers: TM_<frame>_Bent<n> / TM_<frame>_Wall<letter>

        // The per-group layer a timber belongs to -- a bent member -> its bent's layer, a longitudinal /
        // wall member -> its wall's layer. Used for tree-checkbox isolation (toggle the layer on/off).
        public static string GroupLayer(string frameTag, string bentTag, string wallTag, string floorTag = "")
        {
            string f = string.IsNullOrWhiteSpace(frameTag) ? "A" : frameTag.Trim();
            if (!string.IsNullOrEmpty(bentTag)) return GroupPrefix + f + "_Bent" + bentTag;
            if (!string.IsNullOrEmpty(wallTag)) return GroupPrefix + f + "_Wall" + wallTag;
            if (!string.IsNullOrEmpty(floorTag)) return GroupPrefix + f + "_Floor" + floorTag;
            return GroupPrefix + f;
        }

        // Ensure a layer exists (neutral color ACI 7; never green -- the user is red/green colorblind).
        // Returns its id. Call inside an open transaction.
        public static ObjectId EnsureFrameLayer(Transaction tr, Database db, string name)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return lt[name];
            lt.UpgradeOpen();
            var ltr = new LayerTableRecord
            {
                Name = name,
                Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 7)
            };
            ObjectId id = lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
            return id;
        }

        // Read/Write a group layer's visibility (On/Off) for tree-checkbox isolation. Visible = layer On.
        public static bool IsGroupVisible(Database db, string layerName)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                bool vis = !lt.Has(layerName) ||
                           !((LayerTableRecord)tr.GetObject(lt[layerName], OpenMode.ForRead)).IsOff;
                tr.Commit();
                return vis;
            }
        }

        public static void SetGroupVisible(Database db, string layerName, bool visible)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectId lid = EnsureFrameLayer(tr, db, layerName);
                var ltr = (LayerTableRecord)tr.GetObject(lid, OpenMode.ForWrite);
                ltr.IsOff = !visible;
                tr.Commit();
            }
        }

        // A managed timber's placement frame. Convention: the W x D cross-section lies in the local XY
        // plane (WIDTH along X, DEPTH along Y) and the timber is extruded along Z = LENGTH. Reference
        // line = O -> O + Z*L; O is the NEAR end-face centre, O+Z*L the FAR end-face centre. Local
        // extents: X in [-W/2,+W/2], Y in [-D/2,+D/2], Z in [0,L]. NearN/FarN are the OUTWARD normals of
        // the two Z-ends: -Z / +Z for a square-ended box, tilted for a mitered brace (each end lies in
        // its mate's face plane, pointing back toward the mate). The four side faces stay axis-planar.
        public struct TFrame
        {
            public Point3d O; public Vector3d X, Y, Z; public double L, D, W;
            public Vector3d NearN, FarN;
            // Extra CONVEX slice planes (WCS) applied on top of the two end cuts -- the king post
            // gable's second plane, ridge/eave-girt chamfers, etc. Null/empty for a plain box. The
            // analytic Faces() ignore these (they're detail on the nominal box); BuildFramedSolid
            // slices by them so the solid carries the exact convex shape.
            public List<(Point3d P, Vector3d N)> Cuts;
            // CONCAVE/notch features subtracted from the stock (the common-rafter birdsmouth, later
            // the brace arch). Each is a polygon in the timber's LOCAL elevation plane -- a Point3d
            // per vertex with X = length coord (along Z), Y = depth coord (along Y) -- extruded across
            // the full width (X) and boolean-subtracted. LOCAL => transform-invariant. Faces() ignore
            // these too (detail on the nominal box).
            public List<Point3d[]> Subtracts;
            // JOINERY features: LOCALIZED axis-aligned boxes in LOCAL coords (Min/Max over X=width,
            // Y=depth, Z=length). Subtract=true carves a pocket (a mortise); Subtract=false unions a
            // stub (a tenon projecting past the end). LOCAL => transform-invariant; Faces() ignore them
            // (detail on the nominal box, so coincidence/TScan still see the clean bearing faces).
            public List<(Point3d Min, Point3d Max, bool Subtract, int Joint)> Features;
            // PEG bores: subtract-only CYLINDERS (center C, unit Axis, radius R, half-length Half) in LOCAL
            // coords (transform-invariant, like Features). `Joint` = the owning joint id. Faces() ignore
            // them too. Full and Blind bores are both just cylinder segments -- only the endpoints differ.
            public List<(Point3d C, Vector3d Axis, double R, double Half, int Joint)> Pegs;
            // JOINT polygons: id-carrying LOCAL elevation polygons that UNION or SUBTRACT (same shape +
            // extrude as Subtracts, but tagged + signed). A SLOPED joint shoulder -- the rafter-foot housing
            // (horizontal bottom shelf, pitched top) -- can't be an axis-aligned Feature box; the post gets
            // the wedge as a SUBTRACT (pocket) and the rafter gets the SAME wedge as a UNION (the housed stub
            // extending the foot in). The id keeps them distinct from shape Subtracts (e.g. a birdsmouth) for
            // clean re-cut / delete. Each poly: X = length coord (along Z), Y = depth coord (along Y),
            // extruded across the width band [Xlo, Xhi] (timber-local X) -- full width for the housing, the
            // tongue width for a tenon. Faces() ignore them too.
            public List<(Point3d[] Poly, int Joint, bool Subtract, double Xlo, double Xhi)> JointPolys;
            // Same as JointPolys but the polygon lives in the (X, Y) CROSS-SECTION plane and is extruded
            // ALONG the length (across f.Z) over [Xlo, Xhi] -- for a section-shaped feature that runs down the
            // member (e.g. the ridge's chamfered TONGUE bedding into the king post). First step toward general
            // extrusion-axis polygons; the X-extruded JointPolys above are unchanged.
            public List<(Point3d[] Poly, int Joint, bool Subtract, double Xlo, double Xhi)> JointPolysZ;
            // GENERAL oriented-prism cut: a PLANAR polygon stored as 3D LOCAL points (worldPt = O + X*p.X
            // + Y*p.Y + Z*p.Z), extruded PERPENDICULAR to its own plane by the local Extrude vector
            // (direction + length). Unlike JointPolys / JointPolysZ (a cross-section in a frame-axis plane,
            // extruded along a frame axis), the polygon may lie at ANY orientation -- so a cut that is
            // OBLIQUE in this timber's local frame (the purlin dovetail housing in a SLOPED rafter) is still
            // exact. UNION (false) / SUBTRACT (true), id-carrying. Faces() ignore them too.
            public List<(Point3d[] Poly, Vector3d Extrude, int Joint, bool Subtract)> JointPrisms;
        }

        // One rectangular face: center C, outward normal N, two in-plane axes U/V with half-extents.
        public struct TFace
        {
            public Point3d C; public Vector3d N; public Vector3d U; public double UHalf; public Vector3d V; public double VHalf;
        }

        // ---- draw -------------------------------------------------------------------------

        // Args by ROLE (not by axis name): the timber runs along lengthAxis, its section is depth x width
        // along depthAxis / widthAxis. Stored frame: X = width, Y = depth, Z = length (section in XY,
        // extruded along Z).
        public static ObjectId DrawBox(Point3d originWcs, Vector3d lengthAxis, Vector3d depthAxis, Vector3d widthAxis,
            double length, double depth, double width, string type, string designation,
            string nearCut, string farCut)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            ObjectId id;

            var frame = new TFrame
            {
                O = originWcs, X = widthAxis, Y = depthAxis, Z = lengthAxis, L = length, D = depth, W = width,
                NearN = -lengthAxis, FarN = lengthAxis   // square ends along the length (Z) axis
            };

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                Solid3d solid = new Solid3d();
                solid.CreateBox(width, depth, length);   // local X=width, Y=depth, Z=length
                Point3d centroid = originWcs + lengthAxis * (length / 2.0);
                solid.TransformBy(Matrix3d.AlignCoordinateSystem(
                    Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                    centroid, widthAxis, depthAxis, lengthAxis));

                id = btr.AppendEntity(solid);
                tr.AddNewlyCreatedDBObject(solid, true);
                // No history is recorded for a plain box, but clear it uniformly (db-resident) so every
                // managed solid is history-free and serializes on SAVE (see DrawFramedSolid).
                solid.RecordHistory = false;
                solid.ShowHistory = false;

                solid.CreateExtensionDictionary();
                DBDictionary dict = (DBDictionary)tr.GetObject(solid.ExtensionDictionary, OpenMode.ForWrite);
                Xrecord xr = new Xrecord { Data = FrameToBuffer(frame) };
                dict.SetAt(FrameKey, xr);
                tr.AddNewlyCreatedDBObject(xr, true);

                tr.Commit();
            }

            string size = (int)Math.Round(width) + "x" + (int)Math.Round(depth) + "x" + Module1.BuyLongFeet(length);
            var xd = new Module1.DataStructure(
                type, "", designation, size, "0", 0, 0, 0, width, depth, length, nearCut, farCut, false);
            xd.Free = "1";   // editor-created = FREE ASSEMBLY: a regenerate never erases it
            return Module1.SetXdata(id, xd);
        }

        // Build a managed-timber solid from a full frame (square OR mitered ends), slicing the body to
        // each stored end plane and writing the frame xrecord + XData. The shared builder behind
        // DrawMiteredBrace and TFit. Each end plane passes through that end's centre with the INWARD
        // normal (-NearN / -FarN), so Slice keeps the body side; a square end (-X / +X) cuts clean and
        // reproduces DrawBox, a tilted end mitres.
        // Build the (NON-appended) sliced solid for a frame -- the actual timber shape. Shared by
        // DrawFramedSolid (which appends + tags it) and by transient ghosts (e.g. TJoin's brace preview).
        // The caller owns the returned Solid3d (dispose it). Each end is sliced to its plane, keeping the
        // half toward the body centroid -- so the miter angle is exact and the keep-side is
        // handedness-independent (a brace once came out half-length, butt-ended, on two of four corners).
        public static Solid3d BuildFramedSolid(TFrame f)
        {
            Point3d nearC = f.O;
            Point3d farC = f.O + f.Z * f.L;
            double boxLen = f.L + 2.0 * (f.D + f.W) + 24.0;   // long enough that tilted ends still cut it

            Solid3d solid = new Solid3d();
            solid.CreateBox(f.W, f.D, boxLen);   // local X=width, Y=depth, Z=length (over-long)
            Point3d centroid = f.O + f.Z * (f.L / 2.0);
            solid.TransformBy(Matrix3d.AlignCoordinateSystem(
                Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                centroid, f.X, f.Y, f.Z));

            Vector3d nearN = f.NearN.Negate();
            if ((centroid - nearC).DotProduct(nearN) < 0.0) nearN = nearN.Negate();
            using (Solid3d offNear = solid.Slice(new Plane(nearC, nearN), true)) { }

            Vector3d farN = f.FarN.Negate();
            if ((centroid - farC).DotProduct(farN) < 0.0) farN = farN.Negate();
            using (Solid3d offFar = solid.Slice(new Plane(farC, farN), true)) { }

            // Extra convex feature cuts (gable second plane, chamfers): slice keeping the body side,
            // using the same centroid keep-side trick so the stored normal's sign need not be exact.
            if (f.Cuts != null)
            {
                foreach ((Point3d P, Vector3d N) cut in f.Cuts)
                {
                    Vector3d n = cut.N;
                    if (n.Length < 1e-9) continue;
                    if ((centroid - cut.P).DotProduct(n) < 0.0) n = n.Negate();
                    // A cut plane that doesn't intersect the body makes Slice throw (it wraps a null
                    // negative-half). Skip it -- the solid is simply left uncut by that plane.
                    try { using (Solid3d off = solid.Slice(new Plane(cut.P, n), true)) { } }
                    catch { }
                }
            }

            // Concave/notch features (birdsmouth, ...) AND id-carrying joint polygons (the rafter-foot
            // housing): each LOCAL elevation polygon is rebuilt at the -width face, extruded across the full
            // width, then UNIONED or SUBTRACTED. Shape Subtracts (untagged) always subtract; JointPolys carry
            // a sign (the rafter-foot post pocket subtracts, the rafter stub unions) -- they share this body.
            void CutPoly(Point3d[] poly, bool subtract, double xlo, double xhi)
            {
                if (poly == null || poly.Length < 3 || xhi - xlo <= 1e-6) return;
                try
                {
                    Point3d basePt = f.O + f.X * xlo;   // the xlo width position
                    var pts = new Point3dCollection();
                    foreach (Point3d lp in poly) pts.Add(basePt + f.Z * lp.X + f.Y * lp.Y);
                    using (var pl = new Polyline3d(Poly3dType.SimplePoly, pts, true))
                    {
                        DBObjectCollection rc = Region.CreateFromCurves(new DBObjectCollection { pl });
                        if (rc.Count == 0) return;
                        using (var reg = (Region)rc[0])
                        using (var notch = new Solid3d())
                        {
                            for (int k = 1; k < rc.Count; k++) ((DBObject)rc[k]).Dispose();
                            double width = xhi - xlo;
                            double h = reg.Normal.DotProduct(f.X) >= 0.0 ? width : -width;
                            notch.Extrude(reg, h, 0.0);
                            solid.BooleanOperation(
                                subtract ? BooleanOperationType.BoolSubtract : BooleanOperationType.BoolUnite, notch);
                        }
                    }
                }
                catch { /* leave the stock as-is rather than abort the whole emit */ }
            }
            // Same, but the polygon lives in the (X, Y) CROSS-SECTION and is extruded ALONG the length (f.Z)
            // over [zlo, zhi] -- a section-shaped feature running down the member (the ridge tongue).
            void CutPolyZ(Point3d[] poly, bool subtract, double zlo, double zhi)
            {
                if (poly == null || poly.Length < 3 || zhi - zlo <= 1e-6) return;
                try
                {
                    Point3d basePt = f.O + f.Z * zlo;   // the zlo length position
                    var pts = new Point3dCollection();
                    foreach (Point3d lp in poly) pts.Add(basePt + f.X * lp.X + f.Y * lp.Y);
                    using (var pl = new Polyline3d(Poly3dType.SimplePoly, pts, true))
                    {
                        DBObjectCollection rc = Region.CreateFromCurves(new DBObjectCollection { pl });
                        if (rc.Count == 0) return;
                        using (var reg = (Region)rc[0])
                        using (var notch = new Solid3d())
                        {
                            for (int k = 1; k < rc.Count; k++) ((DBObject)rc[k]).Dispose();
                            double len = zhi - zlo;
                            double h = reg.Normal.DotProduct(f.Z) >= 0.0 ? len : -len;
                            notch.Extrude(reg, h, 0.0);
                            solid.BooleanOperation(
                                subtract ? BooleanOperationType.BoolSubtract : BooleanOperationType.BoolUnite, notch);
                        }
                    }
                }
                catch { /* leave the stock as-is rather than abort the whole emit */ }
            }
            // A PLANAR polygon (3D local pts) extruded PERPENDICULAR to its own plane by the local Extrude
            // vector -- a cut at ANY orientation in the local frame (the purlin dovetail housing in a sloped
            // rafter). Region.Extrude is perpendicular to the region; the signed perpendicular extent
            // reg.Normal . eWorld carries both magnitude and the correct direction regardless of winding.
            void CutPrism(Point3d[] poly, Vector3d extrude, bool subtract)
            {
                if (poly == null || poly.Length < 3 || extrude.Length <= 1e-6) return;
                try
                {
                    var pts = new Point3dCollection();
                    foreach (Point3d lp in poly) pts.Add(f.O + f.X * lp.X + f.Y * lp.Y + f.Z * lp.Z);
                    Vector3d eWorld = f.X * extrude.X + f.Y * extrude.Y + f.Z * extrude.Z;   // local -> world
                    using (var pl = new Polyline3d(Poly3dType.SimplePoly, pts, true))
                    {
                        DBObjectCollection rc = Region.CreateFromCurves(new DBObjectCollection { pl });
                        if (rc.Count == 0) return;
                        using (var reg = (Region)rc[0])
                        using (var notch = new Solid3d())
                        {
                            for (int k = 1; k < rc.Count; k++) ((DBObject)rc[k]).Dispose();
                            double h = reg.Normal.DotProduct(eWorld);   // signed perpendicular extent
                            if (System.Math.Abs(h) <= 1e-6) return;
                            notch.Extrude(reg, h, 0.0);
                            solid.BooleanOperation(
                                subtract ? BooleanOperationType.BoolSubtract : BooleanOperationType.BoolUnite, notch);
                        }
                    }
                }
                catch { /* leave the stock as-is rather than abort the whole emit */ }
            }
            // Shape Subtracts cut clean THROUGH the full width (pad each side); their callers don't carry a band.
            if (f.Subtracts != null)
                foreach (Point3d[] poly in f.Subtracts) CutPoly(poly, true, -f.W / 2.0 - 1.0, f.W / 2.0 + 1.0);
            if (f.JointPolys != null)
                foreach ((Point3d[] Poly, int Joint, bool Subtract, double Xlo, double Xhi) jp in f.JointPolys)
                    CutPoly(jp.Poly, jp.Subtract, jp.Xlo, jp.Xhi);
            if (f.JointPolysZ != null)
                foreach ((Point3d[] Poly, int Joint, bool Subtract, double Xlo, double Xhi) jp in f.JointPolysZ)
                    CutPolyZ(jp.Poly, jp.Subtract, jp.Xlo, jp.Xhi);
            if (f.JointPrisms != null)
                foreach ((Point3d[] Poly, Vector3d Extrude, int Joint, bool Subtract) jp in f.JointPrisms)
                    CutPrism(jp.Poly, jp.Extrude, jp.Subtract);

            // Joinery features: each LOCAL axis-aligned box is rebuilt at its frame-mapped position and
            // either subtracted (mortise pocket) or united (tenon stub). A tenon stub overlaps the body
            // slightly (the cutter starts it inside the end) so the union is watertight.
            if (f.Features != null)
            {
                foreach ((Point3d Min, Point3d Max, bool Subtract, int Joint) ft in f.Features)
                {
                    double sx = ft.Max.X - ft.Min.X, sy = ft.Max.Y - ft.Min.Y, sz = ft.Max.Z - ft.Min.Z;
                    if (sx <= 1e-6 || sy <= 1e-6 || sz <= 1e-6) continue;
                    Point3d cl = new Point3d((ft.Min.X + ft.Max.X) / 2.0, (ft.Min.Y + ft.Max.Y) / 2.0, (ft.Min.Z + ft.Max.Z) / 2.0);
                    Point3d cw = f.O + f.X * cl.X + f.Y * cl.Y + f.Z * cl.Z;
                    try
                    {
                        using (var box = new Solid3d())
                        {
                            box.CreateBox(sx, sy, sz);
                            box.TransformBy(Matrix3d.AlignCoordinateSystem(
                                Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                                cw, f.X, f.Y, f.Z));
                            solid.BooleanOperation(
                                ft.Subtract ? BooleanOperationType.BoolSubtract : BooleanOperationType.BoolUnite, box);
                        }
                    }
                    catch { /* skip a degenerate feature rather than abort the emit */ }
                }
            }

            // Peg bores: each LOCAL cylinder (center C, axis Axis, radius R, half-length Half) is rebuilt at
            // its frame-mapped position and subtracted. Full + Blind bores are both just cylinder segments.
            if (f.Pegs != null)
            {
                foreach ((Point3d C, Vector3d Axis, double R, double Half, int Joint) pg in f.Pegs)
                {
                    if (pg.R <= 1e-6 || pg.Half <= 1e-6) continue;
                    Point3d cw = f.O + f.X * pg.C.X + f.Y * pg.C.Y + f.Z * pg.C.Z;
                    Vector3d aw = f.X * pg.Axis.X + f.Y * pg.Axis.Y + f.Z * pg.Axis.Z;
                    if (aw.Length < 1e-9) continue;
                    aw = aw.GetNormal();
                    Vector3d u = aw.GetPerpendicularVector().GetNormal();
                    Vector3d v = aw.CrossProduct(u).GetNormal();
                    try
                    {
                        using (var cyl = new Solid3d())
                        {
                            cyl.CreateFrustum(2.0 * pg.Half, pg.R, pg.R, pg.R);   // cylinder along local Z, centered
                            cyl.TransformBy(Matrix3d.AlignCoordinateSystem(
                                Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                                cw, u, v, aw));
                            solid.BooleanOperation(BooleanOperationType.BoolSubtract, cyl);
                        }
                    }
                    catch { /* skip a degenerate peg rather than abort the emit */ }
                }
            }

            // NOTE: the slice/boolean OPERATION HISTORY (which references the disposed off-cut / feature
            // operand solids and would otherwise break SAVE) is cleared by the callers AFTER the solid is
            // database-resident -- DrawFramedSolid / DrawBox -- because the history props can't be set on
            // a transient solid like this one.
            return solid;
        }

        public static ObjectId DrawFramedSolid(TFrame f, string type, string designation,
            string nearCut, string farCut,
            string frameTag = "", string bentTag = "", string bayTag = "", string wallTag = "",
            string gridLabel = "", string groupLayer = "")
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            ObjectId id;

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                Solid3d solid = BuildFramedSolid(f);
                id = btr.AppendEntity(solid);
                tr.AddNewlyCreatedDBObject(solid, true);
                // Drop the slice/boolean OPERATION HISTORY now that the solid is database-resident (the
                // history props throw on a transient solid). The recorded history references the off-cut /
                // feature operands we disposed without adding to the db -- a dangling reference that makes
                // the drawing fail to SAVE ("cannot be saved to the specified format"). These solids are
                // rebuilt procedurally from the TFrame, so history is unwanted anyway.
                solid.RecordHistory = false;
                solid.ShowHistory = false;
                if (!string.IsNullOrEmpty(groupLayer))   // per-group layer for tree-checkbox isolation
                    solid.LayerId = EnsureFrameLayer(tr, db, groupLayer);

                solid.CreateExtensionDictionary();
                DBDictionary dict = (DBDictionary)tr.GetObject(solid.ExtensionDictionary, OpenMode.ForWrite);
                Xrecord xr = new Xrecord { Data = FrameToBuffer(f) };
                dict.SetAt(FrameKey, xr);
                tr.AddNewlyCreatedDBObject(xr, true);

                tr.Commit();
            }

            string size = (int)Math.Round(f.W) + "x" + (int)Math.Round(f.D) + "x" + Module1.BuyLongFeet(f.L);
            var xd = new Module1.DataStructure(
                type, bentTag ?? "", designation, size, "0", 0, 0, 0, f.W, f.D, f.L, nearCut, farCut, false);
            xd.FrameTag = frameTag ?? "";
            xd.BayTag   = bayTag ?? "";
            xd.WallTag  = wallTag ?? "";
            xd.GridLabel = gridLabel ?? "";
            return Module1.SetXdata(id, xd);
        }

        // Re-section an existing managed timber: change its width / depth / type, keep its placement
        // (O, axes, L), end cuts and feature cuts, and rebuild the solid in place (erase + redraw, like
        // TFit). Designation + cut tags carry over from the old XData. Returns the new ObjectId
        // (a fresh solid -> a new handle). Used by the Frame Browser's property editor. NOTE: resizing
        // a member that carries gable/chamfer Cuts or a birdsmouth Subtract is approximate -- those are
        // positioned for the original section; plain boxes (posts/girts/rafters) re-section exactly.
        public static ObjectId RegenerateSection(ObjectId id, double newW, double newD, string newType)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null || id.IsNull) return ObjectId.Null;
            if (newW <= 0 || newD <= 0) return ObjectId.Null;
            if (!TryReadFrame(doc.Database, id, out TFrame f)) return ObjectId.Null;

            Module1.DataStructure xd = Module1.GetXdata(id);
            string desig   = xd?.Designation ?? "";
            string nearCut = string.IsNullOrEmpty(xd?.JointNear) ? "Butt" : xd.JointNear;
            string farCut  = string.IsNullOrEmpty(xd?.JointFar)  ? "Butt" : xd.JointFar;
            string type    = string.IsNullOrEmpty(newType) ? (xd?.Type ?? "Timber") : newType;

            string frameTag = xd?.FrameTag ?? "";
            string bentTag  = xd?.BentNumber ?? "";
            string bayTag   = xd?.BayTag ?? "";
            string wallTag  = xd?.WallTag ?? "";
            string gridLabel = xd?.GridLabel ?? "";

            f.W = newW; f.D = newD;
            Module1.EraseEntity(id);
            ObjectId nid = DrawFramedSolid(f, type, desig, nearCut, farCut, frameTag, bentTag, bayTag, wallTag, gridLabel);

            // Carry the production number + floor ownership + free-assembly marker (shared patch).
            PatchCarriedIdentity(nid, xd);
            return nid;
        }

        // Raised after RebuildFromFrame replaces a timber's entity (joinery re-cuts erase + redraw,
        // so the ObjectId AND handle change). List surfaces (Browser, BOM) hold ids/handles and go
        // silently stale without this -- selecting a re-cut timber just stopped highlighting.
        public static event System.Action Rebuilt;

        // Rebuild a managed timber's solid from a (possibly modified) FRAME, preserving its XData, group
        // layer, and production tag -- so joinery can add Features and re-cut the solid in place. Mirrors
        // RegenerateSection but takes the whole new frame. Returns the new ObjectId (a fresh handle).
        public static ObjectId RebuildFromFrame(ObjectId id, TFrame f)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null || id.IsNull) return ObjectId.Null;

            Module1.DataStructure xd = Module1.GetXdata(id);
            string type    = string.IsNullOrEmpty(xd?.Type) ? "Timber" : xd.Type;
            string desig   = xd?.Designation ?? "";
            string nearCut = string.IsNullOrEmpty(xd?.JointNear) ? "Butt" : xd.JointNear;
            string farCut  = string.IsNullOrEmpty(xd?.JointFar)  ? "Butt" : xd.JointFar;
            string frameTag = xd?.FrameTag ?? "";
            string bentTag  = xd?.BentNumber ?? "";
            string bayTag   = xd?.BayTag ?? "";
            string wallTag  = xd?.WallTag ?? "";
            string gridLabel = xd?.GridLabel ?? "";

            // Preserve the timber's group layer (bent/wall isolation) across the erase + redraw.
            string layer = "";
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                if (tr.GetObject(id, OpenMode.ForRead) is Entity ent) layer = ent.Layer;
                tr.Commit();
            }

            // Carry the editor's persisted joint specs across the erase + redraw (arbitrary xrecords are otherwise
            // lost). The cut's own joint re-writes its entry afterward; other joints on this stick ride along.
            Dictionary<int, string> jointSpecs = ReadJointSpecs(id);

            Module1.EraseEntity(id);
            ObjectId nid = DrawFramedSolid(f, type, desig, nearCut, farCut, frameTag, bentTag, bayTag, wallTag, gridLabel, layer);

            PatchCarriedIdentity(nid, xd);
            if (!nid.IsNull && jointSpecs.Count > 0) WriteJointSpecsMap(nid, jointSpecs);
            if (!nid.IsNull) { try { Rebuilt?.Invoke(); } catch { } }   // a listener must never break the cut
            return nid;
        }

        // Carry the identity slots a redraw can't know onto the fresh entity: the stable production
        // number (TagHandle -- only when populated), the FLOOR ownership (DrawFramedSolid has no
        // floorTag parameter, so every rebuild silently dropped it -- a re-cut joist lost its floor),
        // and the free-assembly origin marker (Free -- what keeps a hand-placed timber safe from a
        // regenerate). Shared by RebuildFromFrame + RegenerateSection.
        private static void PatchCarriedIdentity(ObjectId nid, Module1.DataStructure xd)
        {
            if (nid.IsNull || xd == null) return;
            bool realTag = !string.IsNullOrEmpty(xd.TagHandle) && xd.TagHandle != "0";
            string floorTag = xd.FloorTag ?? "";
            string free = xd.Free ?? "";
            if (!realTag && floorTag.Length == 0 && free.Length == 0) return;
            Module1.DataStructure nd = Module1.GetXdata(nid);
            if (nd == null) return;
            if (realTag) nd.TagHandle = xd.TagHandle;
            nd.FloorTag = floorTag;
            nd.Free = free;
            Module1.SetXdata(nid, nd);
        }

        // ---- persisted joint specs (the editor's OPAQUE per-joint state string, keyed by joint id) -----------
        // Stored in the JointSpecsKey xrecord so the Joints pane can repopulate a joint's settings on re-pick.
        // The engine treats the state as an opaque string; ConnectionType serializes/parses it.

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

        // Editable sizing for the girt -> post tenon (inches, girt-LOCAL). Thickness / Length = the stub's
        // width (X) and projection (Z). ShoulderTop / ShoulderBottom pull the tenon in from the girt's top (+Y) /
        // bottom (-Y), leaving bearing shoulders above/below the post mortise. Offset shifts
        // the tenon sideways in the width (X): 0 = centered; pushed to the face = a barefaced tenon.
        public struct TenonSpec
        {
            public bool On;
            public double Thickness, Length, ShoulderTop, ShoulderBottom, Offset;
            public static TenonSpec Default => new TenonSpec
            { On = true, Thickness = 2.0, Length = 4.0, ShoulderTop = 1.0, ShoulderBottom = 1.0, Offset = 0.0 };
        }

        // How far a peg bore runs through the mortise host (the post). Full = all the way through; Blind =
        // in one face and stopping BlindDepth past the tenon's far broad face (a peg that doesn't show out
        // the back). The shop pre-bores only the mortise, never the tenon (the tenon is bored in the field).
        public enum PegBore { Full, Blind }

        // Editable peg layout for a girt -> post joint (inches). Count pegs are STACKED across the girt
        // depth (a vertical column on the tenon) at one station Setback into the post from the bearing
        // face; Spacing = center-to-center between stacked pegs; Diameter = bore size; BlindDepth applies
        // only to a Blind bore. BlindFlip swaps which girt.X face a Blind bore enters from (Full ignores it).
        // Count 0 = no pegs.
        public struct PegSpec
        {
            public int Count;
            public double Diameter, Setback, Spacing, BlindDepth;
            public PegBore Bore;
            public bool BlindFlip;
            public static PegSpec Default => new PegSpec
            { Count = 2, Diameter = 1.0, Setback = 2.0, Spacing = 4.0, BlindDepth = 2.0, Bore = PegBore.Full, BlindFlip = false };
        }

        // Optional HOUSING -- a shallow recess in the post that the girt's end seats into. Its footprint is
        // specified like the TENON: Thickness (width X; 0 = full), Offset (lateral X), ShoulderTop/ShoulderBottom
        // (depth Y insets from the girt's top/bottom, world-up oriented). Seat = recess depth into the post
        // (the housing's projection). When On, the tenon shoulder shifts to the housing BACK. Housings do
        // NOT receive pegs.
        // The footprint is a per-face SHOULDER set: ShoulderTop / ShoulderBottom inset from the section's top /
        // bottom (depth axis, world-up oriented), ShoulderSide1 / ShoulderSide2 inset from its two side faces
        // (width axis, member-local -X / +X). Each is measured FROM that face; 0 = flush = full to that face. (A
        // tenon keeps absolute Thickness + Offset; a housing reads as four bearing shoulders.)
        public struct HousingSpec
        {
            public bool On;
            public double Seat, ShoulderTop, ShoulderBottom, ShoulderSide1, ShoulderSide2;
            public static HousingSpec Default => new HousingSpec
            { On = false, Seat = 1.0, ShoulderTop = 0.0, ShoulderBottom = 0.0, ShoulderSide1 = 0.0, ShoulderSide2 = 0.0 };
        }

        // Optional SHOULDER -- the established 3-pt triangle bearing notch (face-bot, face-top, seat-bot): a
        // HOUSING with the back-top corner dropped, so the top face becomes a diagonal (FIVE-SIDED). The girt
        // gets a triangular tongue, the post a matching triangular notch. `Seat` = the let-in depth into the
        // post: like the housing's Seat, it ADVANCES the shared seat, so the tenon seats that much deeper
        // (total penetration = Seat + tenon Length) and the pegs shift in with it. The face edge spans the
        // section (ShoulderTop/ShoulderBottom insets), Thickness = width (0 = full), Offset = lateral. JointPolys.
        public struct ShoulderSpec
        {
            public bool On;
            public double Seat, Thickness, ShoulderTop, ShoulderBottom, Offset;   // Seat = let-in depth into the post
            public static ShoulderSpec Default => new ShoulderSpec
            { On = false, Seat = 1.5, Thickness = 0.0, ShoulderTop = 0.0, ShoulderBottom = 0.0, Offset = 0.0 };
        }

        // The full recipe for a girt -> post joint: the tenon/mortise + the peg layout + an optional
        // housing / shoulder. The TJoint sticky state is one of these; bundling them is the seed of a future
        // persisted-per-joint / catalog spec.
        public struct JointSpec
        {
            public TenonSpec Tenon;
            public PegSpec Peg;
            public HousingSpec Housing;
            public ShoulderSpec Shoulder;
            public static JointSpec Default => new JointSpec
            { Tenon = TenonSpec.Default, Peg = PegSpec.Default, Housing = HousingSpec.Default, Shoulder = ShoulderSpec.Default };

            // The TUSK TENON factory (floor systems phase 4) -- the classic summer -> girt joint as a
            // combination of the SAME kit: a SOFFIT BEARING housing (bottom band only -- its top
            // shoulder insets everything above the bearing) + a deep tenon riding just above it + one
            // peg. Proportions seed a 10" summer; every value is pane-editable like any box tenon.
            public static JointSpec TuskDefault => new JointSpec
            {
                Tenon = new TenonSpec { On = true, Thickness = 2.0, Length = 4.0, ShoulderTop = 4.0, ShoulderBottom = 3.0, Offset = 0.0 },
                Peg = new PegSpec { Count = 1, Diameter = 1.0, Setback = 2.0, Spacing = 4.0, BlindDepth = 2.0, Bore = PegBore.Full, BlindFlip = false },
                Housing = new HousingSpec { On = true, Seat = 1.0, ShoulderTop = 7.0, ShoulderBottom = 0.0, ShoulderSide1 = 0.0, ShoulderSide2 = 0.0 },
                Shoulder = ShoulderSpec.Default
            };
        }

        // The recipe for a principal-rafter FOOT housed into a post SIDE -- a kit like the girt joint, but
        // the cuts are sloped WEDGES (level seat + pitched top). HOUSING = a full-section seat recessed
        // `Seat` into the post. TENON = a reduced tongue (Thickness wide, centered at Offset, ShoulderTop /
        // ShoulderBottom inset from the rafter top / seat) projecting `Length` PAST the housing into a matching
        // mortise. The seat height + pitched top are DERIVED from the rafter/post geometry. (Pegs later.)
        public struct RafterFootSpec
        {
            public bool On;            // housing (the full-section seat) on
            public double Seat;        // housing recess (let-in) into the post side
            public bool Tenon;         // add the reduced tongue + mortise past the housing
            public double Thickness, Length, ShoulderTop, ShoulderBottom, Offset;
            public PegSpec Peg;        // peg layout (Count 0 = none); pins the tongue, bores the POST cheeks only
            public static RafterFootSpec Default => new RafterFootSpec
            { On = true, Seat = 1.0, Tenon = true, Thickness = 2.0, Length = 4.0, ShoulderTop = 1.0, ShoulderBottom = 1.0, Offset = 0.0,
              Peg = new PegSpec { Count = 0, Diameter = 1.0, Setback = 1.5, Spacing = 2.0, BlindDepth = 2.0, Bore = PegBore.Full, BlindFlip = false } };
        }

        // The recipe for a principal-rafter HEAD bearing on a KING POST side -- the legacy "shoulder" only
        // (no tenon): a right-triangle bearing notch at the rafter underside (the ShoulderGenerator seat).
        // Seat = the seat depth along the rafter underside, into the king post.
        public struct RafterHeadSpec
        {
            public bool On;
            public double Seat;    // bearing-seat depth along the rafter underside (the legacy "sitdepth")
            public static RafterHeadSpec Default => new RafterHeadSpec { On = true, Seat = 3.0 };
        }

        // The recipe for a RIDGE -> KING POST drop-in housing -- the king post top is cut to the ridge's
        // cross-section (incl. its chamfered top) so the ridge lowers straight in; only the king post is cut.
        // Seat = the nominal bearing depth (the housing seat is ~1" deep).
        public struct RidgeHousingSpec
        {
            public bool On;
            public double Seat;            // bearing-seat depth (nominal; the cut beds the ridge's full overlap)
            public double ShoulderBottom;  // raise the housing bottom: the ridge's lower N inches stay full as a
                                           // bearing shoulder against the host face (0 = full-depth drop-in)
            public static RidgeHousingSpec Default => new RidgeHousingSpec { On = true, Seat = 1.0, ShoulderBottom = 0.0 };
        }

        // The recipe for a PURLIN housed into a RAFTER -- a let-in DOVETAIL, matched to the reference solid.
        // The rafter (HOST) gets a full-section HOUSING `Seat` deep + a dovetail POCKET; the purlin's end fills
        // the housing and grows the matching dovetail TONGUE. The tongue is CENTERED in width (X), projects
        // `Length` past the housing, flares from `Width` at the base to `Width + 2*Length*tan(Angle)` at the
        // tip (the dovetail lock -- the purlin can't pull out along its length), and is a `Depth` band flush
        // with the purlin's TOP face (so it drops in from the top). All cut as JointPrisms.
        public struct PurlinRafterSpec
        {
            public bool On;
            public double Seat, Length, Width, Depth, Angle;   // housing depth; tongue length; base width; band depth; taper half-angle (deg)
            public static PurlinRafterSpec Default => new PurlinRafterSpec
            { On = true, Seat = 0.75, Length = 1.75, Width = 1.5, Depth = 2.0, Angle = 15.0 };
        }

        // The recipe for a COMMON RAFTER -> RIDGE let-in HOUSING (a gain). The ridge (HOST) gets a full-section
        // pocket `Seat` deep on the side face the common's head dies into; the common's head fills it. The
        // footprint is the common's SECTION SILHOUETTE on the ridge face (so it shears with the roof pitch),
        // and the pocket floor is a plane parallel to the face `Seat` in -- matched to the reference solid.
        // Seat is the only knob; the width/height come from the common's section + its pitch. Cut as JointPrisms.
        public struct CommonRidgeSpec
        {
            public bool On;
            public double Seat;        // let-in housing depth, measured perpendicular into the ridge face
            public static CommonRidgeSpec Default => new CommonRidgeSpec { On = true, Seat = 0.75 };
        }

        // The recipe for a HOUSED COMMON RAFTER -> EAVE GIRT birdsmouth (both timbers cut). The rafter beds
        // `Seat` below the girt top and `Heel` inside the heel face; the girt gets the matching pocket. The
        // heel side, seat run, taper and pitch are all geometric (from the two picked timbers). Seat/Heel are
        // the canonical glossary names (the let-in depths of the birdsmouth's seat + heel cuts).
        public struct CommonEaveSpec
        {
            public bool On;
            public double Seat;    // seat let-in: how far the seat beds BELOW the girt top (vertical)
            public double Heel;    // heel let-in: how far the heel beds INSIDE the heel face (horizontal)
            public static CommonEaveSpec Default => new CommonEaveSpec { On = true, Seat = 1.0, Heel = 0.75 };
        }

        // The recipe for a STRUT tenon onto a HOST FACE (both timbers cut). A strut end bears flush on a host
        // face and a central tongue enters a matching mortise. HOST-NEUTRAL: the host face is any flat bearing
        // face -- a rafter UNDERSIDE (sloped), a king-post / post SIDE (vertical), etc.; the geometry is derived
        // from the bearing pair, so one joint covers them all. Handles ANY strut angle (the tongue walls adapt
        // to the lean). SPECIFIED LIKE THE STANDARD TENON (TenonSpec / RafterFootSpec): Thickness/Length = the
        // tongue's width + projection; Offset = lateral in the WIDTH (0 = centered, clamped inside the stock);
        // ShoulderTop/ShoulderBottom = DEPTH insets pulling the tongue in from the two depth edges, oriented by
        // WORLD UP so ShoulderTop is the HIGHER edge and ShoulderBottom the LOWER -- the reference flips with the
        // world, not the strut's local axes (mirrors SectionBox's world-up rule). Defaults reproduce
        // strut_to_rafter / vstrut_to_rafter / strut_to_kpost .stl (barefaced in depth -> shoulders 0).
        public struct StrutTenonSpec
        {
            public bool On;          // master gate (the joint is active)
            public double Thickness, Length, ShoulderTop, ShoulderBottom, Offset;
            public bool Tenon;       // cut the tongue (INDEPENDENT of the housing -- either / both / neither)
            public HousingSpec Hsg;  // the housing, a per-face SHOULDER footprint like the box tenon (On + Seat +
                                     // ShoulderTop/ShoulderBottom + ShoulderSide1/ShoulderSide2; every shoulder 0 = full section).
            public PegSpec Peg;      // peg layout (the SAME struct the box tenon uses); Peg.Count 0 = no pegs.
                                     // Pegs pin the tongue and bore the HOST cheeks only (the tongue is field-bored).
            public static StrutTenonSpec Default => new StrutTenonSpec
            { On = true, Thickness = 2.0, Length = 4.0, ShoulderTop = 0.0, ShoulderBottom = 0.0, Offset = 0.0, Tenon = true,
              Hsg = new HousingSpec { On = false, Seat = 1.0, ShoulderTop = 0.0, ShoulderBottom = 0.0, ShoulderSide1 = 0.0, ShoulderSide2 = 0.0 },
              Peg = new PegSpec { Count = 0, Diameter = 1.0, Setback = 1.5, Spacing = 2.0, BlindDepth = 2.0, Bore = PegBore.Full, BlindFlip = false } };

            // The BRACE variant's factory seed -- same end->side tenon engine, just thinner (1.5") and
            // conventionally barefaced (the Offset is computed from the brace width at CUT time, so it is
            // not part of the seed). Was the TBrace sticky's hand literal.
            public static StrutTenonSpec BraceDefault => new StrutTenonSpec
            { On = true, Thickness = 1.5, Length = 4.0, ShoulderTop = 0.0, ShoulderBottom = 0.0, Offset = 0.0, Tenon = true,
              Hsg = new HousingSpec { On = false, Seat = 1.0, ShoulderTop = 0.0, ShoulderBottom = 0.0, ShoulderSide1 = 0.0, ShoulderSide2 = 0.0 },
              Peg = new PegSpec { Count = 0, Diameter = 1.0, Setback = 1.5, Spacing = 2.0, BlindDepth = 2.0, Bore = PegBore.Full, BlindFlip = false } };

            // The QP rafter APEX factory seed -- a short housed tongue at the peak bearing: housing ON,
            // Length 2.0, 1.0 shoulders, pegs set back 1.0. Was the TQPRafter sticky's hand literal (and
            // now also seeds the pane's "QP rafter apex" preset, unifying the two).
            public static StrutTenonSpec QPRafterDefault => new StrutTenonSpec
            { On = true, Thickness = 2.0, Length = 2.0, ShoulderTop = 1.0, ShoulderBottom = 1.0, Offset = 0.0, Tenon = true,
              Hsg = new HousingSpec { On = true, Seat = 1.0, ShoulderTop = 0.0, ShoulderBottom = 0.0, ShoulderSide1 = 0.0, ShoulderSide2 = 0.0 },
              Peg = new PegSpec { Count = 0, Diameter = 1.0, Setback = 1.0, Spacing = 2.0, BlindDepth = 2.0, Bore = PegBore.Full, BlindFlip = false } };
        }

        // FRESH managed cutter -- a girt -> post joint as a KIT OF PARTS. `girt` / `post` are the two
        // frames; `gEnd` is the girt's mating END-cap face (its outward normal runs toward the post); `spec`
        // is the joint recipe (see JointSpec). Each element (tenon, housing, pegs, future types) is gated
        // and emitted independently through a shared JointContext, in any combination. Produces:
        //   features -- box ops: Subtract=false is a girt UNION (a male stub: tenon, housing land, ...),
        //               Subtract=true is a post SUBTRACT (a pocket: mortise, housing recess, ...). The
        //               caller routes each off the Subtract flag and stamps the joint id.
        //   pegs     -- subtract cylinders that bore the POST only (the shop bores the tenon in the field).
        //   polys    -- LOCAL elevation polygons (the diagonal shoulder): OnPost = post SUBTRACT, else girt
        //               UNION. The caller routes these onto each frame's JointPolys (see ApplyRafterFoot).
        // Pure geometry, no doc edits. The girt frame is NOT mutated, so a girt can joint a post at BOTH
        // ends (each call just appends features). Returns FALSE when nothing is enabled (a pure butt) or a
        // tenon section collapses, so the caller can warn instead of cutting a degenerate box.
        public static bool GirtPostJoint(TFrame girt, TFrame post, TFace gEnd, JointSpec spec,
            out List<(Point3d Min, Point3d Max, bool Subtract)> features,
            out List<(Point3d C, Vector3d Axis, double R, double Half)> pegs,
            out List<(Point3d[] Poly, bool OnPost, double Xlo, double Xhi)> polys)
        {
            const double overlap = 0.5;
            bool farEnd = gEnd.N.DotProduct(girt.Z) > 0.0;
            var ctx = new JointContext
            {
                Girt = girt, Post = post, FarEnd = farEnd,
                Dir = farEnd ? 1.0 : -1.0,
                ZShoulder = farEnd ? girt.L : 0.0,                 // post bearing face (girt-local z)
                ZUnion    = farEnd ? girt.L - overlap : overlap,   // union start inside the body (overlap)
                HalfW = girt.W / 2.0, HalfD = girt.D / 2.0
            };

            // ORDERED dispatch: housing/shoulder advances the seat, the tenon seats on it, pegs pin the
            // resulting male core. A new element type adds its EmitX here (+ a spec field + a Review sub-menu).
            // Housing and shoulder are alternatives on the same seat (enabling both stacks oddly -- use one).
            EmitHousing(ctx, spec.Housing);
            EmitShoulder(ctx, spec.Shoulder);   // advances the seat (extends the tenon), like the housing
            EmitTenon(ctx, spec.Tenon);
            EmitPegs(ctx, spec.Peg);

            features = ctx.Features;
            pegs = ctx.Pegs;
            polys = ctx.Polys;
            return !ctx.Collapsed && (features.Count > 0 || polys.Count > 0);
        }

        // Shared per-joint state threaded through the element emitters. Built once from the two frames + the
        // mating end; each Emit<Kind> appends to Features (false = girt UNION, true = post SUBTRACT) and
        // Pegs, and may advance SeatDepth (a housing pushes the seat deeper so the tenon measures from its
        // back). `HasMale` + the Male* fields record the last male element so EmitPegs can pin it.
        private class JointContext
        {
            public TFrame Girt, Post;
            public bool FarEnd;
            public double Dir;                 // +1 far end, -1 near end
            public double ZShoulder, ZUnion;   // post face plane; union start inside the body
            public double HalfW, HalfD;        // girt section half-extents
            public double SeatDepth;           // advanced by housings; males seat at ZShoulder + SeatDepth*Dir
            public bool HasMale;
            public double MaleXC, MaleHalfX, MaleYlo, MaleYhi, MaleSeatZ, MaleLen;
            public bool Collapsed;
            public List<(Point3d Min, Point3d Max, bool Subtract)> Features = new List<(Point3d, Point3d, bool)>();
            public List<(Point3d C, Vector3d Axis, double R, double Half)> Pegs = new List<(Point3d, Vector3d, double, double)>();
            // LOCAL elevation polygons (a diagonal shoulder etc.): OnPost = post SUBTRACT, else girt UNION.
            public List<(Point3d[] Poly, bool OnPost, double Xlo, double Xhi)> Polys = new List<(Point3d[], bool, double, double)>();

            // Map a girt-local box to a POST-local AABB (the corner loop every pocket shares).
            public (Point3d Min, Point3d Max) ToPostAABB(double xlo, double xhi, double ylo, double yhi, double zlo, double zhi)
            {
                double mnX = double.MaxValue, mnY = double.MaxValue, mnZ = double.MaxValue;
                double mxX = double.MinValue, mxY = double.MinValue, mxZ = double.MinValue;
                foreach (double lx in new[] { xlo, xhi })
                    foreach (double ly in new[] { ylo, yhi })
                        foreach (double lz in new[] { zlo, zhi })
                        {
                            Point3d w = Girt.O + Girt.X * lx + Girt.Y * ly + Girt.Z * lz;   // girt-local -> WCS
                            Vector3d r = w - Post.O;
                            double px = r.DotProduct(Post.X), py = r.DotProduct(Post.Y), pz = r.DotProduct(Post.Z);
                            if (px < mnX) mnX = px; if (px > mxX) mxX = px;
                            if (py < mnY) mnY = py; if (py > mxY) mxY = py;
                            if (pz < mnZ) mnZ = pz; if (pz > mxZ) mxZ = pz;
                        }
                return (new Point3d(mnX, mnY, mnZ), new Point3d(mxX, mxY, mxZ));
            }
        }

        // Section footprint (girt-local X/Y) shared by the tenon + housing: a width (already resolved; full
        // section passes girt.W) centered at Offset (clamped inside the stock width, so an over-far Offset
        // sits flush) with top/bottom shoulders mapped to WORLD up (+Z), not local +Y -- bent and wall girts
        // run their depth (Y) axis opposite ways. Returns false if the footprint collapses.
        private static bool SectionBox(JointContext ctx, double width, double offset, double shoulderTop,
            double shoulderBottom, out double xlo, out double xhi, out double ylo, out double yhi)
        {
            double w = System.Math.Min(System.Math.Max(width, 0.0), ctx.Girt.W);
            xlo = offset - w / 2.0; xhi = offset + w / 2.0;
            if (xlo < -ctx.HalfW) { double s = -ctx.HalfW - xlo; xlo += s; xhi += s; }
            if (xhi >  ctx.HalfW) { double s =  xhi - ctx.HalfW; xlo -= s; xhi -= s; }
            bool yUp = ctx.Girt.Y.DotProduct(Vector3d.ZAxis) >= 0.0;
            double rNeg = System.Math.Max(yUp ? shoulderBottom : shoulderTop, 0.0);
            double rPos = System.Math.Max(yUp ? shoulderTop : shoulderBottom, 0.0);
            ylo = -ctx.HalfD + rNeg; yhi = ctx.HalfD - rPos;
            return xhi - xlo > 1e-6 && yhi - ylo > 1e-6;
        }

        // HOUSING -- a shallow recess in the POST that the girt's end seats into, Seat deep from the current
        // seat. Its footprint is a per-face SHOULDER set: ShoulderTop / ShoulderBottom inset the depth, and
        // ShoulderSide1 / ShoulderSide2 inset each width face (all 0 = full section). Advances the seat so a
        // following tenon measures from the housing back. Not pegged.
        private static void EmitHousing(JointContext ctx, HousingSpec h)
        {
            if (!h.On) return;
            double cut = System.Math.Max(h.Seat, 0.0);
            if (cut <= 1e-6) return;
            // Side shoulders inset from each width face -> the resolved width band (both 0 = full section).
            double side1 = System.Math.Max(h.ShoulderSide1, 0.0), side2 = System.Math.Max(h.ShoulderSide2, 0.0);
            double width = ctx.Girt.W - side1 - side2;
            double offset = (side1 - side2) / 2.0;
            if (!SectionBox(ctx, width, offset, h.ShoulderTop, h.ShoulderBottom, out double xlo, out double xhi, out double ylo, out double yhi))
                return;   // footprint collapsed
            double zFace = ctx.ZShoulder + ctx.SeatDepth * ctx.Dir;   // front of this housing (current seat)
            double zBack = zFace + cut * ctx.Dir;

            // Girt UNION: the housed section from inside the body out to the housing back (watertight).
            ctx.Features.Add((new Point3d(xlo, ylo, System.Math.Min(ctx.ZUnion, zBack)),
                              new Point3d(xhi, yhi, System.Math.Max(ctx.ZUnion, zBack)), false));
            // Post SUBTRACT: the pocket from the face plane to the back.
            var p = ctx.ToPostAABB(xlo, xhi, ylo, yhi, System.Math.Min(zFace, zBack), System.Math.Max(zFace, zBack));
            ctx.Features.Add((p.Min, p.Max, true));
            ctx.SeatDepth += cut;
        }

        // SHOULDER -- the established 3-pt triangle bearing notch (face-bot, face-top, seat-bot): a HOUSING
        // with the back-top corner dropped, so the top face is a diagonal (FIVE-SIDED). The girt gets a
        // triangular tongue (UNION), the post a matching triangular notch (SUBTRACT), cut from the SAME world
        // triangle (mirrors RafterFoot's EmitWedge) and stored as id-carrying JointPolys -- a diagonal can't
        // be an axis-aligned box. The face edge is the section depth at the post face; the SEAT (bottom edge)
        // runs `Seat` into the post; the hypotenuse closes face-top -> seat-bot. Like the housing, it ADVANCES
        // the shared seat (`ctx.SeatDepth += cut`), so a following tenon seats `cut` deeper (extends the tenon)
        // and the pegs shift in with it. Not pegged itself.
        // v1 limit: the mated post face must be a DEPTH (+/-Y) face (JointPolys live in the post Z x Y plane,
        // extruded across post.X) -- the same constraint as the rafter foot; a width-face contact is skipped.
        private static void EmitShoulder(JointContext ctx, ShoulderSpec s)
        {
            if (!s.On) return;
            double cut = System.Math.Max(s.Seat, 0.0);
            if (cut <= 1e-6) return;
            // The girt must die into a post DEPTH face: girt length runs along post.Y, girt width along post.X.
            if (System.Math.Abs(ctx.Girt.Z.DotProduct(ctx.Post.Y)) < 0.5) return;

            double hw = s.Thickness > 0.0 ? s.Thickness : ctx.Girt.W;   // 0 = full width
            if (!SectionBox(ctx, hw, s.Offset, s.ShoulderTop, s.ShoulderBottom, out double xlo, out double xhi, out double ylo, out double yhi))
                return;   // footprint collapsed
            // SectionBox already orients the shoulders to WORLD up, so the world-BOTTOM corner (the bearing seat)
            // is ylo when girt.Y points up, else yhi (bent vs wall girts run Y opposite ways).
            bool yUp = ctx.Girt.Y.DotProduct(Vector3d.ZAxis) >= 0.0;
            double seatY = yUp ? ylo : yhi;                          // world-bottom (the seat carries the load)
            double topY  = yUp ? yhi : ylo;                          // world-top (the face top)
            double zFace = ctx.ZShoulder + ctx.SeatDepth * ctx.Dir;  // current seat front (after any housing)
            double zBack = zFace + cut * ctx.Dir;                    // seat runs `cut` into the post

            // The triangle in GIRT-local (JointPolys: X = length along girt.Z, Y = depth along girt.Y).
            var tri = new[]
            {
                new Point3d(zFace, seatY, 0.0),   // face-bot (bearing corner at the post face)
                new Point3d(zFace, topY,  0.0),   // face-top
                new Point3d(zBack, seatY, 0.0)    // seat-bot (into the post)
            };
            // Post band: map the girt width edges (along girt.X) onto post.X (the post's extrusion axis).
            double baseX = (ctx.Girt.O - ctx.Post.O).DotProduct(ctx.Post.X);
            double k = ctx.Girt.X.DotProduct(ctx.Post.X);
            double pX0 = baseX + xlo * k, pX1 = baseX + xhi * k;
            // The SAME world triangle expressed in POST-local (length along post.Z, depth along post.Y).
            var triPost = new Point3d[tri.Length];
            for (int i = 0; i < tri.Length; i++)
            {
                Point3d w = ctx.Girt.O + ctx.Girt.Z * tri[i].X + ctx.Girt.Y * tri[i].Y;
                Vector3d r = w - ctx.Post.O;
                triPost[i] = new Point3d(r.DotProduct(ctx.Post.Z), r.DotProduct(ctx.Post.Y), 0.0);
            }

            ctx.Polys.Add((tri, false, xlo, xhi));                                                   // girt UNION (tongue)
            ctx.Polys.Add((triPost, true, System.Math.Min(pX0, pX1), System.Math.Max(pX0, pX1)));    // post SUBTRACT (notch)
            ctx.SeatDepth += cut;   // advance the seat: the following tenon seats `cut` deeper, pegs shift in
        }

        // TENON -- a shouldered/offset stub from the current seat + its matching mortise. Footprint via the
        // shared SectionBox (Thickness/Offset/shoulders). Records the male core so EmitPegs can pin it.
        private static void EmitTenon(JointContext ctx, TenonSpec tn)
        {
            if (!tn.On) return;
            if (!SectionBox(ctx, tn.Thickness, tn.Offset, tn.ShoulderTop, tn.ShoulderBottom, out double xlo, out double xhi, out double ylo, out double yhi))
            { ctx.Collapsed = true; return; }

            double zSeat = ctx.ZShoulder + ctx.SeatDepth * ctx.Dir;   // = housing back (or the face when none)
            double zTip  = zSeat + tn.Length * ctx.Dir;

            // Tenon UNION (girt-local), projecting past the seat; mortise SUBTRACT (post-local) seat -> tip.
            ctx.Features.Add((new Point3d(xlo, ylo, System.Math.Min(ctx.ZUnion, zTip)),
                              new Point3d(xhi, yhi, System.Math.Max(ctx.ZUnion, zTip)), false));
            var m = ctx.ToPostAABB(xlo, xhi, ylo, yhi, System.Math.Min(zSeat, zTip), System.Math.Max(zSeat, zTip));
            ctx.Features.Add((m.Min, m.Max, true));

            ctx.HasMale = true;
            ctx.MaleXC = (xlo + xhi) / 2.0; ctx.MaleHalfX = (xhi - xlo) / 2.0;
            ctx.MaleYlo = ylo; ctx.MaleYhi = yhi;
            ctx.MaleSeatZ = zSeat; ctx.MaleLen = tn.Length;
        }

        // PEGS -- a column of Count bores STACKED across the girt depth, axis through the tenon thickness
        // (girt.X). Pin the TENON only -- housings do NOT receive pegs, so a joint with no tenon gets none.
        // Full = through the post; Blind = in one girt.X face stopping BlindDepth past the opposite face
        // (BlindFlip swaps the face).
        private static void EmitPegs(JointContext ctx, PegSpec pg)
        {
            if (pg.Count <= 0 || pg.Diameter <= 1e-6 || !ctx.HasMale) return;   // no tenon -> no pegs
            double r = pg.Diameter / 2.0;
            double xC = ctx.MaleXC, coreHalf = ctx.MaleHalfX, ylo = ctx.MaleYlo, yhi = ctx.MaleYhi;
            double fromZ = ctx.MaleSeatZ, maxBack = ctx.MaleLen;
            if (maxBack <= 1e-6) return;   // no tenon depth to pin

            double setback = System.Math.Min(System.Math.Max(pg.Setback, 0.0), maxBack);
            double zPeg    = fromZ + setback * ctx.Dir;
            double yCenter = (ylo + yhi) / 2.0;
            double spanHalf = ctx.Post.W + ctx.Post.D + 1.0;          // generously spans the post along X
            Vector3d axW = ctx.Girt.X;
            int n = pg.Count;
            for (int i = 0; i < n; i++)
            {
                double y = yCenter + (i - (n - 1) / 2.0) * pg.Spacing;
                y = System.Math.Max(ylo + r, System.Math.Min(yhi - r, y));   // keep the bore in the male
                Point3d pegPt = ctx.Girt.O + ctx.Girt.X * xC + ctx.Girt.Y * y + ctx.Girt.Z * zPeg;
                Point3d cW; double half;
                if (pg.Bore == PegBore.Blind)
                {
                    double fdir = pg.BlindFlip ? -1.0 : 1.0;
                    double tStop  = -fdir * (coreHalf + System.Math.Max(pg.BlindDepth, 0.0));
                    double tEntry =  fdir * spanHalf;
                    cW   = pegPt + axW * ((tStop + tEntry) / 2.0);
                    half = System.Math.Abs(tEntry - tStop) / 2.0;
                }
                else { cW = pegPt; half = spanHalf; }
                Vector3d rc = cW - ctx.Post.O;                               // -> POST-local
                Point3d cPost = new Point3d(rc.DotProduct(ctx.Post.X), rc.DotProduct(ctx.Post.Y), rc.DotProduct(ctx.Post.Z));
                Vector3d aPost = new Vector3d(axW.DotProduct(ctx.Post.X), axW.DotProduct(ctx.Post.Y), axW.DotProduct(ctx.Post.Z));
                ctx.Pegs.Add((cPost, aPost, r, half));
            }
        }

        // Shared peg COMPUTE for the polygon-cut tenons (strut / QP apex / rafter foot): a column of `Count` bores
        // that pin a TENON tongue into its HOST. The bores run along `boreAxis` (through the tongue cheeks), are set
        // back into the tongue from its floor center `tongueCtr` by `Setback` along `setbackDir`, and stack along the
        // tongue DEPTH `depthDir` by `Spacing`. Full = a generous through-bore; Blind = enters one cheek and stops
        // `BlindDepth` past the tongue's far cheek (`halfThickAlongBore` from center), `BlindFlip` picking the entry
        // side -- the exact convention of the box-tenon `EmitPegs`. The shop bores the host cheeks; the tongue is
        // field-bored. Returns HOST-LOCAL cylinders (C, Axis, R, Half) -- the shape `TFrame.Pegs` stores. ASCII-only.
        internal static List<(Point3d C, Vector3d Axis, double R, double Half)> TenonPegBores(
            Point3d tongueCtr, Vector3d setbackDir, Vector3d depthDir, Vector3d boreAxis,
            double depthHalf, double tongueLen, double halfThickAlongBore, TFrame host, PegSpec peg)
        {
            var bores = new List<(Point3d, Vector3d, double, double)>();
            if (peg.Count <= 0 || peg.Diameter <= 1e-6 || tongueLen <= 1e-6) return bores;
            double r = peg.Diameter / 2.0;
            double back = System.Math.Min(System.Math.Max(peg.Setback, 0.0), tongueLen);
            double spanHalf = host.W + host.D + 1.0;                       // generously spans the host along the bore
            Vector3d aHost = new Vector3d(boreAxis.DotProduct(host.X), boreAxis.DotProduct(host.Y), boreAxis.DotProduct(host.Z));
            int n = peg.Count;
            for (int i = 0; i < n; i++)
            {
                double yOff = (i - (n - 1) / 2.0) * peg.Spacing;
                if (depthHalf - r > 0.0) yOff = System.Math.Max(-(depthHalf - r), System.Math.Min(depthHalf - r, yOff));
                else yOff = 0.0;
                Point3d pegPt = tongueCtr + setbackDir * back + depthDir * yOff;
                Point3d cW; double half;
                if (peg.Bore == PegBore.Blind)
                {
                    double fdir = peg.BlindFlip ? -1.0 : 1.0;
                    double tStop  = -fdir * (halfThickAlongBore + System.Math.Max(peg.BlindDepth, 0.0));
                    double tEntry =  fdir * spanHalf;
                    cW   = pegPt + boreAxis * ((tStop + tEntry) / 2.0);
                    half = System.Math.Abs(tEntry - tStop) / 2.0;
                }
                else { cW = pegPt; half = spanHalf; }
                Vector3d rc = cW - host.O;                                  // -> HOST-local
                Point3d cHost = new Point3d(rc.DotProduct(host.X), rc.DotProduct(host.Y), rc.DotProduct(host.Z));
                bores.Add((cHost, aHost, r, half));
            }
            return bores;
        }

        // RAFTER-FOOT joint -- a principal rafter housed into a post SIDE, treated as a "girt at a pitch":
        // its plumb foot end butts the post side, so this is a girt -> post joint. Each element is a sloped
        // WEDGE (level seat + pitched top), cut on BOTH timbers from the SAME world geometry: SUBTRACTED from
        // the post (the pocket / mortise) and UNIONED onto the rafter (the housed stub / tenon tongue), via
        // `EmitWedge`. Elements (returned as LOCAL elevation polygons routed by `OnPost`):
        //   HOUSING -- a FULL-section wedge recessed `Seat` into the post (level shelf at z_seat + pitched
        //              top following the rafter top). The stub's front edge is the rafter's plumb foot face.
        //   TENON   -- a REDUCED tongue (Thickness wide at Offset, ShoulderTop down from the rafter top /
        //              ShoulderBottom up from the seat) projecting `Length` PAST the housing into a matching mortise.
        // v1 scope: the mated post face must be a DEPTH (+/-Y) face (the in-bent-plane orientation); the rafter
        // is assumed to lie in that bent plane and match the post width. Returns false on a degenerate /
        // unsupported contact so the caller can warn instead of cutting garbage.
        public static bool RafterFootJoint(TFrame rafter, TFrame post, TFace pFace, RafterFootSpec spec,
            out List<(Point3d[] Poly, bool OnPost, double Xlo, double Xhi)> polys,
            out List<(Point3d C, Vector3d Axis, double R, double Half)> postPegs, out string diag)
        {
            polys = new List<(Point3d[], bool, double, double)>();
            postPegs = new List<(Point3d, Vector3d, double, double)>();
            diag = "";
            double housingDepth = spec.On ? System.Math.Max(spec.Seat, 0.0) : 0.0;
            double tenonLen = spec.Tenon ? System.Math.Max(spec.Length, 0.0) : 0.0;
            bool wantTenon = tenonLen > 1e-6 && spec.Thickness > 1e-6;
            if (housingDepth <= 1e-6 && !wantTenon) { diag = "nothing enabled (housing + tenon both off)"; return false; }

            // The post face the rafter dies into must be a DEPTH (+/-Y) face (cuts live in post Z x Y).
            if (System.Math.Abs(pFace.N.DotProduct(post.Y)) < 0.5)
            { diag = "post face not a depth face (|N.Y|=" + System.Math.Abs(pFace.N.DotProduct(post.Y)).ToString("0.00") + ")"; return false; }

            double hd = rafter.D / 2.0;
            Vector3d bottomDir = rafter.Y.Z < 0.0 ? rafter.Y : rafter.Y.Negate();   // rafter underside (world-down)
            Vector3d topDir = bottomDir.Negate();
            Point3d pBottom = rafter.O + bottomDir * hd;   // a point on the rafter bottom-face plane
            Point3d pTop    = rafter.O + topDir * hd;       // a point on the rafter top-face plane

            // z_seat = world height where the rafter underside crosses the post face plane.
            double denomFace = rafter.Z.DotProduct(pFace.N);
            if (System.Math.Abs(denomFace) < 1e-9) { diag = "rafter runs parallel to the post face"; return false; }
            double tCross = (pFace.C - pBottom).DotProduct(pFace.N) / denomFace;
            Point3d crossPt = pBottom + rafter.Z * tCross;
            double zSeat = crossPt.Z;

            double yFaceSign = pFace.N.DotProduct(post.Y) >= 0.0 ? 1.0 : -1.0;
            double yFace = yFaceSign * (post.D / 2.0);
            double lSeat = (crossPt - post.O).DotProduct(post.Z);   // horizontal shelf, post-local length

            double denomTop = post.Z.DotProduct(topDir);
            if (System.Math.Abs(denomTop) < 1e-9) { diag = "rafter top parallel to post length"; return false; }
            double TopLenAtDepth(double d) => ((pTop - post.O) - post.Y * d).DotProduct(topDir) / denomTop;

            // Emit a post-local wedge (4 corners: length x depth) as a post SUBTRACT over [pLo,pHi] AND the
            // SAME world wedge as a rafter UNION over [rLo,rHi]. The shared world points keep the pocket and
            // the housed stub/tongue identical. Each corner: front-bottom, back-bottom, back-top, front-top.
            // (Accumulate in a LOCAL list -- a local function can't capture the `out` param.)
            var acc = new List<(Point3d[] Poly, bool OnPost, double Xlo, double Xhi)>();
            void EmitWedge(Point3d[] pc, double pLo, double pHi, double rLo, double rHi)
            {
                acc.Add((pc, true, pLo, pHi));
                var rp = new Point3d[pc.Length];
                for (int i = 0; i < pc.Length; i++)
                {
                    Point3d w = post.O + post.Z * pc[i].X + post.Y * pc[i].Y;
                    Vector3d r = w - rafter.O;
                    rp[i] = new Point3d(r.DotProduct(rafter.Z), r.DotProduct(rafter.Y), 0.0);
                }
                acc.Add((rp, false, rLo, rHi));
            }

            const double pad = 1.0;
            double yBack = yFace - yFaceSign * housingDepth;   // housing back (= yFace when no housing)

            // HOUSING: full-section wedge (level shelf at z_seat + pitched top following the rafter top), full
            // width. The stub's front edge is exactly the rafter's plumb foot face, so the union merges there.
            if (housingDepth > 1e-6 && System.Math.Abs(TopLenAtDepth(yFace) - lSeat) > 1e-6)
                EmitWedge(new[]
                {
                    new Point3d(lSeat,                 yFace, 0.0),
                    new Point3d(lSeat,                 yBack, 0.0),
                    new Point3d(TopLenAtDepth(yBack),  yBack, 0.0),
                    new Point3d(TopLenAtDepth(yFace),  yFace, 0.0)
                }, -post.W / 2.0 - pad, post.W / 2.0 + pad, -rafter.W / 2.0, rafter.W / 2.0);

            // TENON: a reduced tongue (Thickness wide, centered at Offset, ShoulderBottom up from the seat /
            // ShoulderTop down from the rafter top) projecting Length PAST the housing into a matching mortise.
            if (wantTenon)
            {
                double yT0 = yBack;                              // tenon front = housing back (or post face)
                double yT1 = yT0 - yFaceSign * tenonLen;         // tenon back, deeper into the post
                double lBot  = lSeat + System.Math.Max(spec.ShoulderBottom, 0.0);
                double lTop0 = TopLenAtDepth(yT0) - System.Math.Max(spec.ShoulderTop, 0.0);
                double lTop1 = TopLenAtDepth(yT1) - System.Math.Max(spec.ShoulderTop, 0.0);
                double half  = System.Math.Min(System.Math.Max(spec.Thickness, 0.0), rafter.W) / 2.0;
                double off   = System.Math.Max(-rafter.W / 2.0 + half, System.Math.Min(rafter.W / 2.0 - half, spec.Offset));
                if (System.Math.Abs(lTop0 - lBot) > 1e-6 && half > 1e-6)
                {
                    EmitWedge(new[]
                    {
                        new Point3d(lBot,  yT0, 0.0),
                        new Point3d(lBot,  yT1, 0.0),
                        new Point3d(lTop1, yT1, 0.0),
                        new Point3d(lTop0, yT0, 0.0)
                    }, off - half, off + half, off - half, off + half);

                    // PEGS -- pin the tongue: bore the POST cheeks across the tenon (the shop bores the tongue in
                    // the field). Shared FULL/BLIND compute with the strut tenon via TenonPegBores: bore axis = post.X
                    // (through the cheeks); setback into the post along the face-inward normal; stacked along the
                    // tongue length (post.Z). tongueCtr sits at the tongue's section center on the housing-back face.
                    Point3d tongueCtr = post.O + post.Z * ((lBot + lTop0) / 2.0) + post.Y * yT0 + post.X * off;
                    Vector3d inDir = post.Y * (-yFaceSign);                       // from the housing back deeper into the post
                    postPegs.AddRange(TenonPegBores(tongueCtr, inDir, post.Z, post.X,
                        (lTop0 - lBot) / 2.0, tenonLen, half, post, spec.Peg));
                }
            }

            diag = "zSeat=" + zSeat.ToString("0.0") + " housing=" + housingDepth.ToString("0.0") +
                   (wantTenon ? " tenon L" + tenonLen.ToString("0.0") + " T" + spec.Thickness.ToString("0.0") : "");
            polys = acc;
            return acc.Count > 0;
        }

        // RAFTER-HEAD joint -- a principal rafter's head bearing on a KING POST side, the legacy "shoulder"
        // notch (ShoulderGenerator): a right-triangle bearing seat at the rafter UNDERSIDE where it meets the
        // king-post face. s0 = underside ^ face (bearing corner); s2 = Seat along the underside INTO the
        // king post (the seat); s1 = up the king-post face by Seat/sin(pitch) (the back cut, square to
        // the rafter), CLAMPED to the rafter section so the notch stays inside the rafter. The SAME world
        // triangle is SUBTRACTED from the king post (the notch) and UNIONED onto the rafter (the bearing
        // tongue), via id-carrying JointPolys (mirrors RafterFoot's EmitWedge). Shoulder only -- no tenon or
        // pegs (the legacy KPRafter head joint). v1 scope: the mated king-post face must be a DEPTH (+/-Y)
        // face and the rafter width parallels the king-post width. Returns false on a degenerate contact.
        public static bool RafterHeadJoint(TFrame rafter, TFrame kingpost, TFace kpFace, RafterHeadSpec spec,
            out List<(Point3d[] Poly, bool OnPost, double Xlo, double Xhi)> polys, out string diag)
        {
            polys = new List<(Point3d[], bool, double, double)>();
            diag = "";
            double sit = spec.On ? System.Math.Max(spec.Seat, 0.0) : 0.0;
            if (sit <= 1e-6) { diag = "shoulder off / zero sit depth"; return false; }

            // The king-post face must be a DEPTH (+/-Y) face (cuts live in king-post Z x Y, extruded across X).
            if (System.Math.Abs(kpFace.N.DotProduct(kingpost.Y)) < 0.5)
            { diag = "king-post face not a depth face (|N.Y|=" + System.Math.Abs(kpFace.N.DotProduct(kingpost.Y)).ToString("0.00") + ")"; return false; }

            double hd = rafter.D / 2.0;
            Vector3d bottomDir = rafter.Y.Z < 0.0 ? rafter.Y : rafter.Y.Negate();   // rafter underside (world-down)
            Vector3d topDir = bottomDir.Negate();
            Point3d pBottom = rafter.O + bottomDir * hd;   // a point on the rafter underside plane
            Point3d pTop    = rafter.O + topDir * hd;       // a point on the rafter top plane

            double denomFace = rafter.Z.DotProduct(kpFace.N);
            if (System.Math.Abs(denomFace) < 1e-9) { diag = "rafter runs parallel to the king-post face"; return false; }
            // s0 = underside ^ face (the bearing corner); s0t = top ^ face (only to size the notch clamp).
            Point3d s0  = pBottom + rafter.Z * ((kpFace.C - pBottom).DotProduct(kpFace.N) / denomFace);
            Point3d s0t = pTop    + rafter.Z * ((kpFace.C - pTop).DotProduct(kpFace.N)    / denomFace);

            Vector3d u = rafter.Z * (denomFace >= 0.0 ? -1.0 : 1.0);   // rafter axis INTO the king post (the head)
            double sinBeta = System.Math.Abs(rafter.Z.Z);
            if (sinBeta < 1e-6) { diag = "rafter is flat (no pitch)"; return false; }
            double c = sit / sinBeta;
            double depthAtFace = System.Math.Abs(s0t.Z - s0.Z);            // rafter section height at the vertical face
            if (depthAtFace > 1e-6) c = System.Math.Min(c, depthAtFace);   // keep the notch inside the rafter

            Point3d s2 = s0 + u * sit;                 // seat-bot: along the underside, into the king post
            Point3d s1 = s0 + Vector3d.ZAxis * c;      // face-top of the notch: up the king-post face (square back)

            // The SAME world triangle, expressed per-timber: king post SUBTRACT (Z x Y, across X) + rafter
            // UNION (Z x Y, across X). Width band = the rafter width, mapped onto the king post's X axis.
            Point3d[] tri = { s0, s1, s2 };
            var kp = new Point3d[3]; var rp = new Point3d[3];
            for (int i = 0; i < 3; i++)
            {
                Vector3d rk = tri[i] - kingpost.O;
                kp[i] = new Point3d(rk.DotProduct(kingpost.Z), rk.DotProduct(kingpost.Y), 0.0);
                Vector3d rr = tri[i] - rafter.O;
                rp[i] = new Point3d(rr.DotProduct(rafter.Z), rr.DotProduct(rafter.Y), 0.0);
            }
            double baseX = (rafter.O - kingpost.O).DotProduct(kingpost.X);
            double k = rafter.X.DotProduct(kingpost.X);
            double kx0 = baseX - (rafter.W / 2.0) * k, kx1 = baseX + (rafter.W / 2.0) * k;

            polys.Add((kp, true,  System.Math.Min(kx0, kx1), System.Math.Max(kx0, kx1)));   // king-post SUBTRACT (notch)
            polys.Add((rp, false, -rafter.W / 2.0, rafter.W / 2.0));                        // rafter UNION (tongue)

            diag = "shoulder sit=" + sit.ToString("0.0") + " plumb=" + c.ToString("0.0");
            return true;
        }

        // RIDGE -> KING POST drop-in housing: the king post top is cut to the ridge's CROSS-SECTION (incl. its
        // chamfered top) so the ridge lowers straight down in. ONLY the king post is cut (one subtract poly);
        // the ridge beds in unchanged (it carries a marker poly only, so the joint id is shared for re-cut /
        // delete). The pocket is open at the top (the subtract exits the king-post peak) and at the MOUTH
        // (the band runs from the ridge's near end out past the king-post bay edge). v1: the ridge must run
        // along the king-post WIDTH (kp.X) so the pocket lives in kp Z x Y -- the JointPolys convention.
        public static bool RidgeKpostJoint(TFrame ridge, TFrame kingpost, RidgeHousingSpec spec,
            out List<(Point3d[] Poly, bool OnPost, double Xlo, double Xhi)> polys,
            out List<(Point3d[] Poly, bool Subtract, double Xlo, double Xhi)> ridgeZPolys, out string diag)
        {
            polys = new List<(Point3d[], bool, double, double)>();
            ridgeZPolys = new List<(Point3d[], bool, double, double)>();
            diag = "";
            if (!spec.On) { diag = "ridge housing off"; return false; }
            if (System.Math.Abs(ridge.Z.DotProduct(kingpost.X)) < 0.5)
            { diag = "ridge not along the king-post width (|rZ.kpX|=" + System.Math.Abs(ridge.Z.DotProduct(kingpost.X)).ToString("0.00") + ")"; return false; }

            // The king-post pocket's cross-section: the ridge profile RISEN TO THE APEX (ridge-local cx/cy),
            // its bottom raised by the bottom-shoulder inset (the ridge's lower edge bears, not let in).
            var sec = RidgeSection(ridge, true, spec.ShoulderBottom);
            if (sec.Count < 3) { diag = "ridge section collapsed"; return false; }

            // The band along kp.X (the building direction). The ridge runs apex-to-apex, INSET by the post
            // width at its near end, so it reaches a king post only at the end that lands INSIDE the king-post
            // width. END housing: that end is the back wall, the pocket runs from it out the bay-side edge
            // (open mouth). PASS-THROUGH (the ridge crosses the whole king post): a full-width notch.
            Point3d e0 = ridge.O, e1 = ridge.O + ridge.Z * ridge.L;
            double x0 = (e0 - kingpost.O).DotProduct(kingpost.X);
            double x1 = (e1 - kingpost.O).DotProduct(kingpost.X);
            double halfKp = kingpost.W / 2.0;
            const double tol = 0.5, pad = 1.0;
            bool e0In = System.Math.Abs(x0) <= halfKp + tol, e1In = System.Math.Abs(x1) <= halfKp + tol;
            double xlo, xhi; Point3d sectO; string mode; bool nearAtL = false;
            if (e0In || e1In)
            {
                double backX, otherX;
                if (e0In && (!e1In || System.Math.Abs(x0) <= System.Math.Abs(x1))) { backX = x0; otherX = x1; sectO = e0; nearAtL = false; }
                else                                                                { backX = x1; otherX = x0; sectO = e1; nearAtL = true; }
                // The ridge butts the king post at this face (backX) and beds INTO it by Seat, the way the
                // ridge points (away from its body, into the king post) -- a Seat-deep housing.
                double inward = (backX - otherX) >= 0.0 ? 1.0 : -1.0;
                double inX = backX + System.Math.Max(spec.Seat, 0.0) * inward;
                inX = System.Math.Max(-halfKp - pad, System.Math.Min(halfKp + pad, inX));   // stay on the king post
                xlo = System.Math.Min(backX, inX); xhi = System.Math.Max(backX, inX); mode = "end";
            }
            else if (System.Math.Min(x0, x1) < -halfKp && System.Math.Max(x0, x1) > halfKp)
            {
                xlo = -halfKp - pad; xhi = halfKp + pad; sectO = e0; mode = "through";   // ridge crosses the whole king post
            }
            else
            {
                diag = "the ridge does not reach this king post -- pick the king post the ridge beds into (nearest end " +
                       (System.Math.Min(System.Math.Abs(x0), System.Math.Abs(x1)) - halfKp).ToString("0.0") + "\" past the edge)";
                return false;
            }

            // The pocket polygon: each section corner -> world -> king-post-local (Z, Y). Independent of the
            // length position (ridge.Z || kp.X), so any cross-section along the ridge gives the same Z/Y.
            var poly = new Point3d[sec.Count];
            for (int i = 0; i < sec.Count; i++)
            {
                Point3d w = sectO + ridge.X * sec[i].cx + ridge.Y * sec[i].cy;
                Vector3d r = w - kingpost.O;
                poly[i] = new Point3d(r.DotProduct(kingpost.Z), r.DotProduct(kingpost.Y), 0.0);
            }

            polys.Add((poly, true, xlo, xhi));   // king post SUBTRACT (the pocket carries the joint id)

            // Ridge TONGUE: the ridge's ACTUAL chamfered cross-section (capped at the ridge top), extruded
            // ALONG the ridge from its near end Seat into the king post (a 0.5" overlap into the body for a
            // watertight union). A JointPolysZ union, so the chamfered sides bed FLUSH to the chamfered pocket.
            // It also carries the joint id, so the pair is found for re-cut / delete.
            if (mode == "end")
            {
                var tsec = RidgeSection(ridge, false, spec.ShoulderBottom);   // capped at the ridge top, bottom raised by the shoulder
                if (tsec.Count >= 3)
                {
                    double seat = System.Math.Max(spec.Seat, 0.0), ov = 0.5;
                    double tzlo = nearAtL ? ridge.L - ov : -seat;
                    double tzhi = nearAtL ? ridge.L + seat : ov;
                    var tpoly = new Point3d[tsec.Count];
                    for (int i = 0; i < tsec.Count; i++) tpoly[i] = new Point3d(tsec[i].cx, tsec[i].cy, 0.0);
                    ridgeZPolys.Add((tpoly, false, tzlo, tzhi));
                }
            }
            diag = "ridge housing (" + mode + "): pocket " + sec.Count + "-gon, band [" + xlo.ToString("0.0") + ".." + xhi.ToString("0.0") + "], seat " + spec.Seat.ToString("0.0") + (spec.ShoulderBottom > 0.0 ? ", bottom shoulder " + spec.ShoulderBottom.ToString("0.0") : "") + (ridgeZPolys.Count > 0 ? " + chamfered tongue" : "");
            return true;
        }

        // FRESH managed cutter -- a PURLIN housed into a RAFTER as a let-in DOVETAIL, matched to the reference
        // solid. Built directly in the PURLIN's local frame (X=width, Y=depth, Z=length) -- exactly how the
        // reference reads -- then converted to world + each timber's local frame. TWO parts, both JointPrisms
        // (a planar polygon extruded perpendicular):
        //   HOUSING -- the purlin's FULL section, `Seat` deep into the rafter from the mating end: a rectangle
        //              in X-Y extruded along the length. Purlin UNION (the housed stub) + rafter SUBTRACT.
        //   TONGUE  -- a dovetail: the flare profile (a hexagon in X-Z -- constant `Width` then splaying to the
        //              tip at `Angle`) extruded `Depth` along Y, flush with the purlin's TOP face (the +/-Y
        //              face pointing most up). The flare widens DEEPER into the rafter, so the purlin can't
        //              pull back out (the lock). Purlin UNION + rafter SUBTRACT. `rFace` is unused now -- the
        //              mating end is found from geometry. Returns false on a zero dimension.
        public static bool PurlinRafterJoint(TFrame purlin, TFrame rafter, TFace rFace, PurlinRafterSpec spec,
            out List<(Point3d[] Poly, Vector3d Extrude, bool OnRafter)> prisms, out string diag)
        {
            prisms = new List<(Point3d[], Vector3d, bool)>();
            diag = "";
            double seat = System.Math.Max(spec.Seat, 0.0);       // full-section housing depth into the rafter
            double len = System.Math.Max(spec.Length, 0.0);      // dovetail tongue length past the housing
            double baseHalf = System.Math.Max(spec.Width, 0.0) / 2.0;
            double band = System.Math.Min(System.Math.Max(spec.Depth, 0.0), purlin.D);   // tongue band into the depth
            double tipHalf = baseHalf + len * System.Math.Tan(spec.Angle * System.Math.PI / 180.0);
            if (!spec.On || (seat <= 1e-6 && len <= 1e-6) || baseHalf <= 1e-6 || band <= 1e-6)
            { diag = "housed dovetail disabled or a zero dimension"; return false; }

            double halfW = purlin.W / 2.0, halfD = purlin.D / 2.0;

            // Mating end (nearest the rafter) + its OUTWARD length direction = into the rafter.
            Point3d rC = rafter.O + rafter.Z * (rafter.L / 2.0);
            Point3d c0 = purlin.O, c1 = purlin.O + purlin.Z * purlin.L;
            bool nearEnd = (c0 - rC).Length <= (c1 - rC).Length;
            double zf = nearEnd ? 0.0 : purlin.L;      // mating-end length coord (purlin-local Z)
            double s = nearEnd ? -1.0 : 1.0;           // +s*Z runs INTO the rafter
            // TOP depth face = the +/-Y face pointing most UP (world Z); the tongue beds flush with it.
            double topSign = purlin.Y.DotProduct(Vector3d.ZAxis) >= 0.0 ? 1.0 : -1.0;
            double yInner = topSign * (halfD - band), yTop = topSign * halfD;

            const double ov = 0.5;                     // overlap into the body / housing for watertight unions
            double D(double d) => zf + s * d;          // purlin-local length coord at depth d into the rafter

            // HOUSING: full-section rectangle (X-Y) at d=-ov, extruded along the length to d=seat.
            Point3d[] housing =
            {
                new Point3d(-halfW, -halfD, D(-ov)),
                new Point3d( halfW, -halfD, D(-ov)),
                new Point3d( halfW,  halfD, D(-ov)),
                new Point3d(-halfW,  halfD, D(-ov))
            };
            Vector3d housingExt = new Vector3d(0.0, 0.0, s * (ov + seat));

            // TONGUE: the dovetail flare profile (hexagon in X-Z) at Y=yInner, extruded `band` to the top face.
            Point3d[] tongue =
            {
                new Point3d(-baseHalf, yInner, D(seat - ov)),    // base, -X (overlap back into the housing)
                new Point3d( baseHalf, yInner, D(seat - ov)),    // base, +X
                new Point3d( baseHalf, yInner, D(seat)),         // flare start, +X
                new Point3d( tipHalf,  yInner, D(seat + len)),   // tip, +X (splayed -- the lock)
                new Point3d(-tipHalf,  yInner, D(seat + len)),   // tip, -X
                new Point3d(-baseHalf, yInner, D(seat))          // flare start, -X
            };
            Vector3d tongueExt = new Vector3d(0.0, yTop - yInner, 0.0);

            // purlin-local pt/vec -> world -> frame f local.
            Point3d[] Local(TFrame f, Point3d[] loc)
            {
                var p = new Point3d[loc.Length];
                for (int i = 0; i < loc.Length; i++)
                {
                    Point3d w = purlin.O + purlin.X * loc[i].X + purlin.Y * loc[i].Y + purlin.Z * loc[i].Z;
                    Vector3d r = w - f.O;
                    p[i] = new Point3d(r.DotProduct(f.X), r.DotProduct(f.Y), r.DotProduct(f.Z));
                }
                return p;
            }
            Vector3d LocalVec(TFrame f, Vector3d pv)
            {
                Vector3d w = purlin.X * pv.X + purlin.Y * pv.Y + purlin.Z * pv.Z;
                return new Vector3d(w.DotProduct(f.X), w.DotProduct(f.Y), w.DotProduct(f.Z));
            }

            foreach ((Point3d[] poly, Vector3d ext) part in new[] { (housing, housingExt), (tongue, tongueExt) })
            {
                prisms.Add((Local(purlin, part.poly), LocalVec(purlin, part.ext), false));   // purlin UNION
                prisms.Add((Local(rafter, part.poly), LocalVec(rafter, part.ext), true));    // rafter SUBTRACT
            }
            diag = "housed dovetail: housing " + seat.ToString("0.0") + " deep + tongue " + spec.Width.ToString("0.0") +
                   "->" + (2.0 * tipHalf).ToString("0.0") + " wide x " + band.ToString("0.0") + " band x " +
                   len.ToString("0.0") + " long (" + (nearEnd ? "near" : "far") + " end, top " +
                   (topSign > 0 ? "+Y" : "-Y") + ")";
            return true;
        }

        // Cut a COMMON RAFTER's head into a RIDGE as a let-in HOUSING (a gain). `common` is the rafter, `ridge`
        // the host, `rFace` the ridge SIDE face the head dies into (from FindFootContact). The gain = the
        // common's full section swept ALONG ITS AXIS into the ridge until it is `Seat` deep PERPENDICULAR to
        // the face: so the footprint on the face shears with the pitch and the pocket floor is a plane
        // parallel to the face. Built in WORLD then mapped to each frame -- a parallelogram spanning the
        // in-face footprint height (vVec) and the along-axis let-in (E), extruded across the common's WIDTH.
        // Returns TWO prisms with the SAME shape: ridge SUBTRACT (the gain) + common UNION (the head fills it,
        // which also stamps the shared joint id). Returns false on a zero seat or a common parallel to the face.
        public static bool CommonRidgeJoint(TFrame common, TFrame ridge, TFace rFace, CommonRidgeSpec spec,
            out List<(Point3d[] Poly, Vector3d Extrude, bool OnRidge)> prisms, out string diag)
        {
            prisms = new List<(Point3d[], Vector3d, bool)>();
            diag = "";
            double seat = System.Math.Max(spec.Seat, 0.0);
            if (!spec.On || seat <= 1e-6) { diag = "common->ridge housing disabled or a zero seat"; return false; }

            Vector3d nIn = (-rFace.N).GetNormal();                       // INTO the ridge body (face normal points out)
            Vector3d a = common.Z.DotProduct(nIn) >= 0.0 ? common.Z : -common.Z;   // common axis, INTO the ridge
            a = a.GetNormal();
            double adn = a.DotProduct(nIn);
            if (adn <= 1e-4) { diag = "common runs parallel to the ridge face"; return false; }

            // Fc: the common's centre line crosses the face plane.
            double denom = common.Z.DotProduct(rFace.N);
            if (System.Math.Abs(denom) <= 1e-6) { diag = "common runs parallel to the ridge face"; return false; }
            double t = (rFace.C - common.O).DotProduct(rFace.N) / denom;
            Point3d Fc = common.O + common.Z * t;

            double W = common.W, Dd = common.D;
            Vector3d uHalf = common.X * (W / 2.0);                       // half width (the extrude axis)
            Vector3d delta = common.Y.GetNormal();                       // section depth direction
            Vector3d E = a * (seat / adn);                              // along the axis, `Seat` deep perpendicular
            Vector3d vVec = (delta - a * (delta.DotProduct(nIn) / adn)) * Dd;   // footprint height (in-face silhouette)
            Vector3d vHalf = vVec * 0.5;

            const double ov = 0.5;                                       // back the base out of the face so the union overlaps the body
            Point3d baseFc = Fc - a * (ov / adn);
            Vector3d Efull = a * ((seat + ov) / adn);
            Point3d b0 = baseFc - uHalf - vHalf, b1 = baseFc - uHalf + vHalf;
            Point3d[] baseW = { b0, b1, b1 + Efull, b0 + Efull };        // parallelogram (vVec x Efull) at -width edge
            Vector3d extW = common.X * W;                                // perpendicular to the base -> across the full width

            Point3d[] Loc(TFrame f, Point3d[] w)
            {
                var p = new Point3d[w.Length];
                for (int i = 0; i < w.Length; i++)
                {
                    Vector3d r = w[i] - f.O;
                    p[i] = new Point3d(r.DotProduct(f.X), r.DotProduct(f.Y), r.DotProduct(f.Z));
                }
                return p;
            }
            Vector3d LocV(TFrame f, Vector3d v) =>
                new Vector3d(v.DotProduct(f.X), v.DotProduct(f.Y), v.DotProduct(f.Z));

            prisms.Add((Loc(ridge, baseW), LocV(ridge, extW), true));    // ridge SUBTRACT -- the gain
            prisms.Add((Loc(common, baseW), LocV(common, extW), false)); // common UNION  -- the head fills it
            double pitch = System.Math.Acos(System.Math.Min(1.0, adn)) * 180.0 / System.Math.PI;
            diag = "common->ridge housing: " + seat.ToString("0.00") + " let-in, footprint " +
                   W.ToString("0.0") + " x " + vVec.Length.ToString("0.0") + " (pitch " + pitch.ToString("0.0") + " deg)";
            return true;
        }

        // Build the HOUSED COMMON RAFTER -> EAVE GIRT birdsmouth as ONE joint solid (= common_to_eavegirt.stl):
        // it is ADDED (union) to the common and SUBTRACTED from the eave girt -- the TPurlin pattern. The solid
        // is the 6-pt housing hexagon: starting from the UN-HOUSED bearing rafter (seat on the girt top, heel
        // on the girt face), adding it recesses the seat `Seat` below the girt top and the heel `Heel`
        // inside the heel face; subtracting it from the girt cuts the matching pocket. Worked in the bent
        // ELEVATION (eh = up-slope horizontal from the OUTER girt face, ev = up):
        //   EaveHt = the rafter TOP plane's elevation at the outer girt face (run 0); Roof(run)=EaveHt+run*m.
        //   cp     = the rafter's vertical depth projection (underside = Roof - cp).
        //   seatZ  = girtTop - Seat (the seat bearing); heel = GirtW - Heel in from the inner face.
        // `rPoly` is the hexagon in rafter-local (along, deep) for a full-width UNION; `gPoly` the SAME hexagon
        // in the girt cross-section (cx, cy) for a SUBTRACT over the rafter's width band. Matched vertex-exact
        // to ConnectionTypes/common_to_eavegirt.stl. v1: INNER heel (no tail) only -- a tail common (heel on the
        // OUTER face) returns false until a reference is in hand. Returns false on a vertical rafter / degenerate
        // girt. NOTE the union only houses an UN-HOUSED bearing rafter (on a full box the hexagon is interior).
        public static bool CommonEaveJoint(TFrame rafter, TFrame girt, CommonEaveSpec spec,
            out Point3d[] rPoly, out double rXlo, out double rXhi,
            out Point3d[] gPoly, out double gZlo, out double gZhi, out string diag)
        {
            rPoly = null; gPoly = null; rXlo = rXhi = gZlo = gZhi = 0.0; diag = "";
            if (!spec.On) { diag = "birdsmouth disabled"; return false; }

            Vector3d up = Vector3d.ZAxis;
            Vector3d aRaw = rafter.Z.DotProduct(up) >= 0.0 ? rafter.Z : -rafter.Z;   // up-slope axis
            Vector3d ehRaw = aRaw - up * aRaw.DotProduct(up);
            if (ehRaw.Length <= 1e-6) { diag = "rafter is vertical -- no birdsmouth"; return false; }
            Vector3d eh = ehRaw.GetNormal();          // up-slope HORIZONTAL
            Vector3d ev = up;
            Vector3d a = aRaw.GetNormal();
            double ahz = a.DotProduct(eh);
            if (ahz <= 1e-6) { diag = "rafter is vertical -- no birdsmouth"; return false; }
            double m = a.DotProduct(ev) / ahz;        // pitch slope (rise/run)
            double cp = rafter.D / ahz;               // rafter depth projected vertically

            // Girt faces: TOP (seat datum) + the OUTER (down-slope) and INNER (up-slope) side faces.
            TFace topF = default, outF = default, innF = default;
            bool haveTop = false, haveOut = false, haveInn = false;
            double topDot = -1e9, outDot = 1e9, innDot = -1e9;
            foreach (TFace gf in Faces(girt))
            {
                double nu = gf.N.DotProduct(up);
                if (nu > 0.5) { if (nu > topDot) { topDot = nu; topF = gf; haveTop = true; } continue; }
                if (System.Math.Abs(nu) >= 0.5) continue;                 // skip the bottom; keep verticals
                double nh = gf.N.DotProduct(eh);
                if (nh < outDot) { outDot = nh; outF = gf; haveOut = true; }   // outward normal -> down-slope
                if (nh > innDot) { innDot = nh; innF = gf; haveInn = true; }   // outward normal -> up-slope
            }
            if (!haveTop || !haveOut || !haveInn) { diag = "girt has no clear top + two side faces"; return false; }

            Point3d cOuter = outF.C;                                       // run = 0 here (the OUTER girt face)
            double Run(Point3d p) => (p - cOuter).DotProduct(eh);
            double girtTop = topF.C.Z;
            double girtW = Run(innF.C);                                    // inner-face run (outer is 0)
            if (girtW <= 1e-6) { diag = "girt width degenerate"; return false; }

            // EaveHt = the roof plane (rafter TOP depth-face) elevation at run 0: elev = EaveHt + run*m.
            Vector3d topN = rafter.Y.DotProduct(up) <= 0.0 ? -rafter.Y : rafter.Y;   // up-facing depth normal
            Point3d topPt = rafter.O + topN * (rafter.D / 2.0);
            double eaveHt = topPt.Z - Run(topPt) * m;

            // Heel side: INNER unless the rafter tails past the OUTER face (its down-slope end run < 0).
            Point3d e0 = rafter.O, e1 = rafter.O + rafter.Z * rafter.L;
            Point3d eaveEnd = e0.Z <= e1.Z ? e0 : e1;
            if (Run(eaveEnd) < -1e-3) { diag = "tail common (heel on the outer face) not yet built -- needs a reference"; return false; }

            double seatLet = System.Math.Max(spec.Seat, 0.0), heelLet = System.Math.Max(spec.Heel, 0.0);
            double seatZ = girtTop - seatLet;
            double Roof(double r) => eaveHt + r * m;
            double seatOuterRun = System.Math.Max(0.0, m > 1e-9 ? (seatZ - eaveHt) / m : 0.0);
            double heelRun = girtW - heelLet;                             // inner heel, let-in from the inner face
            double botZ = Roof(girtW) - cp;                              // notch bottom = underside at the inner face
            if (heelRun <= seatOuterRun + 1e-6) { diag = "birdsmouth degenerate (seat/heel collapsed)"; return false; }

            // Rafter NOTCH (run, elev), wrapping the inner-top corner: seat outer -> roof^top -> inner^top ->
            // inner^underside -> heel^bottom -> heel^seat.
            (double r, double z)[] reN =
            {
                (seatOuterRun, seatZ),
                ((girtTop - eaveHt) / m, girtTop),
                (girtW, girtTop),
                (girtW, botZ),
                (heelRun, botZ),
                (heelRun, seatZ)
            };
            // ONE joint solid (= common_to_eavegirt.stl) mapped into each frame: rafter-local (along, deep)
            // for the rafter UNION, girt cross-section (cx, cy) for the girt SUBTRACT (the TPurlin pattern).
            rPoly = new Point3d[reN.Length];
            gPoly = new Point3d[reN.Length];
            for (int i = 0; i < reN.Length; i++)
            {
                Point3d w = cOuter + eh * reN[i].r + ev * (reN[i].z - cOuter.Z);   // run/elev -> world (eh horizontal, ev = up)
                Vector3d rr = w - rafter.O;
                rPoly[i] = new Point3d(rr.DotProduct(rafter.Z), rr.DotProduct(rafter.Y), 0.0);   // rafter (along, deep)
                Vector3d gr = w - girt.O;
                gPoly[i] = new Point3d(gr.DotProduct(girt.X), gr.DotProduct(girt.Y), 0.0);       // girt section (cx, cy)
            }
            rXlo = -rafter.W / 2.0; rXhi = rafter.W / 2.0;                 // EXACT width: a UNION must not widen the stick
            double zc = (rafter.O - girt.O).DotProduct(girt.Z);            // rafter's position along the girt length
            gZlo = zc - rafter.W / 2.0; gZhi = zc + rafter.W / 2.0;        // girt pocket matches the rafter width (snug)

            diag = "housed birdsmouth (union->common, subtract->girt): seat let-in " + seatLet.ToString("0.00") +
                   " (Z " + seatZ.ToString("0.0") + "), heel let-in " + heelLet.ToString("0.00") + " inner (run " +
                   heelRun.ToString("0.0") + "), pitch " + (System.Math.Atan(m) * 180.0 / System.Math.PI).ToString("0.0") + " deg";
            return true;
        }

        // FRESH managed cutter -- a STRUT tenon onto a HOST FACE. ONE joint solid (= strut_to_rafter /
        // vstrut_to_rafter / strut_to_kpost .stl) mapped into each frame and routed by sign: the strut UNIONs
        // the tongue, the host SUBTRACTs the matching mortise (the TPurlin pattern). HOST-NEUTRAL: the host is
        // whatever the strut beds into -- a rafter underside, a king-post / post side -- the bearing plane,
        // footprint and pitch come from the bearing pair, not from any assumed role. v1 limit: the strut's
        // mating face must be its END cap (a placed strut whose end is cut to bear) -- a square-cut strut that
        // doesn't present a facing pair returns a clear message.
        // Male (strut) tongue = `JointPolys` across strut.X (always valid -- it is the strut's OWN width axis).
        // Host mortise = an orientation-agnostic `JointPrisms` (CutPrism): the same solid extruded along the
        // tongue WIDTH whatever host axis that happens to be. (A plain JointPolys on the host would assume the
        // width == host.X, which holds for a bent girt but NOT a bay floor girt, where the mortise would then
        // extrude along the wrong axis.) Caller routes `sPoly` -> strut.JointPolys, (`hPoly`,`hExtrude`) ->
        // host.JointPrisms, both stamped with the shared joint id.
        // Returns LISTS so a HOUSING (full-section seat) can ride alongside the tongue: malePolys = strut
        // JointPolys UNIONs, hostPrisms = host JointPrisms SUBTRACTs (the caller stamps the joint id). The bearing
        // is normally AUTO-FOUND (a strut plane coincident + opposing a host face). Pass `hasBearing` to OVERRIDE
        // it with an explicit (bearingCtr, bearingFaceN) -- the QP rafter apex feeds the male rafter's beveled
        // peak end-cap, where the seat is CREATED by the housing (no pre-existing coincident host face to find).
        public static bool StrutTenonJoint(TFrame strut, TFrame host, StrutTenonSpec spec,
            out List<(Point3d[] Poly, double Xlo, double Xhi)> malePolys,
            out List<(Point3d[] Poly, Vector3d Extrude)> hostPrisms,
            out List<(Point3d C, Vector3d Axis, double R, double Half)> hostPegs, out string diag,
            bool hasBearing = false, Point3d bearingCtr = default, Vector3d bearingFaceN = default)
        {
            malePolys = new List<(Point3d[], double, double)>();
            hostPrisms = new List<(Point3d[], Vector3d)>();
            hostPegs = new List<(Point3d, Vector3d, double, double)>();
            diag = "";
            if (!spec.On) { diag = "strut tenon disabled"; return false; }

            Vector3d up = Vector3d.ZAxis;

            // Bearing plane: (capCtr = footprint center, faceN = host-face outward normal). Override or auto-find.
            Vector3d faceN; Point3d capCtr;
            if (hasBearing)
            {
                if (bearingFaceN.Length < 1e-9) { diag = "bad bearing normal"; return false; }
                faceN = bearingFaceN.GetNormal(); capCtr = bearingCtr;
            }
            else
            {
                // Candidate STRUT bearing planes = its 6 box faces PLUS any clip-CUTS -- the bay brace's flat top
                // is a CUT (its nominal end is the diagonal plane), so a face-only search never sees it. Each as
                // (point on plane, OUTWARD normal away from the strut body).
                Point3d centroid = strut.O + strut.Z * (strut.L / 2.0);
                var cand = new List<(Point3d P, Vector3d N)>();
                foreach (TFace fa in Faces(strut)) cand.Add((fa.C, fa.N));
                if (strut.Cuts != null)
                    foreach ((Point3d P, Vector3d N) c in strut.Cuts)
                    {
                        if (c.N.Length < 1e-9) continue;
                        Vector3d nOut = c.N.GetNormal();
                        if ((centroid - c.P).DotProduct(nOut) > 0.0) nOut = -nOut;   // point AWAY from the body
                        cand.Add((c.P, nOut));
                    }

                // Bearing = a strut plane (face or cut) coincident + opposing a host face, with the strut
                // CENTERLINE crossing that host face inside its extent. Pick the smallest coincidence gap.
                TFace hostFace = default; Point3d pcBest = default;
                double best = double.MaxValue; bool found = false, anyOpp = false;
                foreach (TFace fb in Faces(host))
                {
                    double zn = strut.Z.DotProduct(fb.N);
                    if (System.Math.Abs(zn) < 1e-6) continue;                         // centerline parallel to the face
                    Point3d Pc = strut.O + strut.Z * ((fb.C - strut.O).DotProduct(fb.N) / zn);   // centerline-plane intersection
                    Vector3d d = Pc - fb.C;
                    if (System.Math.Abs(d.DotProduct(fb.U)) > fb.UHalf + 1e-6) continue;   // centerline hits inside the face
                    if (System.Math.Abs(d.DotProduct(fb.V)) > fb.VHalf + 1e-6) continue;
                    foreach (var cp in cand)
                    {
                        if (cp.N.DotProduct(fb.N) > -0.5) continue;                    // strut plane opposes the host face
                        anyOpp = true;
                        double g = System.Math.Abs((fb.C - cp.P).DotProduct(fb.N));    // plane coincidence gap
                        if (g < best) { best = g; hostFace = fb; pcBest = Pc; found = true; }
                    }
                }
                if (!found || best > 0.25)
                {
                    diag = !anyOpp
                        ? "no strut face or cut opposes a host face the strut points at -- check the strut beds on the host"
                        : "strut not seated: closest bearing gap " + best.ToString("0.00") + " > 0.25 -- move the strut to bear flush on the host";
                    return false;
                }
                faceN = hostFace.N; capCtr = pcBest;
            }
            Vector3d bn = -faceN;                            // into the host from the bearing face

            // VIRTUAL bearing cap on the bearing plane: footprint center = capCtr; V (depth-on-face) =
            // strut.X x faceN stretched by the strut's tilt, exactly like Faces().Cap.
            Vector3d capV = strut.X.CrossProduct(faceN);
            double capVHalf;
            if (capV.Length < 1e-9) { capV = strut.Y; capVHalf = strut.D / 2.0; }
            else { capV = capV.GetNormal(); double dd = System.Math.Abs(strut.Y.DotProduct(capV)); capVHalf = dd > 1e-6 ? (strut.D / 2.0) / dd : strut.D / 2.0; }
            TFace strutCap = new TFace { C = capCtr, N = -faceN, U = strut.X, UHalf = strut.W / 2.0, V = capV, VHalf = capVHalf };

            // Bearing footprint from the (virtual) strut cap, ORIENTED BY WORLD UP so faceUp points to the higher
            // edge (like SectionBox), so the shoulders flip with the world, not the strut's local axes.
            Vector3d vFace = strutCap.V.GetNormal();
            Vector3d faceUp = vFace.DotProduct(up) >= 0.0 ? vFace : -vFace;   // toward the higher depth edge
            Point3d pHi = strutCap.C + faceUp * strutCap.VHalf;   // higher (world-up) bearing edge
            Point3d pLo = strutCap.C - faceUp * strutCap.VHalf;   // lower bearing edge

            double hw = strut.W / 2.0;
            const double ov = 0.5;

            // Strut axis toward the host (used by both the housing's upper end and the tongue walls).
            Vector3d sAxisUp = strut.Z.DotProduct(bn) >= 0.0 ? strut.Z : -strut.Z;   // strut axis toward the host
            double axN = sAxisUp.DotProduct(bn);                                      // into-host rise per unit axis

            // TENON (the tongue) and HOUSING (the seat) are INDEPENDENT -- either, both, or neither.
            double seat = spec.Hsg.On ? System.Math.Max(spec.Hsg.Seat, 0.0) : 0.0;
            double len  = spec.Tenon   ? System.Math.Max(spec.Length, 0.0) : 0.0;
            double w    = spec.Tenon   ? System.Math.Min(System.Math.Max(spec.Thickness, 0.0), strut.W) : 0.0;
            bool wantHousing = seat > 1e-6;
            bool wantTenon   = len > 1e-6 && w > 1e-6;
            if (!wantHousing && !wantTenon) { diag = "nothing enabled (tenon + housing both off)"; return false; }

            // The housing FLOOR is `Seat` deep. Its LOWER arris is let into the host PERPENDICULAR (bn); its UPPER
            // end follows the MALE AXIS to the same floor plane (lands ON the stock's top face, never above it).
            // lowerBack/upperBack = the floor edge (= pLo/pHi when there is no housing). Verified vertex-exact vs
            // qprafter_right_correct.stl + qprafterleft_correct.stl.
            double axShift = wantHousing ? (axN > 1e-3 ? seat / axN : seat) : 0.0;
            Point3d lowerBack = pLo + bn * seat;            // floor, lower (perpendicular let-in)
            Point3d upperBack = pHi + sAxisUp * axShift;    // floor, upper (along the male axis)

            // HOUSING -- the male's section beds `Seat` into the host as a PARTIAL footprint (box-tenon style): the
            // DEPTH shoulders keep `ShoulderBottom` of the section flush at the bottom + `ShoulderTop` flush at the
            // top (only the middle band is recessed), and ShoulderSide1/ShoulderSide2 inset each width face. RULE:
            // never grow the male's section, so the neck is the bevel->floor quad inside the stock. ALL shoulders 0
            // == the verified full-section trapezoid {pLo,pHi,upperBack,lowerBack}. The SAME quad is UNIONED onto the
            // male (the neck bridges body->tongue) and SUBTRACTED from the host (the pocket); NO -bn mouth-open (it
            // lowered pHi and left an uncut sliver).
            if (wantHousing)
            {
                double hdepth = 2.0 * strutCap.VHalf;                         // full bearing depth on the face
                double shTopH = System.Math.Max(spec.Hsg.ShoulderTop, 0.0);   // top inset (world-up higher edge)
                double shBotH = System.Math.Max(spec.Hsg.ShoulderBottom, 0.0);// bottom inset (world-up lower edge)
                if (shTopH + shBotH > hdepth - 1e-3)                          // never collapse the recessed band, so the
                { double k = (hdepth - 1e-3) / (shTopH + shBotH); shTopH *= k; shBotH *= k; }   // neck still bridges body->tongue
                Point3d pLoH = pLo + faceUp * shBotH;                         // housing lower edge, inset up
                Point3d pHiH = pHi - faceUp * shTopH;                         // housing upper edge, inset down
                // Floor: lower always PERPENDICULAR (bn). Upper PERPENDICULAR when shouldered (it sits below the top
                // arris, so it can't poke out), else the current ALONG-AXIS edge (keeps a flush-to-top recess in stock).
                Point3d lowerBackH = pLoH + bn * seat;
                Point3d upperBackH = shTopH > 1e-6 ? pHiH + bn * seat : upperBack;
                // Width band: SIDE shoulders inset from each width face (Side1 from -X, Side2 from +X); both 0 = full
                // (+/-hw). Skip the housing if the sides inset past each other (no band left).
                double xloH = -hw + System.Math.Max(spec.Hsg.ShoulderSide1, 0.0);
                double xhiH =  hw - System.Math.Max(spec.Hsg.ShoulderSide2, 0.0);
                if (xhiH - xloH > 1e-6)
                {
                    Point3d[] tq = { pLoH, pHiH, upperBackH, lowerBackH };
                    var ms = new Point3d[tq.Length];
                    for (int i = 0; i < tq.Length; i++)
                    { Vector3d sr = tq[i] - strut.O; ms[i] = new Point3d(sr.DotProduct(strut.Z), sr.DotProduct(strut.Y), 0.0); }
                    malePolys.Add((ms, xloH, xhiH));
                    var hps = new Point3d[tq.Length];
                    for (int i = 0; i < tq.Length; i++)
                    { Vector3d hr = (tq[i] + strut.X * xloH) - host.O; hps[i] = new Point3d(hr.DotProduct(host.X), hr.DotProduct(host.Y), hr.DotProduct(host.Z)); }
                    Vector3d extWh = strut.X * (xhiH - xloH);
                    hostPrisms.Add((hps, new Vector3d(extWh.DotProduct(host.X), extWh.DotProduct(host.Y), extWh.DotProduct(host.Z))));
                }
            }

            // TENON -- the reduced tongue, based on the housing FLOOR edge (lowerBack..upperBack; = the bevel with no
            // housing), inset by the shoulders, projecting Length PAST the floor (penetration = Seat + Length).
            if (wantTenon)
            {
                double depth = 2.0 * strutCap.VHalf;                          // full bearing depth on the face
                double sBot = System.Math.Max(spec.ShoulderBottom, 0.0);      // lower-edge inset
                double sTop = System.Math.Max(spec.ShoulderTop, 0.0);         // higher-edge inset
                if (depth - sBot - sTop <= 1e-6) { diag = "tenon depth collapsed by the shoulders"; return false; }
                Point3d loEdge = lowerBack + faceUp * sBot;   // lower tongue edge on the floor (perpendicular let-in)
                Point3d hiEdge = upperBack - faceUp * sTop;   // higher tongue edge on the floor (along the axis)
                double xlo = spec.Offset - w / 2.0, xhi = spec.Offset + w / 2.0;
                if (xlo < -hw) { double s = -hw - xlo; xlo += s; xhi += s; }
                if (xhi >  hw) { double s =  xhi - hw; xlo -= s; xhi -= s; }

                // The two tongue WALLS: one SQUARE to the bearing face (bn), the other ALONG THE STRUT AXIS (by the
                // lean), so the tongue root never undercuts the insertion.
                Vector3d alongTop = axN > 1e-6 ? sAxisUp * (len / axN) : bn * len;
                Vector3d squareTop = bn * len;
                bool loAlongAxis = sAxisUp.DotProduct(faceUp) > 1e-9;
                Vector3d loWall = loAlongAxis ? alongTop : squareTop;
                Vector3d hiWall = loAlongAxis ? squareTop : alongTop;

                Point3d A = loEdge, B = loEdge + loWall, C = hiEdge + hiWall, D = hiEdge;
                // Mouth-opened copy (the floor corners pushed back along each wall by ov). The HOST mortise always
                // uses it; the MALE tongue uses it too WHEN HOUSING IS ON so the tongue OVERLAPS the neck instead of
                // meeting it on a coincident floor face (which left boolean strays). With no housing the tongue base
                // stays on the bevel (connecting to the body) -- tenon-alone unchanged.
                Point3d[] hquad = { A - loWall.GetNormal() * ov, B, C, D - hiWall.GetNormal() * ov };
                Point3d[] mquad = wantHousing ? hquad : new[] { A, B, C, D };
                var sPoly = new Point3d[mquad.Length];
                for (int i = 0; i < mquad.Length; i++)
                { Vector3d sr = mquad[i] - strut.O; sPoly[i] = new Point3d(sr.DotProduct(strut.Z), sr.DotProduct(strut.Y), 0.0); }
                malePolys.Add((sPoly, xlo, xhi));

                Vector3d wWorld = strut.X;
                var hPoly = new Point3d[hquad.Length];
                for (int i = 0; i < hquad.Length; i++)
                { Vector3d hr = (hquad[i] + wWorld * xlo) - host.O; hPoly[i] = new Point3d(hr.DotProduct(host.X), hr.DotProduct(host.Y), hr.DotProduct(host.Z)); }
                Vector3d extW = wWorld * (xhi - xlo);
                hostPrisms.Add((hPoly, new Vector3d(extW.DotProduct(host.X), extW.DotProduct(host.Y), extW.DotProduct(host.Z))));

                // PEGS -- pin the tongue: bore the HOST cheeks across the tenon (the shop bores the tongue in the
                // field). Shared FULL/BLIND compute with the rafter-foot tenon via TenonPegBores. Bore axis = strut.X
                // (through the cheeks); setback along bn (into the host, perpendicular to faceUp so the depth station
                // holds); stacked along the tongue DEPTH (faceUp). tongueCtr carries the lateral Offset so a blind
                // bore stops the right distance past the (offset) tongue's far cheek.
                Point3d tongueCtr = loEdge + (hiEdge - loEdge) * 0.5 + strut.X * ((xlo + xhi) / 2.0);
                double depthHalf = (hiEdge - loEdge).DotProduct(faceUp) / 2.0;
                hostPegs.AddRange(TenonPegBores(tongueCtr, bn, faceUp, strut.X, depthHalf, len, (xhi - xlo) / 2.0, host, spec.Peg));
            }

            double faceTilt = System.Math.Acos(System.Math.Min(1.0, System.Math.Abs(bn.DotProduct(up)))) * 180.0 / System.Math.PI;
            diag = "strut " + (wantTenon ? "tenon L" + len.ToString("0.0") + " T" + w.ToString("0.0") : "(no tenon)") +
                   (wantHousing ? " + housing " + seat.ToString("0.0") : "") +
                   (hostPegs.Count > 0 ? " + " + hostPegs.Count + " peg(s)" : "") +
                   ", bearing " + faceTilt.ToString("0.0") + " deg from level";
            return true;
        }

        // The king-post pocket cross-section (ridge-local cx=width, cy=depth). It is the ridge's section
        // EXTENDED UP TO THE APEX: the ridge's two top chamfers ARE the rafter top-lines, which meet at the
        // roof-pitch peak (= the king-post highest point). So instead of capping at the flat ridge top, we
        // start the box well ABOVE the ridge and let the chamfers close to a peak at their intersection --
        // the cut runs "from the seat to the king-post highest point" (the back plane reaches the apex), while
        // the sides + chamfers still reproduce the ridge profile. Without >= 2 chamfers, cap at the ridge top.
        private static List<(double cx, double cy)> RidgeSection(TFrame ridge, bool toApex, double shoulderBot = 0.0)
        {
            double hw = ridge.W / 2.0, hd = ridge.D / 2.0;
            // Bottom-shoulder inset: raise the section's LOW edge (the ridge depth axis points up, the same
            // convention RidgeSection already uses for the chamfered top / apex), so the ridge's lower
            // `shoulderBot` inches stay full and bear against the host face instead of being let in.
            double bot = -hd + System.Math.Max(0.0, System.Math.Min(shoulderBot, ridge.D));
            var cuts = new List<(double a, double b, double c)>();
            if (ridge.Cuts != null)
                foreach ((Point3d P, Vector3d N) cut in ridge.Cuts)
                {
                    if (System.Math.Abs(cut.N.DotProduct(ridge.Z)) > 0.01) continue;   // longitudinal cuts only
                    double a = cut.N.DotProduct(ridge.X), b = cut.N.DotProduct(ridge.Y);
                    double c = (ridge.O - cut.P).DotProduct(cut.N);                     // value at the section centre
                    if (System.Math.Abs(a) < 1e-12 && System.Math.Abs(b) < 1e-12) continue;
                    double s = c >= 0.0 ? 1.0 : -1.0;                                   // keep the centroid (value c) side
                    cuts.Add((a * s, b * s, c * s));
                }
            // toApex + >= 2 chamfers (a gable top): rise to the apex (king-post pocket) -- start the box well
            // above the ridge so the chamfers close to a peak at their intersection. Otherwise (the TONGUE)
            // cap at the flat ridge top -> the ridge's ACTUAL chamfered cross-section.
            double top = (toApex && cuts.Count >= 2) ? hd + 4.0 * (ridge.W + ridge.D) : hd;
            var poly = new List<(double cx, double cy)> { (-hw, bot), (hw, bot), (hw, top), (-hw, top) };
            foreach ((double a, double b, double c) cl in cuts)
            {
                poly = ClipHalf(poly, cl.a, cl.b, cl.c);
                if (poly.Count < 3) break;
            }
            return poly;
        }

        // Sutherland-Hodgman: clip a 2D polygon to the half-plane a*x + b*y + c >= 0.
        private static List<(double cx, double cy)> ClipHalf(List<(double cx, double cy)> poly, double a, double b, double c)
        {
            var outp = new List<(double cx, double cy)>();
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                (double cx, double cy) cur = poly[i], nxt = poly[(i + 1) % n];
                double dc = a * cur.cx + b * cur.cy + c, dn = a * nxt.cx + b * nxt.cy + c;
                bool inC = dc >= -1e-9, inN = dn >= -1e-9;
                if (inC) outp.Add(cur);
                if (inC != inN)
                {
                    double t = dc / (dc - dn);
                    outp.Add((cur.cx + t * (nxt.cx - cur.cx), cur.cy + t * (nxt.cy - cur.cy)));
                }
            }
            return outp;
        }

        // Draw a knee brace between two picked faces whose planes are NOT parallel, mitering each end
        // flush to its mate's face plane (so the now-coplanar ends register nodes in TScan). The brace
        // is seated in the CORNER -- the line where the two face planes meet: the FOOT sits footRun back
        // from the corner along face A, the HEAD headRun back along face B. Its broad faces (width W) lie
        // out of the plane of the two normals (the bent-elevation orientation). The body is built as an
        // over-length box and SLICED by each face plane (DrawFramedSolid keeps the body-side half).
        //
        // bodyA / bodyB are the two timbers' body CENTRES. The foot must sit on the OPEN side of face A
        // (away from timber B's bulk) and the head on the open side of face B (away from timber A's
        // bulk), so the brace tucks into the corner instead of crossing a member. We derive each open
        // direction from the body centre -- the stored face normal can be flipped, and the old "step
        // toward the face centre" rule flips when a member's centre sits near the corner (a girt at
        // mid-post made the foot jump to the wrong side of the girt).
        public static ObjectId DrawMiteredBrace(TFace fa, TFace fb, double depth, double width,
            double footRun, double headRun, string type, string designation,
            Point3d bodyA, Point3d bodyB)
        {
            if (!TryBraceFrame(fa, fb, depth, width, footRun, headRun, bodyA, bodyB, out TFrame frame))
                return ObjectId.Null;
            ObjectId id = DrawFramedSolid(frame, type, designation, "matchface", "matchface");
            if (!id.IsNull)
            {
                // Editor-created = FREE ASSEMBLY: a regenerate never erases it (same stamp as DrawBox).
                Module1.DataStructure xd = Module1.GetXdata(id);
                if (xd != null) { xd.Free = "1"; Module1.SetXdata(id, xd); }
            }
            return id;
        }

        // The corner-relative anchors + unit step directions a brace foot/head are measured along (so a
        // jig can invert the cursor into runs, and TryBraceFrame can build from runs). foot = Pa +
        // dirFoot*footRun (on face A, stepping away from timber B's body); head = Pb + dirHead*headRun.
        // Returns false when the faces are parallel (no corner) or a step direction collapses.
        public static bool TryBraceAnchors(TFace fa, TFace fb, Point3d bodyA, Point3d bodyB,
            out Point3d pa, out Vector3d dirFoot, out Point3d pb, out Vector3d dirHead)
        {
            pa = default; pb = default; dirFoot = default; dirHead = default;

            // Corner line where the two face planes meet (direction = fa.N x fb.N).
            Vector3d uRaw = fa.N.CrossProduct(fb.N);
            double uu = uRaw.DotProduct(uRaw);
            if (uu < 1e-12) return false;                               // faces parallel -- no corner
            double dA = fa.N.DotProduct(fa.C.GetAsVector());
            double dB = fb.N.DotProduct(fb.C.GetAsVector());
            Vector3d p0v = (dA * fb.N.CrossProduct(uRaw) + dB * uRaw.CrossProduct(fa.N)) / uu;
            Point3d P0 = Point3d.Origin + p0v;                          // a point on the corner line
            Vector3d u = uRaw.GetNormal();

            // Reliable OUTWARD normals: flip each face normal so it points away from its own timber's
            // body centre (the stored normal may be either sign).
            Vector3d na = fa.N; if ((fa.C - bodyA).DotProduct(na) < 0.0) na = na.Negate();
            Vector3d nb = fb.N; if ((fb.C - bodyB).DotProduct(nb) < 0.0) nb = nb.Negate();

            // Foot on face A: step away from timber B's body (+nb projected into plane A, normal na).
            pa = P0 + (fa.C - P0).DotProduct(u) * u;                    // corner point level with face A
            Vector3d df = nb - nb.DotProduct(na) * na;
            if (df.Length < 1e-6) return false;
            dirFoot = df.GetNormal();

            // Head on face B: step away from timber A's body (+na projected into plane B, normal nb).
            pb = P0 + (fb.C - P0).DotProduct(u) * u;
            Vector3d dh = na - na.DotProduct(nb) * nb;
            if (dh.Length < 1e-6) return false;
            dirHead = dh.GetNormal();
            return true;
        }

        // Build the placement frame for a knee brace from the foot/head runs. Shared by DrawMiteredBrace
        // (which slices the solid) and BraceJig (which ghosts the box). Returns false on a degenerate corner.
        public static bool TryBraceFrame(TFace fa, TFace fb, double depth, double width,
            double footRun, double headRun, Point3d bodyA, Point3d bodyB, out TFrame frame)
        {
            frame = default;
            if (!TryBraceAnchors(fa, fb, bodyA, bodyB, out Point3d pa, out Vector3d dirFoot,
                                 out Point3d pb, out Vector3d dirHead)) return false;

            Point3d foot = pa + dirFoot * footRun;
            Point3d head = pb + dirHead * headRun;

            Vector3d xb = head - foot;
            if (xb.Length < 1e-6) return false;
            xb = xb.GetNormal();

            // Width axis: along the corner (out of the plane of the two normals), orthonormalized to xb
            // (Gram-Schmidt) so the placement frame is rigid -- otherwise AlignCoordinateSystem throws
            // eCannotScaleNonUniformly on a sheared frame.
            Vector3d u = fa.N.CrossProduct(fb.N).GetNormal();
            Vector3d zb = u - u.DotProduct(xb) * xb;
            if (zb.Length < 1e-6) zb = xb.GetPerpendicularVector();
            zb = zb.GetNormal();
            Vector3d yb = zb.CrossProduct(xb).GetNormal();   // (xb, yb, zb) right-handed: xb x yb = zb

            // Reliable outward end normals (same flip as TryBraceAnchors) for the mitered end faces.
            Vector3d na = fa.N; if ((fa.C - bodyA).DotProduct(na) < 0.0) na = na.Negate();
            Vector3d nb = fb.N; if ((fb.C - bodyB).DotProduct(nb) < 0.0) nb = nb.Negate();

            // Map to the section-in-XY / length-along-Z convention: length = Z (the brace axis xb),
            // depth = Y (yb), width = X (recomputed as Y x Z so the frame stays right-handed; width is
            // symmetric so its sign doesn't matter). Mitered ends face back toward their mates so
            // FacesMate sees opposing normals -> nodes.
            frame = new TFrame
            {
                O = foot, X = yb.CrossProduct(xb).GetNormal(), Y = yb, Z = xb,
                L = (head - foot).Length, D = depth, W = width,
                NearN = na.Negate(), FarN = nb.Negate()
            };
            return true;
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

        // Erase every MANAGED timber in the current space (those carrying a TMFrame xrecord) -- the
        // tree editor's draw/redraw clears the prior frame before re-emitting, leaving non-managed
        // solids untouched. Returns the count erased. Reversible via AutoCAD UNDO. NOTE: today this
        // clears ALL managed timbers (one generated frame at a time); per-frame clearing (multiple
        // managed frames in one drawing) needs a frame tag on each timber -- see the grouping layer.
        public static int EraseAllManaged(Database db) => EraseFrame(db, null);

        // Erase managed timbers (those carrying a TMFrame xrecord). When `frameTag` is null, clears
        // EVERY managed timber (legacy whole-frame redraw). When `frameTag` is given, clears only the
        // GENERATOR'S OWN timbers carrying that FrameTag -- a regenerate never touches hand-placed
        // (free-assembly) work, assigned or not: a timber survives when it carries the Free marker
        // (stamped at editor creation) OR a FloorTag (floor-owned members are free by construction --
        // the generator never writes FloorTag, so this also protects joists/summers placed before the
        // marker existed). Non-managed solids untouched. Reversible via AutoCAD UNDO. Returns the count.
        public static int EraseFrame(Database db, string frameTag)
        {
            int n = 0;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return 0;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
                    if (!TryReadFrame(tr, ent, out _)) continue;
                    if (frameTag != null)
                    {
                        if (ReadXTextField(tr, ent, "FrameTag") != frameTag) continue;
                        if (ReadXTextField(tr, ent, "Free") == "1") continue;        // hand-placed: keep
                        if (ReadXTextField(tr, ent, "FloorTag") != "") continue;     // floor-owned: keep
                    }
                    ent.UpgradeOpen(); ent.Erase(); n++;
                }
                tr.Commit();
            }
            return n;
        }

        // Erase the drawn structural-grid entities (lines + bubbles) carrying a TMGrid xrecord whose
        // value matches `frameTag` -- the per-frame grid redraw (mirrors EraseFrame). Returns the count.
        public static int EraseGrid(Database db, string frameTag)
        {
            int n = 0;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return 0;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
                    if (ReadGridTag(tr, ent) != frameTag) continue;
                    ent.UpgradeOpen(); ent.Erase(); n++;
                }
                tr.Commit();
            }
            return n;
        }

        // The FrameTag stored in an entity's TMGrid xrecord, or null if it carries none (not a grid entity).
        private static string ReadGridTag(Transaction tr, Entity ent)
        {
            if (ent.ExtensionDictionary.IsNull) return null;
            if (!(tr.GetObject(ent.ExtensionDictionary, OpenMode.ForRead) is DBDictionary dict)) return null;
            if (!dict.Contains(GridKey)) return null;
            if (!(tr.GetObject(dict.GetAt(GridKey), OpenMode.ForRead) is Xrecord xr)) return null;
            TypedValue[] v = xr.Data?.AsArray();
            return (v != null && v.Length > 0) ? v[0].Value.ToString() : "";
        }

        // Tag an entity as part of frame `frameTag`'s structural grid (a TMGrid xrecord on its extension
        // dictionary) so EraseGrid can clear it on redraw. Call inside an open transaction with the
        // entity open for write and newly appended.
        public static void WriteGridTag(Transaction tr, Entity ent, string frameTag)
        {
            ent.CreateExtensionDictionary();
            var dict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, OpenMode.ForWrite);
            var xr = new Xrecord { Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, frameTag ?? "")) };
            dict.SetAt(GridKey, xr);
            tr.AddNewlyCreatedDBObject(xr, true);
        }

        // Tag a shop-drawing entity (outline / label) with a TMShop xrecord so EraseShop can clear the 2D
        // assembly maps on redraw -- the shop counterpart of WriteGridTag, keyed separately (ShopKey) so
        // regenerating the maps never touches the structural grid. Call inside an open transaction with
        // the entity open for write and newly appended.
        public static void WriteShopTag(Transaction tr, Entity ent)
        {
            ent.CreateExtensionDictionary();
            var dict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, OpenMode.ForWrite);
            var xr = new Xrecord { Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, ShopLayer)) };
            dict.SetAt(ShopKey, xr);
            tr.AddNewlyCreatedDBObject(xr, true);
        }

        private static bool HasShopTag(Transaction tr, Entity ent)
        {
            if (ent.ExtensionDictionary.IsNull) return false;
            if (!(tr.GetObject(ent.ExtensionDictionary, OpenMode.ForRead) is DBDictionary dict)) return false;
            return dict.Contains(ShopKey);
        }

        // Erase every drawn shop-map entity (those carrying a TMShop xrecord) in the current space --
        // the 2D-map redraw primitive (mirrors EraseGrid). Returns the count erased. Reversible via UNDO.
        public static int EraseShop(Database db)
        {
            int n = 0;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return 0;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
                    if (!HasShopTag(tr, ent)) continue;
                    ent.UpgradeOpen(); ent.Erase(); n++;
                }
                tr.Commit();
            }
            return n;
        }

        // Read one text-valued XData field from an entity's extension dictionary inside an existing
        // transaction (Module1.GetXdata opens its own transaction, which we cannot nest in the erase
        // loop). Returns "" when the dictionary or key is absent.
        private static string ReadXTextField(Transaction tr, Entity ent, string key)
        {
            if (ent.ExtensionDictionary.IsNull) return "";
            if (!(tr.GetObject(ent.ExtensionDictionary, OpenMode.ForRead) is DBDictionary dict)) return "";
            if (!dict.Contains(key)) return "";
            if (!(tr.GetObject(dict.GetAt(key), OpenMode.ForRead) is Xrecord xr)) return "";
            TypedValue[] v = xr.Data?.AsArray();
            return (v != null && v.Length > 0) ? v[0].Value.ToString() : "";
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

        public static bool TryReadFrame(Database db, ObjectId id, out TFrame f)
        {
            using Transaction tr = db.TransactionManager.StartTransaction();
            f = default;
            if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) return false;
            bool ok = TryReadFrame(tr, ent, out f);
            tr.Commit();
            return ok;
        }

        // ---- faces ------------------------------------------------------------------------

        public static TFace[] Faces(TFrame f)
        {
            Point3d mid = f.O + f.Z * (f.L / 2.0);   // length runs along Z
            double hL = f.L / 2.0, hD = f.D / 2.0, hW = f.W / 2.0;
            // End caps may be NON-square (a plumb rafter foot, a mitered brace): the cap normal isn't along
            // f.Z, so the in-plane depth axis is f.X x N (not f.Y) and the cap is taller than D by 1/cos(tilt).
            // For a square end (N = -+f.Z) this reduces to V = +-f.Y, VHalf = hD (a flipped V sign is harmless
            // -- a face is symmetric in +-V), so girt/post caps are unchanged.
            (Vector3d V, double VHalf) Cap(Vector3d n)
            {
                Vector3d v = f.X.CrossProduct(n);
                if (v.Length < 1e-9) return (f.Y, hD);          // N parallel to the width axis (degenerate)
                v = v.GetNormal();
                double dot = System.Math.Abs(f.Y.DotProduct(v));
                return (v, dot > 1e-6 ? hD / dot : hD);
            }
            (Vector3d nV, double nVHalf) = Cap(f.NearN);
            (Vector3d fV, double fVHalf) = Cap(f.FarN);
            return new[]
            {
                new TFace { C = f.O,            N = f.NearN, U = f.X, UHalf = hW, V = nV, VHalf = nVHalf }, // near end (z=0)
                new TFace { C = f.O + f.Z*f.L,  N = f.FarN,  U = f.X, UHalf = hW, V = fV, VHalf = fVHalf }, // far end  (z=L)
                new TFace { C = mid + f.X*hW,   N =  f.X, U = f.Z, UHalf = hL, V = f.Y, VHalf = hD }, // +width
                new TFace { C = mid - f.X*hW,   N = -f.X, U = f.Z, UHalf = hL, V = f.Y, VHalf = hD }, // -width
                new TFace { C = mid + f.Y*hD,   N =  f.Y, U = f.Z, UHalf = hL, V = f.X, VHalf = hW }, // +depth
                new TFace { C = mid - f.Y*hD,   N = -f.Y, U = f.Z, UHalf = hL, V = f.X, VHalf = hW }, // -depth
            };
        }

        // Face whose plane the pick point lies nearest (perpendicular distance), preferring one the
        // point projects inside.
        public static TFace NearestFace(TFrame f, Point3d pick)
        {
            TFace best = default; double bestScore = double.MaxValue; bool bestInside = false;
            foreach (TFace fc in Faces(f))
            {
                Vector3d d = pick - fc.C;
                double perp = Math.Abs(d.DotProduct(fc.N));
                bool inside = Math.Abs(d.DotProduct(fc.U)) <= fc.UHalf + 1e-6
                              && Math.Abs(d.DotProduct(fc.V)) <= fc.VHalf + 1e-6;
                double score = perp + (inside ? 0 : 1e6);
                if (score < bestScore || (inside && !bestInside)) { best = fc; bestScore = score; bestInside = inside; }
            }
            return best;
        }

        // Find the pair of faces (one from each timber) that FACE each other: parallel + opposing
        // normals, B in front of A, with lateral overlap. Picks the closest such pair (smallest
        // gap). Returns false when none overlap (e.g. the timbers are too offset to connect).
        public static bool FindFacingPair(TFrame A, TFrame B, out TFace fa, out TFace fb, out double gap)
        {
            fa = default; fb = default; gap = double.MaxValue;
            bool found = false;
            foreach (TFace a in Faces(A))
                foreach (TFace b in Faces(B))
                {
                    if (a.N.DotProduct(b.N) > -0.99) continue;            // parallel + opposing
                    double g = (b.C - a.C).DotProduct(a.N);
                    if (g <= 1e-6) continue;                             // B must be in front of A
                    Vector3d d = b.C - a.C;
                    double du = Math.Abs(d.DotProduct(a.U)), dv = Math.Abs(d.DotProduct(a.V));
                    double bu = Math.Abs(b.U.DotProduct(a.U)) * b.UHalf + Math.Abs(b.V.DotProduct(a.U)) * b.VHalf;
                    double bv = Math.Abs(b.U.DotProduct(a.V)) * b.UHalf + Math.Abs(b.V.DotProduct(a.V)) * b.VHalf;
                    if (du > a.UHalf + bu || dv > a.VHalf + bv) continue; // must overlap laterally
                    if (g < gap) { gap = g; fa = a; fb = b; found = true; }
                }
            return found;
        }

        // Two faces MATE if their outward normals oppose, they are coplanar, and their rectangles
        // overlap. Returns the CENTRE OF THE OVERLAP rectangle (the true contact point -- e.g. a post
        // capped by a long girt nodes at the post, not halfway out along the girt).
        public static bool FacesMate(TFace a, TFace b, double tol, out Point3d at)
        {
            at = default;
            if (a.N.DotProduct(b.N) > -0.999) return false;             // must be opposing
            Vector3d d = b.C - a.C;
            if (Math.Abs(d.DotProduct(a.N)) > tol) return false;        // coplanar
            double cu = d.DotProduct(a.U), cv = d.DotProduct(a.V);      // b centre offset in a's plane
            // b's half-extents measured along a's in-plane axes (axes may be swapped between faces).
            double bu = Math.Abs(b.U.DotProduct(a.U)) * b.UHalf + Math.Abs(b.V.DotProduct(a.U)) * b.VHalf;
            double bv = Math.Abs(b.U.DotProduct(a.V)) * b.UHalf + Math.Abs(b.V.DotProduct(a.V)) * b.VHalf;
            if (Math.Abs(cu) > a.UHalf + bu + tol || Math.Abs(cv) > a.VHalf + bv + tol) return false; // disjoint
            // Centre of the overlap interval on each in-plane axis.
            double midU = (Math.Max(-a.UHalf, cu - bu) + Math.Min(a.UHalf, cu + bu)) / 2.0;
            double midV = (Math.Max(-a.VHalf, cv - bv) + Math.Min(a.VHalf, cv + bv)) / 2.0;
            at = a.C + midU * a.U + midV * a.V;
            return true;
        }

        // ---- interactive face pick (subentity) --------------------------------------------

        // Interactively pick ONE face of a managed timber. `ForceSubSelections` makes GetSelection
        // pick a face (AutoCAD highlights it natively); we read its FullSubentityPath, find the face
        // centroid via a Brep over that path, and return the matching ANALYTIC TFace (clean extents +
        // outward normal). Returns the timber id + face.
        public static bool PickFace(Editor ed, Database db, string msg, out ObjectId id, out TFace face)
        {
            id = ObjectId.Null; face = default;
            var pso = new PromptSelectionOptions
            { MessageForAdding = msg, SingleOnly = true, ForceSubSelections = true, SinglePickInSpace = true };
            // Filter the pick to 3DSOLIDs only -- managed timbers are solids, so this hides every other
            // entity (lines, dims, text, blocks, scarf debris) from the selection. A stray non-managed
            // solid still falls through to the TryReadFrame check below.
            var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "3DSOLID") });
            PromptSelectionResult psr = ed.GetSelection(pso, filter);
            if (psr.Status != PromptStatus.OK || psr.Value == null || psr.Value.Count == 0) return false;

            SelectedObject so = psr.Value[0];
            if (so == null) return false;
            id = so.ObjectId;
            if (!TryReadFrame(db, id, out TFrame frame)) { ed.WriteMessage("\nNot a managed timber."); return false; }

            SelectedSubObject[] subs = so.GetSubentities();
            if (subs == null || subs.Length == 0) { ed.WriteMessage("\nNo face captured."); return false; }
            FullSubentityPath path = subs[0].FullSubentityPath;
            if (path.SubentId.Type != SubentityType.Face) { ed.WriteMessage("\nPick a FACE (not an edge/vertex)."); return false; }

            if (!FaceCentroid(db, id, path, out Point3d c)) { ed.WriteMessage("\nCouldn't read the face geometry."); return false; }
            face = NearestFaceByCenter(frame, c);
            return true;
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

        // ---- scarf splice -----------------------------------------------------------------

        // A raw (non-appended) box solid spanning the frame's length range [x0,x1], full D x W, centred
        // in depth/width -- the un-scarfed stub of a beam piece.
        public static Solid3d MakeBoxSolid(TFrame f, double x0, double x1)
        {
            var s = new Solid3d();
            s.CreateBox(f.W, f.D, x1 - x0);   // local X=width, Y=depth, Z=length
            Point3d c = f.O + f.Z * ((x0 + x1) / 2.0);
            s.TransformBy(Matrix3d.AlignCoordinateSystem(
                Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis, c, f.X, f.Y, f.Z));
            return s;
        }

        // A raw (non-appended) box over an arbitrary frame-local range -- args by ROLE: LENGTH [x0,x1]
        // along Z from O, DEPTH [y0,y1] along Y and WIDTH [z0,z1] along X, all measured from the
        // centreline (so +/- D/2, +/- W/2 are the faces).
        public static Solid3d MakeBoxSolidLocal(TFrame f,
            double x0, double x1, double y0, double y1, double z0, double z1)
        {
            var s = new Solid3d();
            s.CreateBox(z1 - z0, y1 - y0, x1 - x0);   // local X=width, Y=depth, Z=length
            Point3d c = f.O + f.X * ((z0 + z1) / 2.0) + f.Y * ((y0 + y1) / 2.0) + f.Z * ((x0 + x1) / 2.0);
            s.TransformBy(Matrix3d.AlignCoordinateSystem(
                Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis, c, f.X, f.Y, f.Z));
            return s;
        }


        // Build the shop scarf TONGUE solid at scarf centre xc (length along beam) with scarf length l,
        // ported from drawscarf.lsp. The profile lives in the beam's length-depth plane (ry measured up
        // from the bottom face) and is extruded across the full width. Stage A: the splayed/squinted
        // blade only -- tables + key come later. Returns the appended Solid3d id.
        public static ObjectId BuildScarfTongue(Transaction tr, BlockTableRecord btr, TFrame f, double xc, double l)
        {
            double D = f.D, W = f.W, h = l / 2.0;
            // Hooked scarf face (rx = length from the node, ry = depth up from the bottom). Blade through
            // the node (0, D/2): unit tangent T (+rx), normal N (+ry). The two bearing faces are the blade
            // offset +/-0.5N -- so they sit 1" apart perpendicular to the face (the HOOK), NOT coplanar.
            // tongue1 (this) carries a single perpendicular step A->B at the keyway's right edge; tongue2
            // (the 180 rotation) carries the matching step on the left, so the 2"x1" keyway VOID opens
            // between them automatically.
            double tlen = Math.Sqrt(l * l + (9.0 - D) * (9.0 - D));
            double Tx = l / tlen, Ty = (9.0 - D) / tlen;   // unit tangent, points +rx
            double Nx = -Ty, Ny = Tx;                      // unit normal = (D-9, l)/|T|, points +ry
            double nry = D / 2.0;                           // node = (0, D/2)
            double s5 = (h - 0.5 * Nx) / Tx, s8 = (-h + 0.5 * Nx) / Tx;   // params to reach rx = +/-h
            var P = new (double rx, double ry)[]
            {
                (-h, 0), (h - 1.5, 0), (h - 1.5, 2), (h, 2),       // bottom + right under-squint (unchanged)
                (0.5 * Nx + s5 * Tx,  nry + 0.5 * Ny + s5 * Ty),   // pt5: +0.5N bearing blade at rx = h
                (Tx + 0.5 * Nx,       nry + Ty + 0.5 * Ny),        // A : keyway top-right = node + T + 0.5N
                (Tx - 0.5 * Nx,       nry + Ty - 0.5 * Ny),        // B : keyway bot-right = node + T - 0.5N
                (-0.5 * Nx + s8 * Tx, nry - 0.5 * Ny + s8 * Ty)    // pt8: -0.5N bearing blade at rx = -h
            };
            // Profile in the (length=Z, depth=Y) plane at the -W/2 width face, extruded across the width
            // (along X). Keeping the (rx,ry) winding at area>0 makes the region normal -f.X (since
            // Z x Y = -X), so extruding by -W carries it +X from -W/2 to +W/2.
            double area = 0;
            for (int i = 0; i < P.Length; i++)
            { var a = P[i]; var b = P[(i + 1) % P.Length]; area += a.rx * b.ry - b.rx * a.ry; }
            Point3d Map(double rx, double ry) => f.O + f.Z * (xc + rx) + f.Y * (-D / 2.0 + ry) + f.X * (-W / 2.0);
            var pts = new Point3dCollection();
            if (area > 0) foreach (var p in P) pts.Add(Map(p.rx, p.ry));
            else for (int i = P.Length - 1; i >= 0; i--) pts.Add(Map(P[i].rx, P[i].ry));

            var poly = new Polyline3d(Poly3dType.SimplePoly, pts, true);
            btr.AppendEntity(poly); tr.AddNewlyCreatedDBObject(poly, true);
            var curves = new DBObjectCollection { poly };
            DBObjectCollection regs = Region.CreateFromCurves(curves);
            var reg = (Region)regs[0];
            var solid = new Solid3d();
            // The profile lives in the plane f.X = -W/2; we want the tongue to span [-W/2, +W/2].
            // Region.CreateFromCurves picks the normal SIGN itself (the winding doesn't reliably force
            // it), so extrude TOWARD +f.X explicitly -- read the region's actual normal and choose the
            // height sign from it. (Trusting the winding sent the whole tongue width -W to one side,
            // shifting the scarf off the beam by W.)
            double hgt = reg.Normal.DotProduct(f.X) > 0.0 ? W : -W;
            solid.Extrude(reg, hgt, 0.0);
            reg.Dispose();
            poly.Erase();
            ObjectId id = btr.AppendEntity(solid); tr.AddNewlyCreatedDBObject(solid, true);
            return id;
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

        // ---- move / rotate ----------------------------------------------------------------

        // Rigidly transform a frame: O moves as a POINT (translation + rotation), the axes and end-cut
        // normals move as VECTORS (rotation only -- Vector3d.TransformBy ignores the translation). Dims
        // L/D/W are invariant under a rigid motion. Use only with rigid m (displacement / rotation);
        // a scaling/shear m would denormalize the axes and break the analytic faces.
        public static TFrame TransformFrame(TFrame f, Matrix3d m)
        {
            List<(Point3d, Vector3d)> cuts = null;
            if (f.Cuts != null)
            {
                cuts = new List<(Point3d, Vector3d)>(f.Cuts.Count);
                foreach ((Point3d P, Vector3d N) c in f.Cuts)
                    cuts.Add((c.P.TransformBy(m), c.N.TransformBy(m)));
            }
            return new TFrame
            {
                O = f.O.TransformBy(m),
                X = f.X.TransformBy(m), Y = f.Y.TransformBy(m), Z = f.Z.TransformBy(m),
                L = f.L, D = f.D, W = f.W,
                NearN = f.NearN.TransformBy(m), FarN = f.FarN.TransformBy(m),
                Cuts = cuts,
                Subtracts = f.Subtracts,  // LOCAL polygons -- invariant under a rigid frame move
                Features = f.Features,    // LOCAL boxes -- invariant too
                Pegs = f.Pegs,            // LOCAL cylinders -- invariant too
                JointPolys = f.JointPolys,  // LOCAL polygons -- invariant too
                JointPolysZ = f.JointPolysZ, // LOCAL Z-extruded polygons -- invariant too
                JointPrisms = f.JointPrisms // LOCAL oriented prisms -- invariant too
            };
        }

        // Rewrite a managed timber's stored TFrame + scarf node through a rigid transform m, in lockstep
        // with the solid (which the caller -- the ManagedTransformOverrule -- has already moved via
        // base.TransformBy). Plain entities with no TMFrame/TMScarf are left untouched, so native
        // MOVE/ROTATE/MIRROR keep the analytic faces TScan/TSpan/TFit rely on in sync automatically.
        public static void ApplyManagedTransform(Transaction tr, Entity ent, Matrix3d m)
        {
            if (ent.ExtensionDictionary.IsNull) return;
            var dict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, OpenMode.ForWrite);

            if (dict.Contains(FrameKey) && TryReadFrame(tr, ent, out TFrame f))
            {
                var xr = (Xrecord)tr.GetObject(dict.GetAt(FrameKey), OpenMode.ForWrite);
                xr.Data = FrameToBuffer(TransformFrame(f, m));
            }
            if (dict.Contains(ScarfKey))
            {
                var xr = (Xrecord)tr.GetObject(dict.GetAt(ScarfKey), OpenMode.ForWrite);
                TypedValue[] a = xr.Data.AsArray();
                if (a.Length >= 3)
                {
                    Point3d p = new Point3d(Convert.ToDouble(a[0].Value),
                        Convert.ToDouble(a[1].Value), Convert.ToDouble(a[2].Value)).TransformBy(m);
                    xr.Data = new ResultBuffer(
                        new TypedValue((int)DxfCode.Real, p.X),
                        new TypedValue((int)DxfCode.Real, p.Y),
                        new TypedValue((int)DxfCode.Real, p.Z));
                }
            }
            if (dict.Contains(SeatKey))
            {
                var xr = (Xrecord)tr.GetObject(dict.GetAt(SeatKey), OpenMode.ForWrite);
                TypedValue[] a = xr.Data.AsArray();
                var rb = new ResultBuffer();
                for (int i = 0; i + 2 < a.Length; i += 3)
                {
                    Point3d p = new Point3d(Convert.ToDouble(a[i].Value), Convert.ToDouble(a[i + 1].Value),
                        Convert.ToDouble(a[i + 2].Value)).TransformBy(m);
                    rb.Add(new TypedValue((int)DxfCode.Real, p.X));
                    rb.Add(new TypedValue((int)DxfCode.Real, p.Y));
                    rb.Add(new TypedValue((int)DxfCode.Real, p.Z));
                }
                xr.Data = rb;
            }
        }
    }

    public partial class ManagedCommands
    {
        // Session-sticky joint recipes -- remembered across cuts, seeded from the USER's saved defaults
        // (JointDefaults; factory *Spec.Default when none saved). Console review loops mutate these in
        // place per session; "Set as default" in the Joints pane persists + re-seeds them (ReseedJointSticky).
        private static ManagedTimber.JointSpec _joint = JointDefaults.Joint;
        // Post foot -> sill stub tenon (floor systems phase 3): traditionally a SHORT UNPEGGED stub
        // (gravity does the work), so its sticky seeds tenon-only, 2" long, no pegs. Reviewed by
        // TJointAll's sill pass; session-sticky like _joint.
        private static ManagedTimber.JointSpec _sillJoint = SillJointSeed();
        private static ManagedTimber.JointSpec SillJointSeed()
        {
            var s = ManagedTimber.JointSpec.Default;
            s.Tenon.Length = 2.0;
            s.Peg.Count = 0;
            return s;
        }
        // Summer end -> girt side (floor systems phase 4): the classic TUSK TENON (soffit-bearing
        // housing + deep tenon). Reviewed by TJointAll's summer pass; session-sticky like _joint.
        private static ManagedTimber.JointSpec _summerJoint = JointDefaults.Tusk;
        private static ManagedTimber.RafterFootSpec _rfoot = JointDefaults.RafterFoot;
        private static ManagedTimber.RafterHeadSpec _rhead = JointDefaults.RafterHead;
        private static ManagedTimber.RidgeHousingSpec _ridge = JointDefaults.Ridge;
        private static ManagedTimber.RidgeHousingSpec _ridgeRafter = JointDefaults.Ridge;   // ridge -> principal-rafter head (king-post-less bents; shares the one "Ridge housing" default)
        private static ManagedTimber.PurlinRafterSpec _purlin = JointDefaults.Purlin;
        private static ManagedTimber.CommonRidgeSpec _comridge = JointDefaults.CommonRidge;
        private static ManagedTimber.CommonEaveSpec _comeave = JointDefaults.CommonEave;
        private static ManagedTimber.StrutTenonSpec _strut = JointDefaults.Strut;
        // Braces are the SAME end->side tenon (StrutTenonJoint), just thinner (1.5") and conventionally
        // BAREFACED -- the tongue flush to one cheek (a clean soffit). Barefaced is computed from the brace
        // width at cut time (= (W - Thickness)/2), so it tracks any stock; Flip picks which cheek.
        private static ManagedTimber.StrutTenonSpec _brace = JointDefaults.Brace;
        private static bool _braceBarefaced = true;
        private static bool _braceFlip = false;

        // Re-seed the session sticky for one saved/reset joint default so the console T* commands pick it
        // up immediately (no restart). Called by JointDefaults.Save/Reset. Only the MATCHING key re-seeds
        // -- never the rest, so unrelated in-session console tweaks survive. Structs: assignment = full copy.
        internal static void ReseedJointSticky(string key)
        {
            switch (key)
            {
                case JointDefaults.KeyBox:         _joint = JointDefaults.Joint; break;
                case JointDefaults.KeyStrut:       _strut = JointDefaults.Strut; break;
                case JointDefaults.KeyBrace:       _brace = JointDefaults.Brace; break;
                case JointDefaults.KeyRafterFoot:  _rfoot = JointDefaults.RafterFoot; break;
                case JointDefaults.KeyRafterHead:  _rhead = JointDefaults.RafterHead; break;
                case JointDefaults.KeyRidge:       _ridge = JointDefaults.Ridge; _ridgeRafter = JointDefaults.Ridge; break;
                case JointDefaults.KeyCommonRidge: _comridge = JointDefaults.CommonRidge; break;
                case JointDefaults.KeyBirdsmouth:  _comeave = JointDefaults.CommonEave; break;
                case JointDefaults.KeyPurlin:      _purlin = JointDefaults.Purlin; _joistDove = JoistDoveSeed(); break;
                case JointDefaults.KeyQPRafter:    _qprafter = JointDefaults.QPRafter; break;
                case JointDefaults.KeyTusk:        _summerJoint = JointDefaults.Tusk; break;
            }
        }

        // Place one managed timber. Pick the EXTRUSION DIRECTION with the cursor: a start point (the near
        // end-face centre) and a direction point. The timber extrudes the given LENGTH along that
        // direction, so you DON'T have to re-roll the UCS Z per member. The W x D section's roll comes
        // from the UCS: DEPTH follows UCS Y (vertical in the rotate-90-about-X bent UCS), projected
        // perpendicular to the length; WIDTH completes a right-handed frame. Stored in WCS.
        [CommandMethod("TPlace")]
        public static void PlaceTimber()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            if (!GetSection(ed, out double w, out double d, out string type)) return;

            PromptPointResult p1 = ed.GetPoint("\nStart point (near end centre): ");
            if (p1.Status != PromptStatus.OK) return;

            // Section in UCS XY (width=X, depth=Y), length extruded along UCS Z. Axes resolved to WCS.
            Matrix3d ucs = ed.CurrentUserCoordinateSystem;
            Point3d a = p1.Value.TransformBy(ucs);
            CoordinateSystem3d cs = ucs.CoordinateSystem3d;
            Vector3d ux = cs.Xaxis.GetNormal(), uy = cs.Yaxis.GetNormal(), uz = cs.Zaxis.GetNormal();

            // Two-phase ghost jig: phase 1 rolls the W x D section about the base point, phase 2
            // drags the length out along UCS Z. Both phases preview the timber live.
            var jig = new PlaceJig(a, ux, uy, uz, w, d, 96.0);
            jig.Phase = PlaceJig.JigPhase.Roll;
            if (ed.Drag(jig).Status != PromptStatus.OK) return;
            jig.Phase = PlaceJig.JigPhase.Length;
            if (ed.Drag(jig).Status != PromptStatus.OK) return;

            double angle = jig.Angle, len = jig.Length;
            // Roll the section axes about UCS Z (right-handed: widthAxis x depthAxis = lengthAxis).
            Vector3d widthAxis  = ux * Math.Cos(angle) + uy * Math.Sin(angle);
            Vector3d depthAxis  = ux * (-Math.Sin(angle)) + uy * Math.Cos(angle);
            Vector3d lengthAxis = uz;

            ObjectId id = ManagedTimber.DrawBox(a, lengthAxis, depthAxis, widthAxis, len, d, w, type, "", "butt", "butt");
            ed.WriteMessage("\nTPlace: " + type + " " + (int)w + "x" + (int)d + "x" + len.ToString("0.#") +
                            " roll " + (angle * 180.0 / Math.PI).ToString("0.#") + " deg (" + id.Handle + ").");
        }

        // Assign freely-placed timbers to the frame's organization HIERARCHY:
        //   Frame -> Bent N -> members | Wall X -> Bay -> members | Floor N -> members
        // Pick one or more managed timbers, then say which frame + owner -- or set the target on the
        // TPanel Assembly group first and the command runs promptless (ManagedAssembly). Floor is the
        // owner of floor-system members (joists, summers), numbered bottom-up (1 = first). Writes the grouping
        // tags as an IN-PLACE XData patch -- the solid is NOT rebuilt (no erase/redraw, same handle)
        // -- so the Browser regroups them on Refresh. A wall/floor exists the moment a timber claims
        // it (implicit organizational labels). Repetitive free families (Joist, Summer) also get their
        // owner-addressed GridLabel minted here (J-<floor>-1..n in run order, continuing after the
        // group's existing numbering) -- the label conventions' free-timber scheme.
        [CommandMethod("TAssign")]
        public static void AssignToFrame()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            // Targets: a pane handoff (the Frame Browser's selected rows, consumed ONCE) or an
            // interactive selection (honors a pickfirst set). Either way only MANAGED timbers
            // (frame xrecord) survive; stale browser rows (erased handles) are skipped.
            List<ObjectId> picked = ManagedAssembly.TakeIds();
            if (picked == null)
            {
                var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "3DSOLID") });
                var pso = new PromptSelectionOptions { MessageForAdding = "\nSelect timbers to assign: " };
                PromptSelectionResult sel = ed.GetSelection(pso, filter);
                if (sel.Status != PromptStatus.OK) return;
                picked = new List<ObjectId>(sel.Value.GetObjectIds());
            }

            var ids = new List<ObjectId>();
            var frames = new Dictionary<ObjectId, ManagedTimber.TFrame>();
            int skipped = 0;
            foreach (ObjectId id in picked)
            {
                if (!id.IsNull && !id.IsErased
                    && ManagedTimber.TryReadFrame(db, id, out ManagedTimber.TFrame f)) { ids.Add(id); frames[id] = f; }
                else skipped++;
            }
            if (ids.Count == 0) { ed.WriteMessage("\nNo managed timbers in the selection."); return; }

            // The palette's Assembly pane supplies the target SILENTLY when it is set (the pane is
            // the command's visibility -- the ManagedSection pattern); the command-line prompts
            // below remain for a console-driven assign or an unparseable pane value.
            string frame = null;
            string bentTag = "", wallTag = "", bayTag = "", floorTag = "", colTag = "";
            if (ManagedAssembly.HasCurrent)
            {
                string owner = ManagedAssembly.Owner;
                switch (ManagedAssembly.Kind)
                {
                    case "Bent":
                        // Owner box = the bent number ("2", or typed intersection "2C"); the pane's
                        // second coordinate box supplies the column letter -- together the grid
                        // intersection a free post stands on.
                        SplitIntersection(owner, out string pb, out string pc);
                        if (pb.Length > 0)
                        {
                            bentTag = pb;
                            colTag = pc.Length > 0 ? pc : LettersOnly(ManagedAssembly.Bay);
                        }
                        break;
                    case "Wall":
                        if (owner.Length > 0 && char.IsLetter(owner[0]))
                        { wallTag = owner.ToUpperInvariant(); bayTag = ManagedAssembly.Bay.ToUpperInvariant(); }
                        break;
                    case "Floor":
                        if (int.TryParse(owner, out int fn) && fn > 0) floorTag = fn.ToString();
                        break;
                }
                if (bentTag.Length + wallTag.Length + floorTag.Length > 0) frame = ManagedAssembly.FrameTag;
            }

            if (frame == null)
            {
                // Frame tag: default to the first selected timber's existing frame, else "A".
                string defFrame = "A";
                Module1.DataStructure first = Module1.GetXdata(ids[0]);
                if (first != null && !string.IsNullOrEmpty(first.FrameTag)) defFrame = first.FrameTag;
                PromptResult fr = ed.GetString(
                    new PromptStringOptions("\nFrame tag: ") { DefaultValue = defFrame, AllowSpaces = false });
                if (fr.Status != PromptStatus.OK) return;
                frame = string.IsNullOrWhiteSpace(fr.StringResult) ? defFrame : fr.StringResult.Trim();

                // Owner kind: a numbered bent, a lettered wall (-> optional bay), or a numbered floor.
                var pko = new PromptKeywordOptions("\nAssign as");   // API appends "[Bent/Wall/Floor] <Bent>:"
                pko.Keywords.Add("Bent");
                pko.Keywords.Add("Wall");
                pko.Keywords.Add("Floor");
                pko.Keywords.Default = "Bent";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK) return;

                if (kr.StringResult == "Bent")
                {
                    // A bare number owns the timber ("2"); a grid INTERSECTION ("2C") also stamps the
                    // post-style GridLabel -- how a free post joins the frame at bent 2 x wall C.
                    PromptResult br = ed.GetString(new PromptStringOptions(
                        "\nBent number (or intersection, e.g. 2C): ") { DefaultValue = "1", AllowSpaces = false });
                    if (br.Status != PromptStatus.OK) return;
                    SplitIntersection(br.StringResult, out bentTag, out colTag);
                    if (bentTag.Length == 0) { ed.WriteMessage("\nTAssign: a bent must lead with its number."); return; }
                }
                else if (kr.StringResult == "Wall")
                {
                    PromptResult wr = ed.GetString(
                        new PromptStringOptions("\nWall letter: ") { DefaultValue = "A", AllowSpaces = false });
                    if (wr.Status != PromptStatus.OK) return;
                    wallTag = (string.IsNullOrWhiteSpace(wr.StringResult) ? "A" : wr.StringResult.Trim()).ToUpperInvariant();

                    PromptResult yr = ed.GetString(
                        new PromptStringOptions("\nBay (Roman numeral, blank for none): ") { AllowSpaces = false });
                    if (yr.Status != PromptStatus.OK) return;
                    bayTag = (yr.StringResult ?? "").Trim().ToUpperInvariant();
                }
                else   // Floor: floor-system members (joists, summers), level numbered bottom-up
                {
                    PromptIntegerResult lr = ed.GetInteger(new PromptIntegerOptions("\nFloor number (1 = first): ")
                    { DefaultValue = 1, LowerLimit = 1, AllowNegative = false, AllowZero = false });
                    if (lr.Status != PromptStatus.OK) return;
                    floorTag = lr.Value.ToString();
                }
            }

            // Owner-addressed labels (FAM-owner-seq, e.g. J-1-1 / P-A-1): minted in run order before
            // the patch so the loop below writes tags + label in one SetXdata. A grid INTERSECTION
            // assign skips the mint -- the intersection itself is the address (P-2C below).
            Dictionary<ObjectId, string> mint = colTag.Length > 0
                ? new Dictionary<ObjectId, string>()
                : MintOwnerLabels(db, ids, frames, bentTag, wallTag, bayTag, floorTag);

            // In-place XData patch (no rebuild): set the grouping tags on each timber + move it to the
            // group layer so it isolates with its bent/wall/floor.
            string groupLayer = ManagedTimber.GroupLayer(frame, bentTag, wallTag, floorTag);
            int n = 0;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectId lid = ManagedTimber.EnsureFrameLayer(tr, db, groupLayer);
                foreach (ObjectId id in ids)
                    if (tr.GetObject(id, OpenMode.ForWrite) is Entity ent) ent.LayerId = lid;
                tr.Commit();
            }
            foreach (ObjectId id in ids)
            {
                Module1.DataStructure xd = Module1.GetXdata(id);
                if (xd == null) continue;
                // A FULLY unassigned timber cannot be a skeleton member (the emitter stamps every
                // emit), so its first assignment also marks it FREE -- hand-placed timbers from
                // before the marker existed gain regenerate protection the moment they're assigned.
                // An already-assigned timber is left as-is (it could be a skeleton member re-owned).
                if (string.IsNullOrEmpty(xd.FrameTag) && string.IsNullOrEmpty(xd.BentNumber)
                    && string.IsNullOrEmpty(xd.WallTag) && string.IsNullOrEmpty(xd.BayTag)
                    && string.IsNullOrEmpty(xd.FloorTag) && string.IsNullOrEmpty(xd.GridLabel))
                    xd.Free = "1";
                xd.FrameTag = frame;
                xd.BentNumber = bentTag;
                xd.WallTag = wallTag;
                xd.BayTag = bayTag;
                xd.FloorTag = floorTag;
                if (mint.TryGetValue(id, out string label)) xd.GridLabel = label;
                else if (colTag.Length > 0)
                {
                    // Grid intersection, TYPE-FIRST like the emitter (P-2C beside the skeleton's
                    // KP-2C -- distinct labels, so the shop-map dedup labels both). FamilyFor
                    // guarantees the prefix (unknown types use their initial).
                    xd.GridLabel = FamilyFor(xd.Type) + "-" + bentTag + colTag;
                }
                Module1.SetXdata(id, xd);
                n++;
            }

            string target = bentTag.Length > 0
                ? "Bent " + bentTag + (colTag.Length > 0 ? " (grid " + bentTag + colTag + ")" : "")
                : wallTag.Length > 0 ? "Wall " + wallTag + (bayTag.Length > 0 ? " / Bay " + bayTag : "")
                : "Floor " + floorTag;
            ed.WriteMessage("\nTAssign: " + n + " timber(s) -> Frame " + frame + " / " + target
                + (mint.Count > 0 ? " (" + mint.Count + " label(s) minted)" : "")
                + (skipped > 0 ? " (skipped " + skipped + " non-managed)" : "") + ".");
            ManagedAssembly.RaiseApplied();   // an open Frame Browser refreshes its rows
        }

        // The free families TAssign mints owner-addressed labels for (label grammar FAM-OWNER-SEQ;
        // hierarchy: floor-system members belong to their FLOOR -> J-1-1..n). Editor-local map -- the
        // generator's FamilyCode table stays on its side of the boundary.
        private static readonly Dictionary<string, string> OwnerLabelFamilies =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { { "Joist", "J" }, { "Summer", "SB" } };

        // Bent family codes for the grid-intersection mint (P-2C, KP-2C) -- the editor-local mirror of
        // the generator's BentFamilyCode (the boundary keeps the tables separate on purpose). Unknown
        // roles mint the bare anchor.
        private static readonly Dictionary<string, string> BentLabelFamilies =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { { "Post", "P" }, { "KingPost", "KP" }, { "QueenPost", "QP" }, { "Rafter", "RF" },
              { "Strut", "ST" }, { "VStrut", "VS" }, { "HBeam", "HB" }, { "HPost", "HP" },
              { "Girt", "G" }, { "Brace", "B" }, { "Ridge", "RG" }, { "Common", "C" },
              { "Purlin", "PU" }, { "EaveGirt", "EG" }, { "FloorGirt", "FG" } };

        // The label family for ANY type: the owner table, the bent table, else the type's initial
        // letter uppercased -- every TAssign label carries a family prefix (Robert's call; the bare
        // anchor read as unlabeled).
        private static string FamilyFor(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return "T";
            if (OwnerLabelFamilies.TryGetValue(type, out string f)) return f;
            if (BentLabelFamilies.TryGetValue(type, out f)) return f;
            return char.ToUpperInvariant(type.Trim()[0]).ToString();
        }

        // Mint FAM-<owner>-<seq> GridLabels for the selected family members. Owner preference: floor,
        // else bay, else wall, else bent. Sequence runs ALONG THE ROW (midpoints sorted on the
        // horizontal direction across the members, oriented +X/+Y so numbering is reproducible) and
        // continues after the highest existing sequence in the same group among UNSELECTED timbers --
        // so adding a second field to a floor extends the count, while re-assigning the whole field
        // renumbers it.
        private static Dictionary<ObjectId, string> MintOwnerLabels(Database db, List<ObjectId> ids,
            Dictionary<ObjectId, ManagedTimber.TFrame> frames, string bentTag, string wallTag, string bayTag,
            string floorTag)
        {
            var mint = new Dictionary<ObjectId, string>();
            string owner = floorTag.Length > 0 ? floorTag
                : bayTag.Length > 0 ? bayTag : wallTag.Length > 0 ? wallTag : bentTag;
            if (owner.Length == 0) return mint;

            var byFam = new Dictionary<string, List<ObjectId>>(StringComparer.Ordinal);
            foreach (ObjectId id in ids)
            {
                Module1.DataStructure xd = Module1.GetXdata(id);
                if (xd == null || string.IsNullOrEmpty(xd.Type)) continue;
                string fam = FamilyFor(xd.Type);   // EVERY type mints FAM-owner-seq now
                if (!byFam.TryGetValue(fam, out var list)) byFam[fam] = list = new List<ObjectId>();
                list.Add(id);
            }
            if (byFam.Count == 0) return mint;

            List<ManagedTimber.ShopInfo> all = ManagedTimber.EnumerateForShop(db);
            var selected = new HashSet<ObjectId>(ids);
            foreach (var kv in byFam)
            {
                string prefix = kv.Key + "-" + owner + "-";
                int next = 1;
                foreach (var t in all)
                {
                    if (selected.Contains(t.Id)) continue;
                    string gl = t.GridLabel ?? "";
                    if (!gl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    if (int.TryParse(gl.Substring(prefix.Length), out int seq) && seq >= next) next = seq + 1;
                }

                Vector3d run = Vector3d.ZAxis.CrossProduct(frames[kv.Value[0]].Z);
                if (run.Length < 1e-6) run = Vector3d.XAxis;         // vertical member: any stable order
                run = run.GetNormal();
                if (run.X < -1e-9 || (Math.Abs(run.X) < 1e-9 && run.Y < 0)) run = -run;
                kv.Value.Sort((a, b) => MidAlong(frames[a], run).CompareTo(MidAlong(frames[b], run)));
                foreach (ObjectId id in kv.Value) mint[id] = prefix + (next++);
            }
            return mint;
        }

        private static double MidAlong(ManagedTimber.TFrame f, Vector3d dir)
            => (f.O + f.Z * (f.L / 2.0)).GetAsVector().DotProduct(dir);

        // A 1-2 letter column string, uppercased; anything else -> "".
        private static string LettersOnly(string s)
        {
            s = (s ?? "").Trim().ToUpperInvariant();
            if (s.Length == 0 || s.Length > 2) return "";
            foreach (char c in s) if (!char.IsLetter(c)) return "";
            return s;
        }

        // "2C" -> bent "2" + column "C"; "2" -> bent "2", no column; "1.1B" -> "1.1" + "B" (sub-bent
        // lines carry dots). Digits lead, a 1-2 letter column may trail; anything else parses empty.
        private static void SplitIntersection(string s, out string bent, out string col)
        {
            s = (s ?? "").Trim().ToUpperInvariant();
            int i = 0;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
            bent = s.Substring(0, i);
            col = s.Substring(i);
            foreach (char c in col)
                if (!char.IsLetter(c)) { col = ""; break; }
            if (col.Length > 2 || bent.Length == 0) col = "";
        }

        // Span two timbers: pick two managed timbers; the facing faces are found from their stored
        // frames and a timber of the current W/D is dropped flush in the gap, perpendicular to both
        // faces (so its ends bear flush -> TScan finds the nodes). Declines if no facing overlap.
        // (Milestone 1: the girt centers on the face overlap; choosing which faces / the height, and
        // the angled match-face miter, come with subentity face-picking later.)
        [CommandMethod("TSpan")]
        public static void SpanFaces()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!GetSection(ed, out double w, out double d, out string type)) return;

            if (!PickTimber(ed, db, "\nPick first timber: ", out _, out ManagedTimber.TFrame A)) return;
            if (!PickTimber(ed, db, "\nPick second timber: ", out _, out ManagedTimber.TFrame B)) return;

            if (!ManagedTimber.FindFacingPair(A, B, out ManagedTimber.TFace fa, out ManagedTimber.TFace fb, out double gap))
            { ed.WriteMessage("\nThose timbers have no facing, overlapping faces to span."); return; }

            // Lateral (fa.V) + gap (fa.N) placement: centre on the overlap. The position ALONG the
            // timbers (fa.U) is set by the jig -- the UCS Y=0 datum initially, then dragged.
            Vector3d off = (fb.C - fa.C) - (fb.C - fa.C).DotProduct(fa.N) * fa.N;
            Point3d origin = fa.C + off * 0.5;

            // DEPTH rides the more-vertical in-plane axis. On post sides the rail (fa.U = the host's
            // length axis) is vertical and depth lies on it (a girt between posts); on girt sides the
            // rail is horizontal and depth lies on fa.V (a summer between bent girts) -- the fixed
            // fa.U assignment was rolling that section 90 degrees.
            bool railVertical = Math.Abs(fa.U.GetNormal().DotProduct(Vector3d.ZAxis))
                             >= Math.Abs(fa.V.GetNormal().DotProduct(Vector3d.ZAxis));
            Vector3d dAxis = railVertical ? fa.U : fa.V;
            Vector3d wAxis = railVertical ? fa.V : fa.U;

            // Ghost the span and let the user set its height along the post rail, measured from the UCS
            // origin (datum s=0 = base). The girt's Center/Bottom/Top face lands on that line. The drag
            // loop handles the live keywords; a pick commits. Height is picked in an elevation UCS (or
            // typed via the Height keyword) -- in Plan UCS a pick can't read height, so warn.
            CoordinateSystem3d ucs = ed.CurrentUserCoordinateSystem.CoordinateSystem3d;
            var jig = new SpanJig(origin, fa, gap, railVertical ? d : w, railVertical ? w : d, ucs.Origin);
            bool canPickHeight = Math.Abs(fa.U.GetNormal().DotProduct(ucs.Zaxis.GetNormal())) < 0.5;
            if (!canPickHeight)
                ed.WriteMessage("\nTip: a pick can't set the height in this UCS (post is end-on) -- " +
                                "use an elevation UCS (Bent/Wall) or the Height keyword.");

            while (true)
            {
                PromptResult pr = ed.Drag(jig);
                if (pr.Status == PromptStatus.Keyword)
                {
                    if (pr.StringResult == "Height")
                    {
                        var hopts = new PromptDistanceOptions("\nHeight above base: ")
                        { DefaultValue = jig.LineY, UseDefaultValue = true, AllowNegative = true };
                        PromptDoubleResult hr = ed.GetDistance(hopts);
                        if (hr.Status == PromptStatus.OK) jig.SetHeight(hr.Value);
                    }
                    else jig.SetJustify(pr.StringResult);
                    continue;
                }
                if (pr.Status != PromptStatus.OK) return;
                break;
            }

            ObjectId id = ManagedTimber.DrawBox(jig.Origin, fa.N, dAxis, wAxis, gap, d, w, type, "", "matchface", "matchface");
            ed.WriteMessage("\nTSpan: " + type + " " + (int)w + "x" + (int)d + "x" + gap.ToString("0.#") +
                            " " + jig.Mode + " @ height " + jig.LineY.ToString("0.#") + " (" + id.Handle + ").");
        }

        // Connect two EXPLICITLY-picked faces with a member. You pick the exact face on each timber
        // (native subentity highlight), so multi-candidate ambiguity (brace above/below a girt) is your
        // choice, not a guess. Two cases, chosen automatically from the picked faces:
        //   FACING (opposing-parallel) faces -- a girt between two posts: a member fills the
        //     perpendicular gap with SQUARE ends (flush).
        //   ANGLED faces -- a brace from a post inner face to a girt underside: the member runs
        //     diagonally and each end is MITERED flush to its face plane, so TScan finds the nodes.
        [CommandMethod("TJoin")]
        public static void Join()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!GetSection(ed, out double w, out double d, out string type)) return;

            if (!ManagedTimber.PickFace(ed, db, "\nPick the FIRST face: ", out ObjectId idA, out ManagedTimber.TFace fa)) return;
            if (!ManagedTimber.PickFace(ed, db, "\nPick the SECOND face: ", out ObjectId idB, out ManagedTimber.TFace fb)) return;

            double facing = fa.N.DotProduct(fb.N);
            if (facing > 0.99)
            { ed.WriteMessage("\nThose two faces point the SAME way -- pick faces that look toward each other."); return; }

            ObjectId id;
            if (facing < -0.99)
            {
                // Opposing-parallel: square-ended span filling the perpendicular gap, centred on the
                // overlap. Depth rides the more-vertical in-plane axis (same rule as TSpan -- a fixed
                // fa.U assignment rolled the section 90 degrees on girt-side spans).
                double gap = (fb.C - fa.C).DotProduct(fa.N);
                if (gap <= 1e-6) { ed.WriteMessage("\nNo positive gap between the faces."); return; }
                Vector3d off = (fb.C - fa.C) - gap * fa.N;
                Point3d origin = fa.C + off * 0.5;
                bool railVertical = Math.Abs(fa.U.GetNormal().DotProduct(Vector3d.ZAxis))
                                 >= Math.Abs(fa.V.GetNormal().DotProduct(Vector3d.ZAxis));
                id = ManagedTimber.DrawBox(origin, fa.N, railVertical ? fa.U : fa.V,
                                           railVertical ? fa.V : fa.U, gap, d, w, type, "", "butt", "butt");
                ed.WriteMessage("\nTJoin (square): " + type + " " + (int)w + "x" + (int)d + "x" + gap.ToString("0.#") +
                                " (" + id.Handle + ").");
            }
            else
            {
                // Angled: knee brace seated in the corner, each end mitered flush to its picked face. The
                // runs are how far the foot/head sit back from the corner along each picked face. Body
                // centres let the geometry place the foot/head on the OPEN side of each face (away from
                // the other timber's bulk) regardless of how the stored face normals are signed.
                if (!ManagedTimber.TryReadFrame(db, idA, out ManagedTimber.TFrame frA) ||
                    !ManagedTimber.TryReadFrame(db, idB, out ManagedTimber.TFrame frB))
                { ed.WriteMessage("\nCouldn't read the timber frames."); return; }
                Point3d bodyA = frA.O + frA.Z * (frA.L / 2.0);
                Point3d bodyB = frB.O + frB.Z * (frB.L / 2.0);

                // The palette (ManagedBrace) initializes the foot/head runs ONCE; the ghost previews the
                // brace and the user approves with Enter or cancels with Esc. To resize, set the palette
                // first and re-run (the size is read once at command start, not tracked live).
                double footRun = ManagedBrace.HasCurrent ? ManagedBrace.FootRun : 18.0;
                double headRun = ManagedBrace.HasCurrent ? ManagedBrace.HeadRun : 18.0;
                if (!ManagedTimber.TryBraceFrame(fa, fb, d, w, footRun, headRun, bodyA, bodyB,
                                                 out ManagedTimber.TFrame bframe))
                { ed.WriteMessage("\nThose faces don't form a brace corner."); return; }

                bool place;
                using (Solid3d ghost = ManagedTimber.BuildFramedSolid(bframe))
                {
                    ghost.ColorIndex = 5;   // blue, colour-blind safe
                    TransientManager tm = TransientManager.CurrentTransientManager;
                    var ints = new IntegerCollection();
                    tm.AddTransient(ghost, TransientDrawingMode.DirectShortTerm, 128, ints);
                    try
                    {
                        // No "[Yes/No] <Yes>" in the message -- PromptKeywordOptions appends that itself
                        // (putting it in the text too made the prompt render doubled).
                        var pko = new PromptKeywordOptions(
                            "\nPlace the brace (foot " + footRun.ToString("0.#") + ", head " +
                            headRun.ToString("0.#") + " -- set size on the palette)? ");
                        pko.Keywords.Add("Yes");
                        pko.Keywords.Add("No");
                        pko.Keywords.Default = "Yes";
                        pko.AllowNone = true;   // Enter = Yes
                        PromptResult kr = ed.GetKeywords(pko);
                        place = kr.Status == PromptStatus.None ||
                                (kr.Status == PromptStatus.OK && kr.StringResult == "Yes");
                    }
                    finally { tm.EraseTransient(ghost, ints); }
                }
                if (!place) { ed.WriteMessage("\nBrace cancelled."); return; }

                id = ManagedTimber.DrawMiteredBrace(fa, fb, d, w, footRun, headRun, type, "", bodyA, bodyB);
                if (id.IsNull) { ed.WriteMessage("\nCouldn't build a brace between those faces."); return; }
                ed.WriteMessage("\nTJoin (knee brace): " + type + " " + (int)w + "x" + (int)d +
                                " foot " + footRun.ToString("0.#") + " head " + headRun.ToString("0.#") +
                                " (" + id.Handle + ").");
            }
        }

        // Fit the END of an existing timber onto a target face: pick the timber's end face (the one to
        // move), then the face it should land on. The picked end is trimmed OR extended along the
        // timber's own axis until it lands flush on the target plane -- square if the target is square
        // to the axis, mitered if angled (a rafter foot on a plate, a strut into a brace). The other end
        // stays put. The solid is rebuilt; type/designation carry over. TScan then finds the new node.
        [CommandMethod("TFit")]
        public static void Fit()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!ManagedTimber.PickFace(ed, db, "\nPick the END to move (the timber's end face): ",
                out ObjectId mid, out ManagedTimber.TFace endFace)) return;
            if (!ManagedTimber.TryReadFrame(db, mid, out ManagedTimber.TFrame f))
            { ed.WriteMessage("\nNot a managed timber."); return; }

            // Must be an END face (normal roughly along the LENGTH axis Z), not a side face.
            Vector3d axis = f.Z.GetNormal();
            if (Math.Abs(endFace.N.DotProduct(axis)) < 0.5)
            { ed.WriteMessage("\nThat's a SIDE face -- pick the timber's END face (the end you want to land)."); return; }
            bool isNear = endFace.C.DistanceTo(f.O) < endFace.C.DistanceTo(f.O + f.Z * f.L);

            if (!ManagedTimber.PickFace(ed, db, "\nPick the TARGET face to land on: ",
                out ObjectId tid, out ManagedTimber.TFace target)) return;
            if (tid == mid) { ed.WriteMessage("\nPick a target on a DIFFERENT timber."); return; }

            // Crossing of the timber centreline (O + t*Z) with the target plane.
            double denom = f.Z.DotProduct(target.N);
            if (Math.Abs(denom) < 1e-6)
            { ed.WriteMessage("\nThe timber runs parallel to that face -- it can't land there."); return; }
            double t = (target.C - f.O).DotProduct(target.N) / denom;
            Point3d crossing = f.O + f.Z * t;

            ManagedTimber.TFrame nf = f;
            if (isNear)
            {
                Point3d farC = f.O + f.Z * f.L;
                double newL = (farC - crossing).DotProduct(f.Z);
                if (newL <= 1e-3) { ed.WriteMessage("\nThat would collapse the timber past its far end."); return; }
                nf.O = crossing; nf.L = newL; nf.NearN = target.N.Negate();   // far end unchanged
            }
            else
            {
                double newL = (crossing - f.O).DotProduct(f.Z);
                if (newL <= 1e-3) { ed.WriteMessage("\nThat would collapse the timber past its near end."); return; }
                nf.L = newL; nf.FarN = target.N.Negate();                     // near end unchanged
            }

            // Tag the fitted end "matchface" on the STORED xdata, then rebuild through RebuildFromFrame
            // -- which preserves the timber's whole identity (frame/owner tags, grid label, group layer,
            // production number, floor + free markers, persisted joint specs). The old bare
            // DrawFramedSolid redraw silently stripped all of it on every TFit.
            Module1.DataStructure old = Module1.GetXdata(mid);
            string type = string.IsNullOrEmpty(old?.Type) ? "Timber" : old.Type;
            if (old != null)
            {
                if (isNear) old.JointNear = "matchface"; else old.JointFar = "matchface";
                Module1.SetXdata(mid, old);
            }

            ObjectId nid = ManagedTimber.RebuildFromFrame(mid, nf);
            ed.WriteMessage("\nTFit: " + type + " " + (isNear ? "near" : "far") +
                            " end fitted; new length " + nf.L.ToString("0.#") + " (" + nid.Handle + ").");
        }

        // Re-section ONE managed timber in place: pick any managed timber (skeleton OR free -- the editor
        // is one surface over the managed set), then change its W x D. Placement (O/axes/L), end cuts,
        // feature cuts, and the grouping tags (frame/bent/bay/wall/grid label) all carry over via
        // RegenerateSection. The prompts default to the timber's CURRENT section, so a quick change is one
        // or two keystrokes; Enter on both leaves it unchanged. Type is kept (re-section is a section edit,
        // not a retype). The solid is rebuilt (erase + redraw -> a new handle); re-run TScan afterward as
        // the side faces may have moved.
        [CommandMethod("TSection")]
        public static void Section()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the timber to re-section: ", out ObjectId id, out ManagedTimber.TFrame f)) return;

            if (!GetPositive(ed, "Width", f.W, out double newW)) return;
            if (!GetPositive(ed, "Depth", f.D, out double newD)) return;

            if (Math.Abs(newW - f.W) < 1e-6 && Math.Abs(newD - f.D) < 1e-6)
            { ed.WriteMessage("\nTSection: section unchanged (" + (int)f.W + "x" + (int)f.D + ")."); return; }

            ObjectId nid = ManagedTimber.RegenerateSection(id, newW, newD, "");   // "" = keep existing type
            if (nid.IsNull) { ed.WriteMessage("\nTSection: could not re-section that timber."); return; }

            Module1.DataStructure xd = Module1.GetXdata(nid);
            string type = string.IsNullOrEmpty(xd?.Type) ? "Timber" : xd.Type;
            ed.WriteMessage("\nTSection: " + type + " -> " + (int)newW + "x" + (int)newD +
                            " (" + nid.Handle + "). Re-run TScan to refresh nodes.");
        }

        // Apply a keyed hook scarf to ONE full-length beam at a picked point, splitting it into two
        // interlocking managed timbers (the shop joint from drawscarf.lsp). Stage A: the splayed/squinted
        // BLADE only -- tables + key come in Stages B/C. Each piece overlaps the other by the scarf
        // length, so the assembled run stays the beam's drawn length while each piece's stored Size is its
        // FULL physical length including the scarf tongue (printed for manufacturing). TScan nodes at the
        // scarf centre.
        [CommandMethod("TScarf")]
        public static void ScarfSplice()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the beam to scarf: ", out ObjectId beamId, out ManagedTimber.TFrame f)) return;

            double l = 3.0 * f.D;
            if (l + 1.0 > f.L)
            { ed.WriteMessage("\nThe beam is too short for a " + l.ToString("0.#") + "\" scarf."); return; }

            // Ghost the scarf footprint sliding along the beam; the cursor projects onto the beam axis
            // (clamped to fit), and the Length keyword resizes the region live. A pick commits.
            var jig = new ScarfJig(f, l);
            while (true)
            {
                PromptResult dr = ed.Drag(jig);
                if (dr.Status == PromptStatus.Keyword)
                {
                    if (dr.StringResult == "Length")
                    {
                        var lopts = new PromptDistanceOptions("\nScarf length: ")
                        { DefaultValue = jig.Len, UseDefaultValue = true, AllowNegative = false, AllowZero = false };
                        PromptDoubleResult lr = ed.GetDistance(lopts);
                        if (lr.Status == PromptStatus.OK)
                        {
                            if (lr.Value + 1.0 > f.L)
                                ed.WriteMessage("\nThe beam is too short for a " + lr.Value.ToString("0.#") + "\" scarf.");
                            else jig.SetLength(lr.Value);
                        }
                    }
                    continue;
                }
                if (dr.Status != PromptStatus.OK) return;
                break;
            }
            double xc = jig.Xc;
            l = jig.Len;
            double h = l / 2.0;
            if (xc - h < 0.5 || xc + h > f.L - 0.5)
            { ed.WriteMessage("\nThe scarf (" + l.ToString("0.#") + "\") doesn't fit within the beam at that point."); return; }

            Module1.DataStructure od = Module1.GetXdata(beamId);
            string type = string.IsNullOrEmpty(od?.Type) ? "Timber" : od.Type;
            string desg = od?.Designation ?? "";
            Point3d cs = f.O + f.Z * xc;

            // Piece frames: piece1 = [0, xc+h], piece2 = [xc-h, L] along Z -- they overlap over the scarf.
            ManagedTimber.TFrame f1 = f; f1.L = xc + h; f1.NearN = f.Z.Negate(); f1.FarN = f.Z;
            ManagedTimber.TFrame f2 = f; f2.O = f.O + f.Z * (xc - h); f2.L = f.L - (xc - h); f2.NearN = f.Z.Negate(); f2.FarN = f.Z;

            ObjectId nP1, nP2;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                // Tongue1 (left piece) + tongue2 = 180-degree point reflection about the scarf centre.
                ObjectId t1id = ManagedTimber.BuildScarfTongue(tr, btr, f, xc, l);
                var tongue1 = (Solid3d)tr.GetObject(t1id, OpenMode.ForWrite);
                var tongue2 = (Solid3d)tongue1.Clone();
                btr.AppendEntity(tongue2); tr.AddNewlyCreatedDBObject(tongue2, true);
                tongue2.TransformBy(Matrix3d.Rotation(Math.PI, f.X, cs));   // reflect about the WIDTH axis

                // Un-scarfed stubs, then union each piece's tongue onto its stub.
                Solid3d leftStub = ManagedTimber.MakeBoxSolid(f, 0, xc - h);
                Solid3d rightStub = ManagedTimber.MakeBoxSolid(f, xc + h, f.L);
                nP1 = btr.AppendEntity(leftStub); tr.AddNewlyCreatedDBObject(leftStub, true);
                nP2 = btr.AppendEntity(rightStub); tr.AddNewlyCreatedDBObject(rightStub, true);
                leftStub.BooleanOperation(BooleanOperationType.BoolUnite, tongue1);
                rightStub.BooleanOperation(BooleanOperationType.BoolUnite, tongue2);

                // Stage B: bearing tables (locks). Each 1.5x2 abutment notch is filled across the OUTER
                // width bands [+1,+W/2] and [-W/2,-1] ((W-2)/2 each); the middle 2" band [-1,+1] stays
                // open as the key slot (Stage C). Bottom-right abutment -> piece2; the 180-rotated
                // top-left abutment -> piece1. Locks overrun Mx into their stub for a clean union.
                double D = f.D, W = f.W, Mx = 0.25;
                // bottom-right (piece2 / rightStub)
                UnionBox(tr, btr, rightStub, f, xc + h - 1.5, xc + h + Mx, -D / 2.0, -D / 2.0 + 2.0, 1.0, W / 2.0);
                UnionBox(tr, btr, rightStub, f, xc + h - 1.5, xc + h + Mx, -D / 2.0, -D / 2.0 + 2.0, -W / 2.0, -1.0);
                // top-left (piece1 / leftStub)
                UnionBox(tr, btr, leftStub, f, xc - h - Mx, xc - h + 1.5, D / 2.0 - 2.0, D / 2.0, 1.0, W / 2.0);
                UnionBox(tr, btr, leftStub, f, xc - h - Mx, xc - h + 1.5, D / 2.0 - 2.0, D / 2.0, -W / 2.0, -1.0);

                // Stage C: the key band -- the middle 2" width band [-1,+1] of each abutment, drawn as the
                // ALTERNATING piece's material (per drawscarf.lsp): bottom-right -> piece1, top-left ->
                // piece2. It interlocks through the other piece's tables (integral, not a loose wedge).
                // It runs flush to the abutment face (no overrun into the mating piece) and Mx into its
                // own tongue for a clean union.
                UnionBox(tr, btr, leftStub, f, xc + h - 1.5 - Mx, xc + h, -D / 2.0, -D / 2.0 + 2.0, -1.0, 1.0);
                UnionBox(tr, btr, rightStub, f, xc - h, xc - h + 1.5 + Mx, D / 2.0 - 2.0, D / 2.0, -1.0, 1.0);

                // Drop boolean OPERATION HISTORY so the scarf pieces save cleanly (see BuildFramedSolid).
                try { leftStub.RecordHistory = false; leftStub.ShowHistory = false;
                      rightStub.RecordHistory = false; rightStub.ShowHistory = false; } catch { }

                ManagedTimber.WriteFrameXrecord(tr, leftStub, f1);
                ManagedTimber.WriteFrameXrecord(tr, rightStub, f2);

                // The key is INTEGRAL: Stage C above unions the middle width band as the alternating
                // piece's material (per drwscarf.lsp's scarfkey1/scarfkey2), so the two halves interlock
                // through each other's tables. There is no loose key wedge -- and the keyway HOOK + VOID
                // are formed by the tongue profile (BuildScarfTongue) + its 180 rotation, not by a cut.

                ((Entity)tr.GetObject(beamId, OpenMode.ForWrite)).Erase();
                tr.Commit();
            }

            // XData (outside the txn, like the other commands) + the explicit splice node.
            string sz1 = (int)Math.Round(f.W) + "x" + (int)Math.Round(f.D) + "x" + Module1.BuyLongFeet(f1.L);
            string sz2 = (int)Math.Round(f.W) + "x" + (int)Math.Round(f.D) + "x" + Module1.BuyLongFeet(f2.L);
            // Pieces inherit the parent's free-assembly origin: scarfed FREE timbers stay regen-proof;
            // scarfed SKELETON halves stay skeleton (a regenerate replaces the unsplit member).
            var xd1 = new Module1.DataStructure(type, "", desg, sz1, "0", 0, 0, 0, f.W, f.D, f1.L, "scarf", "scarf", false);
            var xd2 = new Module1.DataStructure(type, "", desg, sz2, "0", 0, 0, 0, f.W, f.D, f2.L, "scarf", "scarf", false);
            xd1.Free = od?.Free ?? "";
            xd2.Free = od?.Free ?? "";
            Module1.SetXdata(nP1, xd1);
            Module1.SetXdata(nP2, xd2);
            ManagedTimber.WriteScarfNode(db, nP1, cs);
            ManagedTimber.WriteScarfNode(db, nP2, cs);

            ed.WriteMessage("\nTScarf: l=" + l.ToString("0.#") +
                            "  piece1 overall " + f1.L.ToString("0.#") + "\" (" + nP1.Handle + ")" +
                            ",  piece2 overall " + f2.L.ToString("0.#") + "\" (" + nP2.Handle + ").");
        }

        // Stamp the joint TYPE onto both rebuilt timbers (the same TMJointSpecs the Join pane writes), so the
        // BOM and a pane re-pick can recover the joint's elements. Serializes the preset built from the live
        // command spec. No-op for an unkeyed cut (jid 0).
        private static void StampJoint(ObjectId a, ObjectId b, int jid, ConnectionType ct)
        {
            if (jid == 0 || ct == null) return;
            string st = ct.SerializeState();
            ManagedTimber.WriteJointSpec(a, jid, st);
            ManagedTimber.WriteJointSpec(b, jid, st);
        }

        // Cut a girt -> post MORTISE & TENON (+ peg bores). Pick the TENONED timber (a girt) then the
        // MORTISED timber (a post): the girt end-cap that bears on a post SIDE face is found, the joint
        // recipe is reviewed (sticky tenon thickness / length / top + bottom shoulders / width offset, and a
        // Pegs sub-menu), the girt gets a shouldered/offset tenon, the post a matching mortise + the peg
        // bores (the shop bores only the mortise), and both solids rebuild in place. v1 handles the
        // end-into-face case (a horizontal girt into a vertical post); the cuts survive MOVE / TFit (LOCAL,
        // serialized Features/Pegs) and TScan still reports the bearing node (nominal faces unchanged).
        // Re-cutting the same girt+post REPLACES the joint (by its id); re-run per end for a both-ends girt.
        [CommandMethod("TJoint")]
        public static void JointMortiseTenon()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the TENONED timber (girt / summer): ", out ObjectId girtId, out ManagedTimber.TFrame girt)) return;
            if (!PickTimber(ed, db, "\nPick the MORTISED timber (post / carrier): ", out ObjectId postId, out ManagedTimber.TFrame post)) return;
            if (girtId == postId) { ed.WriteMessage("\nPick two different timbers."); return; }

            // The girt END-cap (Faces 0/1) that mates a post SIDE face (coplanar, opposing, overlapping).
            ManagedTimber.TFace[] gf = ManagedTimber.Faces(girt);
            ManagedTimber.TFace[] pf = ManagedTimber.Faces(post);
            bool found = false;
            ManagedTimber.TFace gEnd = default;
            for (int gi = 0; gi <= 1 && !found; gi++)
                foreach (ManagedTimber.TFace ps in pf)
                {
                    if (Math.Abs(ps.N.DotProduct(post.Z)) >= 0.5) continue;       // the post face must be a SIDE
                    if (ManagedTimber.FacesMate(gf[gi], ps, 0.25, out _)) { gEnd = gf[gi]; found = true; break; }
                }
            if (!found)
            { ed.WriteMessage("\nNo end-into-face contact -- the tenoned end must bear on a side face of the host."); return; }

            if (!ReviewJoint(ed)) return;   // review / adjust the sticky joint recipe (Enter / Cut proceeds)

            if (!ManagedTimber.GirtPostJoint(girt, post, gEnd, _joint,
                    out List<(Point3d Min, Point3d Max, bool Subtract)> features,
                    out List<(Point3d C, Vector3d Axis, double R, double Half)> pegs,
                    out List<(Point3d[] Poly, bool OnPost, double Xlo, double Xhi)> polys))
            {
                ed.WriteMessage("\nNothing to cut -- enable a tenon, housing or shoulder (or the tenon collapsed).");
                return;
            }

            if (girt.Features == null) girt.Features = new List<(Point3d, Point3d, bool, int)>();
            if (post.Features == null) post.Features = new List<(Point3d, Point3d, bool, int)>();

            // De-dup: if this contact already carries a joint, replace it (re-cut = edit) instead of
            // stacking. The girt tenon at this end identifies the joint; reuse its id (group-remove all its
            // features from both timbers), or -- for a shoulder-only joint (no tenon box) -- the shared
            // JointPolys id, or for a legacy/unkeyed (id 0) cut sweep by geometry.
            bool farEnd = gEnd.N.DotProduct(girt.Z) > 0.0;
            int ti = girt.Features.FindIndex(f => !f.Subtract &&
                         (((f.Min.Z + f.Max.Z) / 2.0 > girt.L / 2.0) == farEnd));
            int reuseId = ti >= 0 ? girt.Features[ti].Joint : 0;
            if (reuseId == 0) reuseId = ExistingRafterFootId(girt, post);   // shoulder-only (poly) joint at this pair
            int jid;
            if (reuseId != 0)
            {
                girt.Features.RemoveAll(f => f.Joint == reuseId);
                post.Features.RemoveAll(f => f.Joint == reuseId);
                post.Pegs?.RemoveAll(p => p.Joint == reuseId);            // old pegs go with the old joint
                girt.JointPolys?.RemoveAll(j => j.Joint == reuseId);      // old shoulder polys too
                post.JointPolys?.RemoveAll(j => j.Joint == reuseId);
                jid = reuseId;
            }
            else if (ti >= 0)
            {
                girt.Features.RemoveAt(ti);
                // legacy id-0 joint predates ids: drop old post subtracts overlapping any new pocket.
                post.Features.RemoveAll(f => f.Subtract &&
                    features.Exists(nf => nf.Subtract && BoxesOverlap(f.Min, f.Max, nf.Min, nf.Max)));
                jid = NextJointId(db);
            }
            else jid = NextJointId(db);

            ApplyJoint(ref girt, ref post, jid, features, pegs);
            ApplyRafterFoot(ref girt, ref post, jid, polys);   // shoulder triangle polys (shared poly applier)

            ObjectId ngirt = ManagedTimber.RebuildFromFrame(girtId, girt);
            ObjectId npost = ManagedTimber.RebuildFromFrame(postId, post);
            StampJoint(ngirt, npost, jid, ConnectionType.BoxTenon(_joint));
            ed.WriteMessage("\nTJoint: joint #" + jid + " cut -- " + JointSummary(_joint) +
                            " (girt " + ngirt.Handle + ", post " + npost.Handle + ").");
        }

        // Route a cutter result onto the two frames, stamped with the joint id: each feature box to the
        // girt (UNION) or post (SUBTRACT) by its Subtract flag; each peg cylinder to the post. Shared by
        // TJoint + TJointAll (TFrame is a struct -> ref).
        private static void ApplyJoint(ref ManagedTimber.TFrame girt, ref ManagedTimber.TFrame post, int jid,
            List<(Point3d Min, Point3d Max, bool Subtract)> features,
            List<(Point3d C, Vector3d Axis, double R, double Half)> pegs)
        {
            if (girt.Features == null) girt.Features = new List<(Point3d, Point3d, bool, int)>();
            if (post.Features == null) post.Features = new List<(Point3d, Point3d, bool, int)>();
            foreach ((Point3d Min, Point3d Max, bool Subtract) f in features)
                (f.Subtract ? post.Features : girt.Features).Add((f.Min, f.Max, f.Subtract, jid));
            if (pegs.Count > 0)
            {
                if (post.Pegs == null) post.Pegs = new List<(Point3d, Vector3d, double, double, int)>();
                foreach ((Point3d C, Vector3d Axis, double R, double Half) p in pegs)
                    post.Pegs.Add((p.C, p.Axis, p.R, p.Half, jid));
            }
        }

        // Human-readable list of the elements a JointSpec cuts (for command messages).
        private static string JointSummary(ManagedTimber.JointSpec s)
        {
            string parts = "";
            if (s.Tenon.On) parts = "tenon";
            if (s.Housing.On && s.Housing.Seat > 1e-6) parts += (parts.Length > 0 ? " + " : "") + "housing";
            if (s.Shoulder.On && s.Shoulder.Seat > 1e-6) parts += (parts.Length > 0 ? " + " : "") + "shoulder";
            if (s.Peg.Count > 0) parts += (parts.Length > 0 ? " + " : "") + s.Peg.Count + " peg(s)";
            return parts.Length > 0 ? parts : "(none)";
        }

        // Role sets for the batch auto-cut: a girt-family END that bears on a Post SIDE face gets a M&T;
        // a Post FOOT that bears on a Sill side (its top) gets the stub tenon (floor systems phase 3).
        private static readonly HashSet<string> GirtRoles = new HashSet<string> { "Girt", "EaveGirt", "FloorGirt" };
        private static readonly HashSet<string> PostRoles = new HashSet<string> { "Post" };
        private static readonly HashSet<string> SillRoles = new HashSet<string> { "Sill" };
        private static readonly HashSet<string> SummerRoles = new HashSet<string> { "Summer" };
        // A summer's end can die into a bent floor girt, the tie, or (first floor) a sill.
        private static readonly HashSet<string> SummerHostRoles = new HashSet<string> { "Girt", "FloorGirt", "Sill" };
        private static readonly HashSet<string> JoistRoles = new HashSet<string> { "Joist" };
        // A joist's end dies into any floor carrier.
        private static readonly HashSet<string> JoistHostRoles = new HashSet<string> { "Girt", "FloorGirt", "EaveGirt", "Summer", "Sill" };

        // Batch-cut the frame's end->side joinery, DELIBERATELY: the first prompt scopes the batch to
        // All timbers or a SELECTION -- the selected timbers are the ones that GET joints (the male
        // side: girt ends, post feet, summer ends, joist ends); hosts are always found drawing-wide.
        // Passes, each with its own sticky recipe, reviewed only when its role is in scope:
        //   girt-family end -> post side  (mortise & tenon + pegs; _joint)
        //   post foot -> sill             (short unpegged stub; _sillJoint)
        //   summer end -> girt/sill       (tusk tenon; _summerJoint)
        //   joist end -> carrier          (housed dovetail; _joistDove -- the deliberate half of TJoist)
        // Contacts that already carry a joint are SKIPPED (idempotent -- safe to re-run after manual
        // tweaks); a host fed by several members rebuilds once.
        [CommandMethod("TJointAll")]
        public static void JointAll()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            // DELIBERATE scope (Robert's rule: joinery is applied to selected timbers or groups).
            HashSet<ObjectId> scope = null;
            var sko = new PromptKeywordOptions("\nCut joinery for") { AllowNone = true };
            sko.Keywords.Add("All");
            sko.Keywords.Add("Select");
            sko.Keywords.Default = "All";
            PromptResult skr = ed.GetKeywords(sko);
            if (skr.Status != PromptStatus.OK && skr.Status != PromptStatus.None) return;
            if (skr.Status == PromptStatus.OK && skr.StringResult == "Select")
            {
                var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "3DSOLID") });
                var pso = new PromptSelectionOptions { MessageForAdding = "\nSelect the timbers to joint: " };
                PromptSelectionResult sel = ed.GetSelection(pso, filter);
                if (sel.Status != PromptStatus.OK) return;
                scope = new HashSet<ObjectId>(sel.Value.GetObjectIds());
            }

            var all = ManagedTimber.EnumerateWithRole(db);
            // Working frames carry the accumulating Features/Pegs; geometry (O/axes/L/D/W) never changes and
            // Faces() ignore features, so mates can be found against the originals throughout the pass.
            var work = new Dictionary<ObjectId, ManagedTimber.TFrame>();
            foreach ((ObjectId Id, ManagedTimber.TFrame F, string Role) t in all) work[t.Id] = t.F;
            var dirty = new HashSet<ObjectId>();
            var cuts = new List<(ObjectId girt, ObjectId post, int jid, ConnectionType ct)>();   // for the joint-type stamp
            int nextId = NextJointId(db);
            int cut = 0, skipped = 0, failed = 0;

            bool InScope(ObjectId id) => scope == null || scope.Contains(id);
            bool RolePresent(HashSet<string> roles)
            {
                foreach ((ObjectId Id, ManagedTimber.TFrame F, string Role) t in all)
                    if (roles.Contains(t.Role) && InScope(t.Id)) return true;
                return false;
            }

            // One end->side pass: every in-scope `maleRoles` END that bears on a `hostRoles` SIDE face
            // gets the M&T from `spec` (already-jointed contacts skip). Girt / sill / summer passes.
            void Pass(HashSet<string> maleRoles, HashSet<string> hostRoles,
                ManagedTimber.JointSpec spec, ConnectionType ct)
            {
                foreach ((ObjectId Id, ManagedTimber.TFrame F, string Role) g in all)
                {
                    if (!maleRoles.Contains(g.Role) || !InScope(g.Id)) continue;
                    ManagedTimber.TFrame girt = work[g.Id];
                    ManagedTimber.TFace[] gf = ManagedTimber.Faces(girt);
                    for (int gi = 0; gi <= 1; gi++)
                    {
                        ManagedTimber.TFace gEnd = gf[gi];

                        // The host whose SIDE face this end bears on (same test as TJoint).
                        ObjectId postId = ObjectId.Null;
                        foreach ((ObjectId Id, ManagedTimber.TFrame F, string Role) pc in all)
                        {
                            if (pc.Id == g.Id || !hostRoles.Contains(pc.Role)) continue;
                            bool mate = false;
                            foreach (ManagedTimber.TFace ps in ManagedTimber.Faces(pc.F))
                            {
                                if (Math.Abs(ps.N.DotProduct(pc.F.Z)) >= 0.5) continue;   // host face must be a SIDE
                                if (ManagedTimber.FacesMate(gEnd, ps, 0.25, out _)) { mate = true; break; }
                            }
                            if (mate) { postId = pc.Id; break; }
                        }
                        if (postId.IsNull) continue;

                        // Skip a contact that already carries a tenon (a union box) OR a shoulder (shared polys)
                        // at this end (idempotent -- safe to re-run).
                        bool farEnd = gEnd.N.DotProduct(girt.Z) > 0.0;
                        ManagedTimber.TFrame post = work[postId];
                        if ((girt.Features != null && girt.Features.Exists(f => !f.Subtract &&
                                (((f.Min.Z + f.Max.Z) / 2.0 > girt.L / 2.0) == farEnd)))
                            || ExistingRafterFootId(girt, post) != 0)
                        { skipped++; continue; }

                        if (!ManagedTimber.GirtPostJoint(girt, post, gEnd, spec,
                                out List<(Point3d Min, Point3d Max, bool Subtract)> features,
                                out List<(Point3d C, Vector3d Axis, double R, double Half)> pegs,
                                out List<(Point3d[] Poly, bool OnPost, double Xlo, double Xhi)> polys))
                        { failed++; continue; }

                        int jid = nextId++;
                        ApplyJoint(ref girt, ref post, jid, features, pegs);
                        ApplyRafterFoot(ref girt, ref post, jid, polys);   // shoulder triangle polys
                        work[g.Id] = girt; work[postId] = post;   // persist the (struct) frames' new lists
                        dirty.Add(g.Id); dirty.Add(postId);
                        cuts.Add((g.Id, postId, jid, ct));
                        cut++;
                    }
                }
            }

            // The girt -> post pass: reviewed first (Escape here aborts the whole batch, the
            // long-standing contract); runs only when a girt-family male is in scope.
            if (RolePresent(GirtRoles))
            {
                if (!ReviewJoint(ed)) return;
                Pass(GirtRoles, PostRoles, _joint, ConnectionType.BoxTenon(_joint));
            }
            int girtCuts = cut;

            // Extra passes only when the frame carries the roles in scope. Each has its own sticky
            // recipe, reviewed through the same editor by temporarily swapping the _joint sticky;
            // Escape skips just that pass.
            bool ReviewSwapped(ref ManagedTimber.JointSpec sticky)
            {
                ManagedTimber.JointSpec saveJoint = _joint;
                _joint = sticky;
                bool go = ReviewJoint(ed);
                sticky = _joint;
                _joint = saveJoint;
                return go;
            }

            // SILL pass: post foot -> sill, the short unpegged stub.
            if (RolePresent(SillRoles))
            {
                ed.WriteMessage("\nSill pass -- post foot -> sill stub tenon:");
                if (ReviewSwapped(ref _sillJoint))
                    Pass(PostRoles, SillRoles, _sillJoint, ConnectionType.BoxTenon(_sillJoint));
                else ed.WriteMessage("\nSill stub tenons skipped.");
            }
            int sillCuts = cut - girtCuts;

            // SUMMER pass: summer end -> girt/sill side, the tusk tenon (soffit bearing + deep tenon).
            if (RolePresent(SummerRoles))
            {
                ed.WriteMessage("\nSummer pass -- summer end -> girt tusk tenon:");
                if (ReviewSwapped(ref _summerJoint))
                    Pass(SummerRoles, SummerHostRoles, _summerJoint, ConnectionType.TuskTenon(_summerJoint));
                else ed.WriteMessage("\nSummer tusk tenons skipped.");
            }
            int summerCuts = cut - girtCuts - sillCuts;

            // JOIST pass: joist end -> carrier, the housed dovetail -- the deliberate half of TJoist's
            // Joint option (place plain, adjust, select, cut). The pass turns the sticky ON (running it
            // IS the deliberate act); Off + Done in the review still vetoes. Already-dovetailed pairs
            // skip by their shared joint id. Cuts JointPrisms via PurlinRafterJoint, not GirtPostJoint.
            if (RolePresent(JoistRoles))
            {
                ed.WriteMessage("\nJoist pass -- joist end -> carrier housed dovetail:");
                _joistDove.On = true;
                if (!ReviewJoistDove(ed) || !_joistDove.On)
                    ed.WriteMessage("\nJoist dovetails skipped.");
                else
                {
                    ConnectionType dove = ConnectionType.HousedDovetail(_joistDove);
                    foreach ((ObjectId Id, ManagedTimber.TFrame F, string Role) j in all)
                    {
                        if (!JoistRoles.Contains(j.Role) || !InScope(j.Id)) continue;
                        foreach ((ObjectId Id, ManagedTimber.TFrame F, string Role) h in all)
                        {
                            if (h.Id == j.Id || !JoistHostRoles.Contains(h.Role)) continue;
                            ManagedTimber.TFrame joist = work[j.Id];
                            ManagedTimber.TFrame host = work[h.Id];
                            if (ExistingRafterFootId(joist, host) != 0) { skipped++; continue; }
                            if (!FindFootContact(joist, host, out ManagedTimber.TFace hFace)) continue;
                            if (!ManagedTimber.PurlinRafterJoint(joist, host, hFace, _joistDove,
                                    out List<(Point3d[] Poly, Vector3d Extrude, bool OnRafter)> prisms, out _))
                            { failed++; continue; }
                            int jid = nextId++;
                            if (joist.JointPrisms == null) joist.JointPrisms = new List<(Point3d[], Vector3d, int, bool)>();
                            if (host.JointPrisms == null) host.JointPrisms = new List<(Point3d[], Vector3d, int, bool)>();
                            foreach ((Point3d[] Poly, Vector3d Extrude, bool OnRafter) p in prisms)
                                (p.OnRafter ? host.JointPrisms : joist.JointPrisms).Add((p.Poly, p.Extrude, jid, p.OnRafter));
                            work[j.Id] = joist; work[h.Id] = host;
                            dirty.Add(j.Id); dirty.Add(h.Id);
                            cuts.Add((j.Id, h.Id, jid, dove));
                            cut++;
                        }
                    }
                }
            }
            int joistCuts = cut - girtCuts - sillCuts - summerCuts;

            var remap = new Dictionary<ObjectId, ObjectId>();
            foreach (ObjectId id in dirty) remap[id] = ManagedTimber.RebuildFromFrame(id, work[id]);
            foreach ((ObjectId girt, ObjectId post, int jid, ConnectionType ct) c in cuts)
                StampJoint(remap[c.girt], remap[c.post], c.jid, c.ct);
            ed.WriteMessage("\nTJointAll: cut " + cut + " joint(s)" +
                            (sillCuts > 0 ? " (" + sillCuts + " post-foot -> sill)" : "") +
                            (summerCuts > 0 ? " (" + summerCuts + " summer -> girt)" : "") +
                            (joistCuts > 0 ? " (" + joistCuts + " joist end(s))" : "") +
                            ", skipped " + skipped + " already-jointed" +
                            (failed > 0 ? ", " + failed + " collapsed" : "") + ".");
        }

        // Review / adjust the sticky joint recipe (_joint) as a KIT OF PARTS: the elements (Tenon, Housing,
        // Pegs) are peers, each a toggleable sub-menu. Enter / "Cut" proceeds (returns true); Escape cancels
        // (false). Shared by TJoint (per cut) and TJointAll (once). A new element type adds a keyword + a
        // Review<Kind> sub-menu here.
        private static bool ReviewJoint(Editor ed)
        {
            while (true)
            {
                string tn = _joint.Tenon.On ? "On (T" + _joint.Tenon.Thickness + " L" + _joint.Tenon.Length + ")" : "Off";
                string hs = _joint.Housing.On ? "On (" + _joint.Housing.Seat + ")" : "Off";
                string sh = _joint.Shoulder.On ? "On (" + _joint.Shoulder.Seat + ")" : "Off";
                var pko = new PromptKeywordOptions(
                    "\nJoint -- Tenon: " + tn + " | Housing: " + hs + " | Shoulder: " + sh + " | Pegs: " + _joint.Peg.Count + ". ")
                { AllowNone = true };
                pko.Keywords.Add("Cut");
                pko.Keywords.Add("Tenon");
                pko.Keywords.Add("Housing");
                pko.Keywords.Add("Shoulder");
                pko.Keywords.Add("Pegs");
                pko.Keywords.Default = "Cut";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return false;   // cancelled
                string kw = kr.Status == PromptStatus.None ? "Cut" : kr.StringResult;
                if (kw == "Cut") return true;
                switch (kw)
                {
                    case "Tenon":    ReviewTenon(ed); break;
                    case "Housing":  ReviewHousing(ed, ref _joint.Housing); break;
                    case "Shoulder": ReviewShoulder(ed); break;
                    case "Pegs":     ReviewPegs(ed, ref _joint.Peg); break;
                }
            }
        }

        // The tenon sub-menu: edit the sticky _joint.Tenon. Enter / "Done" returns.
        private static void ReviewTenon(Editor ed)
        {
            while (true)
            {
                var pko = new PromptKeywordOptions(
                    "\nTenon On=" + (_joint.Tenon.On ? "Yes" : "No") + " Thickness=" + _joint.Tenon.Thickness +
                    " Length=" + _joint.Tenon.Length + " TopShoulder=" + _joint.Tenon.ShoulderTop +
                    " BotShoulder=" + _joint.Tenon.ShoulderBottom + " Offset=" + _joint.Tenon.Offset + ". ")
                { AllowNone = true };
                pko.Keywords.Add("Done");
                pko.Keywords.Add("On");
                pko.Keywords.Add("Thickness");
                pko.Keywords.Add("Length");
                pko.Keywords.Add("TopShoulder");
                pko.Keywords.Add("BotShoulder");
                pko.Keywords.Add("Offset");
                pko.Keywords.Default = "Done";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return;
                string kw = kr.Status == PromptStatus.None ? "Done" : kr.StringResult;
                if (kw == "Done") return;
                switch (kw)
                {
                    case "On":        _joint.Tenon.On = !_joint.Tenon.On; break;
                    case "Thickness": if (GetPositive(ed, "Tenon thickness", _joint.Tenon.Thickness, out double tv)) _joint.Tenon.Thickness = tv; break;
                    case "Length":    if (GetPositive(ed, "Tenon length",    _joint.Tenon.Length,    out double lv)) _joint.Tenon.Length    = lv; break;
                    case "TopShoulder": if (GetDouble  (ed, "Top shoulder",      _joint.Tenon.ShoulderTop, false, out double rt)) _joint.Tenon.ShoulderTop = rt; break;
                    case "BotShoulder": if (GetDouble  (ed, "Bottom shoulder",   _joint.Tenon.ShoulderBottom, false, out double rb)) _joint.Tenon.ShoulderBottom = rb; break;
                    case "Offset":    if (GetDouble  (ed, "Width offset",    _joint.Tenon.Offset,    true,  out double ov)) _joint.Tenon.Offset = ov; break;
                }
            }
        }

        // The shared housing sub-menu: edit a HousingSpec footprint (On + Seat + the four per-face shoulders --
        // Top / Bottom + Side1 / Side2; every shoulder 0 = full section). Enter / "Done" returns. Used by TJoint
        // (box) AND the polygon tenons (strut / brace / QP apex) -- one housing UI everywhere.
        private static void ReviewHousing(Editor ed, ref ManagedTimber.HousingSpec hsg)
        {
            while (true)
            {
                var pko = new PromptKeywordOptions(
                    "\nHousing On=" + (hsg.On ? "Yes" : "No") + " Seat=" + hsg.Seat +
                    " TopShoulder=" + hsg.ShoulderTop + " BotShoulder=" + hsg.ShoulderBottom +
                    " Side1=" + hsg.ShoulderSide1 + " Side2=" + hsg.ShoulderSide2 + ". ")
                { AllowNone = true };
                pko.Keywords.Add("Done");
                pko.Keywords.Add("On");
                pko.Keywords.Add("Seat");
                pko.Keywords.Add("TopShoulder");
                pko.Keywords.Add("BotShoulder");
                pko.Keywords.Add("Side1");
                pko.Keywords.Add("Side2");
                pko.Keywords.Default = "Done";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return;
                string kw = kr.Status == PromptStatus.None ? "Done" : kr.StringResult;
                if (kw == "Done") return;
                switch (kw)
                {
                    case "On":        hsg.On = !hsg.On; break;
                    case "Seat":      if (GetPositive(ed, "Housing seat depth (let-in)", hsg.Seat, out double cv)) { hsg.Seat = cv; hsg.On = true; } break;
                    case "TopShoulder": if (GetDouble(ed, "Top shoulder (flush band at the top)",    hsg.ShoulderTop, false, out double rt)) hsg.ShoulderTop = rt; break;
                    case "BotShoulder": if (GetDouble(ed, "Bottom shoulder (flush band at the bottom)", hsg.ShoulderBottom, false, out double rb)) hsg.ShoulderBottom = rb; break;
                    case "Side1":     if (GetDouble(ed, "Side-1 shoulder (inset from one side)",       hsg.ShoulderSide1, false, out double s1)) hsg.ShoulderSide1 = s1; break;
                    case "Side2":     if (GetDouble(ed, "Side-2 shoulder (inset from the other side)", hsg.ShoulderSide2, false, out double s2)) hsg.ShoulderSide2 = s2; break;
                }
            }
        }

        // The shoulder sub-menu of TJoint: edit the sticky _joint.Shoulder -- the 3-pt triangle notch
        // (face-bot, face-top, seat-bot). Seat = the bearing-seat depth into the post; like the housing it
        // advances the seat, so it extends the tenon and shifts the pegs. The face edge spans the section
        // (top/bottom shoulders); the top is a diagonal. Enter / "Done" returns.
        private static void ReviewShoulder(Editor ed)
        {
            while (true)
            {
                var pko = new PromptKeywordOptions(
                    "\nShoulder (3-pt triangle) On=" + (_joint.Shoulder.On ? "Yes" : "No") + " Seat=" + _joint.Shoulder.Seat +
                    " Thickness=" + (_joint.Shoulder.Thickness > 0.0 ? _joint.Shoulder.Thickness.ToString() : "full") +
                    " TopShoulder=" + _joint.Shoulder.ShoulderTop + " BotShoulder=" + _joint.Shoulder.ShoulderBottom +
                    " Offset=" + _joint.Shoulder.Offset + ". ")
                { AllowNone = true };
                pko.Keywords.Add("Done");
                pko.Keywords.Add("On");
                pko.Keywords.Add("Seat");
                pko.Keywords.Add("Thickness");
                pko.Keywords.Add("TopShoulder");
                pko.Keywords.Add("BotShoulder");
                pko.Keywords.Add("Offset");
                pko.Keywords.Default = "Done";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return;
                string kw = kr.Status == PromptStatus.None ? "Done" : kr.StringResult;
                if (kw == "Done") return;
                switch (kw)
                {
                    case "On":        _joint.Shoulder.On = !_joint.Shoulder.On; break;
                    case "Seat":      if (GetPositive(ed, "Seat depth into the post", _joint.Shoulder.Seat, out double cv)) _joint.Shoulder.Seat = cv; break;
                    case "Thickness": if (GetDouble(ed, "Shoulder width (0 = full)", _joint.Shoulder.Thickness, false, out double hw)) _joint.Shoulder.Thickness = hw; break;
                    case "TopShoulder": if (GetDouble(ed, "Top shoulder (inset the face top)", _joint.Shoulder.ShoulderTop, false, out double rt)) _joint.Shoulder.ShoulderTop = rt; break;
                    case "BotShoulder": if (GetDouble(ed, "Bottom shoulder (raise the seat)", _joint.Shoulder.ShoulderBottom, false, out double rb)) _joint.Shoulder.ShoulderBottom = rb; break;
                    case "Offset":    if (GetDouble(ed, "Width offset",  _joint.Shoulder.Offset,    true,  out double ov)) _joint.Shoulder.Offset = ov; break;
                }
            }
        }

        // The shared peg sub-menu: edit a PegSpec layout (Count 0 = no pegs). Enter / "Done" returns. Used by
        // TJoint (box) AND the polygon tenons (strut / brace / QP apex / rafter foot) -- one peg UI everywhere.
        private static void ReviewPegs(Editor ed, ref ManagedTimber.PegSpec peg)
        {
            while (true)
            {
                var pko = new PromptKeywordOptions(
                    "\nPegs Count=" + peg.Count + " Diameter=" + peg.Diameter +
                    " Setback=" + peg.Setback + " Spacing=" + peg.Spacing +
                    " Bore=" + peg.Bore + " BlindDepth=" + peg.BlindDepth +
                    " Flip=" + (peg.BlindFlip ? "On" : "Off") + ". ")
                { AllowNone = true };
                pko.Keywords.Add("Done");
                pko.Keywords.Add("Count");
                pko.Keywords.Add("Diameter");
                pko.Keywords.Add("Setback");
                pko.Keywords.Add("Spacing");
                pko.Keywords.Add("Bore");
                pko.Keywords.Add("BlindDepth");
                pko.Keywords.Add("Flip");
                pko.Keywords.Default = "Done";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return;
                string kw = kr.Status == PromptStatus.None ? "Done" : kr.StringResult;
                if (kw == "Done") return;
                switch (kw)
                {
                    case "Count":
                        var io = new PromptIntegerOptions("\nPeg count <" + peg.Count + ">: ")
                        { AllowNegative = false, DefaultValue = peg.Count, UseDefaultValue = true };
                        PromptIntegerResult ir = ed.GetInteger(io);
                        if (ir.Status == PromptStatus.OK) peg.Count = ir.Value;
                        break;
                    case "Diameter":  if (GetPositive(ed, "Peg diameter",  peg.Diameter,   out double dv)) peg.Diameter = dv; break;
                    case "Setback":   if (GetDouble  (ed, "Setback into the tongue", peg.Setback, false, out double sb)) peg.Setback = sb; break;
                    case "Spacing":   if (GetDouble  (ed, "Stacked spacing", peg.Spacing,  false, out double sp)) peg.Spacing = sp; break;
                    case "BlindDepth":if (GetDouble  (ed, "Blind depth past tenon", peg.BlindDepth, false, out double bd)) peg.BlindDepth = bd; break;
                    case "Flip":      peg.BlindFlip = !peg.BlindFlip; break;
                    case "Bore":
                        var bo = new PromptKeywordOptions("\nBore type. ") { AllowNone = true };
                        bo.Keywords.Add("Full");
                        bo.Keywords.Add("Blind");
                        bo.Keywords.Default = peg.Bore == ManagedTimber.PegBore.Blind ? "Blind" : "Full";
                        PromptResult br = ed.GetKeywords(bo);
                        if (br.Status == PromptStatus.OK)
                            peg.Bore = br.StringResult == "Blind" ? ManagedTimber.PegBore.Blind : ManagedTimber.PegBore.Full;
                        break;
                }
            }
        }

        // Doc-wide next joint id = 1 + the max joint id stamped on any managed timber's features (0 is
        // reserved for legacy / unkeyed). Girt + post are read fresh before any write, so this is unique.
        private static int NextJointId(Database db)
        {
            int max = 0;
            foreach (ManagedTimber.TFrame f in ManagedTimber.EnumerateFrameFrames(db, null))
            {
                if (f.Features != null)
                    foreach ((Point3d Min, Point3d Max, bool Subtract, int Joint) ft in f.Features)
                        if (ft.Joint > max) max = ft.Joint;
                if (f.JointPolys != null)   // rafter-foot (and future polygon) joints share the id space
                    foreach ((Point3d[] Poly, int Joint, bool Subtract, double Xlo, double Xhi) jp in f.JointPolys)
                        if (jp.Joint > max) max = jp.Joint;
                if (f.JointPolysZ != null)   // Z-extruded polys (ridge tongue) share it too
                    foreach ((Point3d[] Poly, int Joint, bool Subtract, double Xlo, double Xhi) jp in f.JointPolysZ)
                        if (jp.Joint > max) max = jp.Joint;
                if (f.JointPrisms != null)   // oriented-prism joints (common->ridge, purlin->rafter) share it too --
                    foreach ((Point3d[] Poly, Vector3d Extrude, int Joint, bool Subtract) jp in f.JointPrisms)
                        if (jp.Joint > max) max = jp.Joint;   // missing this collided every prism joint onto one id
                if (f.Pegs != null)          // pegs ride a joint id -- count them so a fresh id never lands on one
                    foreach ((Point3d C, Vector3d Axis, double R, double Half, int Joint) pg in f.Pegs)
                        if (pg.Joint > max) max = pg.Joint;
            }
            return max + 1;
        }

        // True when two axis-aligned boxes (same local frame) overlap on all three axes.
        private static bool BoxesOverlap(Point3d aMin, Point3d aMax, Point3d bMin, Point3d bMax) =>
            aMin.X < bMax.X && aMax.X > bMin.X &&
            aMin.Y < bMax.Y && aMax.Y > bMin.Y &&
            aMin.Z < bMax.Z && aMax.Z > bMin.Z;

        // Remove a girt -> post joint: the tenon + mortise (and anything else sharing its id, e.g. future
        // pegs). Pick the girt then the post, like TJoint; the bearing contact is re-found and the joint at
        // that end is deleted from BOTH timbers, which then rebuild whole. Run once per end for a girt that
        // tenons into the same post at both ends (the matched end is deleted; the other is left intact).
        [CommandMethod("TJointDel")]
        public static void JointDelete()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the TENONED timber (girt / summer): ", out ObjectId girtId, out ManagedTimber.TFrame girt)) return;
            if (!PickTimber(ed, db, "\nPick the MORTISED timber (post / carrier): ", out ObjectId postId, out ManagedTimber.TFrame post)) return;
            if (girtId == postId) { ed.WriteMessage("\nPick two different timbers."); return; }

            // The girt END-cap that bears on a post SIDE face (same find as TJoint).
            ManagedTimber.TFace[] gf = ManagedTimber.Faces(girt);
            ManagedTimber.TFace[] pf = ManagedTimber.Faces(post);
            bool found = false;
            ManagedTimber.TFace gEnd = default;
            for (int gi = 0; gi <= 1 && !found; gi++)
                foreach (ManagedTimber.TFace ps in pf)
                {
                    if (Math.Abs(ps.N.DotProduct(post.Z)) >= 0.5) continue;       // the post face must be a SIDE
                    if (ManagedTimber.FacesMate(gf[gi], ps, 0.25, out _)) { gEnd = gf[gi]; found = true; break; }
                }
            if (!found)
            { ed.WriteMessage("\nNo end-into-face contact -- pick the girt + post that share a joint."); return; }

            bool farEnd = gEnd.N.DotProduct(girt.Z) > 0.0;
            int ti = girt.Features == null ? -1 : girt.Features.FindIndex(f => !f.Subtract &&
                         (((f.Min.Z + f.Max.Z) / 2.0 > girt.L / 2.0) == farEnd));
            // No tenon box at this end? It may still be a shoulder-only (poly) joint -- delete by its id.
            if (ti < 0)
            {
                int sid = ExistingRafterFootId(girt, post);
                if (sid == 0) { ed.WriteMessage("\nNo joint at that contact -- nothing to delete."); return; }
                girt.JointPolys?.RemoveAll(j => j.Joint == sid);
                post.JointPolys?.RemoveAll(j => j.Joint == sid);
                ObjectId ng = ManagedTimber.RebuildFromFrame(girtId, girt);
                ObjectId np = ManagedTimber.RebuildFromFrame(postId, post);
                ed.WriteMessage("\nTJointDel: joint #" + sid + " removed (girt " + ng.Handle + ", post " + np.Handle + ").");
                return;
            }

            int id = girt.Features[ti].Joint;
            if (id != 0)
            {
                girt.Features.RemoveAll(f => f.Joint == id);
                post.Features.RemoveAll(f => f.Joint == id);
                post.Pegs?.RemoveAll(p => p.Joint == id);   // pegs go with the joint
                girt.JointPolys?.RemoveAll(j => j.Joint == id);   // shoulder polys ride the same id
                post.JointPolys?.RemoveAll(j => j.Joint == id);
            }
            else
            {
                // Legacy / unkeyed: map the tenon corners into post-local for its footprint, drop the
                // overlapping post mortise(s).
                var t = girt.Features[ti];
                double mnX = double.MaxValue, mnY = double.MaxValue, mnZ = double.MaxValue;
                double mxX = double.MinValue, mxY = double.MinValue, mxZ = double.MinValue;
                foreach (double lx in new[] { t.Min.X, t.Max.X })
                    foreach (double ly in new[] { t.Min.Y, t.Max.Y })
                        foreach (double lz in new[] { t.Min.Z, t.Max.Z })
                        {
                            Point3d w = girt.O + girt.X * lx + girt.Y * ly + girt.Z * lz;
                            Vector3d r = w - post.O;
                            double px = r.DotProduct(post.X), py = r.DotProduct(post.Y), pz = r.DotProduct(post.Z);
                            if (px < mnX) mnX = px; if (px > mxX) mxX = px;
                            if (py < mnY) mnY = py; if (py > mxY) mxY = py;
                            if (pz < mnZ) mnZ = pz; if (pz > mxZ) mxZ = pz;
                        }
                Point3d fpMin = new Point3d(mnX, mnY, mnZ), fpMax = new Point3d(mxX, mxY, mxZ);
                girt.Features.RemoveAt(ti);
                if (post.Features != null)
                    post.Features.RemoveAll(f => f.Subtract && BoxesOverlap(f.Min, f.Max, fpMin, fpMax));
            }

            ObjectId ngirt = ManagedTimber.RebuildFromFrame(girtId, girt);
            ObjectId npost = ManagedTimber.RebuildFromFrame(postId, post);
            ed.WriteMessage("\nTJointDel: joint " + (id != 0 ? "#" + id : "(legacy)") +
                            " removed (girt " + ngirt.Handle + ", post " + npost.Handle + ").");
        }

        // Cut a principal-rafter FOOT housed into a post SIDE -- a girt-at-a-pitch HOUSING (+ optional TENON).
        // Pick the RAFTER then the POST: the post gets the wedge pocket/mortise and the rafter foot is grown
        // into it (housed stub + tenon tongue) -- a level seat with a pitch-matched top. The recipe (housing
        // depth + tenon thickness/length/shoulders/offset) is reviewed; the cuts are id-keyed polygons (re-cut
        // REPLACES; TRafterFootDel removes) and ride MOVE / TFit + SAVE. Needs a post DEPTH (+/-Y) face.
        [CommandMethod("TRafterFoot")]
        public static void RafterFoot()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the RAFTER: ", out ObjectId rId, out ManagedTimber.TFrame rafter)) return;
            if (!PickTimber(ed, db, "\nPick the POST: ", out ObjectId pId, out ManagedTimber.TFrame post)) return;
            if (rId == pId) { ed.WriteMessage("\nPick two different timbers."); return; }

            if (!ReviewRafterFoot(ed)) return;

            // Cut via the factored apply-half (the same path the ConnectionType facade drives; it finds the contact).
            if (!ApplyRafterFootJoint(db, rId, ref rafter, pId, ref post, _rfoot,
                    out ObjectId nr, out ObjectId np, out int jid, out string diag))
            { ed.WriteMessage("\nNothing to cut -- " + diag + "."); return; }
            StampJoint(nr, np, jid, ConnectionType.RafterFoot(_rfoot));
            ed.WriteMessage("\n[diag] " + diag);
            ed.WriteMessage("\nTRafterFoot: joint #" + jid + " cut -- " + diag +
                            " (rafter " + nr.Handle + ", post " + np.Handle + ").");
        }

        // The apply-half of TRafterFoot, factored for the ConnectionType facade: FINDS the post-side contact
        // itself, cuts the sloped housing (+ optional tenon), shares/mints the joint id (dropping overlapping
        // polys on a re-cut), rebuilds both. False + diag = no contact or nothing enabled.
        public static bool ApplyRafterFootJoint(Database db, ObjectId rId, ref ManagedTimber.TFrame rafter,
            ObjectId pId, ref ManagedTimber.TFrame post, ManagedTimber.RafterFootSpec spec,
            out ObjectId newRafterId, out ObjectId newPostId, out int jid, out string diag)
        {
            newRafterId = ObjectId.Null; newPostId = ObjectId.Null;
            if (!ComputeRafterFootJoint(db, ref rafter, ref post, spec, out jid, out diag)) return false;
            newRafterId = ManagedTimber.RebuildFromFrame(rId, rafter);
            newPostId = ManagedTimber.RebuildFromFrame(pId, post);
            return true;
        }

        // Compute-only core of the rafter-foot joint (housing + optional tenon): find the post-side contact, cut
        // the wedge, share/mint the id and route the polys onto the two frames WITHOUT the DB rebuild. Driven by
        // ApplyRafterFootJoint (commit) and the joint PREVIEW (on cloned frames). False + diag = nothing cut.
        public static bool ComputeRafterFootJoint(Database db, ref ManagedTimber.TFrame rafter,
            ref ManagedTimber.TFrame post, ManagedTimber.RafterFootSpec spec, out int jid, out string diag)
        {
            jid = 0;
            if (!FindFootContact(rafter, post, out ManagedTimber.TFace pFace))
            { diag = "no foot-into-side contact -- the rafter's foot must die into a post side"; return false; }
            if (!ManagedTimber.RafterFootJoint(rafter, post, pFace, spec,
                    out List<(Point3d[] Poly, bool OnPost, double Xlo, double Xhi)> polys,
                    out List<(Point3d C, Vector3d Axis, double R, double Half)> postPegs, out diag))
                return false;
            int reuse = ExistingRafterFootId(rafter, post);
            jid = reuse != 0 ? reuse : NextJointId(db);
            if (reuse != 0) post.Pegs?.RemoveAll(p => p.Joint == reuse);   // old peg bores go with the re-cut joint
            // Replace THIS joint by its id; the geometry-overlap purge applies ONLY to legacy id-0 features --
            // a DIFFERENT identified joint sharing the host must never be swept up by the overlap net.
            foreach ((Point3d[] Poly, bool OnPost, double Xlo, double Xhi) p in polys)
            {
                if (p.OnPost) post.JointPolys?.RemoveAll(j => (reuse != 0 && j.Joint == reuse) || (j.Joint == 0 && PolysOverlap(j.Poly, p.Poly)));
                else          rafter.JointPolys?.RemoveAll(j => (reuse != 0 && j.Joint == reuse) || (j.Joint == 0 && PolysOverlap(j.Poly, p.Poly)));
            }
            ApplyRafterFoot(ref rafter, ref post, jid, polys);
            if (postPegs.Count > 0)
            {
                if (post.Pegs == null) post.Pegs = new List<(Point3d, Vector3d, double, double, int)>();
                foreach ((Point3d C, Vector3d Axis, double R, double Half) pg in postPegs)
                    post.Pegs.Add((pg.C, pg.Axis, pg.R, pg.Half, jid));     // BORE the post cheeks
            }
            return true;
        }

        // Remove a rafter-foot joint: pick the rafter + post, drop the polygons sharing their joint id from
        // both, rebuild. (Parallels TJointDel.)
        [CommandMethod("TRafterFootDel")]
        public static void RafterFootDelete()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the RAFTER: ", out ObjectId rId, out ManagedTimber.TFrame rafter)) return;
            if (!PickTimber(ed, db, "\nPick the POST: ", out ObjectId pId, out ManagedTimber.TFrame post)) return;
            if (rId == pId) { ed.WriteMessage("\nPick two different timbers."); return; }

            int id = ExistingRafterFootId(rafter, post);
            if (id == 0) { ed.WriteMessage("\nNo rafter-foot joint between those two timbers."); return; }
            rafter.JointPolys?.RemoveAll(j => j.Joint == id);
            post.JointPolys?.RemoveAll(j => j.Joint == id);
            post.Pegs?.RemoveAll(p => p.Joint == id);

            ObjectId nr = ManagedTimber.RebuildFromFrame(rId, rafter);
            ObjectId np = ManagedTimber.RebuildFromFrame(pId, post);
            ed.WriteMessage("\nTRafterFootDel: joint #" + id + " removed (rafter " + nr.Handle + ", post " + np.Handle + ").");
        }

        // Cut a principal-rafter HEAD bearing on a KING POST side -- the legacy "shoulder" notch only. Pick
        // the RAFTER then the KING POST: the king post gets the bearing notch and the rafter head grows the
        // matching tongue. Seat is reviewed; the cut is an id-keyed polygon (re-cut REPLACES;
        // TRafterHeadDel removes) and rides MOVE / TFit + SAVE. Needs a king-post DEPTH (+/-Y) face.
        [CommandMethod("TRafterHead")]
        public static void RafterHead()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the RAFTER: ", out ObjectId rId, out ManagedTimber.TFrame rafter)) return;
            if (!PickTimber(ed, db, "\nPick the KING POST: ", out ObjectId pId, out ManagedTimber.TFrame kingpost)) return;
            if (rId == pId) { ed.WriteMessage("\nPick two different timbers."); return; }

            if (!ReviewRafterHead(ed)) return;

            // Cut via the factored apply-half (the same path the ConnectionType facade drives; it finds the contact).
            if (!ApplyRafterHeadJoint(db, rId, ref rafter, pId, ref kingpost, _rhead,
                    out ObjectId nr, out ObjectId np, out int jid, out string diag))
            { ed.WriteMessage("\nNothing to cut -- " + diag + "."); return; }
            StampJoint(nr, np, jid, ConnectionType.RafterHead(_rhead));
            ed.WriteMessage("\n[diag] " + diag);
            ed.WriteMessage("\nTRafterHead: joint #" + jid + " cut -- " + diag +
                            " (rafter " + nr.Handle + ", king post " + np.Handle + ").");
        }

        // Remove a rafter-head joint: pick the rafter + king post, drop the polygons sharing their joint id
        // from both, rebuild. (Parallels TRafterFootDel.)
        [CommandMethod("TRafterHeadDel")]
        public static void RafterHeadDelete()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the RAFTER: ", out ObjectId rId, out ManagedTimber.TFrame rafter)) return;
            if (!PickTimber(ed, db, "\nPick the KING POST: ", out ObjectId pId, out ManagedTimber.TFrame kingpost)) return;
            if (rId == pId) { ed.WriteMessage("\nPick two different timbers."); return; }

            int id = ExistingRafterFootId(rafter, kingpost);
            if (id == 0) { ed.WriteMessage("\nNo rafter-head joint between those two timbers."); return; }
            rafter.JointPolys?.RemoveAll(j => j.Joint == id);
            kingpost.JointPolys?.RemoveAll(j => j.Joint == id);

            ObjectId nr = ManagedTimber.RebuildFromFrame(rId, rafter);
            ObjectId np = ManagedTimber.RebuildFromFrame(pId, kingpost);
            ed.WriteMessage("\nTRafterHeadDel: joint #" + id + " removed (rafter " + nr.Handle + ", king post " + np.Handle + ").");
        }

        // The rafter-head shoulder sub-menu: edit the sticky _rhead (just the bearing-seat depth). Enter /
        // "Cut" proceeds.
        private static bool ReviewRafterHead(Editor ed)
        {
            while (true)
            {
                var pko = new PromptKeywordOptions(
                    "\nRafter head shoulder -- Seat=" + _rhead.Seat + " (On=" + (_rhead.On ? "Yes" : "No") + "). ")
                { AllowNone = true };
                pko.Keywords.Add("Cut");
                pko.Keywords.Add("Seat");
                pko.Keywords.Add("On");
                pko.Keywords.Default = "Cut";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return false;
                string kw = kr.Status == PromptStatus.None ? "Cut" : kr.StringResult;
                if (kw == "Cut") return true;
                switch (kw)
                {
                    case "Seat": if (GetPositive(ed, "Shoulder seat depth into the king post", _rhead.Seat, out double sv)) _rhead.Seat = sv; break;
                    case "On":       _rhead.On = !_rhead.On; break;
                }
            }
        }

        // Cut a RIDGE -> KING POST drop-in housing -- the king post top is cut to the ridge's cross-section
        // (incl. its chamfered top) so the ridge lowers straight in. Pick the RIDGE then the KING POST: only
        // the king post is cut (an id-keyed polygon; re-cut REPLACES, TRidgeDel removes) and it rides MOVE /
        // TFit + SAVE. The ridge must run along the king-post width.
        [CommandMethod("TRidge")]
        public static void Ridge()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the RIDGE: ", out ObjectId rId, out ManagedTimber.TFrame ridge)) return;
            if (!PickTimber(ed, db, "\nPick the KING POST: ", out ObjectId pId, out ManagedTimber.TFrame kingpost)) return;
            if (rId == pId) { ed.WriteMessage("\nPick two different timbers."); return; }

            if (!ReviewRidge(ed)) return;

            if (!ManagedTimber.RidgeKpostJoint(ridge, kingpost, _ridge,
                    out List<(Point3d[] Poly, bool OnPost, double Xlo, double Xhi)> polys,
                    out List<(Point3d[] Poly, bool Subtract, double Xlo, double Xhi)> ridgeZPolys, out string diag))
            { ed.WriteMessage("\nNothing to cut -- " + diag + "."); return; }
            ed.WriteMessage("\n[diag] " + diag);

            int reuse = ExistingRafterFootId(ridge, kingpost);
            int jid = reuse != 0 ? reuse : NextJointId(db);
            // Replace THIS joint by its id; the overlap purge applies ONLY to legacy id-0 features -- the
            // rafter-HEAD notches share the king-post apex and must never be swept up by the overlap net.
            foreach ((Point3d[] Poly, bool OnPost, double Xlo, double Xhi) p in polys)   // king post pocket
                if (p.OnPost) kingpost.JointPolys?.RemoveAll(j => (reuse != 0 && j.Joint == reuse) || (j.Joint == 0 && PolysOverlap(j.Poly, p.Poly)));
            if (reuse != 0) ridge.JointPolysZ?.RemoveAll(j => j.Joint == reuse);   // old tongue (re-cut)

            ApplyRafterFoot(ref ridge, ref kingpost, jid, polys);   // king post pocket subtract (shared id)
            if (ridgeZPolys.Count > 0)   // ridge TONGUE (chamfered, Z-extruded into the pocket)
            {
                if (ridge.JointPolysZ == null) ridge.JointPolysZ = new List<(Point3d[], int, bool, double, double)>();
                foreach ((Point3d[] Poly, bool Subtract, double Xlo, double Xhi) z in ridgeZPolys)
                    ridge.JointPolysZ.Add((z.Poly, jid, z.Subtract, z.Xlo, z.Xhi));
            }

            ObjectId nr = ManagedTimber.RebuildFromFrame(rId, ridge);
            ObjectId np = ManagedTimber.RebuildFromFrame(pId, kingpost);
            StampJoint(nr, np, jid, ConnectionType.RidgeHousing(_ridge));
            ed.WriteMessage("\nTRidge: joint #" + jid + " cut -- " + diag +
                            " (ridge " + nr.Handle + ", king post " + np.Handle + ").");
        }

        // Remove a ridge -> king post joint: pick the ridge + king post, drop the polygons sharing their joint
        // id from both, rebuild. (Parallels TRafterHeadDel.)
        [CommandMethod("TRidgeDel")]
        public static void RidgeDelete()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the RIDGE: ", out ObjectId rId, out ManagedTimber.TFrame ridge)) return;
            if (!PickTimber(ed, db, "\nPick the KING POST: ", out ObjectId pId, out ManagedTimber.TFrame kingpost)) return;
            if (rId == pId) { ed.WriteMessage("\nPick two different timbers."); return; }

            int id = ExistingRafterFootId(ridge, kingpost);
            if (id == 0) { ed.WriteMessage("\nNo ridge -> king post joint between those two timbers."); return; }
            ridge.JointPolys?.RemoveAll(j => j.Joint == id);
            kingpost.JointPolys?.RemoveAll(j => j.Joint == id);
            ridge.JointPolysZ?.RemoveAll(j => j.Joint == id);   // the chamfered tongue

            ObjectId nr = ManagedTimber.RebuildFromFrame(rId, ridge);
            ObjectId np = ManagedTimber.RebuildFromFrame(pId, kingpost);
            ed.WriteMessage("\nTRidgeDel: joint #" + id + " removed (ridge " + nr.Handle + ", king post " + np.Handle + ").");
        }

        // The ridge-housing sub-menu: edit the sticky _ridge (the bearing-seat depth). Enter / "Cut" proceeds.
        private static bool ReviewRidge(Editor ed)
        {
            while (true)
            {
                var pko = new PromptKeywordOptions(
                    "\nRidge housing -- Seat=" + _ridge.Seat + " ShoulderBottom=" + _ridge.ShoulderBottom +
                    " (On=" + (_ridge.On ? "Yes" : "No") + "). ")
                { AllowNone = true };
                pko.Keywords.Add("Cut");
                pko.Keywords.Add("Seat");
                pko.Keywords.Add("ShoulderBottom");
                pko.Keywords.Add("On");
                pko.Keywords.Default = "Cut";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return false;
                string kw = kr.Status == PromptStatus.None ? "Cut" : kr.StringResult;
                if (kw == "Cut") return true;
                switch (kw)
                {
                    case "Seat": if (GetPositive(ed, "Housing seat depth", _ridge.Seat, out double sv)) _ridge.Seat = sv; break;
                    case "ShoulderBottom": if (GetDouble(ed, "Bottom shoulder -- the ridge's lower N inches stay full and bear (0 = full drop-in)", _ridge.ShoulderBottom, false, out double bv)) _ridge.ShoulderBottom = bv; break;
                    case "On":   _ridge.On = !_ridge.On; break;
                }
            }
        }

        // Cut a RIDGE -> PRINCIPAL RAFTER drop-in housing -- the SAME geometry as TRidge (the rafter head is
        // cut to the ridge's chamfered cross-section risen to the apex, so the ridge lowers straight in), but
        // the host is a sloped principal rafter instead of a king post. This is the king-post-LESS bent: the
        // two rafters carry the ridge themselves, so the ridge is housed into BOTH rafter heads (run this once
        // per rafter). Reuses RidgeKpostJoint verbatim -- the pocket maps to the rafter's local Z x Y and
        // extrudes across the rafter WIDTH (= the ridge axis), independent of the rafter pitch. Pick the RIDGE
        // then the RAFTER. Only the rafter is cut (an id-keyed pocket) + a chamfered tongue on the ridge; re-cut
        // REPLACES, TRidgeRafterDel removes, and it rides MOVE / TFit + SAVE.
        [CommandMethod("TRidgeRafter")]
        public static void RidgeRafter()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the RIDGE: ", out ObjectId rId, out ManagedTimber.TFrame ridge)) return;
            if (!PickTimber(ed, db, "\nPick the RAFTER: ", out ObjectId fId, out ManagedTimber.TFrame rafter)) return;
            if (rId == fId) { ed.WriteMessage("\nPick two different timbers."); return; }

            if (!ReviewRidgeRafter(ed)) return;

            // Cut via the factored apply-half (host-neutral; the same path the ConnectionType facade drives).
            if (!ApplyRidgeHousingJoint(db, rId, ref ridge, fId, ref rafter, _ridgeRafter,
                    out ObjectId nr, out ObjectId nf, out int jid, out string diag))
            { ed.WriteMessage("\nNothing to cut -- " + diag + "."); return; }
            StampJoint(nr, nf, jid, ConnectionType.RidgeHousing(_ridgeRafter));
            ed.WriteMessage("\n[diag] " + diag);
            ed.WriteMessage("\nTRidgeRafter: joint #" + jid + " cut -- " + diag +
                            " (ridge " + nr.Handle + ", rafter " + nf.Handle + ").");
        }

        // The apply-half of TRidgeRafter, factored for the ConnectionType facade. HOST-NEUTRAL: the drop-in
        // pocket + chamfered tongue cut identically into a king post OR a principal rafter (RidgeKpostJoint maps
        // to the host's local frame). Id-only removal so a two-bay host keeps the other bay's pocket. False +
        // diag = nothing cut (e.g. the ridge does not run along the host width).
        public static bool ApplyRidgeHousingJoint(Database db, ObjectId rId, ref ManagedTimber.TFrame ridge,
            ObjectId hId, ref ManagedTimber.TFrame host, ManagedTimber.RidgeHousingSpec spec,
            out ObjectId newRidgeId, out ObjectId newHostId, out int jid, out string diag)
        {
            newRidgeId = ObjectId.Null; newHostId = ObjectId.Null;
            if (!ComputeRidgeHousingJoint(db, ref ridge, ref host, spec, out jid, out diag)) return false;
            newRidgeId = ManagedTimber.RebuildFromFrame(rId, ridge);
            newHostId = ManagedTimber.RebuildFromFrame(hId, host);
            return true;
        }

        // Compute-only core of the ridge housing (host pocket SUBTRACT + ridge TONGUE union, host-neutral king-post
        // OR rafter) WITHOUT the DB rebuild. Driven by ApplyRidgeHousingJoint (commit) and the joint PREVIEW (on
        // cloned frames). False + diag = nothing cut.
        public static bool ComputeRidgeHousingJoint(Database db, ref ManagedTimber.TFrame ridge,
            ref ManagedTimber.TFrame host, ManagedTimber.RidgeHousingSpec spec, out int jid, out string diag)
        {
            jid = 0;
            if (!ManagedTimber.RidgeKpostJoint(ridge, host, spec,
                    out List<(Point3d[] Poly, bool OnPost, double Xlo, double Xhi)> polys,
                    out List<(Point3d[] Poly, bool Subtract, double Xlo, double Xhi)> ridgeZPolys, out diag))
                return false;
            int reuse = ExistingRafterFootId(ridge, host);
            jid = reuse != 0 ? reuse : NextJointId(db);
            if (reuse != 0)
            {
                host.JointPolys?.RemoveAll(j => j.Joint == reuse);
                ridge.JointPolysZ?.RemoveAll(j => j.Joint == reuse);
            }
            ApplyRafterFoot(ref ridge, ref host, jid, polys);   // host pocket subtract (OnPost), shared id
            if (ridgeZPolys.Count > 0)   // ridge TONGUE (chamfered, Z-extruded into the pocket)
            {
                if (ridge.JointPolysZ == null) ridge.JointPolysZ = new List<(Point3d[], int, bool, double, double)>();
                foreach ((Point3d[] Poly, bool Subtract, double Xlo, double Xhi) z in ridgeZPolys)
                    ridge.JointPolysZ.Add((z.Poly, jid, z.Subtract, z.Xlo, z.Xhi));
            }
            return true;
        }

        // Remove a ridge -> principal-rafter housing: pick the ridge + rafter, drop the pocket + tongue sharing
        // their joint id from both, rebuild. (Parallels TRidgeDel.)
        [CommandMethod("TRidgeRafterDel")]
        public static void RidgeRafterDelete()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the RIDGE: ", out ObjectId rId, out ManagedTimber.TFrame ridge)) return;
            if (!PickTimber(ed, db, "\nPick the RAFTER: ", out ObjectId fId, out ManagedTimber.TFrame rafter)) return;
            if (rId == fId) { ed.WriteMessage("\nPick two different timbers."); return; }

            int id = ExistingRafterFootId(ridge, rafter);
            if (id == 0) { ed.WriteMessage("\nNo ridge -> rafter joint between those two timbers."); return; }
            ridge.JointPolys?.RemoveAll(j => j.Joint == id);
            rafter.JointPolys?.RemoveAll(j => j.Joint == id);
            ridge.JointPolysZ?.RemoveAll(j => j.Joint == id);   // the chamfered tongue

            ObjectId nr = ManagedTimber.RebuildFromFrame(rId, ridge);
            ObjectId nf = ManagedTimber.RebuildFromFrame(fId, rafter);
            ed.WriteMessage("\nTRidgeRafterDel: joint #" + id + " removed (ridge " + nr.Handle + ", rafter " + nf.Handle + ").");
        }

        // The ridge -> rafter housing sub-menu: edit the sticky _ridgeRafter (the bearing-seat depth). Enter / "Cut" proceeds.
        private static bool ReviewRidgeRafter(Editor ed)
        {
            while (true)
            {
                var pko = new PromptKeywordOptions(
                    "\nRidge->rafter housing -- Seat=" + _ridgeRafter.Seat + " ShoulderBottom=" + _ridgeRafter.ShoulderBottom +
                    " (On=" + (_ridgeRafter.On ? "Yes" : "No") + "). ")
                { AllowNone = true };
                pko.Keywords.Add("Cut");
                pko.Keywords.Add("Seat");
                pko.Keywords.Add("ShoulderBottom");
                pko.Keywords.Add("On");
                pko.Keywords.Default = "Cut";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return false;
                string kw = kr.Status == PromptStatus.None ? "Cut" : kr.StringResult;
                if (kw == "Cut") return true;
                switch (kw)
                {
                    case "Seat": if (GetPositive(ed, "Housing seat depth", _ridgeRafter.Seat, out double sv)) _ridgeRafter.Seat = sv; break;
                    case "ShoulderBottom": if (GetDouble(ed, "Bottom shoulder -- the ridge's lower N inches stay full and bear (0 = full drop-in)", _ridgeRafter.ShoulderBottom, false, out double bv)) _ridgeRafter.ShoulderBottom = bv; break;
                    case "On":   _ridgeRafter.On = !_ridgeRafter.On; break;
                }
            }
        }

        // The post SIDE face a rafter foot dies into. The rafter foot is a PLUMB end (the bent graph clips it
        // with a vertical plane), so it butts the post side like a girt. Pick the rafter END nearest the post
        // (the foot) and the post side face whose outward normal most OPPOSES that foot's outward normal (the
        // face the foot butts). The cutter checks it's a depth face. False if no side face opposes.
        private static bool FindFootContact(ManagedTimber.TFrame rafter, ManagedTimber.TFrame post, out ManagedTimber.TFace pFace)
        {
            pFace = default;
            Point3d postC = post.O + post.Z * (post.L / 2.0);
            Point3d c0 = rafter.O, c1 = rafter.O + rafter.Z * rafter.L;
            Vector3d footN = (c0 - postC).Length <= (c1 - postC).Length ? rafter.NearN : rafter.FarN;

            double best = 0.0; bool found = false;
            foreach (ManagedTimber.TFace ps in ManagedTimber.Faces(post))
            {
                if (Math.Abs(ps.N.DotProduct(post.Z)) >= 0.5) continue;   // SIDE faces only
                double opp = footN.DotProduct(ps.N);                       // most negative = most opposing
                if (!found || opp < best) { best = opp; pFace = ps; found = true; }
            }
            return found && best < -0.1;   // the foot must actually point into a side face
        }

        // The joint id shared by both frames' polygon lists (JointPolys + JointPolysZ) -- a polygon joint
        // (rafter foot/head, ridge) already cut between them, or 0 if none. v1: at most one per pair.
        private static int ExistingRafterFootId(ManagedTimber.TFrame a, ManagedTimber.TFrame b)
        {
            System.Collections.Generic.HashSet<int> bIds = PolyJointIds(b);
            foreach (int id in PolyJointIds(a))
                if (id != 0 && bIds.Contains(id)) return id;
            return 0;
        }

        // All joint ids carried by a frame's polygon features (both extrusion orientations).
        private static System.Collections.Generic.HashSet<int> PolyJointIds(ManagedTimber.TFrame f)
        {
            var s = new System.Collections.Generic.HashSet<int>();
            if (f.JointPolys != null)
                foreach ((Point3d[] Poly, int Joint, bool Subtract, double Xlo, double Xhi) jp in f.JointPolys) s.Add(jp.Joint);
            if (f.JointPolysZ != null)
                foreach ((Point3d[] Poly, int Joint, bool Subtract, double Xlo, double Xhi) jp in f.JointPolysZ) s.Add(jp.Joint);
            if (f.JointPrisms != null)
                foreach ((Point3d[] Poly, Vector3d Extrude, int Joint, bool Subtract) jp in f.JointPrisms) s.Add(jp.Joint);
            return s;
        }

        // Two LOCAL elevation polygons (same frame: X = length, Y = depth) overlap if their length x depth
        // bounding boxes overlap. Used to de-dup a re-cut / orphan rafter-foot pocket by geometry.
        private static bool PolysOverlap(Point3d[] a, Point3d[] b)
        {
            if (a == null || b == null || a.Length == 0 || b.Length == 0) return false;
            double aXmin = double.MaxValue, aXmax = double.MinValue, aYmin = double.MaxValue, aYmax = double.MinValue;
            foreach (Point3d p in a) { if (p.X < aXmin) aXmin = p.X; if (p.X > aXmax) aXmax = p.X; if (p.Y < aYmin) aYmin = p.Y; if (p.Y > aYmax) aYmax = p.Y; }
            double bXmin = double.MaxValue, bXmax = double.MinValue, bYmin = double.MaxValue, bYmax = double.MinValue;
            foreach (Point3d p in b) { if (p.X < bXmin) bXmin = p.X; if (p.X > bXmax) bXmax = p.X; if (p.Y < bYmin) bYmin = p.Y; if (p.Y > bYmax) bYmax = p.Y; }
            return aXmin < bXmax && aXmax > bXmin && aYmin < bYmax && aYmax > bYmin;
        }

        // Two PRISM polygons (3D LOCAL points on the SAME timber) overlap if their axis-aligned bounding boxes do
        // -- de-dup a re-cut / ORPHAN prism joint (common->ridge, purlin->rafter) by geometry, not just id, so a
        // stale tongue/pocket whose id desynced is cleared. Sibling joints sit at different stations, so their
        // boxes don't overlap and they survive.
        private static bool PrismPolysOverlap(Point3d[] a, Point3d[] b)
        {
            if (a == null || b == null || a.Length == 0 || b.Length == 0) return false;
            double aXm = double.MaxValue, aXM = double.MinValue, aYm = double.MaxValue, aYM = double.MinValue, aZm = double.MaxValue, aZM = double.MinValue;
            foreach (Point3d p in a) { if (p.X < aXm) aXm = p.X; if (p.X > aXM) aXM = p.X; if (p.Y < aYm) aYm = p.Y; if (p.Y > aYM) aYM = p.Y; if (p.Z < aZm) aZm = p.Z; if (p.Z > aZM) aZM = p.Z; }
            double bXm = double.MaxValue, bXM = double.MinValue, bYm = double.MaxValue, bYM = double.MinValue, bZm = double.MaxValue, bZM = double.MinValue;
            foreach (Point3d p in b) { if (p.X < bXm) bXm = p.X; if (p.X > bXM) bXM = p.X; if (p.Y < bYm) bYm = p.Y; if (p.Y > bYM) bYM = p.Y; if (p.Z < bZm) bZm = p.Z; if (p.Z > bZM) bZM = p.Z; }
            return aXm < bXM && aXM > bXm && aYm < bYM && aYM > bYm && aZm < bZM && aZM > bZm;
        }

        // Route the cutter's polygons onto the two frames, stamped with the joint id: the post pocket is a
        // SUBTRACT (OnPost), the rafter stub a UNION. TFrame is a struct -> ref.
        private static void ApplyRafterFoot(ref ManagedTimber.TFrame rafter, ref ManagedTimber.TFrame post, int jid,
            List<(Point3d[] Poly, bool OnPost, double Xlo, double Xhi)> polys)
        {
            if (rafter.JointPolys == null) rafter.JointPolys = new List<(Point3d[], int, bool, double, double)>();
            if (post.JointPolys == null) post.JointPolys = new List<(Point3d[], int, bool, double, double)>();
            foreach ((Point3d[] Poly, bool OnPost, double Xlo, double Xhi) p in polys)
                (p.OnPost ? post.JointPolys : rafter.JointPolys).Add((p.Poly, jid, p.OnPost, p.Xlo, p.Xhi));   // post subtract, rafter union
        }

        // The rafter-foot recipe sub-menu: edit the sticky _rfoot (housing depth + the tenon tongue, like the
        // girt tenon -- thickness / length / top + bottom shoulders / width offset). Enter / "Cut" proceeds.
        private static bool ReviewRafterFoot(Editor ed)
        {
            while (true)
            {
                string tn = _rfoot.Tenon ? "On (T" + _rfoot.Thickness + " L" + _rfoot.Length + ")" : "Off";
                string pg = _rfoot.Peg.Count > 0 ? ("On x" + _rfoot.Peg.Count) : "Off";
                var pko = new PromptKeywordOptions(
                    "\nRafter foot -- Seat=" + _rfoot.Seat + " | Tenon: " + tn +
                    " (TopShoulder=" + _rfoot.ShoulderTop + " BotShoulder=" + _rfoot.ShoulderBottom + " Offset=" + _rfoot.Offset + ") | Pegs: " + pg + ". ")
                { AllowNone = true };
                pko.Keywords.Add("Cut");
                pko.Keywords.Add("Seat");
                pko.Keywords.Add("Tenon");
                pko.Keywords.Add("Thickness");
                pko.Keywords.Add("Length");
                pko.Keywords.Add("TopShoulder");
                pko.Keywords.Add("BotShoulder");
                pko.Keywords.Add("Offset");
                pko.Keywords.Add("Pegs");
                pko.Keywords.Default = "Cut";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return false;
                string kw = kr.Status == PromptStatus.None ? "Cut" : kr.StringResult;
                if (kw == "Cut") return true;
                switch (kw)
                {
                    case "Seat":      if (GetDouble  (ed, "Housing seat depth into the post (0 = none)", _rfoot.Seat, false, out double dv)) _rfoot.Seat = dv; break;
                    case "Tenon":     _rfoot.Tenon = !_rfoot.Tenon; break;
                    case "Thickness": if (GetPositive(ed, "Tenon thickness", _rfoot.Thickness, out double tv)) _rfoot.Thickness = tv; break;
                    case "Length":    if (GetPositive(ed, "Tenon length past the housing", _rfoot.Length, out double lv)) _rfoot.Length = lv; break;
                    case "TopShoulder": if (GetDouble  (ed, "Top shoulder (down from the rafter top)", _rfoot.ShoulderTop, false, out double rt)) _rfoot.ShoulderTop = rt; break;
                    case "BotShoulder": if (GetDouble  (ed, "Bottom shoulder (up from the seat)", _rfoot.ShoulderBottom, false, out double rb)) _rfoot.ShoulderBottom = rb; break;
                    case "Offset":    if (GetDouble  (ed, "Width offset", _rfoot.Offset, true, out double ov)) _rfoot.Offset = ov; break;
                    case "Pegs":      ReviewPegs(ed, ref _rfoot.Peg); break;
                }
            }
        }

        // Cut a PURLIN housed into a RAFTER as a let-in DOVETAIL. Pick the PURLIN then the RAFTER: the rafter
        // gets the dovetail pocket (subtract) and the purlin's end grows the matching tongue (union). The recipe
        // (seat / height / width / flare) is reviewed; the cut is an id-keyed JointPrism on each timber (re-cut
        // REPLACES; TPurlinDel removes) and rides MOVE / SAVE. Needs a rafter SIDE face the purlin dies into.
        [CommandMethod("TPurlin")]
        public static void Purlin()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the PURLIN: ", out ObjectId puId, out ManagedTimber.TFrame purlin)) return;
            if (!PickTimber(ed, db, "\nPick the RAFTER: ", out ObjectId rId, out ManagedTimber.TFrame rafter)) return;
            if (puId == rId) { ed.WriteMessage("\nPick two different timbers."); return; }

            if (!ReviewPurlin(ed)) return;

            // Cut via the factored apply-half (the same path the ConnectionType facade drives; it finds the contact).
            if (!ApplyPurlinJoint(db, puId, ref purlin, rId, ref rafter, _purlin,
                    out ObjectId npu, out ObjectId nr, out int jid, out string diag))
            { ed.WriteMessage("\nNothing to cut -- " + diag + "."); return; }
            StampJoint(npu, nr, jid, ConnectionType.HousedDovetail(_purlin));
            ed.WriteMessage("\n[diag] " + diag);
            ed.WriteMessage("\nTPurlin: joint #" + jid + " cut -- " + diag +
                            " (purlin " + npu.Handle + ", rafter " + nr.Handle + ").");
        }

        // Remove a purlin dovetail: pick the purlin + rafter, drop the prisms sharing their joint id, rebuild.
        [CommandMethod("TPurlinDel")]
        public static void PurlinDelete()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the PURLIN: ", out ObjectId puId, out ManagedTimber.TFrame purlin)) return;
            if (!PickTimber(ed, db, "\nPick the RAFTER: ", out ObjectId rId, out ManagedTimber.TFrame rafter)) return;
            if (puId == rId) { ed.WriteMessage("\nPick two different timbers."); return; }

            int id = ExistingRafterFootId(purlin, rafter);
            if (id == 0) { ed.WriteMessage("\nNo purlin joint between those two timbers."); return; }
            purlin.JointPrisms?.RemoveAll(j => j.Joint == id);
            rafter.JointPrisms?.RemoveAll(j => j.Joint == id);

            ObjectId npu = ManagedTimber.RebuildFromFrame(puId, purlin);
            ObjectId nr  = ManagedTimber.RebuildFromFrame(rId, rafter);
            ed.WriteMessage("\nTPurlinDel: joint #" + id + " removed (purlin " + npu.Handle + ", rafter " + nr.Handle + ").");
        }

        // The purlin-dovetail recipe sub-menu: edit the sticky _purlin (housing seat / tongue length / base
        // width / band depth / taper angle). Enter / "Cut" proceeds.
        private static bool ReviewPurlin(Editor ed)
        {
            while (true)
            {
                var pko = new PromptKeywordOptions(
                    "\nHoused dovetail -- Seat=" + _purlin.Seat + " Length=" + _purlin.Length +
                    " Width=" + _purlin.Width + " Depth=" + _purlin.Depth + " Angle=" + _purlin.Angle + ". ")
                { AllowNone = true };
                pko.Keywords.Add("Cut");
                pko.Keywords.Add("Seat");
                pko.Keywords.Add("Length");
                pko.Keywords.Add("Width");
                pko.Keywords.Add("Depth");
                pko.Keywords.Add("Angle");
                pko.Keywords.Default = "Cut";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return false;
                string kw = kr.Status == PromptStatus.None ? "Cut" : kr.StringResult;
                if (kw == "Cut") return true;
                switch (kw)
                {
                    case "Seat":   if (GetPositive(ed, "Full-section housing depth into the rafter", _purlin.Seat, out double sv)) _purlin.Seat = sv; break;
                    case "Length": if (GetPositive(ed, "Dovetail tongue length past the housing", _purlin.Length, out double lv)) _purlin.Length = lv; break;
                    case "Width":  if (GetPositive(ed, "Dovetail base width", _purlin.Width, out double wv)) _purlin.Width = wv; break;
                    case "Depth":  if (GetPositive(ed, "Dovetail band depth (flush with the top face)", _purlin.Depth, out double dv)) _purlin.Depth = dv; break;
                    case "Angle":  if (GetDouble  (ed, "Dovetail taper half-angle (degrees)", _purlin.Angle, false, out double av)) _purlin.Angle = av; break;
                }
            }
        }

        // Cut a COMMON RAFTER's head into a RIDGE as a let-in HOUSING. Pick the COMMON then the RIDGE: the ridge
        // gets the full-section gain (subtract) and the common's head fills it (union, same shape). The seat is
        // reviewed; the cut is an id-keyed JointPrism on each timber (re-cut REPLACES; TCommonRidgeDel removes)
        // and rides MOVE / SAVE. Needs a ridge SIDE face the common's head dies into.
        [CommandMethod("TCommonRidge")]
        public static void CommonRidge()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the COMMON rafter: ", out ObjectId cId, out ManagedTimber.TFrame common)) return;
            if (!PickTimber(ed, db, "\nPick the RIDGE: ", out ObjectId rId, out ManagedTimber.TFrame ridge)) return;
            if (cId == rId) { ed.WriteMessage("\nPick two different timbers."); return; }

            if (!ReviewCommonRidge(ed)) return;

            // Cut via the factored apply-half (the same path the ConnectionType facade drives; it finds the contact).
            if (!ApplyCommonRidgeJoint(db, cId, ref common, rId, ref ridge, _comridge,
                    out ObjectId nc, out ObjectId nr, out int jid, out string diag))
            { ed.WriteMessage("\nNothing to cut -- " + diag + "."); return; }
            StampJoint(nc, nr, jid, ConnectionType.CommonRidge(_comridge));
            ed.WriteMessage("\n[diag] " + diag);
            ed.WriteMessage("\nTCommonRidge: joint #" + jid + " cut -- " + diag +
                            " (common " + nc.Handle + ", ridge " + nr.Handle + ").");
        }

        // Remove a common -> ridge housing: pick the common + ridge, drop the prisms sharing their joint id, rebuild.
        [CommandMethod("TCommonRidgeDel")]
        public static void CommonRidgeDelete()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the COMMON rafter: ", out ObjectId cId, out ManagedTimber.TFrame common)) return;
            if (!PickTimber(ed, db, "\nPick the RIDGE: ", out ObjectId rId, out ManagedTimber.TFrame ridge)) return;
            if (cId == rId) { ed.WriteMessage("\nPick two different timbers."); return; }

            int id = ExistingRafterFootId(common, ridge);
            if (id == 0) { ed.WriteMessage("\nNo common -> ridge joint between those two timbers."); return; }
            common.JointPrisms?.RemoveAll(j => j.Joint == id);
            ridge.JointPrisms?.RemoveAll(j => j.Joint == id);

            ObjectId nc = ManagedTimber.RebuildFromFrame(cId, common);
            ObjectId nr = ManagedTimber.RebuildFromFrame(rId, ridge);
            ed.WriteMessage("\nTCommonRidgeDel: joint #" + id + " removed (common " + nc.Handle + ", ridge " + nr.Handle + ").");
        }

        // The common->ridge housing sub-menu: edit the sticky _comridge (the let-in seat). Enter / "Cut" proceeds.
        private static bool ReviewCommonRidge(Editor ed)
        {
            while (true)
            {
                var pko = new PromptKeywordOptions(
                    "\nCommon->ridge housing -- Seat=" + _comridge.Seat + ". ") { AllowNone = true };
                pko.Keywords.Add("Cut");
                pko.Keywords.Add("Seat");
                pko.Keywords.Default = "Cut";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return false;
                string kw = kr.Status == PromptStatus.None ? "Cut" : kr.StringResult;
                if (kw == "Cut") return true;
                if (kw == "Seat" && GetPositive(ed, "Let-in housing depth into the ridge face", _comridge.Seat, out double sv))
                    _comridge.Seat = sv;
            }
        }

        // Cut a HOUSED COMMON RAFTER -> EAVE GIRT birdsmouth. Pick the COMMON then the GIRT: the rafter is
        // notched (seat let-in below the girt top + heel let-in inside the heel face) and the girt gets the
        // matching top pocket -- BOTH are cut, sharing one joint id (re-cut REPLACES; TCommonEaveDel removes).
        // The recipe (the two let-ins) is reviewed; the cuts ride MOVE / SAVE.
        [CommandMethod("TCommonEave")]
        public static void CommonEave()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the COMMON rafter: ", out ObjectId cId, out ManagedTimber.TFrame common)) return;
            if (!PickTimber(ed, db, "\nPick the EAVE GIRT: ", out ObjectId gId, out ManagedTimber.TFrame girt)) return;
            if (cId == gId) { ed.WriteMessage("\nPick two different timbers."); return; }

            if (!ReviewCommonEave(ed)) return;

            // Cut via the factored apply-half (the same path the ConnectionType facade drives).
            if (!ApplyCommonEaveJoint(db, cId, ref common, gId, ref girt, _comeave,
                    out ObjectId nc, out ObjectId ng, out int jid, out string diag))
            { ed.WriteMessage("\nNothing to cut -- " + diag + "."); return; }
            StampJoint(nc, ng, jid, ConnectionType.Birdsmouth(_comeave));
            ed.WriteMessage("\n[diag] " + diag);
            ed.WriteMessage("\nTCommonEave: joint #" + jid + " cut -- " + diag +
                            " (common " + nc.Handle + ", girt " + ng.Handle + ").");
        }

        // Remove a housed common -> eave-girt birdsmouth: pick the common + girt, drop the rafter notch + girt
        // pocket sharing their joint id, rebuild both.
        [CommandMethod("TCommonEaveDel")]
        public static void CommonEaveDelete()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the COMMON rafter: ", out ObjectId cId, out ManagedTimber.TFrame common)) return;
            if (!PickTimber(ed, db, "\nPick the EAVE GIRT: ", out ObjectId gId, out ManagedTimber.TFrame girt)) return;
            if (cId == gId) { ed.WriteMessage("\nPick two different timbers."); return; }

            int id = ExistingRafterFootId(common, girt);
            if (id == 0) { ed.WriteMessage("\nNo birdsmouth between those two timbers."); return; }
            common.JointPolys?.RemoveAll(j => j.Joint == id);
            girt.JointPolysZ?.RemoveAll(j => j.Joint == id);

            ObjectId nc = ManagedTimber.RebuildFromFrame(cId, common);
            ObjectId ng = ManagedTimber.RebuildFromFrame(gId, girt);
            ed.WriteMessage("\nTCommonEaveDel: birdsmouth #" + id + " removed (common " + nc.Handle + ", girt " + ng.Handle + ").");
        }

        // The housed-birdsmouth sub-menu: edit the sticky _comeave (the seat + heel let-ins). Enter / "Cut" proceeds.
        private static bool ReviewCommonEave(Editor ed)
        {
            while (true)
            {
                var pko = new PromptKeywordOptions(
                    "\nHoused birdsmouth -- Seat=" + _comeave.Seat + " Heel=" + _comeave.Heel + ". ")
                { AllowNone = true };
                pko.Keywords.Add("Cut");
                pko.Keywords.Add("Seat");
                pko.Keywords.Add("Heel");
                pko.Keywords.Default = "Cut";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return false;
                string kw = kr.Status == PromptStatus.None ? "Cut" : kr.StringResult;
                if (kw == "Cut") return true;
                switch (kw)
                {
                    case "Seat": if (GetPositive(ed, "Seat let-in below the girt top", _comeave.Seat, out double sv)) _comeave.Seat = sv; break;
                    case "Heel": if (GetPositive(ed, "Heel let-in inside the heel face", _comeave.Heel, out double hv)) _comeave.Heel = hv; break;
                }
            }
        }

        // Strut tenon onto a host face: pick the strut then the member it beds into (rafter, post, king post),
        // review the tenon, cut. ONE solid is UNIONed to the strut (the tongue) and SUBTRACTed from the host
        // (the matching mortise), sharing one joint id (re-cut REPLACES; TStrutDel removes).
        [CommandMethod("TStrut")]
        public static void Strut()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the STRUT: ", out ObjectId sId, out ManagedTimber.TFrame strut)) return;
            if (!PickTimber(ed, db, "\nPick the member it beds into (rafter / post): ", out ObjectId hId, out ManagedTimber.TFrame host)) return;
            if (sId == hId) { ed.WriteMessage("\nPick two different timbers."); return; }

            if (!ReviewStrut(ed)) return;

            // Cut via the factored apply-half (the same path the ConnectionType facade drives).
            if (!ApplyStrutTenonJoint(db, sId, ref strut, hId, ref host, _strut,
                    out ObjectId ns, out ObjectId nh, out int jid, out string diag))
            { ed.WriteMessage("\nNothing to cut -- " + diag + "."); return; }
            StampJoint(ns, nh, jid, ConnectionType.StrutTenon(_strut));
            ed.WriteMessage("\n[diag] " + diag);
            ed.WriteMessage("\nTStrut: joint #" + jid + " cut -- " + diag +
                            " (strut " + ns.Handle + ", host " + nh.Handle + ").");
        }

        // Drop both halves of a strut/brace tenon by joint id: the strut tongue (JointPolys) + the host mortise
        // (JointPrisms now, JointPolys/JointPolysZ for joints cut by an older build) + the host peg bores (Pegs).
        private static void DropStrutJoint(ManagedTimber.TFrame strut, ManagedTimber.TFrame host, int id)
        {
            strut.JointPolys?.RemoveAll(j => j.Joint == id);
            host.JointPrisms?.RemoveAll(j => j.Joint == id);
            host.JointPolys?.RemoveAll(j => j.Joint == id);
            host.JointPolysZ?.RemoveAll(j => j.Joint == id);
            host.Pegs?.RemoveAll(p => p.Joint == id);
        }

        // The apply-half of TStrut / TBrace, factored so the ConnectionType facade can drive the SAME cut from a
        // timber PAIR (no command pick/review). Computes the tenon, shares/mints the joint id, UNIONs the tongue
        // onto the strut + SUBTRACTs the mortise from the host, rebuilds both solids. The frames are updated in
        // place (ref); the rebuilt ObjectIds + joint id come back for the caller's message. False + diag = nothing cut.
        public static bool ApplyStrutTenonJoint(Database db, ObjectId sId, ref ManagedTimber.TFrame strut,
            ObjectId hId, ref ManagedTimber.TFrame host, ManagedTimber.StrutTenonSpec spec,
            out ObjectId newStrutId, out ObjectId newHostId, out int jid, out string diag)
        {
            newStrutId = ObjectId.Null; newHostId = ObjectId.Null;
            if (!ComputeStrutTenonJoint(db, ref strut, ref host, spec, out jid, out diag)) return false;
            newStrutId = ManagedTimber.RebuildFromFrame(sId, strut);
            newHostId = ManagedTimber.RebuildFromFrame(hId, host);
            return true;
        }

        // Compute-only core of the strut/brace tenon: mutate the two frames (UNION the tongue onto the strut,
        // SUBTRACT the same solid from the host, sharing one joint id) WITHOUT touching the DB. ApplyStrutTenonJoint
        // adds the rebuild; the joint PREVIEW runs this on CLONED frames and ghosts the result. False + diag = nothing cut.
        public static bool ComputeStrutTenonJoint(Database db, ref ManagedTimber.TFrame strut,
            ref ManagedTimber.TFrame host, ManagedTimber.StrutTenonSpec spec, out int jid, out string diag)
        {
            jid = 0;
            if (!ManagedTimber.StrutTenonJoint(strut, host, spec,
                    out List<(Point3d[] Poly, double Xlo, double Xhi)> malePolys,
                    out List<(Point3d[] Poly, Vector3d Extrude)> hostPrisms,
                    out List<(Point3d C, Vector3d Axis, double R, double Half)> hostPegs, out diag))
                return false;
            jid = RouteStrutLists(db, ref strut, ref host, malePolys, hostPrisms, hostPegs);
            return true;
        }

        // Stamp the strut-engine output onto the two frames: share/mint the joint id (replacing any prior joint
        // between this pair), UNION every male poly onto the strut (JointPolys), SUBTRACT every host prism from the
        // host (JointPrisms), and bore every host peg into the host (Pegs). Shared by the strut/brace tenon and the
        // QP rafter apex.
        private static int RouteStrutLists(Database db, ref ManagedTimber.TFrame strut, ref ManagedTimber.TFrame host,
            List<(Point3d[] Poly, double Xlo, double Xhi)> malePolys, List<(Point3d[] Poly, Vector3d Extrude)> hostPrisms,
            List<(Point3d C, Vector3d Axis, double R, double Half)> hostPegs)
        {
            int reuse = ExistingRafterFootId(strut, host);
            int jid = reuse != 0 ? reuse : NextJointId(db);
            if (reuse != 0) DropStrutJoint(strut, host, reuse);
            if (strut.JointPolys == null) strut.JointPolys = new List<(Point3d[], int, bool, double, double)>();
            if (host.JointPrisms == null) host.JointPrisms = new List<(Point3d[], Vector3d, int, bool)>();
            foreach ((Point3d[] Poly, double Xlo, double Xhi) p in malePolys)
                strut.JointPolys.Add((p.Poly, jid, false, p.Xlo, p.Xhi));   // UNION onto the strut/male
            foreach ((Point3d[] Poly, Vector3d Extrude) p in hostPrisms)
                host.JointPrisms.Add((p.Poly, p.Extrude, jid, true));       // SUBTRACT from the host
            if (hostPegs != null && hostPegs.Count > 0)
            {
                if (host.Pegs == null) host.Pegs = new List<(Point3d, Vector3d, double, double, int)>();
                foreach ((Point3d C, Vector3d Axis, double R, double Half) pg in hostPegs)
                    host.Pegs.Add((pg.C, pg.Axis, pg.R, pg.Half, jid));     // BORE the host cheeks
            }
            return jid;
        }

        // Remove a strut tenon: pick the strut + host, drop the tongue + mortise sharing their joint id, rebuild both.
        [CommandMethod("TStrutDel")]
        public static void StrutDelete()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the STRUT: ", out ObjectId sId, out ManagedTimber.TFrame strut)) return;
            if (!PickTimber(ed, db, "\nPick the member it beds into (rafter / post): ", out ObjectId hId, out ManagedTimber.TFrame host)) return;
            if (sId == hId) { ed.WriteMessage("\nPick two different timbers."); return; }

            int id = ExistingRafterFootId(strut, host);
            if (id == 0) { ed.WriteMessage("\nNo strut tenon between those two timbers."); return; }
            DropStrutJoint(strut, host, id);

            ObjectId ns = ManagedTimber.RebuildFromFrame(sId, strut);
            ObjectId nh = ManagedTimber.RebuildFromFrame(hId, host);
            ed.WriteMessage("\nTStrutDel: tenon #" + id + " removed (strut " + ns.Handle + ", host " + nh.Handle + ").");
        }

        // The strut tenon sub-menu: edit the sticky _strut (thickness / length / shoulders / lateral offset).
        // Enter / "Cut" proceeds. Shoulders are world-up keyed (Top = higher edge, Bottom = lower edge).
        private static bool ReviewStrut(Editor ed)
        {
            while (true)
            {
                string hg = _strut.Hsg.On ? ("On (Seat " + _strut.Hsg.Seat + ")") : "Off";
                string pg = _strut.Peg.Count > 0 ? ("On x" + _strut.Peg.Count) : "Off";
                var pko = new PromptKeywordOptions(
                    "\nStrut tenon -- Thickness=" + _strut.Thickness + " Length=" + _strut.Length +
                    " ShoulderTop=" + _strut.ShoulderTop + " ShoulderBottom=" + _strut.ShoulderBottom +
                    " Offset=" + _strut.Offset + " | Housing: " + hg + " | Pegs: " + pg + ". ")
                { AllowNone = true };
                pko.Keywords.Add("Cut");
                pko.Keywords.Add("Thickness");
                pko.Keywords.Add("Length");
                pko.Keywords.Add("ShoulderTop");
                pko.Keywords.Add("ShoulderBottom");
                pko.Keywords.Add("Offset");
                pko.Keywords.Add("Housing");
                pko.Keywords.Add("Seat");
                pko.Keywords.Add("Pegs");
                pko.Keywords.Default = "Cut";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return false;
                string kw = kr.Status == PromptStatus.None ? "Cut" : kr.StringResult;
                if (kw == "Cut") return true;
                switch (kw)
                {
                    case "Thickness":      if (GetPositive(ed, "Tenon thickness (width)", _strut.Thickness, out double tv)) _strut.Thickness = tv; break;
                    case "Length":         if (GetPositive(ed, "Tenon length into the host", _strut.Length, out double lv)) _strut.Length = lv; break;
                    case "ShoulderTop":    if (GetPositive(ed, "Shoulder inset from the higher (world-up) edge", _strut.ShoulderTop, out double stv)) _strut.ShoulderTop = stv; break;
                    case "ShoulderBottom": if (GetPositive(ed, "Shoulder inset from the lower edge", _strut.ShoulderBottom, out double sbv)) _strut.ShoulderBottom = sbv; break;
                    case "Offset":         if (GetDouble(ed, "Lateral offset off the strut center", _strut.Offset, true, out double ov)) _strut.Offset = ov; break;
                    case "Housing":        ReviewHousing(ed, ref _strut.Hsg); break;
                    case "Seat":           if (GetDouble(ed, "Housing seat depth", _strut.Hsg.Seat, false, out double sv)) { _strut.Hsg.Seat = sv; _strut.Hsg.On = sv > 1e-6; } break;
                    case "Pegs":           ReviewPegs(ed, ref _strut.Peg); break;
                }
            }
        }

        // Brace tenon: the SAME end->side tenon as TStrut (StrutTenonJoint) -- no new engine -- just the brace
        // defaults (1.5" thick) and a BAREFACED option (tongue flush to one cheek). Pick the brace then the
        // member it beds into (girt / post). ONE solid UNIONs the strut tongue, SUBTRACTs the host mortise,
        // sharing one joint id (re-cut REPLACES; TBraceDel removes).
        [CommandMethod("TBrace")]
        public static void Brace()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the BRACE: ", out ObjectId sId, out ManagedTimber.TFrame brace)) return;
            if (!PickTimber(ed, db, "\nPick the member it beds into (girt / post): ", out ObjectId hId, out ManagedTimber.TFrame host)) return;
            if (sId == hId) { ed.WriteMessage("\nPick two different timbers."); return; }

            if (!ReviewBrace(ed)) return;

            // Barefaced overrides Offset: push the tongue flush to a cheek (= (W - Thickness)/2), Flip picks side.
            ManagedTimber.StrutTenonSpec spec = _brace;
            if (_braceBarefaced)
            {
                double t = System.Math.Min(System.Math.Max(_brace.Thickness, 0.0), brace.W);
                spec.Offset = (_braceFlip ? -1.0 : 1.0) * (brace.W - t) / 2.0;
            }

            if (!ApplyStrutTenonJoint(db, sId, ref brace, hId, ref host, spec,
                    out ObjectId ns, out ObjectId nh, out int jid, out string diag))
            { ed.WriteMessage("\nNothing to cut -- " + diag + "."); return; }
            // Stamp as the BRACE-named type so re-picking the pair loads the Brace preset in the pane
            // (older brace stamps say "Strut tenon" -- FromState resolves those by name unchanged).
            StampJoint(ns, nh, jid, ConnectionType.BraceTenon(spec));
            ed.WriteMessage("\n[diag] " + diag + (_braceBarefaced ? " (barefaced offset " + spec.Offset.ToString("0.00") + ")" : ""));
            ed.WriteMessage("\nTBrace: joint #" + jid + " cut -- " + diag +
                            " (brace " + ns.Handle + ", host " + nh.Handle + ").");
        }

        // Remove a brace tenon: pick the brace + host, drop the tongue + mortise sharing their joint id, rebuild.
        [CommandMethod("TBraceDel")]
        public static void BraceDelete()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the BRACE: ", out ObjectId sId, out ManagedTimber.TFrame brace)) return;
            if (!PickTimber(ed, db, "\nPick the member it beds into (girt / post): ", out ObjectId hId, out ManagedTimber.TFrame host)) return;
            if (sId == hId) { ed.WriteMessage("\nPick two different timbers."); return; }

            int id = ExistingRafterFootId(brace, host);
            if (id == 0) { ed.WriteMessage("\nNo brace tenon between those two timbers."); return; }
            DropStrutJoint(brace, host, id);

            ObjectId ns = ManagedTimber.RebuildFromFrame(sId, brace);
            ObjectId nh = ManagedTimber.RebuildFromFrame(hId, host);
            ed.WriteMessage("\nTBraceDel: tenon #" + id + " removed (brace " + ns.Handle + ", host " + nh.Handle + ").");
        }

        // The brace tenon sub-menu: the strut fields + Barefaced (flush to a cheek) and Flip (which cheek).
        // When Barefaced is On, Offset is computed from the brace width and the manual Offset is ignored.
        private static bool ReviewBrace(Editor ed)
        {
            while (true)
            {
                string face = _braceBarefaced ? ("On (flush " + (_braceFlip ? "-" : "+") + " cheek)") : "Off";
                string hg = _brace.Hsg.On ? ("On (Seat " + _brace.Hsg.Seat + ")") : "Off";
                string pg = _brace.Peg.Count > 0 ? ("On x" + _brace.Peg.Count) : "Off";
                var pko = new PromptKeywordOptions(
                    "\nBrace tenon -- Thickness=" + _brace.Thickness + " Length=" + _brace.Length +
                    " ShoulderTop=" + _brace.ShoulderTop + " ShoulderBottom=" + _brace.ShoulderBottom +
                    " Barefaced=" + face + (_braceBarefaced ? "" : " Offset=" + _brace.Offset) + " | Housing: " + hg + " | Pegs: " + pg + ". ")
                { AllowNone = true };
                pko.Keywords.Add("Cut");
                pko.Keywords.Add("Thickness");
                pko.Keywords.Add("Length");
                pko.Keywords.Add("ShoulderTop");
                pko.Keywords.Add("ShoulderBottom");
                pko.Keywords.Add("Barefaced");
                pko.Keywords.Add("Flip");
                pko.Keywords.Add("Offset");
                pko.Keywords.Add("Housing");
                pko.Keywords.Add("Seat");
                pko.Keywords.Add("Pegs");
                pko.Keywords.Default = "Cut";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return false;
                string kw = kr.Status == PromptStatus.None ? "Cut" : kr.StringResult;
                if (kw == "Cut") return true;
                switch (kw)
                {
                    case "Thickness":      if (GetPositive(ed, "Tenon thickness (width)", _brace.Thickness, out double tv)) _brace.Thickness = tv; break;
                    case "Length":         if (GetPositive(ed, "Tenon length into the host", _brace.Length, out double lv)) _brace.Length = lv; break;
                    case "ShoulderTop":    if (GetPositive(ed, "Shoulder inset from the higher (world-up) edge", _brace.ShoulderTop, out double stv)) _brace.ShoulderTop = stv; break;
                    case "ShoulderBottom": if (GetPositive(ed, "Shoulder inset from the lower edge", _brace.ShoulderBottom, out double sbv)) _brace.ShoulderBottom = sbv; break;
                    case "Barefaced":      _braceBarefaced = !_braceBarefaced; break;
                    case "Flip":           _braceFlip = !_braceFlip; break;
                    case "Offset":         if (GetDouble(ed, "Lateral offset off the brace center (when not barefaced)", _brace.Offset, true, out double ov)) _brace.Offset = ov; break;
                    case "Housing":        ReviewHousing(ed, ref _brace.Hsg); break;
                    case "Seat":           if (GetDouble(ed, "Housing seat depth", _brace.Hsg.Seat, false, out double sv)) { _brace.Hsg.Seat = sv; _brace.Hsg.On = sv > 1e-6; } break;
                    case "Pegs":           ReviewPegs(ed, ref _brace.Peg); break;
                }
            }
        }

        // ---- QP rafter APEX box tenon (two principal rafters meeting at the peak, no king post) -------------

        // The QP apex is a STRUT TENON + HOUSING (housing on by default) cut at the apex bearing -- same engine,
        // same spec as TStrut, just fed the male rafter's beveled peak end-cap as the bearing. Seeded from
        // the user's saved default (factory = StrutTenonSpec.QPRafterDefault, the old hand literal).
        private static ManagedTimber.StrutTenonSpec _qprafter = JointDefaults.QPRafter;

        // The MALE rafter's beveled PEAK end-cap that meets the HOST at the apex = the male end-cap whose outward
        // normal points most toward the host body. (The two heads don't cleanly coplanar-mate -- one houses INTO
        // the other -- so this picks by direction, not FacesMate.)
        private static bool FindApexSeat(ManagedTimber.TFrame male, ManagedTimber.TFrame host, out ManagedTimber.TFace seatFace)
        {
            seatFace = default;
            ManagedTimber.TFace[] mf = ManagedTimber.Faces(male);
            Point3d hc = host.O + host.Z * (host.L / 2.0);
            double best = 0.0; bool found = false;
            for (int i = 0; i <= 1; i++)   // the two END caps (0 = near, 1 = far)
            {
                Vector3d toHost = hc - mf[i].C;
                if (toHost.Length < 1e-9) continue;
                double d = mf[i].N.DotProduct(toHost.GetNormal());
                if (d > best) { best = d; seatFace = mf[i]; found = true; }
            }
            return found;
        }

        // Compute-only core: find the apex seat (the male rafter's beveled peak end-cap) and drive the STRUT engine
        // with that explicit bearing + the housing -- the QP apex IS a strut tenon + housing. No DB rebuild (Apply
        // adds it). `bearingFaceN = -seatFace.N` so the into-host direction (bn) = the male end-cap's outward normal.
        public static bool ComputeQPRafterJoint(Database db, ref ManagedTimber.TFrame male, ref ManagedTimber.TFrame host,
            ManagedTimber.StrutTenonSpec spec, out int jid, out string diag)
        {
            jid = 0;
            if (!FindApexSeat(male, host, out ManagedTimber.TFace seatFace))
            { diag = "no apex contact -- the two rafter heads must meet at the peak"; return false; }
            if (!ManagedTimber.StrutTenonJoint(male, host, spec,
                    out List<(Point3d[] Poly, double Xlo, double Xhi)> malePolys,
                    out List<(Point3d[] Poly, Vector3d Extrude)> hostPrisms,
                    out List<(Point3d C, Vector3d Axis, double R, double Half)> hostPegs, out diag,
                    hasBearing: true, bearingCtr: seatFace.C, bearingFaceN: -seatFace.N))
                return false;
            jid = RouteStrutLists(db, ref male, ref host, malePolys, hostPrisms, hostPegs);
            return true;
        }

        // Apply-half: Compute then rebuild both solids. Shared by TQPRafter (command) and the ConnectionType facade.
        public static bool ApplyQPRafterJoint(Database db, ObjectId mId, ref ManagedTimber.TFrame male,
            ObjectId hId, ref ManagedTimber.TFrame host, ManagedTimber.StrutTenonSpec spec,
            out ObjectId newMaleId, out ObjectId newHostId, out int jid, out string diag)
        {
            newMaleId = ObjectId.Null; newHostId = ObjectId.Null;
            if (!ComputeQPRafterJoint(db, ref male, ref host, spec, out jid, out diag)) return false;
            newMaleId = ManagedTimber.RebuildFromFrame(mId, male);
            newHostId = ManagedTimber.RebuildFromFrame(hId, host);
            return true;
        }

        // QP rafter APEX box tenon: pick the MALE rafter (its peak end seats + tenons into the host) then the HOST
        // rafter. Cuts the housing + tenon (+ optional pegs); re-cut REPLACES by joint id (TQPRafterDel removes).
        [CommandMethod("TQPRafter")]
        public static void QPRafter()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the MALE rafter (its peak end seats into the other): ", out ObjectId mId, out ManagedTimber.TFrame male)) return;
            if (!PickTimber(ed, db, "\nPick the HOST rafter it beds into: ", out ObjectId hId, out ManagedTimber.TFrame host)) return;
            if (mId == hId) { ed.WriteMessage("\nPick two different timbers."); return; }

            if (!ReviewQPRafter(ed)) return;

            if (!ApplyQPRafterJoint(db, mId, ref male, hId, ref host, _qprafter,
                    out ObjectId nm, out ObjectId nh, out int jid, out string diag))
            { ed.WriteMessage("\nNothing to cut -- " + diag + "."); return; }
            StampJoint(nm, nh, jid, ConnectionType.QPRafterApex(_qprafter));
            ed.WriteMessage("\nTQPRafter: joint #" + jid + " cut -- " + diag +
                            " (male " + nm.Handle + ", host " + nh.Handle + ").");
        }

        // Remove a QP rafter apex joint: pick the two rafters, drop the housing/tenon/pegs sharing their id, rebuild.
        [CommandMethod("TQPRafterDel")]
        public static void QPRafterDelete()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the MALE rafter: ", out ObjectId mId, out ManagedTimber.TFrame male)) return;
            if (!PickTimber(ed, db, "\nPick the HOST rafter: ", out ObjectId hId, out ManagedTimber.TFrame host)) return;
            if (mId == hId) { ed.WriteMessage("\nPick two different timbers."); return; }

            int id = ExistingRafterFootId(male, host);
            if (id == 0) { ed.WriteMessage("\nNo apex joint between those two rafters."); return; }
            DropStrutJoint(male, host, id);   // male tongue + host mortise (JointPrisms) + host peg bores

            ObjectId nm = ManagedTimber.RebuildFromFrame(mId, male);
            ObjectId nh = ManagedTimber.RebuildFromFrame(hId, host);
            ed.WriteMessage("\nTQPRafterDel: joint #" + id + " removed (male " + nm.Handle + ", host " + nh.Handle + ").");
        }

        // QP rafter apex sub-menu (a strut tenon + housing): the tongue (thickness / length / shoulders / offset)
        // + the housing (toggle + Seat). Enter / "Cut" proceeds.
        private static bool ReviewQPRafter(Editor ed)
        {
            while (true)
            {
                string hg = _qprafter.Hsg.On ? ("On (Seat " + _qprafter.Hsg.Seat + ")") : "Off";
                string pg = _qprafter.Peg.Count > 0 ? ("On x" + _qprafter.Peg.Count) : "Off";
                var pko = new PromptKeywordOptions(
                    "\nQP rafter apex -- Thickness=" + _qprafter.Thickness + " Length=" + _qprafter.Length +
                    " ShoulderTop=" + _qprafter.ShoulderTop + " ShoulderBottom=" + _qprafter.ShoulderBottom +
                    " Offset=" + _qprafter.Offset + " | Housing: " + hg + " | Pegs: " + pg + ". ")
                { AllowNone = true };
                pko.Keywords.Add("Cut");
                pko.Keywords.Add("Thickness");
                pko.Keywords.Add("Length");
                pko.Keywords.Add("ShoulderTop");
                pko.Keywords.Add("ShoulderBottom");
                pko.Keywords.Add("Offset");
                pko.Keywords.Add("Housing");
                pko.Keywords.Add("Seat");
                pko.Keywords.Add("Pegs");
                pko.Keywords.Default = "Cut";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return false;
                string kw = kr.Status == PromptStatus.None ? "Cut" : kr.StringResult;
                if (kw == "Cut") return true;
                switch (kw)
                {
                    case "Thickness":      if (GetPositive(ed, "Tenon thickness (tongue width)", _qprafter.Thickness, out double tv)) _qprafter.Thickness = tv; break;
                    case "Length":         if (GetPositive(ed, "Tenon length past the housing", _qprafter.Length, out double lv)) _qprafter.Length = lv; break;
                    case "ShoulderTop":    if (GetDouble(ed, "Shoulder inset from the higher (world-up) edge", _qprafter.ShoulderTop, false, out double stv)) _qprafter.ShoulderTop = stv; break;
                    case "ShoulderBottom": if (GetDouble(ed, "Shoulder inset from the lower edge", _qprafter.ShoulderBottom, false, out double sbv)) _qprafter.ShoulderBottom = sbv; break;
                    case "Offset":         if (GetDouble(ed, "Lateral offset off the tongue center", _qprafter.Offset, true, out double ofv)) _qprafter.Offset = ofv; break;
                    case "Housing":        ReviewHousing(ed, ref _qprafter.Hsg); break;
                    case "Seat":           if (GetDouble(ed, "Housing seat depth", _qprafter.Hsg.Seat, false, out double sv)) { _qprafter.Hsg.Seat = sv; _qprafter.Hsg.On = sv > 1e-6; } break;
                    case "Pegs":           ReviewPegs(ed, ref _qprafter.Peg); break;
                }
            }
        }


        // Rescan managed timbers for coincident (mating) faces and mark the derived nodes. Nodes are
        // NOT stored -- each scan recomputes them; separate the timbers and rescan -> the node is gone.
        [CommandMethod("TScan")]
        public static void Scan()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            int count = ManagedTimber.Enumerate(db).Count;
            var nodes = ManagedTimber.ComputeNodes(db);

            DrawNodeMarkers(doc, db, nodes);
            ed.WriteMessage("\nTScan: " + count + " managed timbers, " + nodes.Count + " node(s).");
            foreach (Point3d n in nodes)
                ed.WriteMessage("\n  node @ " + n.X.ToString("0.#") + "," + n.Y.ToString("0.#") + "," + n.Z.ToString("0.#"));
        }

        // SPIKE (A0): confirm interactive face picking + the BRep read. Run TPickFace, pick a face on a
        // managed timber -> it highlights natively; prints the matched analytic face's centre + normal.
        // (Throwaway; no geometry created.)
        [CommandMethod("TPickFace")]
        public static void PickFaceSpike()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!ManagedTimber.PickFace(ed, db, "\nPick a timber FACE: ", out ObjectId id, out ManagedTimber.TFace f))
            { ed.WriteMessage("\nTPickFace: no face picked."); return; }

            ed.WriteMessage("\nTPickFace: timber " + id.Handle +
                "\n  centre " + f.C.X.ToString("0.#") + "," + f.C.Y.ToString("0.#") + "," + f.C.Z.ToString("0.#") +
                "\n  normal " + f.N.X.ToString("0.##") + "," + f.N.Y.ToString("0.##") + "," + f.N.Z.ToString("0.##") +
                "\n  extents " + f.UHalf.ToString("0.#") + " x " + f.VHalf.ToString("0.#"));
        }

        // (TMove / TRotate removed: a ManagedTransformOverrule on Solid3d now keeps each timber's stored
        // frame + scarf node in lockstep through NATIVE MOVE / ROTATE / MIRROR / ALIGN, so no special
        // commands are needed. See Managed/ManagedTransformOverrule.cs and ApplyManagedTransform above.)

        // ---- orientation presets (palette) ------------------------------------------------
        // The managed verbs read the section roll -- and, for TPlace, the extrusion direction --
        // from the current UCS. These three presets set the standard orthographic UCSs so the
        // user doesn't hand-roll the UCS per member. In the model basis the BENTS lie in the
        // world YZ plane and the WALLS in the world XZ plane, so (Robert's mapping, 2026-07-07 --
        // the two were reversed before):
        //   Plan (Top)  -> X = world +X, Y = world +Y : Z = world +Z (posts / verticals)
        //   Bent        -> X = world +Y, Y = world +Z : Z = world +X (the bent cross-plane;
        //                  TPlace extrudes along the building)
        //   Wall        -> X = world +X, Y = world +Z : Z = world -Y (the wall elevation;
        //                  TPlace extrudes across the span, into the screen)
        // TSpan / TJoin take their direction from the picked faces, so for them the UCS only sets
        // the section roll.

        [CommandMethod("TUcsPlan")]
        public static void UcsPlan()
            => SetUcs(Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis, "Plan (Top)");

        [CommandMethod("TUcsBent")]
        public static void UcsBent()
            => SetUcs(Vector3d.YAxis, Vector3d.ZAxis, Vector3d.XAxis, "Bent elevation");

        [CommandMethod("TUcsWall")]
        public static void UcsWall()
            => SetUcs(Vector3d.XAxis, Vector3d.ZAxis, Vector3d.YAxis.Negate(), "Wall elevation");

        private static void SetUcs(Vector3d x, Vector3d y, Vector3d z, string label)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            ed.CurrentUserCoordinateSystem = Matrix3d.AlignCoordinateSystem(
                Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                Point3d.Origin, x, y, z);
            ed.WriteMessage("\nUCS: " + label + ".");
        }

        // ---- helpers ----------------------------------------------------------------------

        // Pick a managed timber (whole entity); returns its id + stored placement frame.
        private static bool PickTimber(Editor ed, Database db, string msg,
            out ObjectId id, out ManagedTimber.TFrame frame)
        {
            id = ObjectId.Null; frame = default;
            var peo = new PromptEntityOptions(msg);
            peo.SetRejectMessage("\nPick a managed timber (any managed member -- placed or generated).");
            peo.AddAllowedClass(typeof(Solid3d), exactMatch: false);
            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return false;
            id = per.ObjectId;
            if (!ManagedTimber.TryReadFrame(db, id, out frame))
            { ed.WriteMessage("\nNot a managed timber."); return false; }
            return true;
        }

        private static void DrawNodeMarkers(Document doc, Database db, List<Point3d> nodes)
        {
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // Ensure the node layer (blue -- ACI 5, safe for red/green colour-blindness).
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (!lt.Has(ManagedTimber.NodeLayer))
                {
                    lt.UpgradeOpen();
                    var ltr = new LayerTableRecord { Name = ManagedTimber.NodeLayer, Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 5) };
                    lt.Add(ltr); tr.AddNewlyCreatedDBObject(ltr, true);
                }

                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                // Clear previous markers (nodes are transient).
                foreach (ObjectId id in btr)
                    if (tr.GetObject(id, OpenMode.ForRead) is Entity e && e.Layer == ManagedTimber.NodeLayer)
                    { e.UpgradeOpen(); e.Erase(); }

                // Markers are spheres sized to poke OUT of the timbers, so they read from any view
                // (a flat circle goes edge-on and vanishes) and aren't buried inside the solids.
                foreach (Point3d n in nodes)
                {
                    var sph = new Solid3d { Layer = ManagedTimber.NodeLayer };
                    sph.CreateSphere(1.0);   // 2" dia marker
                    sph.TransformBy(Matrix3d.Displacement(n - Point3d.Origin));
                    btr.AppendEntity(sph); tr.AddNewlyCreatedDBObject(sph, true);
                }
                tr.Commit();
            }
        }

        // Build a frame-local box and BoolUnite it into target (in the current transaction).
        private static void UnionBox(Transaction tr, BlockTableRecord btr, Solid3d target, ManagedTimber.TFrame f,
            double x0, double x1, double y0, double y1, double z0, double z1)
        {
            Solid3d box = ManagedTimber.MakeBoxSolidLocal(f, x0, x1, y0, y1, z0, z1);
            btr.AppendEntity(box); tr.AddNewlyCreatedDBObject(box, true);
            target.BooleanOperation(BooleanOperationType.BoolUnite, box);
        }

        // Fill a rectangle with flat floor JOISTS by FOUR face picks: the two facing SPAN faces (the
        // carriers -- girt / summer / sill sides -- the joists run flush between) + the two
        // DISTRIBUTION faces (the run bounds), then Count or center Spacing. Each joist is a flat box
        // hung between the span faces (square ends -> TScan finds the seat nodes), arrayed along the
        // run, with its TOP FLUSH with the carrier tops (the dropped-in dovetail arrangement) minus
        // the sticky Drop (recessed-deck practice; 0 = flush). Role is always "Joist" (the verb IS the
        // family -- the palette section supplies W/D only) so role-keyed systems see the field. Joists
        // are left untagged -- TAssign the field to its FLOOR afterward (which also mints the
        // J-<floor>-n labels). (Joist = the pitch-0 case of a future generalized purlin/common/joist
        // array.)
        //
        // END JOINERY (floor systems phase 2): with the sticky Joint spec ON (the default), every joist
        // lands DOVETAILED -- both ends cut into the two span carriers as dropped-in housed dovetails
        // via the host-neutral PurlinRafterJoint engine (housing + flared top-band tongue, JointPrisms).
        // Each end gets its own joint id + a "Housed dovetail" pane stamp, so the Joints pane can
        // re-cut or delete any single end; the carriers rebuild ONCE for the whole row. The Joint
        // keyword reviews the sticky spec (On/Off + the five dovetail knobs).
        private static double _joistDrop = 0.0;   // session-sticky Drop below the carrier tops
        // Session-sticky joist-end dovetail recipe -- the pane's saved "Housed dovetail" values (the
        // same cut, host-neutral) but seeded OFF: joinery is DELAYED AND DELIBERATE (Robert's rule) --
        // joists land plain, get moved/adjusted freely, then a selection + TJointAll cuts the
        // dovetails. The Joint keyword still opts in at place time.
        private static ManagedTimber.PurlinRafterSpec _joistDove = JoistDoveSeed();
        private static ManagedTimber.PurlinRafterSpec JoistDoveSeed()
        {
            var s = JointDefaults.Purlin;
            s.On = false;
            return s;
        }

        [CommandMethod("TJoist")]
        public static void Joists()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            // W/D from the palette section (or prompts); the role is fixed -- no type prompt.
            const string type = "Joist";
            double w, d;
            if (ManagedSection.HasCurrent)
            {
                w = ManagedSection.Width; d = ManagedSection.Depth;
                ed.WriteMessage("\nSection: " + (int)w + "x" + (int)d + " (palette).");
            }
            else
            {
                if (!GetPositive(ed, "Width", 8.0, out w)) return;
                if (!GetPositive(ed, "Depth", 10.0, out d)) return;
            }

            // Span pair: the two facing bearing faces the joists run between (their timbers are the
            // CARRIERS -- kept for the end-dovetail cut).
            if (!ManagedTimber.PickFace(ed, db, "\nPick the FIRST span (bearing) face: ", out ObjectId caId, out ManagedTimber.TFace fa)) return;
            if (!ManagedTimber.PickFace(ed, db, "\nPick the SECOND span (bearing) face: ", out ObjectId cbId, out ManagedTimber.TFace fb)) return;
            if (fa.N.DotProduct(fb.N) > -0.99)
            { ed.WriteMessage("\nThe two span faces must face each other (opposing-parallel)."); return; }

            double gap = (fb.C - fa.C).DotProduct(fa.N);
            if (gap <= 1e-6) { ed.WriteMessage("\nNo positive gap between the span faces."); return; }
            Vector3d off = (fb.C - fa.C) - gap * fa.N;
            Point3d cL = fa.C + off * 0.5;                       // lateral center between the faces

            // depth = the vertical in-plane axis of the bearing face; width/run = the horizontal one
            // (joists repeat along it). Floor is WCS XY, so "vertical" = WCS Z.
            Vector3d up = Vector3d.ZAxis;
            Vector3d depthAxis, runDir;
            if (System.Math.Abs(fa.U.GetNormal().DotProduct(up)) >= System.Math.Abs(fa.V.GetNormal().DotProduct(up)))
            { depthAxis = fa.U.GetNormal(); runDir = fa.V.GetNormal(); }
            else
            { depthAxis = fa.V.GetNormal(); runDir = fa.U.GetNormal(); }

            // FLUSH TOPS: joist top = carrier top (a full side face's upper edge IS the carrier top),
            // minus the sticky Drop. Unequal carriers take the lower top so no joist stands proud.
            Vector3d vUp = depthAxis.DotProduct(up) >= 0 ? depthAxis : -depthAxis;
            double topA = FaceTop(fa, vUp), topB = FaceTop(fb, vUp);
            double top = System.Math.Min(topA, topB);
            if (System.Math.Abs(topA - topB) > 0.01)
                ed.WriteMessage("\nCarrier tops differ by " + System.Math.Abs(topA - topB).ToString("0.###")
                                + "\" -- using the lower.");

            // Distribution pair: the run bounds, projected onto runDir.
            if (!ManagedTimber.PickFace(ed, db, "\nPick the FIRST distribution (run-bound) face: ", out _, out ManagedTimber.TFace fc)) return;
            if (!ManagedTimber.PickFace(ed, db, "\nPick the SECOND distribution (run-bound) face: ", out _, out ManagedTimber.TFace fd)) return;
            double r0 = fc.C.GetAsVector().DotProduct(runDir);
            double r1 = fd.C.GetAsVector().DotProduct(runDir);
            if (r0 > r1) { double t = r0; r0 = r1; r1 = t; }
            double L = r1 - r0;
            if (L <= 1e-6) { ed.WriteMessage("\nThe distribution faces don't bound a run along the floor."); return; }

            // Count (N even, end-inset) or on-center Spacing (default 36", centered in the run).
            // Drop edits the sticky top recess (0 = flush tops); Joint reviews the sticky end-dovetail
            // recipe (On/Off + knobs). Both re-ask.
            string mode;
            for (; ; )
            {
                var pko = new PromptKeywordOptions("\nDistribute by");
                pko.Keywords.Add("Count");
                pko.Keywords.Add("Spacing");
                pko.Keywords.Add("Drop");
                pko.Keywords.Add("Joint");
                pko.Keywords.Default = "Spacing";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK) return;
                if (kr.StringResult == "Joint") { if (!ReviewJoistDove(ed)) return; continue; }
                if (kr.StringResult != "Drop") { mode = kr.StringResult; break; }
                if (!GetDouble(ed, "Drop below carrier tops", _joistDrop, false, out double dr)) return;
                _joistDrop = System.Math.Max(0.0, dr);
            }

            var stations = new List<double>();
            if (mode == "Count")
            {
                var io = new PromptIntegerOptions("\nNumber of joists: ")
                { DefaultValue = 5, LowerLimit = 1, AllowNegative = false, AllowZero = false };
                PromptIntegerResult ir = ed.GetInteger(io);
                if (ir.Status != PromptStatus.OK) return;
                int N = ir.Value;
                double step = L / (N + 1);
                for (int k = 1; k <= N; k++) stations.Add(r0 + k * step);
            }
            else
            {
                var so = new PromptDistanceOptions("\nOn-center spacing <36>: ")
                { DefaultValue = 36.0, UseDefaultValue = true, AllowNegative = false, AllowZero = false };
                PromptDoubleResult sr = ed.GetDistance(so);
                if (sr.Status != PromptStatus.OK) return;
                double S = sr.Value > 0 ? sr.Value : 36.0;
                int N = System.Math.Max(1, (int)System.Math.Round(L / S));
                double first = r0 + (L - (N - 1) * S) / 2.0;   // center the field in the run
                for (int k = 0; k < N; k++) stations.Add(first + k * S);
            }

            // Place each joist: shift the lateral center to the run station and lift the section so
            // its top sits at (carrier top - Drop); box runs `gap` along fa.N, d deep, w wide.
            cL += vUp * ((top - _joistDrop - d / 2.0) - cL.GetAsVector().DotProduct(vUp));
            double baseRun = cL.GetAsVector().DotProduct(runDir);
            int drawn = 0;
            var joists = new List<(ObjectId Id, ManagedTimber.TFrame F)>();
            foreach (double s in stations)
            {
                Point3d origin = cL + runDir * (s - baseRun);
                ObjectId jId = ManagedTimber.DrawBox(origin, fa.N, depthAxis, runDir, gap, d, w, type, "", "butt", "butt");
                drawn++;
                if (_joistDove.On && !jId.IsNull && ManagedTimber.TryReadFrame(db, jId, out ManagedTimber.TFrame jfr))
                    joists.Add((jId, jfr));
            }

            // END DOVETAILS: cut both ends of every joist into its carrier with the sticky spec. All
            // prisms accumulate on working frames -- each joist rebuilds once, each CARRIER once for
            // the whole row (not per joist). Ids are minted from one batch base (NextJointId scans the
            // DB, which doesn't see the in-memory prisms). Fresh joists carry no prior joints, so no
            // reuse/overlap purge is needed. Each end is stamped "Housed dovetail" for the Joints pane.
            int cutEnds = 0, missedEnds = 0;
            if (joists.Count > 0
                && ManagedTimber.TryReadFrame(db, caId, out ManagedTimber.TFrame carA)
                && ManagedTimber.TryReadFrame(db, cbId, out ManagedTimber.TFrame carB))
            {
                int nextId = NextJointId(db);
                var stamps = new List<(ObjectId J, bool OnA, int Jid)>();
                for (int ji = 0; ji < joists.Count; ji++)
                {
                    ManagedTimber.TFrame jf = joists[ji].F;
                    for (int side = 0; side <= 1; side++)
                    {
                        ManagedTimber.TFrame host = side == 0 ? carA : carB;
                        if (!FindFootContact(jf, host, out ManagedTimber.TFace hFace)
                            || !ManagedTimber.PurlinRafterJoint(jf, host, hFace, _joistDove,
                                   out List<(Point3d[] Poly, Vector3d Extrude, bool OnRafter)> prisms, out _))
                        { missedEnds++; continue; }
                        int jid = nextId++;
                        if (jf.JointPrisms == null) jf.JointPrisms = new List<(Point3d[], Vector3d, int, bool)>();
                        if (host.JointPrisms == null) host.JointPrisms = new List<(Point3d[], Vector3d, int, bool)>();
                        foreach ((Point3d[] Poly, Vector3d Extrude, bool OnRafter) p in prisms)
                            (p.OnRafter ? host.JointPrisms : jf.JointPrisms).Add((p.Poly, p.Extrude, jid, p.OnRafter));
                        if (side == 0) carA = host; else carB = host;
                        stamps.Add((joists[ji].Id, side == 0, jid));
                        cutEnds++;
                    }
                    joists[ji] = (joists[ji].Id, jf);
                }
                if (cutEnds > 0)
                {
                    var remap = new Dictionary<ObjectId, ObjectId>();
                    foreach ((ObjectId Id, ManagedTimber.TFrame F) j in joists)
                        remap[j.Id] = ManagedTimber.RebuildFromFrame(j.Id, j.F);
                    ObjectId nA = ManagedTimber.RebuildFromFrame(caId, carA);
                    ObjectId nB = ManagedTimber.RebuildFromFrame(cbId, carB);
                    ConnectionType dove = ConnectionType.HousedDovetail(_joistDove);   // one recipe for the row
                    foreach ((ObjectId J, bool OnA, int Jid) st in stamps)
                        StampJoint(remap[st.J], st.OnA ? nA : nB, st.Jid, dove);
                }
            }

            ed.WriteMessage("\nTJoist: " + drawn + " " + type + " " + (int)w + "x" + (int)d + "x" +
                            gap.ToString("0.#") + (_joistDrop > 0 ? " dropped " + _joistDrop.ToString("0.###") + "\"" : ", tops flush")
                            + (_joistDove.On
                               ? ", " + cutEnds + " end dovetail(s)" + (missedEnds > 0 ? " (" + missedEnds + " end(s) missed the carrier)" : "")
                               : ", ends butt (Joint off)")
                            + " -- TAssign the field to its floor for J-labels.");
        }

        // The joist end-dovetail sub-menu: toggle + edit the sticky _joistDove (the same five knobs as
        // the purlin housed dovetail -- one engine, one vocabulary). Enter / "Done" returns to TJoist.
        private static bool ReviewJoistDove(Editor ed)
        {
            while (true)
            {
                var pko = new PromptKeywordOptions(
                    "\nJoist end dovetail [" + (_joistDove.On ? "ON" : "OFF") + "] -- Seat=" + _joistDove.Seat +
                    " Length=" + _joistDove.Length + " Width=" + _joistDove.Width +
                    " Depth=" + _joistDove.Depth + " Angle=" + _joistDove.Angle + ". ") { AllowNone = true };
                pko.Keywords.Add("Done");
                pko.Keywords.Add(_joistDove.On ? "Off" : "On");   // the prompt rebuilds each pass
                pko.Keywords.Add("Seat");
                pko.Keywords.Add("Length");
                pko.Keywords.Add("Width");
                pko.Keywords.Add("Depth");
                pko.Keywords.Add("Angle");
                pko.Keywords.Default = "Done";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return false;
                string kw = kr.Status == PromptStatus.None ? "Done" : kr.StringResult;
                if (kw == "Done") return true;
                switch (kw)
                {
                    case "On":     _joistDove.On = true; break;
                    case "Off":    _joistDove.On = false; break;
                    case "Seat":   if (GetPositive(ed, "Full-section housing depth into the carrier", _joistDove.Seat, out double sv)) _joistDove.Seat = sv; break;
                    case "Length": if (GetPositive(ed, "Dovetail tongue length past the housing", _joistDove.Length, out double lv)) _joistDove.Length = lv; break;
                    case "Width":  if (GetPositive(ed, "Dovetail base width", _joistDove.Width, out double wv)) _joistDove.Width = wv; break;
                    case "Depth":  if (GetPositive(ed, "Dovetail band depth (flush with the top face)", _joistDove.Depth, out double dv)) _joistDove.Depth = dv; break;
                    case "Angle":  if (GetDouble  (ed, "Dovetail taper half-angle (degrees)", _joistDove.Angle, false, out double av)) _joistDove.Angle = av; break;
                }
            }
        }

        // Highest reach of a rectangular face along an up axis: center + both projected half-extents.
        private static double FaceTop(ManagedTimber.TFace f, Vector3d vUp)
            => f.C.GetAsVector().DotProduct(vUp)
               + System.Math.Abs(f.U.GetNormal().DotProduct(vUp)) * f.UHalf
               + System.Math.Abs(f.V.GetNormal().DotProduct(vUp)) * f.VHalf;

        // Width / Depth / type for a new member. When the palette has a current section active
        // (ManagedSection), use it with no prompts; otherwise prompt at the command line as before.
        private static bool GetSection(Editor ed, out double w, out double d, out string type)
        {
            if (ManagedSection.HasCurrent)
            {
                w = ManagedSection.Width; d = ManagedSection.Depth; type = ManagedSection.Type;
                ed.WriteMessage("\nSection: " + type + " " + (int)w + "x" + (int)d + " (palette).");
                return true;
            }
            w = 0; d = 0; type = null;
            if (!GetPositive(ed, "Width", 8.0, out w)) return false;
            if (!GetPositive(ed, "Depth", 10.0, out d)) return false;
            type = GetType(ed);
            return type != null;
        }

        private static bool GetPositive(Editor ed, string label, double dflt, out double value)
        {
            var o = new PromptDoubleOptions("\n" + label + " <" + dflt + ">: ")
            { AllowNegative = false, AllowZero = false, DefaultValue = dflt, UseDefaultValue = true };
            PromptDoubleResult r = ed.GetDouble(o);
            value = r.Value;
            return r.Status == PromptStatus.OK;
        }

        // Like GetPositive but allows zero (a shoulder of 0) and, when allowNeg, a negative value (a width
        // offset to either side). Returns false (leaving the sticky default) if the user cancels.
        private static bool GetDouble(Editor ed, string label, double dflt, bool allowNeg, out double value)
        {
            var o = new PromptDoubleOptions("\n" + label + " <" + dflt + ">: ")
            { AllowNegative = allowNeg, AllowZero = true, DefaultValue = dflt, UseDefaultValue = true };
            PromptDoubleResult r = ed.GetDouble(o);
            value = r.Value;
            return r.Status == PromptStatus.OK;
        }

        private static string GetType(Editor ed)
        {
            var so = new PromptStringOptions("\nTimber type <Timber>: ") { AllowSpaces = false };
            PromptResult r = ed.GetString(so);
            if (r.Status != PromptStatus.OK) return null;
            return string.IsNullOrWhiteSpace(r.StringResult) ? "Timber" : r.StringResult;
        }
    }
}
