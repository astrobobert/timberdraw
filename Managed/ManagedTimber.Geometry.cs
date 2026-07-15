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
    // ManagedTimber part: pure JOINT + FACE GEOMETRY -- the joint builders (girt-post kit,
    // rafter/ridge/purlin/common/strut families), faces/mating, and frame transforms.
    // (Verbatim moves; see CLAUDE.md.)
    public static partial class ManagedTimber
    {
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

            // Legs measure CORNER -> TOE (Robert's rule, batch-2 #15 -- the generator's braces have
            // always drawn this way): foot/head anchor the brace's OUTER (toe) edge on the host
            // faces, so the box CENTERLINE sits depth/2 in from that line, toward the corner. The
            // frame's END CENTERS must then be where that SHIFTED centerline crosses the ACTUAL host
            // face planes -- BuildFramedSolid miters through O and O+Z*L, so anchoring them off the
            // planes translates the miters and buries the ends in the post/girt. With the ends on
            // the planes, the miters reproduce the host faces exactly and the toe tips land at
            // foot/head: the same stick the generator emits for the same legs.
            Point3d mid = foot + (head - foot) * 0.5;
            double side = (pa - mid).DotProduct(yb) >= 0.0 ? 1.0 : -1.0;   // which yb sign faces the corner
            Point3d c0 = foot + yb * (side * depth / 2.0);                 // a point on the shifted centerline

            double denomA = xb.DotProduct(na), denomB = xb.DotProduct(nb);
            if (System.Math.Abs(denomA) < 1e-9 || System.Math.Abs(denomB) < 1e-9) return false;  // axis parallel to a face
            double tA = (fa.C - c0).DotProduct(na) / denomA;   // centerline station on face A's plane
            double tB = (fb.C - c0).DotProduct(nb) / denomB;   // centerline station on face B's plane
            if (tB - tA < 1e-6) return false;                  // no body left between the miters

            // Map to the section-in-XY / length-along-Z convention: length = Z (the brace axis xb),
            // depth = Y (yb), width = X (recomputed as Y x Z so the frame stays right-handed; width is
            // symmetric so its sign doesn't matter). Mitered ends face back toward their mates so
            // FacesMate sees opposing normals -> nodes.
            frame = new TFrame
            {
                O = c0 + xb * tA, X = yb.CrossProduct(xb).GetNormal(), Y = yb, Z = xb,
                L = tB - tA, D = depth, W = width,
                NearN = na.Negate(), FarN = nb.Negate()
            };
            return true;
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
}
