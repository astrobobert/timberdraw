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
    // ManagedTimber part: SOLID construction -- box/framed-solid builders, draw/rebuild,
    // re-section, the mitered brace, and the scarf tongue. (Verbatim moves; see CLAUDE.md.)
    public static partial class ManagedTimber
    {
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
            // Identity suffix for Diag lines -- a TFrame carries no handle/label, so the origin +
            // length is the only locator available this deep.
            string loc = " @(" + f.O.X.ToString("0.#") + "," + f.O.Y.ToString("0.#") + ","
                       + f.O.Z.ToString("0.#") + ") L" + f.L.ToString("0.#");
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
                    catch (System.Exception ex) { Diag.Warn("BuildFramedSolid/Cut", "convex cut skipped: " + ex.Message + loc); }
                }
            }

            // Concave/notch features (birdsmouth, ...) AND id-carrying joint polygons (the rafter-foot
            // housing): each LOCAL elevation polygon is rebuilt at the -width face, extruded across the full
            // width, then UNIONED or SUBTRACTED. Shape Subtracts (untagged) always subtract; JointPolys carry
            // a sign (the rafter-foot post pocket subtracts, the rafter stub unions) -- they share this body.
            void CutPoly(Point3d[] poly, bool subtract, double xlo, double xhi, int joint)
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
                catch (System.Exception ex)   // leave the stock as-is rather than abort the whole emit
                {
                    Diag.Warn("BuildFramedSolid/Poly", (joint >= 0 ? "joint " + joint : "shape")
                        + (subtract ? " subtract" : " union") + " skipped: " + ex.Message + loc);
                }
            }
            // Same, but the polygon lives in the (X, Y) CROSS-SECTION and is extruded ALONG the length (f.Z)
            // over [zlo, zhi] -- a section-shaped feature running down the member (the ridge tongue).
            void CutPolyZ(Point3d[] poly, bool subtract, double zlo, double zhi, int joint)
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
                catch (System.Exception ex)   // leave the stock as-is rather than abort the whole emit
                {
                    Diag.Warn("BuildFramedSolid/PolyZ", (joint >= 0 ? "joint " + joint : "shape")
                        + (subtract ? " subtract" : " union") + " skipped: " + ex.Message + loc);
                }
            }
            // A PLANAR polygon (3D local pts) extruded PERPENDICULAR to its own plane by the local Extrude
            // vector -- a cut at ANY orientation in the local frame (the purlin dovetail housing in a sloped
            // rafter). Region.Extrude is perpendicular to the region; the signed perpendicular extent
            // reg.Normal . eWorld carries both magnitude and the correct direction regardless of winding.
            void CutPrism(Point3d[] poly, Vector3d extrude, bool subtract, int joint)
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
                catch (System.Exception ex)   // leave the stock as-is rather than abort the whole emit
                {
                    Diag.Warn("BuildFramedSolid/Prism", (joint >= 0 ? "joint " + joint : "shape")
                        + (subtract ? " subtract" : " union") + " skipped: " + ex.Message + loc);
                }
            }
            // Shape Subtracts cut clean THROUGH the full width (pad each side); their callers don't carry a band.
            if (f.Subtracts != null)
                foreach (Point3d[] poly in f.Subtracts) CutPoly(poly, true, -f.W / 2.0 - 1.0, f.W / 2.0 + 1.0, -1);
            if (f.JointPolys != null)
                foreach ((Point3d[] Poly, int Joint, bool Subtract, double Xlo, double Xhi) jp in f.JointPolys)
                    CutPoly(jp.Poly, jp.Subtract, jp.Xlo, jp.Xhi, jp.Joint);
            if (f.JointPolysZ != null)
                foreach ((Point3d[] Poly, int Joint, bool Subtract, double Xlo, double Xhi) jp in f.JointPolysZ)
                    CutPolyZ(jp.Poly, jp.Subtract, jp.Xlo, jp.Xhi, jp.Joint);
            if (f.JointPrisms != null)
                foreach ((Point3d[] Poly, Vector3d Extrude, int Joint, bool Subtract) jp in f.JointPrisms)
                    CutPrism(jp.Poly, jp.Extrude, jp.Subtract, jp.Joint);

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
                    catch (System.Exception ex)   // skip a degenerate feature rather than abort the emit
                    {
                        Diag.Warn("BuildFramedSolid/Feature", "joint " + ft.Joint
                            + (ft.Subtract ? " mortise" : " tenon") + " box skipped: " + ex.Message + loc);
                    }
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
                    catch (System.Exception ex)   // skip a degenerate peg rather than abort the emit
                    {
                        Diag.Warn("BuildFramedSolid/Peg", "joint " + pg.Joint
                            + " peg bore skipped: " + ex.Message + loc);
                    }
                }
            }

            // SHAPE WINS (batch-2 #13): re-apply the shape Subtracts AFTER the joinery -- a union
            // feature (tenon stub, rafter tongue) applied above would otherwise RE-ADD material
            // inside a TProfile arch (the tenon appeared where the arch had removed wood). Removed
            // is removed: the profile trims tenons and tongues like the rest of the body. The mate's
            // pocket is still sized to the nominal joint -- resize the joint if the arch eats too
            // much of it.
            if (f.Subtracts != null && f.Subtracts.Count > 0
                && ((f.Features != null && f.Features.Exists(x => !x.Subtract))
                    || (f.JointPolys != null && f.JointPolys.Exists(x => !x.Subtract))
                    || (f.JointPolysZ != null && f.JointPolysZ.Exists(x => !x.Subtract))
                    || (f.JointPrisms != null && f.JointPrisms.Exists(x => !x.Subtract))))
                foreach (Point3d[] poly in f.Subtracts) CutPoly(poly, true, -f.W / 2.0 - 1.0, f.W / 2.0 + 1.0, -1);

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
    }
}
