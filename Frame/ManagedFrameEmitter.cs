using System.Collections.Generic;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{
    // Phase 2 rough-in emitter: turns a FrameGraph (the parametric bent generator's output)
    // into MANAGED timbers, so a generated bent is immediately editable by the managed verbs
    // (TFit / TScan / MOVE-overrule / TScarf). Each edge becomes a W x D stock box plus a list of
    // convex CUT planes (the king post gable, ridge/eave-girt chamfers, mitered ends), written
    // through ManagedTimber.DrawFramedSolid (builds the box, slices every stored plane, writes the
    // TMFrame xrecord + XData). The box is derived from the member's half-planes, not its nodes:
    // in-bent members run their length along the LONG faces (un-skews the king post) and centre
    // between them; bay members take their oriented section from parallel plane pairs (places the
    // eave girt flush, rotates the purlin square to the roof).
    //
    // Concave features use a boolean SUBTRACT (TFrame.Subtracts): the common-rafter birdsmouth is
    // done (TryCommonRafter). The brace arch (a curved subtract) is deferred -- aesthetic, not a
    // completeness issue, and the generator's braces are straight.
    public static class ManagedFrameEmitter
    {
        // A plane counts as an END CAP (vs a long face) when its normal runs along the member
        // axis; long faces are parallel to the axis (N . axis ~ 0).
        private const double CapDot = 0.35;
        private const double Eps    = 1.0e-6;

        // MODEL BASIS: the fixed graph->world orientation. The generators are elevation-native (graph
        // X=across, Y=height, Z=along building); AutoCAD is Z-up. A cyclic axis permutation X->Y->Z->X
        // (a proper rotation, det +1, no mirror) stands the frame up so WALLS lie in the XZ plane and
        // BENTS in the YZ plane: across(graph X)->world Y, height(graph Y)->world +Z, building
        // length(graph Z)->world X (the building runs along +X). The current UCS is used ONLY to
        // position/rotate a frame: Compose applies the basis FIRST, then the UCS placement. So the
        // footprint (graph Y=0) lands on the WCS XY plane (in a plan UCS) and a flat grid sits under it.
        public static readonly Matrix3d ModelBasis = Matrix3d.AlignCoordinateSystem(
            Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,    // graph axes
            Point3d.Origin, Vector3d.YAxis, Vector3d.ZAxis, Vector3d.XAxis);   // -> world (X->Y, Y->Z, Z->X)

        public static Matrix3d Compose(Matrix3d ucsPlacement) => ucsPlacement.PostMultiplyBy(ModelBasis);

        // Emit every edge as a managed timber, placed into WCS by `placement` (graph coords ->
        // WCS; see TRoughIn). `frameTag` is the grouping layer's top level: every emitted timber is
        // stamped with it (so a per-frame redraw clears only this frame). Each edge also carries its
        // own bent (Arabic) / bay (Roman) tag from the Build(FrameSpec) walk; a single-bent graph
        // (no walk) leaves those empty, so we fall back to bent "1". Also returns (out) the STRUCTURAL
        // GRID derived from the un-skewed frames -- the caller draws it; each timber's grid label and a
        // longitudinal member's wall letter are stamped from it here.
        public static int Emit(FrameGraph g, Matrix3d placement, string frameTag, out FrameGrid grid)
            => Emit(g, placement, frameTag, out grid, out _);

        // The regen overload also reports how many slots were CEDED to pinned members (Free = "2":
        // skeleton members shape-edited pre-freeze -- Robert's call 2026-07-16, "free edited
        // skeleton timbers" instead of freezing). A pinned member OWNS its slot: emitting its twin
        // would stack a fresh member on top of the edit. Two match keys, blind spots covered like
        // the joinery replay's: a UNIQUE grid-label match (labels are stable when a param change
        // MOVES the slot) or world BOX OVERLAP (stable when an insert RENUMBERS labels; also what
        // catches a scarfed member's two pieces). Brace group symbols (*) are never label-matched.
        public static int Emit(FrameGraph g, Matrix3d placement, string frameTag, out FrameGrid grid, out int ceded)
        {
            // Pass 1: graph-coord frame (+ end cuts) for every drawable edge.
            var items = new List<(FrameEdge e, ManagedTimber.TFrame f, string nearCut, string farCut)>();
            foreach (FrameEdge e in g.Edges)
                if (TryEdgeToFrame(g, e, out ManagedTimber.TFrame f, out string nc, out string fc))
                    items.Add((e, f, nc, fc));

            // The structural grid from the un-skewed frames (f.O centerline, f.Z true length axis).
            var gridFrames = new List<(FrameEdge, ManagedTimber.TFrame)>(items.Count);
            foreach (var it in items) gridFrames.Add((it.e, it.f));
            grid = FrameGrid.Build(gridFrames);

            // The pinned members whose slots this emit must cede (label uniqueness pre-counted).
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            List<(ManagedTimber.TFrame F, string Role, string Label)> pinned = doc != null
                ? ManagedTimber.EnumeratePinned(doc.Database, frameTag)
                : new List<(ManagedTimber.TFrame, string, string)>();
            var pinnedLabelCount = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
            foreach ((ManagedTimber.TFrame F, string Role, string Label) p in pinned)
                if (!string.IsNullOrEmpty(p.Label) && p.Label[0] != '*')
                    pinnedLabelCount[p.Label] = pinnedLabelCount.TryGetValue(p.Label, out int c) ? c + 1 : 1;

            // Pass 2: place + draw, stamping the grouping tags + grid-derived label / wall letter.
            int drawn = 0;
            ceded = 0;
            foreach (var (e, f, nearCut, farCut) in items)
            {
                string bentTag = string.IsNullOrEmpty(e.BentTag) && string.IsNullOrEmpty(e.BayTag)
                                 ? "1" : e.BentTag;
                // Wall letter. A roof timber (common/purlin) belongs to the wall on its SIDE (the eave-
                // girt wall), NOT the nearest column -- a purlin sits partway up the slope, so ColLetter
                // would wrongly snap it to a king-post/vstrut column. Other longitudinal members (eave/
                // floor girt, ridge) keep the nearest-column letter. A FreeBox bay brace LIVES IN a wall
                // plane, so it gets that wall's letter too -- unstamped, every bay brace fell into the
                // Browser's Wall A fallback.
                string wallTag =
                    (e.Role == "Common" || e.Role == "Purlin") ? grid.SideWall(f.O.X) :
                    e.Longitudinal || e.FreeBox                ? grid.ColLetter(f.O.X) : "";
                // Braces carry only a group symbol (*, **, ..) by size+shape; commons / purlins are
                // numbered 1..n per (wall, bay) -- restarting each bay; everything else gets its label.
                string gridLabel =
                    e.Role == "Brace"  ? grid.BraceLabel(e, f) :
                    e.Role == "Common" ? grid.RoofLabel("C", wallTag, e.BayTag) :
                    e.Role == "Purlin" ? grid.RoofLabel("P", wallTag, e.BayTag) :
                    e.Longitudinal     ? grid.WallBayLabel(e, wallTag) :   // eave/floor/ridge/queen/hammer girts
                                         grid.LabelForEdge(e, f);          // bent members (verticals + ties)
                // Per-group layer for tree isolation: bent member -> bent layer; longitudinal/roof/
                // bay-brace -> wall layer (the SideWall fallback below is vestigial now that bay
                // braces stamp a wall letter; kept for any future untagged bay member).
                string groupWall = !string.IsNullOrEmpty(wallTag) ? wallTag
                                 : string.IsNullOrEmpty(bentTag) ? grid.SideWall(f.O.X) : "";
                string groupLayer = ManagedTimber.GroupLayer(frameTag, bentTag, groupWall);
                ManagedTimber.TFrame world = ManagedTimber.TransformFrame(f, placement);

                // Cede the slot to a pinned member: unique label match, or genuine box overlap
                // (pad -0.5: interpenetration, never a touching neighbor). Role must agree.
                bool cede = false;
                foreach ((ManagedTimber.TFrame F, string Role, string Label) p in pinned)
                {
                    if (!string.Equals(p.Role, e.Role, System.StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.IsNullOrEmpty(gridLabel) && gridLabel[0] != '*'
                        && string.Equals(p.Label, gridLabel, System.StringComparison.OrdinalIgnoreCase)
                        && pinnedLabelCount.TryGetValue(gridLabel, out int lc) && lc == 1)
                    { cede = true; break; }
                    if (ManagedTimber.FramesOverlap(world, p.F, -0.5)) { cede = true; break; }
                }
                if (cede) { ceded++; continue; }

                Autodesk.AutoCAD.DatabaseServices.ObjectId id = ManagedTimber.DrawFramedSolid(
                    world, e.Role, e.Designation, nearCut, farCut,
                    frameTag, bentTag, e.BayTag, wallTag, gridLabel, groupLayer);
                // A FreeBox brace seats into its members along OBLIQUE cut planes (post face / girt
                // underside), not its analytic end caps -- so stamp the seat points (centerline x each
                // cut plane, in WCS) explicitly for TScan; coincidence Faces() can't find them.
                if (e.FreeBox && world.Cuts != null && world.Cuts.Count > 0)
                {
                    var seats = SeatNodes(world);
                    if (seats.Count > 0) ManagedTimber.WriteSeatNodes(id, seats);
                }
                // A common rafter seats on the eave girt via a BIRDSMOUTH (a concave subtract, not an
                // analytic box face) -- so Faces()/FacesMate can't find that joint. Stamp its seat point
                // explicitly for TScan: the mid of the girt-top seat cut (CustomProfile[1] foot-top ->
                // [2] seat-inner), at this common's Z station, transformed to WCS -- as for brace seats.
                else if (e.Role == "Common" && e.CustomProfile != null && e.CustomProfile.Length >= 5)
                {
                    // Seat node = mid of the birdsmouth seat. 5-pt: foot-top[1] -> seat-inner[2];
                    // 7-pt (tail form): notch seat-outer[3] -> seat-inner[4] (both at the dropped girt top).
                    bool tailForm = e.CustomProfile.Length >= 7;
                    Point3d sOut = tailForm ? e.CustomProfile[3] : e.CustomProfile[1];
                    Point3d sIn  = tailForm ? e.CustomProfile[4] : e.CustomProfile[2];
                    var seat = new Point3d((sOut.X + sIn.X) / 2.0, sOut.Y, f.O.Z).TransformBy(placement);
                    ManagedTimber.WriteSeatNodes(id, new[] { seat });
                }
                drawn++;
            }
            return drawn;
        }

        // Seat points of a FreeBox brace: where its centerline crosses each member-face cut plane (the
        // post bay-side face and the girt underside). These are the true joint points -- the brace seats
        // along these OBLIQUE planes, not its perpendicular end caps, so Faces()/coincidence can't find
        // them. Frame already in WCS; clamped to the brace segment so a near-parallel plane is skipped.
        private static List<Point3d> SeatNodes(ManagedTimber.TFrame f)
        {
            var pts = new List<Point3d>();
            if (f.Cuts == null) return pts;
            Point3d p0 = f.O, p1 = f.O + f.Z * f.L;
            Vector3d seg = p1 - p0;
            double segLen2 = seg.DotProduct(seg);
            if (segLen2 <= Eps) return pts;
            foreach ((Point3d P, Vector3d N) cut in f.Cuts)
            {
                double denom = seg.DotProduct(cut.N);
                if (System.Math.Abs(denom) < Eps) continue;        // plane parallel to the brace axis
                double t = (cut.P - p0).DotProduct(cut.N) / denom;
                if (t < -0.25 || t > 1.25) continue;               // off the (over-long) member -- not a seat
                pts.Add(p0 + seg * t);
            }
            return pts;
        }

        // Build the structural grid from a graph WITHOUT drawing any timbers (the standalone TGrid
        // path). Runs the same un-skewing TryEdgeToFrame so the column/bent lines match an emit.
        public static FrameGrid BuildGrid(FrameGraph g)
        {
            var frames = new List<(FrameEdge, ManagedTimber.TFrame)>();
            foreach (FrameEdge e in g.Edges)
                if (TryEdgeToFrame(g, e, out ManagedTimber.TFrame f, out _, out _))
                    frames.Add((e, f));
            return FrameGrid.Build(frames);
        }

        // Convert one edge to a TFrame in GRAPH coordinates (+ the per-end cut tags). Returns false
        // (skip) on a degenerate length.
        public static bool TryEdgeToFrame(FrameGraph g, FrameEdge e,
            out ManagedTimber.TFrame f, out string nearCut, out string farCut)
        {
            f = default; nearCut = "Butt"; farCut = "Butt";
            Point3d a = g.Node(e.NodeA).Pos;
            Point3d b = g.Node(e.NodeB).Pos;
            if (e.FreeBox) return TryFreeBrace(e, a, b, ref f);
            if (e.Longitudinal) return TryLongitudinal(e, a, b, ref f);
            if (e.CustomProfile != null && e.CustomProfile.Length >= 5)
                return TryCommonRafter(e, a, b, ref f, ref nearCut, ref farCut);
            return TryInBent(e, a, b, ref f, ref nearCut, ref farCut);
        }

        // Common rafter: a sloped rafter that BEARS on the eave girt (birdsmouth seat + plumb heel)
        // and meets the ridge face (plumb peak cut). It carries no half-planes, only the 5-point
        // concave CustomProfile [peakTop, toeTop, seat, heel, peakUnder] (KingPostBentGraph.AddRaftersAt).
        // The stock = a slope-oriented box with the top face on the rafter top line, plumb ends; the
        // birdsmouth is the stock-minus-profile notch at the foot, emitted as a LOCAL subtract polygon.
        private static bool TryCommonRafter(FrameEdge e, Point3d a, Point3d b,
            ref ManagedTimber.TFrame f, ref string nearCut, ref string farCut)
        {
            Point3d[] cp = e.CustomProfile;
            // 5-pt = today's TOE birdsmouth [peakTop, toeTop, seat, heel, peakUnder]; 7-pt = offset/tail
            // form [peakTop, footTop(tail end), peakUnder, notchA..D] with an INTERIOR birdsmouth so the
            // foot can carry an overhang.
            bool tailForm = cp.Length >= 7;
            Point3d peakTop = cp[0], toeTop = cp[1];
            Point3d peakUnder = tailForm ? cp[2] : cp[4];

            Vector3d slope = peakTop - toeTop;                 // toe -> peak (length axis)
            if (slope.Length <= Eps || System.Math.Abs(slope.X) < Eps) return false;
            Vector3d zAxis = slope.GetNormal();
            Vector3d depthAxis = zAxis.CrossProduct(Vector3d.ZAxis);
            if (depthAxis.DotProduct(peakUnder - peakTop) < 0) depthAxis = depthAxis.Negate(); // toward underside
            Vector3d widthAxis = depthAxis.CrossProduct(zAxis).GetNormal();                    // right-handed
            double D = e.Depth, W = e.Width;

            // End-face centres = where the depth-centerline (D/2 toward the underside) crosses the two
            // PLUMB verticals at the toe (x = toeTop.X) and the peak (x = peakTop.X).
            Point3d q0 = toeTop + depthAxis * (D / 2.0);
            Point3d nearC = q0 + zAxis * ((toeTop.X  - q0.X) / zAxis.X);
            Point3d farC  = q0 + zAxis * ((peakTop.X - q0.X) / zAxis.X);
            double L = nearC.DistanceTo(farC);
            if (L <= Eps) return false;
            // Far (peak) end is plumb to the ridge; the near (tail/foot) end is plumb (vertical fascia) or
            // square (perpendicular to the rafter) per the commons' tail-cut selection.
            nearCut = e.SquareTail ? "square" : "matchface"; farCut = "matchface";

            // Birdsmouth notch (graph XY). 5-pt: a wedge at the toe (seat + plumb heel + stock underside
            // back to the toe). 7-pt: the explicit interior quad from AddRafterOneSide (notchA..D), so the
            // rafter foot continues past it as a tail.
            Point3d[] notchXY;
            if (tailForm) notchXY = new[] { cp[3], cp[4], cp[5], cp[6] };
            else
            {
                Point3d seat = cp[2], heel = cp[3];
                Point3d toeUnder = peakUnder + zAxis * ((toeTop.X - peakUnder.X) / zAxis.X);
                notchXY = new[] { toeTop, seat, heel, toeUnder };
            }
            // Project to LOCAL (length, depth) and drop consecutive duplicate vertices -- when the
            // birdsmouth seat tapers to the rafter-underside exit the heel collapses (nB == nC), so the
            // quad becomes a triangle; a fully-collapsed notch (< 3 distinct points = the girt dropped
            // clear of the rafter) is omitted, leaving an un-notched rafter.
            var local = new List<Point3d>(notchXY.Length);
            foreach (Point3d w in notchXY)
            {
                Vector3d r = w - nearC;
                var lp = new Point3d(r.X * zAxis.X + r.Y * zAxis.Y, r.X * depthAxis.X + r.Y * depthAxis.Y, 0);
                if (local.Count == 0 || lp.DistanceTo(local[local.Count - 1]) > 1e-6) local.Add(lp);
            }
            if (local.Count > 1 && local[0].DistanceTo(local[local.Count - 1]) <= 1e-6) local.RemoveAt(local.Count - 1);

            // Plumb-end OUTWARD normals follow the slope's X sign (toe->peak = toward the ridge): the LEFT slope
            // rises +X (peak.X > toe.X), the RIGHT slope rises -X. Hardcoding +/-X was right for the left slope but
            // FLIPPED the right slope's head normal, so common->ridge picked the wrong ridge face (housing on the
            // wrong side). The body is unaffected -- BuildFramedSolid centroid-corrects the slice keep-side.
            double sx = slope.X >= 0.0 ? 1.0 : -1.0;
            f = new ManagedTimber.TFrame
            {
                O = new Point3d(nearC.X, nearC.Y, a.Z + W / 2.0),
                X = widthAxis, Y = depthAxis, Z = zAxis,
                L = L, D = D, W = W,
                // Far (peak) end plumb toward the ridge (+sx); near (tail) end plumb away (-sx) or square (-zAxis).
                NearN = e.SquareTail ? zAxis.Negate() : new Vector3d(-sx, 0, 0), FarN = new Vector3d(sx, 0, 0),
                Subtracts = local.Count >= 3 ? new List<Point3d[]> { local.ToArray() } : null
            };
            return true;
        }

        // Free box: a straight W x D x L member between two arbitrary 3D points (e.g. a wall-plane bay
        // brace, which lies in the Y-Z plane and so fits neither the in-bent nor the longitudinal path).
        // Width runs along X (across the building -- the wall thickness); depth is the in-plane
        // perpendicular. Plain butt ends, no half-planes -- the universal-timber box.
        private static bool TryFreeBrace(FrameEdge e, Point3d a, Point3d b, ref ManagedTimber.TFrame f)
        {
            Vector3d d = b - a;
            double L = d.Length;
            if (L <= Eps) return false;
            Vector3d z = d / L;
            Vector3d x = System.Math.Abs(z.DotProduct(Vector3d.XAxis)) > 0.99 ? Vector3d.YAxis : Vector3d.XAxis;
            Vector3d y = z.CrossProduct(x).GetNormal();
            x = y.CrossProduct(z).GetNormal();   // re-orthogonalize, right-handed (x x y = z)
            // Member-face seats: any half-planes on the edge become convex CUT planes that trim the
            // (over-long) box flush to the post face / girt underside (square shoulders).
            List<(Point3d, Vector3d)> cuts = null;
            if (e.Planes != null && e.Planes.Count > 0)
            {
                cuts = new List<(Point3d, Vector3d)>(e.Planes.Count);
                foreach (HalfPlane hp in e.Planes) cuts.Add((hp.P, hp.N));
            }
            f = new ManagedTimber.TFrame
            {
                O = a, X = x, Y = y, Z = z,
                L = L, D = e.Depth, W = e.Width,
                NearN = z.Negate(), FarN = z,
                Cuts = cuts
            };
            return true;
        }

        // Bay member: a W x D cross-section (in the bent XY plane) extruded along world Z to fill the
        // clear gap between bents. The section lives in the edge's half-planes (absolute XY), NOT
        // centred on the node, so derive the section's oriented box from PARALLEL PLANE PAIRS (this
        // both places the eave girt flush to the post face and ROTATES the purlin square to the roof).
        // Leftover single planes (ridge / eave-girt chamfers) become convex cut planes.
        private static bool TryLongitudinal(FrameEdge e, Point3d a, Point3d b, ref ManagedTimber.TFrame f)
        {
            double z   = a.Z + e.LongInset;
            double len = (b.Z - a.Z) - e.LongInset;
            if (len <= Eps) return false;

            var pairs   = new List<(Vector3d axis, double gap, double center)>();
            var singles = new List<HalfPlane>();
            GroupParallelPairs(e.Planes, pairs, singles);

            Vector3d widthAxis, depthAxis;
            Point3d secCenter;
            if (pairs.Count >= 2 && TwoBestPairs(pairs, e.Width, e.Depth, out var wp, out var dp)
                && SolveSectionCenter(wp, dp, out secCenter))
            {
                widthAxis = wp.axis;
                depthAxis = dp.axis;
                // Right-handed: widthAxis x depthAxis must be +Z (lengthAxis); flip depth if not.
                if (widthAxis.X * depthAxis.Y - widthAxis.Y * depthAxis.X < 0) depthAxis = depthAxis.Negate();
            }
            else
            {
                // Fallback: axis-aligned box centred on the node (no usable plane pairs).
                widthAxis = Vector3d.XAxis; depthAxis = Vector3d.YAxis;
                secCenter = new Point3d(a.X, a.Y, 0);
                singles.Clear();
            }

            var cuts = new List<(Point3d, Vector3d)>();
            foreach (HalfPlane hp in singles) cuts.Add((hp.P, hp.N));

            f = new ManagedTimber.TFrame
            {
                O = new Point3d(secCenter.X, secCenter.Y, z),
                X = widthAxis, Y = depthAxis, Z = Vector3d.ZAxis,
                L = len, D = e.Depth, W = e.Width,
                NearN = Vector3d.ZAxis.Negate(), FarN = Vector3d.ZAxis,
                Cuts = cuts.Count > 0 ? cuts : null
            };
            return true;
        }

        // In-bent member (or common rafter): the XY elevation is extruded +Width in world Z from
        // zBase = nodeA.Z + ZOffset. The graph's reference line is a member FACE/EDGE, not the
        // centroid (post nodes on the outside face, girt nodes on the bottom face, rafter nodes on
        // the top line). So: the LENGTH axis runs along the two LONG faces (not the node direction --
        // this is what un-skews the king post, whose reference line is the diagonal base-to-apex);
        // the depth-centerline sits midway between the long faces; each end-face centre is where that
        // centerline crosses its primary cap plane; any EXTRA caps (the king post's second gable
        // plane) become convex cut planes that BuildFramedSolid slices, giving the exact peak.
        private static bool TryInBent(FrameEdge e, Point3d a, Point3d b,
            ref ManagedTimber.TFrame f, ref string nearCut, ref string farCut)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double lenAB = System.Math.Sqrt(dx * dx + dy * dy);
            if (lenAB <= Eps) return false;
            Vector3d u0 = new Vector3d(dx / lenAB, dy / lenAB, 0);   // node direction (classify only)

            var longFaces = new List<HalfPlane>();
            var caps      = new List<HalfPlane>();
            foreach (HalfPlane hp in e.Planes)
            {
                if (System.Math.Abs(hp.N.X * u0.X + hp.N.Y * u0.Y) > CapDot) caps.Add(hp);
                else                                                         longFaces.Add(hp);
            }

            // Length axis = the direction the long faces RUN (perpendicular to their shared normal),
            // oriented along the node direction. With <2 long faces fall back to the node direction.
            Vector3d lengthAxis = u0;
            if (longFaces.Count == 2)
            {
                Vector3d nL = longFaces[0].N;
                Vector3d run = new Vector3d(-nL.Y, nL.X, 0);
                if (run.X * u0.X + run.Y * u0.Y < 0) run = run.Negate();
                lengthAxis = run.GetNormal();
            }
            Vector3d widthAxis = Vector3d.ZAxis;
            Vector3d depthAxis = lengthAxis.CrossProduct(Vector3d.ZAxis); // in-plane, right-handed

            // Depth offset: shift the reference line onto the box centerline (midway between the two
            // long faces) -- a post's nodes are on x=0, so this moves the box to span [0, PostD].
            double refC = a.X * depthAxis.X + a.Y * depthAxis.Y;
            double depthOffset = 0.0;
            if (longFaces.Count == 2)
            {
                double c0 = longFaces[0].P.X * depthAxis.X + longFaces[0].P.Y * depthAxis.Y;
                double c1 = longFaces[1].P.X * depthAxis.X + longFaces[1].P.Y * depthAxis.Y;
                depthOffset = (c0 + c1) / 2.0 - refC;
            }
            else if (TryProfileBand(e, depthAxis, out double pmin, out double pmax))
            {
                depthOffset = (pmin + pmax) / 2.0 - refC;   // CustomProfile (commons) fallback
            }
            Point3d q0 = new Point3d(a.X + depthAxis.X * depthOffset, a.Y + depthAxis.Y * depthOffset, 0);

            // Caps -> near/far by smaller |Signed|; the FIRST cap at each end is the primary end (its
            // plane gives NearN/FarN, the end-face centre is the centerline n that plane); extras
            // (king post second gable plane) -> cut planes.
            var nearCaps = new List<HalfPlane>();
            var farCaps  = new List<HalfPlane>();
            foreach (HalfPlane hp in caps)
            {
                if (System.Math.Abs(hp.Signed(a)) <= System.Math.Abs(hp.Signed(b))) nearCaps.Add(hp);
                else                                                                 farCaps.Add(hp);
            }

            double zBase = a.Z + e.ZOffset;
            Point3d nearC = nearCaps.Count >= 1 ? IntersectXY(q0, lengthAxis, nearCaps[0]) : ProjectOnLine(q0, lengthAxis, a);
            Point3d farC  = farCaps.Count  >= 1 ? IntersectXY(q0, lengthAxis, farCaps[0])  : ProjectOnLine(q0, lengthAxis, b);
            if (nearCaps.Count >= 1) nearCut = "matchface";
            if (farCaps.Count  >= 1) farCut  = "matchface";

            double lenXY = nearC.DistanceTo(farC);
            if (lenXY <= Eps) return false;
            Vector3d zAxis = new Vector3d((farC.X - nearC.X) / lenXY, (farC.Y - nearC.Y) / lenXY, 0);

            Vector3d nearN = nearCaps.Count >= 1 ? nearCaps[0].N.Negate() : zAxis.Negate();
            Vector3d farN  = farCaps.Count  >= 1 ? farCaps[0].N.Negate()  : zAxis;

            var cuts = new List<(Point3d, Vector3d)>();
            for (int i = 1; i < nearCaps.Count; i++) cuts.Add((nearCaps[i].P, nearCaps[i].N));
            for (int i = 1; i < farCaps.Count;  i++) cuts.Add((farCaps[i].P,  farCaps[i].N));

            f = new ManagedTimber.TFrame
            {
                O = new Point3d(nearC.X, nearC.Y, zBase + e.Width / 2.0),
                X = widthAxis, Y = depthAxis, Z = zAxis,
                L = lenXY, D = e.Depth, W = e.Width,
                NearN = nearN, FarN = farN,
                Cuts = cuts.Count > 0 ? cuts : null
            };
            return true;
        }

        // Group the planes into PARALLEL PAIRS (two planes with parallel/anti-parallel normals). Each
        // pair -> (axis = a unit normal, gap = separation of the two boundary lines, center = the
        // mid-line coordinate along axis). Planes with no parallel partner -> singles (chamfers).
        private static void GroupParallelPairs(List<HalfPlane> planes,
            List<(Vector3d axis, double gap, double center)> pairs, List<HalfPlane> singles)
        {
            var used = new bool[planes.Count];
            for (int i = 0; i < planes.Count; i++)
            {
                if (used[i]) continue;
                Vector3d ni = planes[i].N;
                int j = -1;
                for (int k = i + 1; k < planes.Count; k++)
                {
                    if (used[k]) continue;
                    Vector3d nk = planes[k].N;
                    if (System.Math.Abs(ni.X * nk.Y - ni.Y * nk.X) < 1e-6) { j = k; break; } // parallel
                }
                if (j < 0) { singles.Add(planes[i]); used[i] = true; continue; }
                double di = planes[i].P.X * ni.X + planes[i].P.Y * ni.Y;
                double dj = planes[j].P.X * ni.X + planes[j].P.Y * ni.Y;
                pairs.Add((ni.GetNormal(), System.Math.Abs(di - dj), (di + dj) / 2.0));
                used[i] = used[j] = true;
            }
        }

        // Pick the pair whose gap best matches the width, and (a different) pair best matching depth.
        private static bool TwoBestPairs(List<(Vector3d axis, double gap, double center)> pairs,
            double width, double depth,
            out (Vector3d axis, double gap, double center) wp,
            out (Vector3d axis, double gap, double center) dp)
        {
            wp = default; dp = default;
            int wi = -1; double wBest = double.MaxValue;
            for (int i = 0; i < pairs.Count; i++)
            {
                double s = System.Math.Abs(pairs[i].gap - width);
                if (s < wBest) { wBest = s; wi = i; }
            }
            int di = -1; double dBest = double.MaxValue;
            for (int i = 0; i < pairs.Count; i++)
            {
                if (i == wi) continue;
                double s = System.Math.Abs(pairs[i].gap - depth);
                if (s < dBest) { dBest = s; di = i; }
            }
            if (wi < 0 || di < 0) return false;
            wp = pairs[wi]; dp = pairs[di];
            return true;
        }

        // The point lying on both pair mid-lines (x.axisW = centerW and x.axisD = centerD).
        private static bool SolveSectionCenter(
            (Vector3d axis, double gap, double center) wp,
            (Vector3d axis, double gap, double center) dp, out Point3d center)
        {
            center = default;
            double wx = wp.axis.X, wy = wp.axis.Y, dx = dp.axis.X, dy = dp.axis.Y;
            double det = wx * dy - wy * dx;
            if (System.Math.Abs(det) < 1e-9) return false;        // axes parallel -- shouldn't happen
            double px = (wp.center * dy - dp.center * wy) / det;
            double py = (wx * dp.center - dx * wp.center) / det;
            center = new Point3d(px, py, 0);
            return true;
        }

        // Project point p onto the XY line (q + t*dir) -- the square-end face centre when an end has
        // no cap plane (keeps the end-face centre on the box centerline).
        private static Point3d ProjectOnLine(Point3d q, Vector3d dir, Point3d p)
        {
            double t = (p.X - q.X) * dir.X + (p.Y - q.Y) * dir.Y;
            return new Point3d(q.X + dir.X * t, q.Y + dir.Y * t, 0);
        }

        // Where the XY line (q + t*u) crosses the half-plane's boundary line. Caps satisfy
        // |hp.N . u| > CapDot, so the denominator is safely non-zero.
        private static Point3d IntersectXY(Point3d q, Vector3d u, HalfPlane hp)
        {
            double denom = u.X * hp.N.X + u.Y * hp.N.Y;
            double t = ((hp.P.X - q.X) * hp.N.X + (hp.P.Y - q.Y) * hp.N.Y) / denom;
            return new Point3d(q.X + u.X * t, q.Y + u.Y * t, 0);
        }

        // Min/max projection of a member's profile polygon onto `axis` (its depth band) -- used to
        // centre a CustomProfile member (e.g. a common rafter) that carries no half-planes.
        private static bool TryProfileBand(FrameEdge e, Vector3d axis, out double min, out double max)
        {
            min = double.MaxValue; max = double.MinValue;
            Point3d[] poly = e.CustomProfile;
            if (poly == null || poly.Length < 3) return false;
            foreach (Point3d p in poly)
            {
                double c = p.X * axis.X + p.Y * axis.Y;
                if (c < min) min = c;
                if (c > max) max = c;
            }
            return true;
        }
    }
}
