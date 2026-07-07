using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{
    // Parameter bundle for a King Post bent. Derived structural levels are computed
    // properties so the graph builder stays consistent.
    // Mirrors the derivations in KPBent.Draw() (TOH/TOG/BOG, Beta, PlumbLength).
    public class KPBentParams
    {
        public double Span;
        public double EaveHt;
        public double Pitch;
        public double PostW, PostD;
        public double GirtW, GirtD;
        public double RafterW, RafterD;
        public double KpostW, KpostD;
        public double RidgeW, RidgeD;   // ridge section (own size, not the king post)
        public double BraceW, BraceD, BraceLength;
        public double BraceAngle;   // degrees from horizontal; default 45
        public double BraceFoot, BraceHead;   // brace legs: Foot = down the post (vertical),
                                              // Head = along the girt (horizontal); tan(Angle) = Foot/Head
        public bool   HasBrace;
        public double StrutW, StrutD;
        public bool   HasStrut;
        public double VStrutW, VStrutD;
        public bool   HasVStrut;
        public double StrutAngle;   // degrees from horizontal; default 45
        public int    OffsetType;   // 0 Back, 1 Centered, 2 Front (BraceStrutPlacement)
        public double[] BaySpacings; // center-to-center bent spacing per bay (face-to-same-face);
                                     // bent count = BaySpacings.Length + 1
        public bool   UseCommons;       // roof mode: true = common rafters; false = purlins
        public int    CommonMode;       // commons spacing: 0 = by count, 1 = by center-to-center spacing
        public int    CommonCount;      // mode 0: number of commons per bay
        public double CommonSpacing;    // mode 1: requested on-center spacing
        public double CommonW, CommonD; // common rafter section (own size, not the principal rafter)
        public int    PurlinMode;       // purlins spacing: 0 = by count, 1 = by on-center spacing (along slope)
        public int    PurlinCount;      // mode 0: number of purlins per slope per bay
        public double PurlinSpacing;    // mode 1: requested on-center spacing (along the slope)
        public double PurlinW, PurlinD; // purlin section: W along the slope, D perpendicular (down from roof)
        public double QueenW, QueenD;       // Queen Post bent: queen post section
        public double UpperGirtW, UpperGirtD;// Queen Post bent: straining beam section
        public double QueenOffset;          // Queen Post bent: queen post INNER face at Span/2 - QueenOffset (from center)
        public bool   HasQueen, HasUpperGirt;// Queen Post bent: member gates (struts reuse HasStrut/StrutAngle)
        public double HBeamW, HBeamD;       // Hammer Beam bent: hammer beam section
        public double HPostW, HPostD;       // Hammer Beam bent: hammer post section
        public double CollarW, CollarD;     // Hammer Beam bent: collar (HBGirt) section
        public int    HBDivisor;            // Hammer Beam bent: 4 = single tier (6 = stacked, later)
        public bool   HasHBeam, HasHPost, HasCollar; // Hammer Beam bent: member gates
        public double FloorGirtW, FloorGirtD, FloorGirtHt; // shared floor girt: section + TOP height
        public bool   HasFloorGirt, HasFloorBrace;  // shared floor girt gates
        // Floor girt braces carry their OWN size/legs (from the FloorBrace:A leaf); zero falls back
        // to the bent girt brace values in the builder -- they used to be hard-linked (Robert's bug).
        public double FloorBraceW, FloorBraceD, FloorBraceFoot, FloorBraceHead;
        // Sill at grade (floor systems phase 3): body Y = 0..SillD (the underside IS the foundation
        // interface, so EaveHt doesn't move); posts shorten to start at the sill top. W = thickness
        // along the building (matches the posts), D = vertical depth.
        public double SillW, SillD;
        public bool   HasSill;
        public double PostBaseY => HasSill ? SillD : 0.0;   // where the posts start
        public double GirtDrop;             // tie elevation: tie TOP sits GirtDrop below the rafter-foot line (TOH), min 6"
        public bool   Make3D;

        // Per-member centering by "Role:Designation" key: 0 Back / 1 Center / 2 Front, already resolved
        // (a member's Justify.Default -> OffsetType). PlaceOf falls back to OffsetType for missing keys.
        public Dictionary<string, int> Place = new Dictionary<string, int>();
        public int PlaceOf(string key) => Place.TryGetValue(key, out int v) ? v : OffsetType;

        public double Beta        => Math.Atan(Pitch);
        public double PlumbLength => RafterD / Math.Cos(Beta);
        public double TOH         => (EaveHt + PostD * Pitch) - PlumbLength;
        // Tie top sits GirtDrop below the rafter-foot line; 6" is the MINIMUM (the heel/birdsmouth +
        // king-post bearing need that clearance). Max(6, ..) also makes an unset GirtDrop (0) safe.
        public double TOG         => TOH - Math.Max(6.0, GirtDrop);
        public double BOG         => TOG - GirtD;
    }

    // Builds the node/edge graph for a King Post FRAME -- a schedule of identical bents
    // at explicit Z (cumulative bay widths), tied together by longitudinal bay members.
    //
    // Each bent's members are the convex intersection of their half-planes (the four
    // shared "rafter lines" cut the post/king-post tops). Bay members run ALONG Z between
    // corresponding nodes of adjacent bents; their length is that bay's width, so variable
    // bay spacing falls out for free.
    public static partial class KingPostBentGraph
    {
        // Connector node ids a bay member uses to tie one bent to the next.
        private struct BentConnectors { public int Apex, PostTopL, PostTopR; }

        public static FrameGraph Build(KPBentParams p)
        {
            var g = new FrameGraph();
            double[] bays = p.BaySpacings ?? new double[0];

            // Bents placed face-to-same-face apart by each bay's spacing (center-to-center).
            var conns  = new List<BentConnectors>();
            var bentZs = new List<double>();
            double z = 0;
            for (int i = 0; i <= bays.Length; i++)   // bays.Length + 1 bents
            {
                bentZs.Add(z);
                int start = g.Edges.Count;
                conns.Add(BuildBent(g, p, z));
                TagEdges(g, start, "" + (i + 1), "");   // bent members -> bent "1".."N"
                if (i < bays.Length) z += bays[i];
            }

            // Per bay: longitudinal bay members, plus the roof mode (commons XOR purlins).
            for (int i = 0; i + 1 < conns.Count; i++)
            {
                int start = g.Edges.Count;
                AddBayMembers(g, p, conns[i], conns[i + 1]);
                AddFloorBayMembers(g, p, bentZs[i], bentZs[i + 1]);
                if (p.UseCommons) AddCommonRafters(g, p, bentZs[i], bentZs[i + 1]);
                else              AddPurlins(g, p, bentZs[i], bentZs[i + 1]);
                TagEdges(g, start, "", Roman(i + 1));   // longitudinal/bay members -> bay "I".."N" (bent left blank)
            }

            return g;
        }

        // Builds the graph from the explicit-instance FrameSpec (the tree-editor source of truth).
        // TWO AXES: an ordered list of BENTS (each at its cumulative graph-Z, spaced by its own
        // Separation) and the longitudinal WALLS (A left / B right), each holding a parallel list of
        // bay cells -- one per inter-bent interval. A bay cell is single-sided; for each interval the
        // two aligned wall-bays are MERGED into a legacy L/R bay that the existing side-gated bay
        // builders consume unchanged (Wall A -> :L + ridge/roof, Wall B -> :R).
        public static FrameGraph Build(FrameSpec spec)
        {
            var g = new FrameGraph();
            if (spec == null || spec.Bents.Count == 0) return g;
            WallSpec wa = spec.WallLeft();
            WallSpec wb = spec.WallRight();
            WallSpec wc = spec.WallCenter();   // the C line that owns the ridge (null on legacy frames)

            // Pass A: place each bent at its cumulative Z; tag its edges with the Arabic bent number.
            var conns = new List<BentConnectors>(spec.Bents.Count);
            var bentZs = new List<double>(spec.Bents.Count);
            double z = 0;
            for (int i = 0; i < spec.Bents.Count; i++)
            {
                BentSpec b = spec.Bents[i];
                bentZs.Add(z);
                // A Free Assembly bent emits no parametric geometry; record its position (for numbering
                // + the temp grid line) but add a null connector so the bay pass skips its intervals.
                if (b.IsFreeAssembly) { conns.Add(default); z += b.Separation; continue; }
                int start = g.Edges.Count;
                conns.Add(BuildBentByType(g, ToBentParams(spec, b), z, BentEnabled(b), b.BentType));
                TagEdges(g, start, "" + (i + 1), "");
                z += b.Separation;
            }

            // Pass B: one bay per gap between consecutive bents. Merge the two aligned wall-bays and
            // draw the longitudinal/roof members; tag with the Roman bay numeral (wall letter is
            // derived by side in the emitter -- explicit wall tagging is a later stage).
            for (int i = 0; i + 1 < spec.Bents.Count; i++)
            {
                // Skip any interval bounded by a Free Assembly bent -- the parametric girts/braces
                // have no postless bent to span to; that bay is hand-built (free-assembly).
                if (spec.Bents[i].IsFreeAssembly || spec.Bents[i + 1].IsFreeAssembly) continue;
                BentConnectors nearC = conns[i], farC = conns[i + 1];
                BaySpec aBay = (wa != null && i < wa.Bays.Count) ? wa.Bays[i] : null;
                BaySpec bBay = (wb != null && i < wb.Bays.Count) ? wb.Bays[i] : null;
                BaySpec cBay = (wc != null && i < wc.Bays.Count) ? wc.Bays[i] : null;
                BaySpec y = BaySpec.Merge(aBay, bBay, cBay);   // ridge from the C line (cBay)

                int start = g.Edges.Count;
                KPBentParams bp = ToBayParams(spec, y);
                // Per-side girt TOP elevations (each eave/floor girt carries its own Ht; 0/unset falls back
                // to the reveal / bent floor-girt height so a bay stays at the default).
                double EaveTopHt(string key)  { MemberSize sz = y.SizeOf(key); return sz != null && sz.Ht > 0 ? sz.Ht : spec.EaveHt + 1.0; }
                double FloorHt(string key)    { MemberSize sz = y.SizeOf(key); return sz != null && sz.Ht > 0 ? sz.Ht : bp.FloorGirtHt; }
                double leftEaveHt = EaveTopHt("EaveGirt:L"), rightEaveHt = EaveTopHt("EaveGirt:R");
                AddBayMembers(g, bp, nearC, farC,
                    y.IsEnabled("Ridge:R"), y.IsEnabled("EaveGirt:L"), y.IsEnabled("EaveGirt:R"),
                    leftEaveHt, rightEaveHt);
                AddFloorBayMembers(g, bp, bentZs[i], bentZs[i + 1],
                    y.IsEnabled("FloorGirt:L"), y.IsEnabled("FloorGirt:R"),
                    FloorHt("FloorGirt:L"), FloorHt("FloorGirt:R"));
                AddSillBayMembers(g, bp, bentZs[i], bentZs[i + 1],
                    y.IsEnabled("Sill:L"), y.IsEnabled("Sill:R"));
                AddBayBraces(g, bp, y, bentZs[i], bentZs[i + 1]);
                // Per-side roof: each eave owns its slope's commons/purlins (left = Wall A's bay, right
                // = Wall E's), so the two slopes can differ. Common rafters bed on that side's eave girt.
                if (aBay != null && aBay.IsEnabled("Commons:X"))
                    AddCommonRaftersSide(g, bp, bentZs[i], bentZs[i + 1], true,  aBay.CommonMode, aBay.CommonCount, aBay.CommonSpacing, aBay.CommonW, aBay.CommonD, leftEaveHt,  aBay.CommonTail, aBay.CommonTailCut);
                if (bBay != null && bBay.IsEnabled("Commons:X"))
                    AddCommonRaftersSide(g, bp, bentZs[i], bentZs[i + 1], false, bBay.CommonMode, bBay.CommonCount, bBay.CommonSpacing, bBay.CommonW, bBay.CommonD, rightEaveHt, bBay.CommonTail, bBay.CommonTailCut);
                if (aBay != null && aBay.IsEnabled("Purlins:X"))
                    AddPurlinsSide(g, bp, bentZs[i], bentZs[i + 1], true,  aBay.PurlinMode, aBay.PurlinCount, aBay.PurlinSpacing, aBay.PurlinW, aBay.PurlinD);
                if (bBay != null && bBay.IsEnabled("Purlins:X"))
                    AddPurlinsSide(g, bp, bentZs[i], bentZs[i + 1], false, bBay.PurlinMode, bBay.PurlinCount, bBay.PurlinSpacing, bBay.PurlinW, bBay.PurlinD);
                // Ridge -> king-post braces (opt-in), owned by the center line (cBay).
                AddRidgeBraces(g, bp, cBay, bentZs[i], bentZs[i + 1]);
                // Queen / hammer girt + braces (opt-in), owned by the interior lines: each emits at its
                // own side (left = before the center line, right = after), from its bay's config. A
                // hammer line also carries its TIER = its order in from the eave (1, 2..), so divisor-6's
                // two stacked tiers (A-G) land on the right hammer posts.
                int centerIdx = wc != null ? spec.Walls.IndexOf(wc) : -1;
                int leftEaveIdx  = wa != null ? spec.Walls.IndexOf(wa) : 0;
                int rightEaveIdx = wb != null ? spec.Walls.IndexOf(wb) : spec.Walls.Count - 1;
                for (int wi = 0; wi < spec.Walls.Count; wi++)
                {
                    WallSpec iw = spec.Walls[wi];
                    if ((iw.Role != BayRole.Queen && iw.Role != BayRole.Hammer) || i >= iw.Bays.Count) continue;
                    bool left = centerIdx < 0 || wi < centerIdx;
                    if (iw.Role == BayRole.Queen)
                        AddQueenGirt(g, bp, iw.Bays[i], left, bentZs[i], bentZs[i + 1]);
                    else
                    {
                        int tier = left ? wi - leftEaveIdx : rightEaveIdx - wi;   // 1 = nearest the eave
                        AddHammerGirt(g, bp, iw.Bays[i], left, tier, bentZs[i], bentZs[i + 1]);
                    }
                }
                TagEdges(g, start, "", Roman(i + 1));
            }

            return g;
        }

        // Stamp every edge appended at or after `start` with the given bent / bay tags (only the
        // non-empty tag is written, so a bent pass leaves BayTag alone and vice versa). This is the
        // grouping layer's "tag by range" trick -- no need to thread identity through every builder.
        private static void TagEdges(FrameGraph g, int start, string bentTag, string bayTag)
        {
            for (int i = start; i < g.Edges.Count; i++)
            {
                if (bentTag.Length > 0) g.Edges[i].BentTag = bentTag;
                if (bayTag.Length  > 0) g.Edges[i].BayTag  = bayTag;
            }
        }

        // Roman numeral for a bay index (1->I, 2->II, ...). Bays rarely exceed a handful, but the
        // full subtractive table keeps it correct for any count.
        private static string Roman(int n)
        {
            if (n <= 0) return "";
            int[] v = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            string[] s = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < v.Length && n > 0; i++)
                while (n >= v[i]) { sb.Append(s[i]); n -= v[i]; }
            return sb.ToString();
        }

        // Synthesize a per-bent KPBentParams. Optional members' Has-flags follow membership so
        // the BuildBent gates (Has* AND On(key)) align with the explicit timber set.
        // Dispatch to the bent builder for this BentType. All return BentConnectors, so the
        // bay/roof pass is type-agnostic.
        private static BentConnectors BuildBentByType(FrameGraph g, KPBentParams p, double bentZ,
            Func<string, bool> enabled, string bentType)
        {
            switch (bentType)
            {
                case "QueenPost":      return BuildQueenBent(g, p, bentZ, enabled);
                case "HammerBeam":     return BuildHammerBent(g, p, bentZ, enabled);
                case "KingPostTruss":  return BuildKingPostTrussBent(g, p, bentZ, enabled);
                case "QueenPostTruss": return BuildQueenPostTrussBent(g, p, bentZ, enabled);
                default:               return BuildBent(g, p, bentZ, enabled);
            }
        }

        // Membership gate for the generator's existing combined-key blocks: a combined key is on
        // if EITHER split L/R timber is enabled (Stage 1 -- both sides still draw together). Split
        // keys (Post:A, etc.) pass straight through. (Stage 2 will gate each side separately.)
        private static Func<string, bool> BentEnabled(BentSpec b) => key =>
        {
            switch (key)
            {
                case "Brace:AB":       return b.IsEnabled("Brace:A") || b.IsEnabled("Brace:E");
                case "Strut:S":        return b.IsEnabled("Strut:A") || b.IsEnabled("Strut:E");
                case "VStrut:V":       return b.IsEnabled("VStrut:A") || b.IsEnabled("VStrut:E");
                case "QueenBrace:BD":  return b.IsEnabled("QueenBrace:B") || b.IsEnabled("QueenBrace:D");
                case "CollarBrace:AE": return b.IsEnabled("CollarBrace:A") || b.IsEnabled("CollarBrace:E");
                case "FloorBrace:FB":  return b.IsEnabled("FloorBrace:A") || b.IsEnabled("FloorBrace:E");
                default:               return b.IsEnabled(key);
            }
        };

        // Synthesize a per-bent KPBentParams from the universal timbers. Each section reads its
        // own timber's MemberSize (representative LEFT side -- both sides are equal by default in
        // Stage 1); missing timbers fall back to settings so trusses (no wall posts) still skeleton.
        private static KPBentParams ToBentParams(FrameSpec s, BentSpec b)
        {
            KPBentParams d = Commands.ReadKPParams();
            MemberSize post   = b.SizeOf("Post:A");
            MemberSize girt   = b.SizeOf("Girt:AE");
            MemberSize rafter = b.SizeOf("Rafter:A");
            MemberSize kpost  = b.SizeOf("KingPost:C");
            MemberSize brace  = b.SizeOf("Brace:A") ?? b.SizeOf("QueenBrace:B")
                                 ?? b.SizeOf("CollarBrace:A") ?? b.SizeOf("FloorBrace:A");
            MemberSize strut  = b.SizeOf("Strut:A");
            MemberSize vstrut = b.SizeOf("VStrut:A");
            MemberSize queen  = b.SizeOf("Queen:B");
            MemberSize ugirt  = b.SizeOf("UpperGirt:BD");
            MemberSize hbeam  = b.SizeOf("HBeam:A");
            MemberSize hpost  = b.SizeOf("HPost:A");
            MemberSize collar = b.SizeOf("Collar:AE");
            MemberSize floor  = b.SizeOf("FloorGirt:FG");
            MemberSize fbrace = b.SizeOf("FloorBrace:A");
            MemberSize sill   = b.SizeOf("Sill:SL");

            return new KPBentParams
            {
                Span = s.Span, EaveHt = s.EaveHt, Pitch = s.Pitch, Make3D = s.Make3D, OffsetType = b.OffsetType,
                Place = BuildPlace(b.Timbers, b.OffsetType),
                PostW = post?.W ?? d.PostW, PostD = post?.D ?? d.PostD,
                GirtW = girt?.W ?? d.GirtW, GirtD = girt?.D ?? d.GirtD,
                RafterW = rafter?.W ?? d.RafterW, RafterD = rafter?.D ?? d.RafterD,
                KpostW = kpost?.W ?? d.KpostW, KpostD = kpost?.D ?? d.KpostD,
                HasBrace = b.IsEnabled("Brace:A") || b.IsEnabled("Brace:E")
                           || b.IsEnabled("QueenBrace:B") || b.IsEnabled("QueenBrace:D")
                           || b.IsEnabled("CollarBrace:A") || b.IsEnabled("CollarBrace:E"),
                BraceW = brace?.W ?? d.BraceW, BraceD = brace?.D ?? d.BraceD,
                BraceLength = brace?.Length ?? d.BraceLength, BraceAngle = brace?.Angle ?? d.BraceAngle,
                BraceFoot = (brace != null && brace.Foot > 0) ? brace.Foot
                            : (brace?.Length ?? d.BraceLength) * Math.Tan((brace?.Angle ?? d.BraceAngle) * Math.PI / 180.0),
                BraceHead = (brace != null && brace.Head > 0) ? brace.Head : (brace?.Length ?? d.BraceLength),
                HasStrut = b.IsEnabled("Strut:A") || b.IsEnabled("Strut:E"),
                StrutW = strut?.W ?? d.StrutW, StrutD = strut?.D ?? d.StrutD, StrutAngle = strut?.Angle ?? d.StrutAngle,
                HasVStrut = b.IsEnabled("VStrut:A") || b.IsEnabled("VStrut:E"),
                VStrutW = vstrut?.W ?? d.VStrutW, VStrutD = vstrut?.D ?? d.VStrutD,
                HasQueen = b.IsEnabled("Queen:B") || b.IsEnabled("Queen:D"),
                HasUpperGirt = b.IsEnabled("UpperGirt:BD"),
                QueenW = queen?.W ?? d.PostW, QueenD = queen?.D ?? d.PostD,
                UpperGirtW = ugirt?.W ?? d.GirtW, UpperGirtD = ugirt?.D ?? d.GirtD,
                QueenOffset = b.QueenOffset,
                HasHBeam = b.IsEnabled("HBeam:A") || b.IsEnabled("HBeam:E"),
                HasHPost = b.IsEnabled("HPost:A") || b.IsEnabled("HPost:E"),
                HasCollar = b.IsEnabled("Collar:AE"),
                HBeamW = hbeam?.W ?? d.GirtW, HBeamD = hbeam?.D ?? d.GirtD,
                HPostW = hpost?.W ?? d.PostW, HPostD = hpost?.D ?? d.PostD,
                CollarW = collar?.W ?? d.GirtW, CollarD = collar?.D ?? d.GirtD, HBDivisor = b.HBDivisor,
                HasFloorGirt = b.IsEnabled("FloorGirt:FG"),
                HasFloorBrace = b.IsEnabled("FloorBrace:A") || b.IsEnabled("FloorBrace:E"),
                // The floor braces' OWN spec (zero -> the builder falls back to the girt brace).
                FloorBraceW = fbrace?.W ?? 0, FloorBraceD = fbrace?.D ?? 0,
                FloorBraceFoot = fbrace?.Foot ?? 0, FloorBraceHead = fbrace?.Head ?? 0,
                FloorGirtW = floor?.W ?? d.GirtW, FloorGirtD = floor?.D ?? d.GirtD,
                FloorGirtHt = floor?.Ht ?? d.FloorGirtHt,
                HasSill = b.IsEnabled("Sill:SL"),
                SillW = sill?.W ?? post?.W ?? d.PostW, SillD = sill?.D ?? post?.W ?? d.PostW,
                GirtDrop = b.GirtDrop                 // tie elevation (>=6"); drives TOG/BOG + everything on them
            };
        }

        // Synthesize a per-bay KPBentParams: frame geometry + this bay's ridge/eave/floor/roof
        // sizes (from the bay's own timbers; insets/floor height from the first bent).
        private static KPBentParams ToBayParams(FrameSpec s, BaySpec y)
        {
            KPBentParams d = Commands.ReadKPParams();
            BentSpec rep = s.FirstBent();
            MemberSize repPost  = rep?.SizeOf("Post:A");
            MemberSize repFloor = rep?.SizeOf("FloorGirt:FG");
            MemberSize repKpost = rep?.SizeOf("KingPost:C");   // for the ridge -> king-post brace seat
            MemberSize repRafter = rep?.SizeOf("Rafter:A");    // RafterD drives PlumbLength (girt heights)
            MemberSize repQueen = rep?.SizeOf("Queen:B");      // queen line position + girt brace seat
            MemberSize repHPost = rep?.SizeOf("HPost:A");      // hammer line position + girt brace seat
            MemberSize eave  = y.SizeOf("EaveGirt:L");
            MemberSize floor = y.SizeOf("FloorGirt:L");
            MemberSize ridge = y.SizeOf("Ridge:R");
            MemberSize sill  = y.SizeOf("Sill:L") ?? y.SizeOf("Sill:R") ?? rep?.SizeOf("Sill:SL");

            return new KPBentParams
            {
                Span = s.Span, EaveHt = s.EaveHt, Pitch = s.Pitch, Make3D = s.Make3D, OffsetType = y.OffsetType,
                Place = BuildPlace(y.Timbers, y.OffsetType),
                PostW = repPost?.W ?? d.PostW, PostD = repPost?.D ?? d.PostD,
                KpostW = repKpost?.W ?? d.KpostW, KpostD = repKpost?.D ?? d.KpostD,
                RafterW = repRafter?.W ?? d.RafterW, RafterD = repRafter?.D ?? d.RafterD,
                QueenW = repQueen?.W ?? d.QueenW, QueenD = repQueen?.D ?? d.QueenD,
                QueenOffset = rep?.QueenOffset ?? d.QueenOffset,
                HPostW = repHPost?.W ?? d.HPostW, HPostD = repHPost?.D ?? d.HPostD,
                HBDivisor = rep?.HBDivisor ?? d.HBDivisor,
                GirtDrop = rep?.GirtDrop ?? d.GirtDrop,   // keep the TOG/BOG datum consistent with the bent

                GirtW = eave?.W ?? d.GirtW, GirtD = eave?.D ?? d.GirtD,
                FloorGirtW = floor?.W ?? d.GirtW, FloorGirtD = floor?.D ?? d.GirtD,
                FloorGirtHt = repFloor?.Ht ?? d.FloorGirtHt,
                SillW = sill?.W ?? repPost?.W ?? d.PostW, SillD = sill?.D ?? repPost?.W ?? d.PostW,
                RidgeW = ridge?.W ?? d.RidgeW, RidgeD = ridge?.D ?? d.RidgeD,
                CommonMode = y.CommonMode, CommonCount = y.CommonCount, CommonSpacing = y.CommonSpacing,
                CommonW = y.CommonW, CommonD = y.CommonD,
                PurlinMode = y.PurlinMode, PurlinCount = y.PurlinCount, PurlinSpacing = y.PurlinSpacing,
                PurlinW = y.PurlinW, PurlinD = y.PurlinD
            };
        }

        // Builds one King Post bent into g with all nodes at building-length position bentZ.
        // Returns the connector node ids used by bay members. `enabled` (optional) gates
        // members by "Role:Designation" key (null = all enabled). Node creation and geometry
        // math run unconditionally, so connectors are always returned even with members off.
        private static BentConnectors BuildBent(FrameGraph g, KPBentParams p, double bentZ,
            Func<string, bool> enabled = null)
        {
            bool On(string key) => enabled == null || enabled(key);
            double hs     = p.Span / 2.0;
            double xPeakL = hs - p.KpostD / 2.0;
            double xPeakR = hs + p.KpostD / 2.0;
            double yTopFoot = p.EaveHt + p.PostD * p.Pitch;
            double yTopPeak = p.EaveHt + (hs - p.KpostD / 2.0) * p.Pitch;
            double apexY    = p.EaveHt + hs * p.Pitch;

            // Shared rafter face lines (XY; point + direction). Z is irrelevant to the
            // half-plane clip (it operates on X/Y), so these stay at Z = 0.
            Point3d  ltP = new Point3d(0, p.EaveHt, 0);                 Vector3d ltD = new Vector3d(1,  p.Pitch, 0);
            Point3d  lbP = new Point3d(0, p.EaveHt - p.PlumbLength, 0); Vector3d lbD = ltD;
            Point3d  rtP = new Point3d(p.Span, p.EaveHt, 0);            Vector3d rtD = new Vector3d(1, -p.Pitch, 0);
            Point3d  rbP = new Point3d(p.Span, p.EaveHt - p.PlumbLength, 0); Vector3d rbD = rtD;

            // Nodes carry Z = bentZ (their position along the building length).
            int postBaseL = g.AddNode("PostBaseL", new Point3d(0, 0, bentZ));
            int postTopL  = g.AddNode("PostTopL",  new Point3d(0, p.EaveHt, bentZ));
            int postBaseR = g.AddNode("PostBaseR", new Point3d(p.Span - p.PostD, 0, bentZ));
            int postTopR  = g.AddNode("PostTopR",  new Point3d(p.Span - p.PostD, p.EaveHt, bentZ));
            int girtL     = g.AddNode("GirtL", new Point3d(p.PostD, p.BOG, bentZ));
            int girtR     = g.AddNode("GirtR", new Point3d(p.Span - p.PostD, p.BOG, bentZ));
            int eaveL     = g.AddNode("EaveL", new Point3d(p.PostD, yTopFoot, bentZ));
            int peakL     = g.AddNode("PeakL", new Point3d(xPeakL, yTopPeak, bentZ));
            int eaveR     = g.AddNode("EaveR", new Point3d(p.Span - p.PostD, yTopFoot, bentZ));
            int peakR     = g.AddNode("PeakR", new Point3d(xPeakR, yTopPeak, bentZ));
            int kpostBase = g.AddNode("KPostBase", new Point3d(xPeakL, p.TOG, bentZ));
            int apex      = g.AddNode("Apex",      new Point3d(hs, apexY, bentZ));

            // Left post: between x=0 and x=PostD, base at y=0 (or the sill top when the bent carries
            // a sill -- the post shortens and tenons down into it), top cut by left rafter top line.
            if (On("Post:A"))
            g.AddEdge("Post", postBaseL, postTopL, p.PostW, p.PostD, "A").Planes.AddRange(new[]
            {
                HalfPlane.KeepRightOfX(0), HalfPlane.KeepLeftOfX(p.PostD),
                HalfPlane.KeepAboveY(p.PostBaseY), HalfPlane.KeepBelowLine(ltP, ltD)
            });

            // Right post: mirror, top cut by right rafter top line.
            if (On("Post:E"))
            g.AddEdge("Post", postBaseR, postTopR, p.PostW, p.PostD, "E").Planes.AddRange(new[]
            {
                HalfPlane.KeepRightOfX(p.Span - p.PostD), HalfPlane.KeepLeftOfX(p.Span),
                HalfPlane.KeepAboveY(p.PostBaseY),        HalfPlane.KeepBelowLine(rtP, rtD)
            });

            // Girt: rectangle between post inner faces at girt height.
            if (On("Girt:AE"))
            g.AddEdge("Girt", girtL, girtR, p.GirtW, p.GirtD, "AE").Planes.AddRange(new[]
            {
                HalfPlane.KeepRightOfX(p.PostD), HalfPlane.KeepLeftOfX(p.Span - p.PostD),
                HalfPlane.KeepAboveY(p.BOG),     HalfPlane.KeepBelowY(p.BOG + p.GirtD)
            });

            // Left rafter: between top and bottom lines, foot (x=PostD) to peak (x=xPeakL).
            if (On("Rafter:A"))
            g.AddEdge("Rafter", eaveL, peakL, p.RafterW, p.RafterD, "A").Planes.AddRange(new[]
            {
                HalfPlane.KeepBelowLine(ltP, ltD), HalfPlane.KeepAboveLine(lbP, lbD),
                HalfPlane.KeepRightOfX(p.PostD),   HalfPlane.KeepLeftOfX(xPeakL)
            });

            // Right rafter: peak (x=xPeakR) to foot (x=Span-PostD).
            if (On("Rafter:E"))
            g.AddEdge("Rafter", eaveR, peakR, p.RafterW, p.RafterD, "E").Planes.AddRange(new[]
            {
                HalfPlane.KeepBelowLine(rtP, rtD), HalfPlane.KeepAboveLine(rbP, rbD),
                HalfPlane.KeepRightOfX(xPeakR),    HalfPlane.KeepLeftOfX(p.Span - p.PostD)
            });

            // King post: pentagon -- sides at kingpost faces, base at TOG, top cut by
            // BOTH rafter top lines (their intersection is the ridge apex).
            if (On("KingPost:C"))
            g.AddEdge("KingPost", kpostBase, apex, p.KpostW, p.KpostD, "C").Planes.AddRange(new[]
            {
                HalfPlane.KeepRightOfX(xPeakL), HalfPlane.KeepLeftOfX(xPeakR),
                HalfPlane.KeepAboveY(p.TOG),
                HalfPlane.KeepBelowLine(ltP, ltD), HalfPlane.KeepBelowLine(rtP, rtD)
            });

            // Knee braces: post inner face -> girt bottom. Foot = leg DOWN the post (vertical);
            // Head = leg ALONG the girt (horizontal). Body INSIDE the triangle via OffsetToward.
            if (p.HasBrace && p.BraceFoot > 0 && p.BraceHead > 0 && On("Brace:AB"))
            {
                double foot = p.BraceFoot;   // down the post (vertical)
                double head = p.BraceHead;   // along the girt (horizontal)

                Point3d bla = new Point3d(p.PostD, p.BOG - foot, 0);  // post face (down by foot)
                Point3d blb = new Point3d(p.PostD + head, p.BOG, 0);  // girt bottom (along by head)
                Point3d cL  = new Point3d(p.PostD, p.BOG, 0);
                int blaN = g.AddNode("BraceLPost", new Point3d(bla.X, bla.Y, bentZ));
                int blbN = g.AddNode("BraceLGirt", new Point3d(blb.X, blb.Y, bentZ));
                FrameEdge bl = g.AddEdge("Brace", blaN, blbN, p.BraceW, p.BraceD, "AB");
                bl.Planes.Add(HalfPlane.KeepRightOfX(p.PostD));
                bl.Planes.Add(HalfPlane.KeepBelowY(p.BOG));
                AddLongFacesOneSided(bl, bla, blb, p.BraceD, OffsetToward(bla, blb, cL));  // body INSIDE the triangle
                bl.ZOffset = ZOffsetFor(Math.Min(p.PostW, p.GirtW), p.BraceW, p.PlaceOf("Brace:A"));

                Point3d bra = new Point3d(p.Span - p.PostD, p.BOG - foot, 0);
                Point3d brb = new Point3d(p.Span - p.PostD - head, p.BOG, 0);
                Point3d cR  = new Point3d(p.Span - p.PostD, p.BOG, 0);
                int braN = g.AddNode("BraceRPost", new Point3d(bra.X, bra.Y, bentZ));
                int brbN = g.AddNode("BraceRGirt", new Point3d(brb.X, brb.Y, bentZ));
                FrameEdge br = g.AddEdge("Brace", braN, brbN, p.BraceW, p.BraceD, "AB");
                br.Planes.Add(HalfPlane.KeepLeftOfX(p.Span - p.PostD));
                br.Planes.Add(HalfPlane.KeepBelowY(p.BOG));
                AddLongFacesOneSided(br, bra, brb, p.BraceD, OffsetToward(bra, brb, cR));
                br.ZOffset = ZOffsetFor(Math.Min(p.PostW, p.GirtW), p.BraceW, p.PlaceOf("Brace:E"));
            }

            // Struts (king post -> rafter, starting TOG+6) + vertical struts at the
            // strut/rafter intersection. StrutAngle (default 45) sets the lean.
            double theta = p.StrutAngle * Math.PI / 180.0;
            double ct = Math.Cos(theta), st = Math.Sin(theta);

            Point3d  asL = new Point3d(xPeakL, p.TOG + 6.0, 0);   // left  king-post face
            Point3d  asR = new Point3d(xPeakR, p.TOG + 6.0, 0);   // right king-post face
            Point3d  hitL = IntersectRayLine(asL, new Vector3d(-ct, st, 0), lbP, ltD);
            Point3d  hitR = IntersectRayLine(asR, new Vector3d( ct, st, 0), rbP, rtD);

            if (p.HasStrut && p.StrutD > 0 && On("Strut:S"))
            {
                int aL = g.AddNode("StrutLKpost",  new Point3d(asL.X, asL.Y, bentZ));
                int bL = g.AddNode("StrutLRafter", new Point3d(hitL.X, hitL.Y, bentZ));
                FrameEdge sL = g.AddEdge("Strut", aL, bL, p.StrutW, p.StrutD, "S");
                sL.Planes.Add(HalfPlane.KeepLeftOfX(xPeakL));      // king-post face
                sL.Planes.Add(HalfPlane.KeepBelowLine(lbP, ltD));  // rafter underside
                AddLongFacesOneSided(sL, asL, hitL, p.StrutD, PerpUp(asL.GetVectorTo(hitL)));
                sL.ZOffset = ZOffsetFor(Math.Min(p.KpostW, p.RafterW), p.StrutW, p.PlaceOf("Strut:A"));

                int aR = g.AddNode("StrutRKpost",  new Point3d(asR.X, asR.Y, bentZ));
                int bR = g.AddNode("StrutRRafter", new Point3d(hitR.X, hitR.Y, bentZ));
                FrameEdge sR = g.AddEdge("Strut", aR, bR, p.StrutW, p.StrutD, "S");
                sR.Planes.Add(HalfPlane.KeepRightOfX(xPeakR));     // king-post face (right)
                sR.Planes.Add(HalfPlane.KeepBelowLine(rbP, rtD));  // rafter underside (right)
                AddLongFacesOneSided(sR, asR, hitR, p.StrutD, PerpUp(asR.GetVectorTo(hitR)));
                sR.ZOffset = ZOffsetFor(Math.Min(p.KpostW, p.RafterW), p.StrutW, p.PlaceOf("Strut:E"));
            }

            if (p.HasVStrut && p.VStrutD > 0 && On("VStrut:V"))
            {
                Point3d vbL = new Point3d(hitL.X, p.TOG, 0);
                int vaL = g.AddNode("VStrutLGirt",   new Point3d(vbL.X, vbL.Y, bentZ));
                int vtL = g.AddNode("VStrutLRafter", new Point3d(hitL.X, hitL.Y, bentZ));
                FrameEdge vL = g.AddEdge("VStrut", vaL, vtL, p.VStrutW, p.VStrutD, "V");
                vL.Planes.Add(HalfPlane.KeepAboveY(p.TOG));         // girt top
                vL.Planes.Add(HalfPlane.KeepBelowLine(lbP, ltD));   // rafter underside
                AddLongFacesOneSided(vL, vbL, hitL, p.VStrutD, new Vector3d(hitL.X < hs ? -1 : 1, 0, 0));
                vL.ZOffset = ZOffsetFor(Math.Min(p.GirtW, p.RafterW), p.VStrutW, p.PlaceOf("VStrut:A"));

                Point3d vbR = new Point3d(hitR.X, p.TOG, 0);
                int vaR = g.AddNode("VStrutRGirt",   new Point3d(vbR.X, vbR.Y, bentZ));
                int vtR = g.AddNode("VStrutRRafter", new Point3d(hitR.X, hitR.Y, bentZ));
                FrameEdge vR = g.AddEdge("VStrut", vaR, vtR, p.VStrutW, p.VStrutD, "V");
                vR.Planes.Add(HalfPlane.KeepAboveY(p.TOG));
                vR.Planes.Add(HalfPlane.KeepBelowLine(rbP, rtD));
                AddLongFacesOneSided(vR, vbR, hitR, p.VStrutD, new Vector3d(hitR.X < hs ? -1 : 1, 0, 0));
                vR.ZOffset = ZOffsetFor(Math.Min(p.GirtW, p.RafterW), p.VStrutW, p.PlaceOf("VStrut:E"));
            }

            AddFloorGirt(g, p, bentZ, enabled);
            AddSill(g, p, bentZ, enabled);
            return new BentConnectors { Apex = apex, PostTopL = postTopL, PostTopR = postTopR };
        }

        // Longitudinal bay members tying bent `ca` to the next bent `cb`.
        // Ridge along the peak; eave plates along both wall tops. Each member fills the
        // CLEAR gap between bent faces: length = center-to-center spacing - bent thickness
        // (inset = PostW at the near bent).
        private static void AddBayMembers(FrameGraph g, KPBentParams p,
            BentConnectors ca, BentConnectors cb)
            => AddBayMembers(g, p, ca, cb, true, true, true, p.EaveHt + 1.0, p.EaveHt + 1.0);

        // `leftEaveHt` / `rightEaveHt` are the per-side eave-girt TOP elevations (each eave girt carries
        // its own Height; default = EaveHt + 1, the reveal). The legacy wrapper passes the reveal for both.
        private static void AddBayMembers(FrameGraph g, KPBentParams p,
            BentConnectors ca, BentConnectors cb, bool ridge, bool eaveL, bool eaveR,
            double leftEaveHt, double rightEaveHt)
        {
            // Ridge: own section (RidgeW x RidgeD), chamfered cross-section (top corners cut
            // by the rafter planes). Its faces (hs +/- RidgeW/2) are where the commons land.
            if (ridge)
            {
                FrameEdge ridgeE = MakeLongitudinal(g, "Ridge", ca.Apex, cb.Apex, p.RidgeW, p.RidgeD, p.PostW, "R");
                AddRidgeCrossSection(ridgeE, p);
            }

            // Eave girts: single chamfer on the OUTSIDE face; outside face aligned with the post outside
            // face (x=0 left, x=Span right). Top = the side's eave-girt Height. L/R independent.
            if (eaveL)
            {
                FrameEdge egL = MakeLongitudinal(g, "EaveGirt", ca.PostTopL, cb.PostTopL, p.GirtW, p.GirtD, p.PostW, "EL");
                AddEaveGirtCrossSection(egL, p, true, leftEaveHt);
            }
            if (eaveR)
            {
                FrameEdge egR = MakeLongitudinal(g, "EaveGirt", ca.PostTopR, cb.PostTopR, p.GirtW, p.GirtD, p.PostW, "ER");
                AddEaveGirtCrossSection(egR, p, false, rightEaveHt);
            }
        }

        // Shared in-bent FLOOR GIRT (all bent types): a rectangle between the post inner faces
        // at FloorGirtHt, with knee braces down to each post. Bent-type-agnostic.
        private static void AddFloorGirt(FrameGraph g, KPBentParams p, double bentZ,
            Func<string, bool> enabled = null)
        {
            bool On(string key) => enabled == null || enabled(key);
            if (!p.HasFloorGirt) return;
            double top = p.FloorGirtHt;
            double bot = top - p.FloorGirtD;

            if (On("FloorGirt:FG"))
            g.AddEdge("Girt",
                g.AddNode("FloorGirtL", new Point3d(p.PostD, top, bentZ)),
                g.AddNode("FloorGirtR", new Point3d(p.Span - p.PostD, top, bentZ)),
                p.FloorGirtW, p.FloorGirtD, "FG").Planes.AddRange(new[]
            {
                HalfPlane.KeepRightOfX(p.PostD), HalfPlane.KeepLeftOfX(p.Span - p.PostD),
                HalfPlane.KeepAboveY(bot),       HalfPlane.KeepBelowY(top)
            });

            // Knee braces: post inner face -> floor-girt bottom (corner at `bot`). Body INSIDE the
            // triangle. The floor braces read their OWN leaf spec first; a zero falls back to the
            // bent girt brace value (the old hard link, now just the default).
            double fbFoot = p.FloorBraceFoot > 0 ? p.FloorBraceFoot : p.BraceFoot;
            double fbHead = p.FloorBraceHead > 0 ? p.FloorBraceHead : p.BraceHead;
            double fbW    = p.FloorBraceW    > 0 ? p.FloorBraceW    : p.BraceW;
            double fbD    = p.FloorBraceD    > 0 ? p.FloorBraceD    : p.BraceD;
            if (p.HasFloorBrace && fbFoot > 0 && fbHead > 0 && On("FloorBrace:FB"))
            {
                double foot = fbFoot;   // down the post (vertical)
                double head = fbHead;   // along the girt (horizontal)

                Point3d bla = new Point3d(p.PostD, bot - foot, 0);
                Point3d blb = new Point3d(p.PostD + head, bot, 0);
                Point3d cL  = new Point3d(p.PostD, bot, 0);
                FrameEdge bl = g.AddEdge("Brace",
                    g.AddNode("FBraceLPost", new Point3d(bla.X, bla.Y, bentZ)),
                    g.AddNode("FBraceLGirt", new Point3d(blb.X, blb.Y, bentZ)),
                    fbW, fbD, "FB");
                bl.Planes.Add(HalfPlane.KeepRightOfX(p.PostD));
                bl.Planes.Add(HalfPlane.KeepBelowY(bot));
                AddLongFacesOneSided(bl, bla, blb, fbD, OffsetToward(bla, blb, cL));
                bl.ZOffset = ZOffsetFor(Math.Min(p.PostW, p.FloorGirtW), fbW, p.PlaceOf("FloorBrace:A"));

                Point3d bra = new Point3d(p.Span - p.PostD, bot - foot, 0);
                Point3d brb = new Point3d(p.Span - p.PostD - head, bot, 0);
                Point3d cR  = new Point3d(p.Span - p.PostD, bot, 0);
                FrameEdge br = g.AddEdge("Brace",
                    g.AddNode("FBraceRPost", new Point3d(bra.X, bra.Y, bentZ)),
                    g.AddNode("FBraceRGirt", new Point3d(brb.X, brb.Y, bentZ)),
                    fbW, fbD, "FB");
                br.Planes.Add(HalfPlane.KeepLeftOfX(p.Span - p.PostD));
                br.Planes.Add(HalfPlane.KeepBelowY(bot));
                AddLongFacesOneSided(br, bra, brb, fbD, OffsetToward(bra, brb, cR));
                br.ZOffset = ZOffsetFor(Math.Min(p.PostW, p.FloorGirtW), fbW, p.PlaceOf("FloorBrace:E"));
            }
        }

        // Shared in-bent SILL (post-bearing bent types): a full-span rectangle at grade, Y = 0..SillD
        // (the underside IS the foundation interface), running post OUTER face to post OUTER face --
        // so every post foot bears on its bent's transverse sill (the mortise lands here). The posts
        // shorten to start at the sill top (KeepAboveY(PostBaseY) in each builder). Bent-type-agnostic.
        private static void AddSill(FrameGraph g, KPBentParams p, double bentZ,
            Func<string, bool> enabled = null)
        {
            bool On(string key) => enabled == null || enabled(key);
            if (!p.HasSill || !On("Sill:SL")) return;
            g.AddEdge("Sill",
                g.AddNode("SillL", new Point3d(0, p.SillD, bentZ)),
                g.AddNode("SillR", new Point3d(p.Span, p.SillD, bentZ)),
                p.SillW, p.SillD, "SL").Planes.AddRange(new[]
            {
                HalfPlane.KeepRightOfX(0), HalfPlane.KeepLeftOfX(p.Span),
                HalfPlane.KeepAboveY(0),   HalfPlane.KeepBelowY(p.SillD)
            });
        }

        // Bay-level SILLS (per eave wall): the floor-girt recipe at grade -- longitudinal members
        // running post face to post face along each wall (the standard LongInset), outside face
        // flush with the post outside, Y = 0..SillD. They butt the transverse sills' sides; the
        // post feet bear on the TRANSVERSE sills (corner laps are catalogued future work).
        private static void AddSillBayMembers(FrameGraph g, KPBentParams p, double zA, double zB,
            bool sillL, bool sillR)
        {
            if (sillL)
            {
                FrameEdge sl = MakeLongitudinal(g, "Sill",
                    g.AddNode("SillBayL", new Point3d(0, p.SillD, zA)),
                    g.AddNode("SillBayL", new Point3d(0, p.SillD, zB)),
                    p.SillW, p.SillD, p.PostW, "SL");
                sl.Planes.Add(HalfPlane.KeepRightOfX(0));
                sl.Planes.Add(HalfPlane.KeepLeftOfX(p.SillW));
                sl.Planes.Add(HalfPlane.KeepAboveY(0));
                sl.Planes.Add(HalfPlane.KeepBelowY(p.SillD));
            }

            if (sillR)
            {
                FrameEdge sr = MakeLongitudinal(g, "Sill",
                    g.AddNode("SillBayR", new Point3d(p.Span - p.SillW, p.SillD, zA)),
                    g.AddNode("SillBayR", new Point3d(p.Span - p.SillW, p.SillD, zB)),
                    p.SillW, p.SillD, p.PostW, "SR");
                sr.Planes.Add(HalfPlane.KeepRightOfX(p.Span - p.SillW));
                sr.Planes.Add(HalfPlane.KeepLeftOfX(p.Span));
                sr.Planes.Add(HalfPlane.KeepAboveY(0));
                sr.Planes.Add(HalfPlane.KeepBelowY(p.SillD));
            }
        }

        // Shared bay-level FLOOR GIRTS (all bent types): longitudinal members running bent to
        // bent along each wall at FloorGirtHt, outside face flush with the post outside.
        // Legacy/whole-frame path: both walls follow the single HasFloorGirt gate.
        private static void AddFloorBayMembers(FrameGraph g, KPBentParams p, double zA, double zB)
            => AddFloorBayMembers(g, p, zA, zB, p.HasFloorGirt, p.HasFloorGirt, p.FloorGirtHt, p.FloorGirtHt);

        // `leftHt` / `rightHt` are the per-side bay floor-girt TOP elevations (each eave bay can step its
        // own floor line); the legacy/whole-frame wrapper passes the shared FloorGirtHt for both.
        private static void AddFloorBayMembers(FrameGraph g, KPBentParams p, double zA, double zB,
            bool floorL, bool floorR, double leftHt, double rightHt)
        {
            if (floorL)
            {
                double topL = leftHt, botL = topL - p.FloorGirtD;
                FrameEdge fl = MakeLongitudinal(g, "FloorGirt",
                    g.AddNode("FloorBayL", new Point3d(0, topL, zA)),
                    g.AddNode("FloorBayL", new Point3d(0, topL, zB)),
                    p.FloorGirtW, p.FloorGirtD, p.PostW, "FL");
                fl.Planes.Add(HalfPlane.KeepRightOfX(0));
                fl.Planes.Add(HalfPlane.KeepLeftOfX(p.FloorGirtW));
                fl.Planes.Add(HalfPlane.KeepAboveY(botL));
                fl.Planes.Add(HalfPlane.KeepBelowY(topL));
            }

            if (floorR)
            {
                double topR = rightHt, botR = topR - p.FloorGirtD;
                FrameEdge fr = MakeLongitudinal(g, "FloorGirt",
                    g.AddNode("FloorBayR", new Point3d(p.Span - p.FloorGirtW, topR, zA)),
                    g.AddNode("FloorBayR", new Point3d(p.Span - p.FloorGirtW, topR, zB)),
                    p.FloorGirtW, p.FloorGirtD, p.PostW, "FR");
                fr.Planes.Add(HalfPlane.KeepRightOfX(p.Span - p.FloorGirtW));
                fr.Planes.Add(HalfPlane.KeepLeftOfX(p.Span));
                fr.Planes.Add(HalfPlane.KeepAboveY(botR));
                fr.Planes.Add(HalfPlane.KeepBelowY(topR));
            }
        }

        // Bay knee braces in the WALL plane (Y height x Z building-length, at a fixed x): an EAVE
        // brace bracing each post up to the eave girt, and a FLOOR brace bracing each post up to the
        // floor girt -- one at EACH bent end of the bay per enabled wall (symmetric). Each is a plain
        // free-box member (FreeBox -> emitter's TryFreeBrace). Left wall x = PostD/2, right = Span-PostD/2.
        private static void AddBayBraces(FrameGraph g, KPBentParams p, BaySpec y, double zA, double zB)
        {
            // Each brace's head tracks the girt it braces into (per side): the eave brace under the eave
            // girt (its Height, default EaveHt+1), the floor brace under the bay floor girt (its Height,
            // default the bent FloorGirtHt). So when a girt drops, its braces drop with it.
            double EaveTop(bool right)  { MemberSize sz = y.SizeOf(right ? "EaveGirt:R"  : "EaveGirt:L");  return sz != null && sz.Ht > 0 ? sz.Ht : p.EaveHt + 1.0; }
            double FloorTop(bool right) { MemberSize sz = y.SizeOf(right ? "FloorGirt:R" : "FloorGirt:L"); return sz != null && sz.Ht > 0 ? sz.Ht : p.FloorGirtHt; }

            // Each brace is its own timber, keyed by wall (A left / E right) + bay end (L low-Y at zA,
            // R high-Y at zB). The L brace leans +Z off the low bent, the R brace leans -Z off the high
            // bent. JUSTIFIED across the wall thickness (graph X) within the NARROWER of post / girt,
            // per its OWN placement (Back = flush the outer face, Center, Front).
            void One(string key, double girtW, double girtUnder)
            {
                if (!y.IsEnabled(key)) return;
                MemberSize sz = y.SizeOf(key);
                if (sz == null || sz.Foot <= 0 || sz.Head <= 0) return;
                string d = key.Substring(key.IndexOf(':') + 1);   // "AL".."ER" (wall + end)
                bool right = d[0] == 'E';                          // wall side (x)
                bool highY = d[1] == 'R';                          // bay end
                double zBent = highY ? zB : zA;
                int dir = highY ? -1 : +1;
                double xWall = WallBraceX(right, System.Math.Min(p.PostW, girtW), sz.W, p.PlaceOf(key), p.Span);
                string desig = (key.StartsWith("Eave") ? "EB" : "FB") + d;
                AddTriBrace(g, sz, xWall, girtUnder, zBent, dir, p.PostW, desig);
            }
            One("EaveBrace:AL",  p.GirtW,      EaveTop(false) - p.GirtD);
            One("EaveBrace:AR",  p.GirtW,      EaveTop(false) - p.GirtD);
            One("EaveBrace:EL",  p.GirtW,      EaveTop(true)  - p.GirtD);
            One("EaveBrace:ER",  p.GirtW,      EaveTop(true)  - p.GirtD);
            One("FloorBrace:AL", p.FloorGirtW, FloorTop(false) - p.FloorGirtD);
            One("FloorBrace:AR", p.FloorGirtW, FloorTop(false) - p.FloorGirtD);
            One("FloorBrace:EL", p.FloorGirtW, FloorTop(true)  - p.FloorGirtD);
            One("FloorBrace:ER", p.FloorGirtW, FloorTop(true)  - p.FloorGirtD);
        }

        // Ridge -> king-post braces (opt-in, owned by the center line): a knee brace in the central
        // plane (x = hs) filling the king-post / ridge corner -- one at each bent end of the bay. The
        // foot seats DOWN the king post, the head ALONG the ridge underside; reuses the wall-brace
        // recipe (AddTriBrace) at the ridge line. KingPost / HammerBeam only (needs a king post to seat
        // the foot); a QueenPost frame laps its rafters at the ridge, so there is nothing to brace to.
        private static void AddRidgeBraces(FrameGraph g, KPBentParams p, BaySpec center, double zA, double zB)
        {
            if (center == null) return;
            double hs = p.Span / 2.0;
            double ridgeUnder = p.EaveHt + (hs - p.RidgeW / 2.0) * p.Pitch + 1.0 - p.RidgeD;   // ridge bottom
            void One(string key, double zBent, int dir)
            {
                if (!center.IsEnabled(key)) return;
                MemberSize sz = center.SizeOf(key);
                if (sz == null || sz.Foot <= 0 || sz.Head <= 0) return;
                string end = key.Substring(key.IndexOf(':') + 1);   // "L" (low-Y) / "R" (high-Y)
                AddTriBrace(g, sz, hs, ridgeUnder, zBent, dir, p.KpostW, "RB" + end);
            }
            One("RidgeBrace:L", zA, +1);   // low-Y end, leans +Z
            One("RidgeBrace:R", zB, -1);   // high-Y end, leans -Z
        }

        // A longitudinal girt tying an interior post line (queen / hammer post) bent-to-bent at its top,
        // plus knee braces down to the posts -- the eave girt+brace recipe generalized to an interior
        // line. xCenter = the post centerline (across span); topY = the post top (rafter underside);
        // postW = the post's Z-width (the brace foot seat). Gated by the bay's girt / brace enables.
        private static void AddInteriorGirt(FrameGraph g, KPBentParams p, BaySpec bay,
            double xCenter, double topY, double postW, double zA, double zB,
            string girtKey, string braceKey, string role, string desig)
        {
            if (bay.IsEnabled(girtKey))
            {
                MemberSize sz = bay.SizeOf(girtKey);
                double gw = sz != null && sz.W > 0 ? sz.W : p.GirtW;
                double gd = sz != null && sz.D > 0 ? sz.D : p.GirtD;
                FrameEdge e = MakeLongitudinal(g, role,
                    g.AddNode(role, new Point3d(xCenter, topY, zA)),
                    g.AddNode(role, new Point3d(xCenter, topY, zB)),
                    gw, gd, postW, desig);
                e.Planes.Add(HalfPlane.KeepRightOfX(xCenter - gw / 2.0));
                e.Planes.Add(HalfPlane.KeepLeftOfX(xCenter + gw / 2.0));
                e.Planes.Add(HalfPlane.KeepBelowY(topY));
                e.Planes.Add(HalfPlane.KeepAboveY(topY - gd));
            }

            double girtUnder = topY - (bay.SizeOf(girtKey)?.D ?? p.GirtD);   // brace head seats here
            void Brace(string key, double zBent, int dir)
            {
                if (!bay.IsEnabled(key)) return;
                MemberSize sz = bay.SizeOf(key);
                if (sz == null || sz.Foot <= 0 || sz.Head <= 0) return;
                string end = key.Substring(key.IndexOf(':') + 1);   // "L" (low-Y) / "R" (high-Y)
                AddTriBrace(g, sz, xCenter, girtUnder, zBent, dir, postW, desig + end);
            }
            Brace(braceKey + ":L", zA, +1);
            Brace(braceKey + ":R", zB, -1);
        }

        // Queen-post girt + braces (opt-in), owned by a queen line (B left / D right). The queen
        // centerline is QueenOffset + QueenD/2 in from center; its top sits at the rafter underside.
        private static void AddQueenGirt(FrameGraph g, KPBentParams p, BaySpec bay, bool left, double zA, double zB)
        {
            double hs = p.Span / 2.0;
            double qInner = hs - p.QueenOffset;                                  // left inner face x
            double cl = qInner - p.QueenD / 2.0;                                 // left centerline
            double xCenter = left ? cl : p.Span - cl;
            double topY = (p.EaveHt - p.PlumbLength) + qInner * p.Pitch;         // queen top = rafter underside
            AddInteriorGirt(g, p, bay, xCenter, topY, p.QueenW, zA, zB,
                "QueenGirt:S", "QueenGirtBrace", "QueenGirt", left ? "QGL" : "QGR");
        }

        // Hammer-post girt + braces (opt-in), owned by a hammer line. `tier` selects the stacked hammer
        // post (1 = nearest the eave, 2 = next in, for divisor 6): its inner face is tier*hbLen in from
        // the eave post -- matching BuildHammerBent's tier-i post (bLix = PostD + i*hbLength). The girt
        // top sits at the rafter underside there. Each tier is its own wall/bay, so they toggle apart.
        private static void AddHammerGirt(FrameGraph g, KPBentParams p, BaySpec bay, bool left, int tier, double zA, double zB)
        {
            if (tier < 1) tier = 1;
            double hbLen = (p.Span - 2.0 * p.PostD - p.KpostD) / System.Math.Max(2, p.HBDivisor);
            double xInner = p.PostD + tier * hbLen;                              // tier hammer post inner face (left)
            double cl = xInner - p.HPostD / 2.0;                                 // left centerline
            double xCenter = left ? cl : p.Span - cl;
            double topY = (p.EaveHt - p.PlumbLength) + xInner * p.Pitch;
            AddInteriorGirt(g, p, bay, xCenter, topY, p.HPostW, zA, zB,
                "HPostGirt:S", "HPostGirtBrace", "HPostGirt", (left ? "HGL" : "HGR") + tier);
        }

        // Across-thickness centerline X for a wall brace: justify the W-wide brace within the band
        // [0, refW] measured from the OUTER face (x=0 left wall / x=Span right wall) at the placement
        // (0 Back = flush outer, 1 Center, 2 Front = flush inner).
        private static double WallBraceX(bool right, double refW, double w, int placement, double span)
        {
            double fromOuter;
            switch (placement)
            {
                case 1:  fromOuter = refW / 2.0;       break;   // Center
                case 2:  fromOuter = refW - w / 2.0;   break;   // Front (inner face)
                default: fromOuter = w / 2.0;          break;   // Back (outer face)
            }
            return right ? span - fromOuter : fromOuter;
        }

        // One wall brace filling the post/girt corner triangle in the wall plane (x = xWall): foot seats
        // DOWN the post (bay-side face), head seats UNDER the girt (stepped into the bay). The body is
        // offset half a depth toward the corner (Foot->Head hypotenuse = outer face); the box is built
        // OVER-LONG and trimmed square by two member-face cut planes (post bay-side face + girt underside).
        private static void AddTriBrace(FrameGraph g, MemberSize sz, double xWall, double girtUnder,
            double zBent, int dir, double postW, string desig)
        {
            // The post extrudes +Z from the bent line (occupies [zBent, zBent+PostW]); its bay-side face
            // is half a post further in the column 1->2 (+Z) direction than the bent centerline.
            double zPost = zBent + dir * (postW / 2.0) + postW / 2.0;
            Point3d corner = new Point3d(xWall, girtUnder, zPost);
            Point3d footPt = new Point3d(xWall, girtUnder - sz.Foot, zPost);                // down the post
            Point3d headPt = new Point3d(xWall, girtUnder, zPost + dir * sz.Head);          // along the girt

            Vector3d axis = (headPt - footPt).GetNormal();
            Vector3d perp = new Vector3d(0, axis.Z, -axis.Y).GetNormal();                   // perp in the Y-Z plane
            if (perp.DotProduct(corner - (footPt + (headPt - footPt) * 0.5)) < 0) perp = perp.Negate();
            Vector3d inset = perp * (sz.D * 0.5);                                           // outer face on the hypotenuse

            double over = sz.D + 2.0;                                                       // member cuts trim the ends
            FrameEdge e = AddBraceBox(g, sz, footPt - axis * over + inset, headPt + axis * over + inset, desig);
            e.Planes.Add(new HalfPlane(new Point3d(0, 0, zPost), new Vector3d(0, 0, 1)));        // post bay-side face seat
            e.Planes.Add(new HalfPlane(new Point3d(0, girtUnder, 0), new Vector3d(0, 1, 0)));    // girt underside seat
        }

        private static FrameEdge AddBraceBox(FrameGraph g, MemberSize sz, Point3d lo, Point3d hi, string desig)
        {
            FrameEdge e = g.AddEdge("Brace",
                g.AddNode("BayBrace", lo), g.AddNode("BayBrace", hi),
                sz.W, sz.D, desig);
            e.FreeBox = true;
            return e;
        }

        // Eave girt cross-section: a W x D rectangle whose OUTSIDE face is flush with the
        // post outside face (x=0 left / x=Span right), with a single 1" chamfer on the
        // outside top corner cut by the rafter top plane -- same start rule as the ridge
        // (chamfer start 1" below the top sits on the rafter plane, here at eave height).
        // `topY` is the eave girt's TOP elevation (its Height; default EaveHt + 1 = the reveal). The outer
        // chamfer (to the roof plane) only applies when the girt top is ABOVE the roof line at the wall
        // (topY > EaveHt) -- i.e. the reveal corner exists; once the girt is dropped to/below the roof
        // line there is nothing to chamfer (a flat bearing block), and the chamfer plane would miss the
        // box (a non-intersecting Slice throws), so it is omitted.
        private static void AddEaveGirtCrossSection(FrameEdge e, KPBentParams p, bool leftSide, double topY)
        {
            double W = e.Width, D = e.Depth;
            bool chamfer = topY > p.EaveHt + 1e-6;
            if (leftSide)
            {
                Point3d ltP = new Point3d(0, p.EaveHt, 0); Vector3d ltD = new Vector3d(1, p.Pitch, 0);
                e.Planes.Add(HalfPlane.KeepRightOfX(0));          // outside face x=0
                e.Planes.Add(HalfPlane.KeepLeftOfX(W));           // inside face
                e.Planes.Add(HalfPlane.KeepAboveY(topY - D));     // bottom
                e.Planes.Add(HalfPlane.KeepBelowY(topY));         // top
                if (chamfer) e.Planes.Add(HalfPlane.KeepBelowLine(ltP, ltD));  // outside (left) chamfer
            }
            else
            {
                Point3d rtP = new Point3d(p.Span, p.EaveHt, 0); Vector3d rtD = new Vector3d(1, -p.Pitch, 0);
                e.Planes.Add(HalfPlane.KeepLeftOfX(p.Span));         // outside face x=Span
                e.Planes.Add(HalfPlane.KeepRightOfX(p.Span - W));    // inside face
                e.Planes.Add(HalfPlane.KeepAboveY(topY - D));        // bottom
                e.Planes.Add(HalfPlane.KeepBelowY(topY));            // top
                if (chamfer) e.Planes.Add(HalfPlane.KeepBelowLine(rtP, rtD));     // outside (right) chamfer
            }
        }

        private static FrameEdge MakeLongitudinal(FrameGraph g, string role, int a, int b,
            double w, double d, double inset, string desig)
        {
            FrameEdge e = g.AddEdge(role, a, b, w, d, desig);
            e.Longitudinal = true;
            e.LongInset = inset;
            return e;
        }

        // Ridge cross-section (bent XY plane): a W x D rectangle centered at the peak, with
        // the top two corners chamfered along the rafter top planes. topY puts the chamfer
        // start 1" below the ridge top, ON the rafter plane (a 1" reveal above the roof line).
        private static void AddRidgeCrossSection(FrameEdge e, KPBentParams p)
        {
            double hs = p.Span / 2.0;
            double W = e.Width, D = e.Depth;
            double topY = p.EaveHt + (hs - W / 2.0) * p.Pitch + 1.0;
            Point3d ltP = new Point3d(0, p.EaveHt, 0);      Vector3d ltD = new Vector3d(1,  p.Pitch, 0);
            Point3d rtP = new Point3d(p.Span, p.EaveHt, 0); Vector3d rtD = new Vector3d(1, -p.Pitch, 0);
            e.Planes.Add(HalfPlane.KeepRightOfX(hs - W / 2.0));
            e.Planes.Add(HalfPlane.KeepLeftOfX(hs + W / 2.0));
            e.Planes.Add(HalfPlane.KeepAboveY(topY - D));
            e.Planes.Add(HalfPlane.KeepBelowY(topY));
            e.Planes.Add(HalfPlane.KeepBelowLine(ltP, ltD));  // left chamfer
            e.Planes.Add(HalfPlane.KeepBelowLine(rtP, rtD));  // right chamfer
        }

        // Common rafters: fill each bay's CLEAR span (eave girt) with full rafter pairs
        // (eave->ridge) at the stations chosen by the spacing mode. The clear span is
        // [zA+PostW, zB] (the near bent consumes PostW = the eave girt's LongInset); the
        // layout is anchored at the eave-girt midpoint so the near and far end spaces are
        // equal. Each station yields a left+right common pair carrying its offset-from-bent
        // (zc - zA) as metadata.
        //   Mode 0 (count):   N evenly spaced commons, equal end gaps. step = clear/(N+1).
        //   Mode 1 (spacing): fixed on-center S; pick the phase (common on the eave-girt
        //                     midpoint = odd count, or straddling it = even count) whose end
        //                     space is closest to S.
        // Symmetric wrapper (legacy whole-frame / BuildQueen / BuildHammer paths): both slopes from p.
        private static void AddCommonRafters(FrameGraph g, KPBentParams p, double zA, double zB)
        {
            AddCommonRaftersSide(g, p, zA, zB, true,  p.CommonMode, p.CommonCount, p.CommonSpacing, p.CommonW, p.CommonD, p.EaveHt + 1.0, 0.0, TailCut.Plumb);
            AddCommonRaftersSide(g, p, zA, zB, false, p.CommonMode, p.CommonCount, p.CommonSpacing, p.CommonW, p.CommonD, p.EaveHt + 1.0, 0.0, TailCut.Plumb);
        }

        // ONE slope's commons, from THIS eave's own bay config (mode/count/spacing + section cw x cd);
        // the global geometry (span, pitch, ridge, eave girt) comes from `p`. Per-side so the two
        // slopes can differ (asymmetric roofs) -- the left slope is owned by Wall A, the right by Wall E.
        private static void AddCommonRaftersSide(FrameGraph g, KPBentParams p, double zA, double zB, bool left,
            int mode, int count, double spacing, double cw, double cd, double eaveGirtHt, double tail, TailCut tailCut)
        {
            double L = zB - zA;
            if (L <= 0) return;
            if (cw <= 0) cw = p.CommonW;
            if (cd <= 0) cd = p.CommonD;
            double inset = p.PostW;        // near bent thickness (= eave girt LongInset)
            double lo = inset, hi = L;     // clear span in offset-from-zA terms
            double clear = hi - lo;
            if (clear <= 0) return;
            double mid = (lo + hi) / 2.0;  // eave-girt midpoint

            var offsets = new List<double>();
            if (mode == 0)
            {
                int N = Math.Max(1, count);
                double step = clear / (N + 1);
                for (int k = 1; k <= N; k++) offsets.Add(lo + k * step);
            }
            else
            {
                double S = spacing > 0 ? spacing : 48.0;
                List<double> best = null;
                double bestScore = double.MaxValue;
                foreach (double phase in new[] { 0.0, S / 2.0 })
                {
                    List<double> stations = StationsForPhase(lo, hi, mid, S, phase);
                    if (stations.Count == 0) continue;
                    double endGap = stations[0] - lo;             // symmetric: = hi - last station
                    double score  = Math.Abs(endGap - S);
                    if (score < bestScore) { bestScore = score; best = stations; }
                }
                if (best != null) offsets = best;
            }

            foreach (double o in offsets)
            {
                double zc = zA + o;
                AddRafterOneSide(g, p, zc - cw / 2.0, o, left, cw, cd, eaveGirtHt, tail, tailCut);   // center the common on zc
            }
        }

        // Stations at on-center S, symmetric about `mid`, shifted by `phase`, kept strictly
        // inside (lo, hi). phase 0 -> a station on the midpoint (odd count); phase S/2 ->
        // stations straddle the midpoint (even count).
        private static List<double> StationsForPhase(double lo, double hi, double mid,
            double S, double phase)
        {
            var stations = new List<double>();
            if (S <= 0) return stations;
            const double eps = 1e-6;
            for (double x = mid + phase; x < hi - eps; x += S) stations.Add(x);
            for (double x = mid + phase - S; x > lo + eps; x -= S) stations.Add(x);
            stations.Sort();
            return stations;
        }

        // Adds one COMMON rafter at extrusion base Z = zBase (own section CommonW x CommonD, peak at the
        // RIDGE face hs +/- RidgeW/2). The roof TOP plane is fixed (EaveHt + x*Pitch). The rafter beds on
        // the eave girt with an INTERIOR birdsmouth at the girt top `eaveGirtHt` (seat over [seatOuter,
        // inner], plumb heel at the inner face) and runs out to a plumb foot at `seatOuter` (no tail) or
        // `+/-tail` past the outer face (overhang, plumb or square per tailCut). At eaveGirtHt = EaveHt+1 and tail 0 this
        // reproduces the classic toe birdsmouth (toe at 1/Pitch); dropping eaveGirtHt below EaveHt clears
        // the rafter for a tail. `offset` is the common's distance from the near bent (carried metadata).
        private static void AddRafterOneSide(FrameGraph g, KPBentParams p, double zBase, double offset,
            bool left, double cw, double cd, double eaveGirtHt, double tail, TailCut tailCut)
        {
            double hs  = p.Span / 2.0;
            double cp  = cd / Math.Cos(p.Beta);                 // underside offset (perp depth)
            double rt  = tail;                                   // tail overhang run (per eave bay)
            double girtTop = eaveGirtHt;                         // eave-girt top this side beds on

            // Roof TOP plane y at across-span x (fixed -- the eave Height / tail never move it).
            double Roof(double x) => left ? p.EaveHt + x * p.Pitch : p.EaveHt + (p.Span - x) * p.Pitch;
            double xPeak = left ? hs - p.RidgeW / 2.0 : hs + p.RidgeW / 2.0;
            double yPeak = Roof(xPeak);
            string desig = left ? "CA" : "CE";
            string eaveNode = left ? "CommonEaveL" : "CommonEaveR";
            string peakNode = left ? "CommonPeakL" : "CommonPeakR";
            Point3d peakTop   = new Point3d(xPeak, yPeak, 0);
            Point3d peakUnder = new Point3d(xPeak, yPeak - cp, 0);

            // Seat runs (from the OUTER face inward): outer = where the roof plane meets the girt top
            // (the toe), clamped to the outer face; inner = the girt inner face OR where the rafter
            // UNDERSIDE meets the girt top (Roof = girtTop + cp), whichever is nearer -- so as the girt
            // drops and the rafter stops reaching the inner face, the birdsmouth TAPERS to that exit point
            // instead of inverting. No contact at all (inner <= outer) -> a degenerate notch the emitter
            // skips (the rafter clears the girt).
            double cpRun  = p.Pitch > 0 ? cp / p.Pitch : 0.0;
            double toeRun = p.Pitch > 0 ? (girtTop - p.EaveHt) / p.Pitch : 0.0;
            double seatOuterRun = Math.Max(0.0, toeRun);
            // Inner = the girt inner face OR the rafter-underside exit, never outboard of the outer seat
            // (clamped so a no-contact case collapses to a skipped notch rather than cutting the tail).
            double seatInnerRun = Math.Max(seatOuterRun, Math.Min(p.GirtW, toeRun + cpRun));
            double seatOuterX = left ? seatOuterRun : p.Span - seatOuterRun;
            double seatInnerX = left ? seatInnerRun : p.Span - seatInnerRun;
            double footX  = (rt > 1e-6) ? (left ? -rt : p.Span + rt) : seatOuterX;
            Point3d footTop = new Point3d(footX, Roof(footX), 0);
            // Birdsmouth notch quad: below the seat (girtTop) over [seatOuter, seatInner], down to the
            // rafter underside; the heel plumb collapses to a point when the seat tapers to the exit.
            Point3d nA = new Point3d(seatOuterX, girtTop, 0);
            Point3d nB = new Point3d(seatInnerX, girtTop, 0);
            Point3d nC = new Point3d(seatInnerX, Roof(seatInnerX) - cp, 0);
            Point3d nD = new Point3d(seatOuterX, Roof(seatOuterX) - cp, 0);
            int e0 = g.AddNode(eaveNode, new Point3d(footX, Roof(footX), zBase));
            int k0 = g.AddNode(peakNode, new Point3d(xPeak, yPeak, zBase));
            FrameEdge ec = g.AddEdge("Common", e0, k0, cw, cd, desig);
            ec.CustomProfile = new[] { peakTop, footTop, peakUnder, nA, nB, nC, nD };   // 7-pt (interior birdsmouth + optional tail)
            ec.SquareTail = rt > 1e-6 && tailCut == TailCut.Square;   // square tail cut only when a tail exists
            ec.BayOffset = offset;
        }

        // Purlins: longitudinal members (bent-to-bent, like the ridge) distributed up each
        // roof slope from eave to ridge. The roof mode alternative to common rafters. Each
        // purlin's cross-section is SQUARE TO THE ROOF (faces parallel/perpendicular to the
        // rafter slope) with its TOP FACE flush with the rafter top plane, extending down
        // PurlinD perpendicular; PurlinW is measured along the slope. Stations along the
        // slope use the same two modes as commons, anchored at the rafter midpoint so the
        // foot->first and last->peak end spaces are equal.
        // Symmetric wrapper (legacy whole-frame / BuildQueen / BuildHammer paths): both slopes from p.
        private static void AddPurlins(FrameGraph g, KPBentParams p, double zA, double zB)
        {
            AddPurlinsSide(g, p, zA, zB, true,  p.PurlinMode, p.PurlinCount, p.PurlinSpacing, p.PurlinW, p.PurlinD);
            AddPurlinsSide(g, p, zA, zB, false, p.PurlinMode, p.PurlinCount, p.PurlinSpacing, p.PurlinW, p.PurlinD);
        }

        // ONE slope's purlins, from THIS eave's own bay config (mode/count/spacing + section pw0 x pd0);
        // global geometry from `p`. Per-side so the slopes can differ (left owned by Wall A, right by E).
        private static void AddPurlinsSide(FrameGraph g, KPBentParams p, double zA, double zB, bool left,
            int mode, int count, double spacing, double pw0, double pd0)
        {
            double cosB = Math.Cos(p.Beta), sinB = Math.Sin(p.Beta);
            double pw = pw0 > 0 ? pw0 : 4.0;
            double pd = pd0 > 0 ? pd0 : 6.0;

            // Slope span runs (on the rafter top line) from the eave girt's LOWER CHAMFER
            // point (x = 0, at (0,EaveHt)) to the ridge's UPPER CHAMFER point
            // (x = hs - RidgeW/2 + 1/Pitch). Both lie on the rafter top line.
            double hs    = p.Span / 2.0;
            double rake  = (p.Pitch > 0) ? 1.0 / p.Pitch : 0.0;  // run for the 1" chamfer reveal
            double footL = 0.0;                                  // eave girt lower chamfer x (0,EaveHt)
            double peakL = hs - p.RidgeW / 2.0 + rake;           // ridge upper chamfer x
            double Lslope = (peakL - footL) / cosB;
            if (Lslope <= 0) return;

            // Slope stations (distance from the foot along the slope), anchored at mid.
            var stations = new List<double>();
            if (mode == 0)
            {
                int N = Math.Max(1, count);
                double step = Lslope / (N + 1);
                for (int k = 1; k <= N; k++) stations.Add(k * step);
            }
            else
            {
                double S = spacing > 0 ? spacing : 48.0;
                double mid = Lslope / 2.0;
                List<double> best = null;
                double bestScore = double.MaxValue;
                foreach (double phase in new[] { 0.0, S / 2.0 })
                {
                    List<double> st = StationsForPhase(0.0, Lslope, mid, S, phase);
                    if (st.Count == 0) continue;
                    double endGap = st[0];                       // symmetric: = Lslope - last
                    double score  = Math.Abs(endGap - S);
                    if (score < bestScore) { bestScore = score; best = st; }
                }
                if (best != null) stations = best;
            }

            if (left)
            {
                // Left slope: foot at (0, EaveHt), up to the right.
                Point3d  footPtL  = new Point3d(footL, p.EaveHt + footL * p.Pitch, 0);
                Vector3d slopeUpL = new Vector3d(cosB, sinB, 0);
                Vector3d downPerpL = new Vector3d(sinB, -cosB, 0);   // perpendicular, into material (down)
                Point3d  topLPL = new Point3d(0, p.EaveHt, 0); Vector3d topLDL = new Vector3d(1, p.Pitch, 0);
                foreach (double s in stations)
                    AddPurlinSlope(g, pw, pd, p.PostW, footPtL, slopeUpL, downPerpL, topLPL, topLDL, s, zA, zB, "PL");
            }
            else
            {
                // Right slope (mirror): foot at (Span, ...), up to the left.
                Point3d  footPtR  = new Point3d(p.Span - footL, p.EaveHt + footL * p.Pitch, 0);
                Vector3d slopeUpR = new Vector3d(-cosB, sinB, 0);
                Vector3d downPerpR = new Vector3d(-sinB, -cosB, 0);
                Point3d  topLPR = new Point3d(p.Span, p.EaveHt, 0); Vector3d topLDR = new Vector3d(1, -p.Pitch, 0);
                foreach (double s in stations)
                    AddPurlinSlope(g, pw, pd, p.PostW, footPtR, slopeUpR, downPerpR, topLPR, topLDR, s, zA, zB, "PR");
            }
        }

        // One purlin at slope distance `s` from the foot, square to the roof: top face on the
        // rafter top line, body PurlinD deep perpendicular (down), PurlinW along the slope.
        private static void AddPurlinSlope(FrameGraph g, double pw, double pd, double inset,
            Point3d footPt, Vector3d slopeUp, Vector3d downPerp, Point3d topLP, Vector3d topLD,
            double s, double zA, double zB, string desig)
        {
            double hw = pw / 2.0;
            Point3d p0   = footPt + slopeUp * s;
            Point3d pbot = p0 + downPerp * pd;
            Point3d low  = p0 - slopeUp * hw;
            Point3d high = p0 + slopeUp * hw;

            int a = g.AddNode("Purlin", new Point3d(p0.X, p0.Y, zA));
            int b = g.AddNode("Purlin", new Point3d(p0.X, p0.Y, zB));
            FrameEdge e = MakeLongitudinal(g, "Purlin", a, b, pw, pd, inset, desig);
            e.BayOffset = s;   // offset from the eave girt up the rake (slope distance)
            e.Planes.Add(HalfPlane.KeepBelowLine(topLP, topLD));   // top face = rafter top plane
            e.Planes.Add(HalfPlane.KeepAboveLine(pbot, topLD));    // bottom face (PurlinD down)
            e.Planes.Add(HalfPlane.Through(low,  downPerp, p0));   // low-slope end
            e.Planes.Add(HalfPlane.Through(high, downPerp, p0));   // high-slope end
        }

        // Ray (o + t*d) intersected with the infinite line through q along e.
        private static Point3d IntersectRayLine(Point3d o, Vector3d d, Point3d q, Vector3d e)
        {
            double denom = d.X * e.Y - d.Y * e.X;
            if (Math.Abs(denom) < 1e-9) return o;
            double t = ((q.X - o.X) * e.Y - (q.Y - o.Y) * e.X) / denom;
            return new Point3d(o.X + t * d.X, o.Y + t * d.Y, 0);
        }

        // FACE-referenced long faces: one face ON the a->b line; body extends by `depth`
        // along `offsetDir` (unit, perpendicular to the axis).
        private static void AddLongFacesOneSided(FrameEdge e, Point3d a, Point3d b,
            double depth, Vector3d offsetDir)
        {
            Vector3d axis   = a.GetVectorTo(b);
            Point3d  inside = a + offsetDir * (depth * 0.5);
            e.Planes.Add(HalfPlane.Through(a, axis, inside));
            e.Planes.Add(HalfPlane.Through(a + offsetDir * depth, axis, inside));
        }

        // Resolve each timber's per-member placement to 0/1/2 keyed by "Role:Designation": Justify.Default
        // inherits the frame's OffsetType; Back/Center/Front -> 0/1/2 ((int)Justify - 1).
        private static Dictionary<string, int> BuildPlace(IEnumerable<Timber> timbers, int offsetType)
        {
            var d = new Dictionary<string, int>();
            if (timbers == null) return d;
            foreach (Timber t in timbers)
                d[t.Key] = t.Size.Place == Justify.Default ? offsetType : (int)t.Size.Place - 1;
            return d;
        }

        // Z placement (across the wall thickness) for a thin member: justified within `refW` (the
        // NARROWER of the two timbers it connects to) at the resolved placement (0 Back / 1 Center /
        // 2 Front). Back = flush the bent line (shared back face); slack clamps to >= 0.
        private static double ZOffsetFor(double refW, double memberW, int placement)
        {
            double slack = refW - memberW;
            if (slack < 0.0) slack = 0.0;
            switch (placement)
            {
                case 1: return slack / 2.0;  // Center
                case 2: return slack;        // Front
                default: return 0.0;         // Back
            }
        }

        // Unit perpendicular to the a->b axis pointing AWAY from reference point c.
        private static Vector3d OffsetAwayFrom(Point3d a, Point3d b, Point3d c)
        {
            Vector3d axis = a.GetVectorTo(b);
            Vector3d perp = new Vector3d(-axis.Y, axis.X, 0).GetNormal();
            Point3d  mid  = a + axis * 0.5;
            if ((c.X - mid.X) * perp.X + (c.Y - mid.Y) * perp.Y > 0) perp = perp.Negate();
            return perp;
        }

        // Perpendicular to a->b pointing TOWARD c -- a brace body offset this way fills the triangle
        // toward the corner (the a->b hypotenuse is the brace's OUTER face).
        private static Vector3d OffsetToward(Point3d a, Point3d b, Point3d c) => OffsetAwayFrom(a, b, c).Negate();

        // Unit perpendicular to axis with a positive Y component (the "upper" side).
        private static Vector3d PerpUp(Vector3d axis)
        {
            Vector3d n = new Vector3d(-axis.Y, axis.X, 0).GetNormal();
            if (n.Y < 0) n = n.Negate();
            return n;
        }
    }
}
