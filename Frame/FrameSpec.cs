using System.Collections.Generic;
using System.ComponentModel;

namespace TimberDraw
{
    // Explicit-instance model for a frame -- the SOURCE OF TRUTH the tree UI edits and the
    // generator builds from. The node/edge FrameGraph is a DERIVED render artifact produced
    // by KingPostBentGraph.Build(FrameSpec).
    //
    // TWO ORTHOGONAL AXES (2026-06-13): a frame is a list of BENTS (transverse cross-frames,
    // spaced along the building) and a list of WALLS (longitudinal lines across the span). Each
    // BENT carries its SEPARATION to the next bent (graph Z); each WALL carries its separation to
    // the next wall (graph X). A BAY is the DERIVED cell between two adjacent bents: each wall holds
    // a parallel list of bay cells (count = Bents-1), and a wall-bay owns that wall's longitudinal
    // members for the interval. Walls A (left) / B (right) are primary; interior framing ("sub"
    // bents/walls) comes from managed TPlace + TAssign, not this parametric model.
    //
    // Graph axes: X = across-span (wall spacing), Y = height, Z = along-building (bent spacing).
    // ManagedFrameEmitter.ModelBasis maps graph->world at draw time.
    //
    // UNIVERSAL TIMBER: each member is a `Timber` (Role + Designation + own MemberSize), held in the
    // bent/bay's List<Timber>. The bent/bay TYPE is a FACTORY that emits the default timber list.
    public class FrameSpec
    {
        // The grouping layer's top level: this frame's tag. Each emitted timber is stamped with it,
        // so a Draw clears only THIS frame's timbers (per-frame redraw) and multiple managed frames
        // can coexist in one drawing. Default "A".
        // The frame's user-typed identity Name -- also the drawing group tag (per-frame erase/redraw +
        // grouping layers). Blank by default ("Frame (set name)"); grouping falls back to "A" when blank.
        [Category("0 Identity"), DisplayName("Name")]
        public string FrameTag { get; set; } = "";

        // Span SEED. The authoritative across-span is the sum of the wall separations (walls define
        // their separation); Span exposes that, falling back to the seed before walls exist. Setting
        // Span (the geometry PropertyGrid) writes the seed and, for the primary A/B walls, wall A's
        // separation -- so the two stay coherent.
        private double _span;
        [Category("1 Geometry"), DisplayName("Span")]
        public double Span
        {
            get => (Walls != null && Walls.Count > 0) ? WallSpanSum() : _span;
            set
            {
                _span = value;
                if (Walls == null || Walls.Count == 0) return;
                // With the materialized A-E lines (>2 walls) the interior lines must move WITH the span,
                // so scale every separation proportionally (the last/right-eave's 0 stays 0). The simple
                // two-wall case just writes Wall A's separation = the new span.
                double old = WallSpanSum();
                if (Walls.Count > 1 && old > 0) { double k = value / old; foreach (WallSpec w in Walls) w.Separation *= k; }
                else Walls[0].Separation = value;
            }
        }
        // The along-building dimension -- the twin of Span on the bent axis. The authoritative length
        // is the sum of the bent separations; setting it writes Bent 1's separation (the clean 2-bent
        // case), and per-bent Separation stays editable. Purely derived from the bents (which always
        // persist their Separation), so it needs no seed/serialization the way Span does.
        [Category("1 Geometry"), DisplayName("Length")]
        public double Length
        {
            get { double s = 0; if (Bents != null) foreach (BentSpec b in Bents) s += b.Separation; return s; }
            set { if (Bents != null && Bents.Count > 0) Bents[0].Separation = value; }
        }
        [Category("1 Geometry"), DisplayName("Eave Height")]
        public double EaveHt { get; set; }
        // Stored as rise/run. The UI edits it as rise-per-12 ("rise : 12"): entered value / 12.
        [Browsable(false)] public double Pitch { get; set; }
        [Category("1 Geometry"), DisplayName("Pitch (rise : 12)")]
        public double PitchRise
        {
            get => Pitch * 12.0;
            set => Pitch = value / 12.0;
        }
        // Vestigial 2D/3D switch (legacy DrawElement path). The managed emitter always builds 3D solids;
        // hidden from the tree UI. Kept for saved-JSON round-trip.
        [Browsable(false)] public bool Make3D { get; set; }
        // Frame-wide centering is RETIRED from the UI -- the default now lives per Bent + per Bay (each carries
        // its own OffsetType/BraceCentering). Kept here as a hidden field ONLY to seed fresh bents/bays
        // (NewSeeded / NewFromSettings) and to migrate old saves (FromJson seeds each bent/bay from it).
        [Browsable(false)]
        public int OffsetType { get; set; }

        // Axis 1: the transverse bents, in order along the building. Each carries Separation to the next.
        [Browsable(false)] public List<BentSpec> Bents { get; set; } = new List<BentSpec>();
        // Axis 2: the longitudinal walls (A left, B right). Each carries Separation to the next + a
        // parallel list of bay cells (one per inter-bent interval).
        [Browsable(false)] public List<WallSpec> Walls { get; set; } = new List<WallSpec>();

        private double WallSpanSum() { double s = 0; foreach (WallSpec w in Walls) s += w.Separation; return s; }

        // Number of bay cells per wall = inter-bent intervals.
        [Browsable(false)] public int BayCount => Bents.Count > 0 ? Bents.Count - 1 : 0;

        // Append a new (untyped) bent and re-derive the bay cells in every wall. Returns the bent.
        public BentSpec AddBent()
        {
            BentSpec b = BentSpec.NewDefault();
            b.Name = (Bents.Count + 1).ToString();   // "Bent " prefixed at display
            Bents.Add(b);
            SyncBays();
            return b;
        }

        // Remove a bent and re-derive the bay cells. The interval that collapses is dropped per wall.
        public bool RemoveBent(BentSpec b)
        {
            int i = Bents.IndexOf(b);
            if (i < 0) return false;
            Bents.RemoveAt(i);
            // Drop the bay cell at the collapsing interval (clamp to range) in each wall before resync.
            foreach (WallSpec w in Walls)
            {
                int bi = i < w.Bays.Count ? i : w.Bays.Count - 1;
                if (bi >= 0 && bi < w.Bays.Count) w.Bays.RemoveAt(bi);
            }
            SyncBays();
            Renumber();
            return true;
        }

        // The gap (graph-Z) that an insert at `before`/`after` of `sel` would straddle. Returns 0 with
        // interior=false when the insert is at an END (before the first / after the last bent) -- there
        // the frame EXTENDS by the entered distance, so there's no split limit.
        public double GapForBentInsert(BentSpec sel, bool before, out bool interior)
        {
            interior = false;
            int i = Bents.IndexOf(sel);
            if (i < 0) return 0.0;
            int j = before ? i : i + 1;          // new bent's array index
            bool hasUp = j > 0, hasDown = j < Bents.Count;
            interior = hasUp && hasDown;
            return interior ? Bents[j - 1].Separation : 0.0;
        }

        // Insert a new (Free Assembly) bent before/after `sel`, `sep` from its upstream neighbor.
        // INTERIOR: splits the straddled gap G into `sep` + (G-sep) (end-to-end distance unchanged).
        // END (no upstream or no downstream): extends the frame by `sep`. Downstream bents renumber.
        public BentSpec InsertBent(BentSpec sel, bool before, double sep)
        {
            int i = Bents.IndexOf(sel);
            if (i < 0) return null;
            int j = before ? i : i + 1;
            int oldCount = Bents.Count;
            bool hasUp = j > 0, hasDown = j < oldCount;

            var nb = BentSpec.NewDefault();
            nb.BentType = "Free Assembly";
            if (hasUp && hasDown)                       // interior split
            {
                double G = Bents[j - 1].Separation;
                if (sep <= 0.0 || sep >= G) return null;   // caller validates; guard anyway
                Bents[j - 1].Separation = sep;
                nb.Separation = G - sep;
            }
            else if (hasUp)                              // extend past the last bent
            {
                Bents[j - 1].Separation = sep;
                nb.Separation = 0.0;
            }
            else                                         // extend before the first bent
            {
                nb.Separation = sep;
            }
            Bents.Insert(j, nb);

            // Insert a fresh bay at the matching interval in each wall (the new interval is new->down
            // when a downstream bent exists, else up->new at the end).
            int bayAt = hasDown ? j : j - 1;
            WallSpec leftEave = WallLeft();
            foreach (WallSpec w in Walls)
            {
                int at = System.Math.Max(0, System.Math.Min(bayAt, w.Bays.Count));
                BaySpec nbay = BaySpec.NewDefault(w.Role);
                nbay.OwnerIsWallA = (w == leftEave);
                w.Bays.Insert(at, nbay);
            }
            Renumber();
            return nb;
        }

        // The gap (graph-X) a wall insert at `before`/`after` of `sel` would straddle. Mirrors
        // GapForBentInsert: interior=false at an END (extends the span by the entered distance).
        public double GapForWallInsert(WallSpec sel, bool before, out bool interior)
        {
            interior = false;
            int i = Walls.IndexOf(sel);
            if (i < 0) return 0.0;
            int j = before ? i : i + 1;
            bool hasUp = j > 0, hasDown = j < Walls.Count;
            interior = hasUp && hasDown;
            return interior ? Walls[j - 1].Separation : 0.0;
        }

        // Insert a wall before/after `sel`, `sep` from its upstream neighbor. INTERIOR splits the
        // straddled gap; END extends the span (a new eave). RenumberWalls then sets the letters,
        // RightSide, and FreeAssembly by position (eaves parametric, interior Free Assembly).
        public WallSpec InsertWall(WallSpec sel, bool before, double sep)
        {
            int i = Walls.IndexOf(sel);
            if (i < 0) return null;
            int j = before ? i : i + 1;
            int oldCount = Walls.Count;
            bool hasUp = j > 0, hasDown = j < oldCount;

            var nw = new WallSpec();
            if (hasUp && hasDown)                       // interior split
            {
                double G = Walls[j - 1].Separation;
                if (sep <= 0.0 || sep >= G) return null;
                Walls[j - 1].Separation = sep;
                nw.Separation = G - sep;
            }
            else if (hasUp)                              // extend past the last wall (new right eave)
            {
                Walls[j - 1].Separation = sep;
                nw.Separation = 0.0;
            }
            else                                         // extend before the first wall (new left eave)
            {
                nw.Separation = sep;
            }
            Walls.Insert(j, nw);
            RenumberWalls();
            SyncBays();                  // the new wall gets a fresh BayCount-long bay list
            return nw;
        }

        // Remove any wall (keep at least one). Interior removal folds the gap into the upstream
        // neighbor (downstream eaves stay put); the new last wall's trailing separation is zeroed.
        public bool RemoveWall(WallSpec w)
        {
            int i = Walls.IndexOf(w);
            if (i < 0 || Walls.Count <= 1) return false;
            if (i > 0 && i < Walls.Count - 1) Walls[i - 1].Separation += w.Separation;
            Walls.RemoveAt(i);
            if (Walls.Count > 0) Walls[Walls.Count - 1].Separation = 0.0;
            RenumberWalls();
            return true;
        }

        // Reletter walls by POSITION: first = A (left eave), last = E (right eave), interior = B,C,D..
        // skipping I/O (mirrors FrameGrid.ColLetterAt). The eaves are parametric; interior walls are
        // derived Free Assembly. RightSide marks the right eave (only when there's more than one wall).
        public void RenumberWalls()
        {
            const string Alpha = "ABCDEFGHJKLMNPQRSTUVWXYZ";
            int n = Walls.Count;
            for (int i = 0; i < n; i++)
            {
                string letter = (i == 0) ? "A" : (i == n - 1) ? "E" : Alpha[System.Math.Min(i, Alpha.Length - 1)].ToString();
                Walls[i].Letter = letter;
                Walls[i].Name = letter;   // "Wall " prefixed at display
                Walls[i].RightSide = (n > 1 && i == n - 1);
                Walls[i].FreeAssembly = (i > 0 && i < n - 1);
            }
        }

        // Make every wall's bay list exactly BayCount long: keep existing cells (by index), append
        // defaults for new intervals, trim extras. Idempotent -- safe to call after any bent change.
        public void SyncBays()
        {
            int want = BayCount;
            WallSpec leftEave = WallLeft();
            foreach (WallSpec w in Walls)
            {
                while (w.Bays.Count < want)
                {
                    BaySpec y = BaySpec.NewDefault(w.Role);   // catalog per the wall's line role
                    y.OwnerIsWallA = (w == leftEave);          // roof tag / legacy merge anchor
                    y.Name = "Bay " + (w.Bays.Count + 1);
                    w.Bays.Add(y);
                }
                while (w.Bays.Count > want) w.Bays.RemoveAt(w.Bays.Count - 1);
            }
        }

        // Renumber bent/wall/bay Names positionally after a structural change.
        public void Renumber()
        {
            for (int i = 0; i < Bents.Count; i++) Bents[i].Name = (i + 1).ToString();   // "Bent " prefixed at display
            foreach (WallSpec w in Walls)
                for (int i = 0; i < w.Bays.Count; i++) w.Bays[i].Name = "Bay " + (i + 1);
        }

        // First bent (used to reference post/girt sizes for bay-member insets).
        public BentSpec FirstBent() => Bents.Count > 0 ? Bents[0] : null;

        // The primary eave walls by side (null if absent). WallLeft = the first non-right EAVE line
        // (skips interior lines B/C/D); WallRight = the right-eave line.
        public WallSpec WallLeft()  { foreach (WallSpec w in Walls) if (!w.RightSide && w.Role == BayRole.Eave) return w; return null; }
        public WallSpec WallRight() { foreach (WallSpec w in Walls) if (w.RightSide)  return w; return null; }
        // The central line that owns the ridge (null when there is none, e.g. legacy two-wall frames).
        public WallSpec WallCenter() { foreach (WallSpec w in Walls) if (w.Role == BayRole.Center) return w; return null; }

        // A new spec carries the current frame geometry from settings + the two primary walls A/B
        // (A.Separation = Span), but NO bents (the user builds the frame).
        public static FrameSpec NewFromSettings()
        {
            KPBentParams p = Commands.ReadKPParams();
            var s = new FrameSpec
            {
                FrameTag = "A",
                EaveHt = p.EaveHt, Pitch = p.Pitch,
                Make3D = p.Make3D, OffsetType = p.OffsetType
            };
            s._span = p.Span;
            s.Walls.Add(WallSpec.NewDefault("A", right: false, sep: p.Span));
            s.Walls.Add(WallSpec.NewDefault("E", right: true,  sep: 0.0));   // right eave anchored "E"
            return s;
        }

        // The fresh-start seed: a fully-typed, immediately-generatable starter. Two KingPost bents
        // (so there is ONE bay = a full module: eave/floor girts + ridge + roof option) and both eaves
        // (Walls A + E, the A/E shape NewFromSettings uses). Each bent is typed exactly as the tree does
        // when a Bent Type is set -- RebuildTimbers() for the factory defaults, then TypeDefaults.Apply
        // to overlay the user's saved KingPost template, if any. The user grows it via Insert
        // Before/After. (The start path is now always-fresh: see FrameTreeControl.ResetToSeed.)
        public static FrameSpec NewSeeded()
        {
            KPBentParams p = Commands.ReadKPParams();
            var s = new FrameSpec
            {
                FrameTag = "",   // unnamed -> "Frame (set name)"; grouping falls back to "A"
                EaveHt = p.EaveHt, Pitch = p.Pitch,
                Make3D = p.Make3D, OffsetType = p.OffsetType
            };
            s._span = p.Span;

            for (int i = 0; i < 2; i++)
            {
                BentSpec b = BentSpec.NewDefault();   // Separation seeded from BaySpacings[0]
                b.Name = (i + 1).ToString();          // "Bent " prefixed at display
                b.BentType = "KingPost";
                b.RebuildTimbers();                   // factory defaults for the type
                TypeDefaults.Apply(b);                // overlay the saved KingPost default, if any
                s.Bents.Add(b);
            }
            // Five longitudinal lines A-E for the KingPost frame: A/E eave posts, B/D vstrut lines, C
            // king-post/ridge line. Lettered dense sequential; x anchored to the bent members (B/D at a
            // placeholder quarter-span until the grid derives the exact vstrut x). Separations are the
            // x-gaps and sum to Span. The Center line (C) owns the ridge; B/D are addressing-only.
            double hs = p.Span / 2.0;
            double vstrutX = p.Span / 4.0;   // placeholder vstrut x (precise anchoring is the grid step)
            s.Walls.Add(WallSpec.NewDefault("A", right: false, sep: vstrutX,      role: BayRole.Eave));
            s.Walls.Add(WallSpec.NewDefault("B", right: false, sep: hs - vstrutX, role: BayRole.Vstrut));
            s.Walls.Add(WallSpec.NewDefault("C", right: false, sep: hs - vstrutX, role: BayRole.Center));
            s.Walls.Add(WallSpec.NewDefault("D", right: false, sep: vstrutX,      role: BayRole.Vstrut));
            s.Walls.Add(WallSpec.NewDefault("E", right: true,  sep: 0.0,          role: BayRole.Eave));
            s.SyncBays();   // two bents -> one bay per wall (catalog per each wall's role)
            return s;
        }

        // Migrate a LEGACY frame (two eave walls A/E, the ridge + roof carried only by Wall A) to the
        // materialized A-E line model: derive the five anchored lines, move the ridge to the Center line
        // (C), and mirror Wall A's roof onto Wall E (the per-side default -- so an old symmetric roof
        // keeps drawing both slopes). Preserves the user's eave/roof/ridge edits (Enabled + Size, by
        // key). No-op once a Center line exists (already migrated) or there is nothing to anchor.
        // The interior (B/D) role is then set from the bent type by SyncWallRoles (queen / hammer /
        // vstrut), so a migrated QueenPost / HammerBeam frame gets its proper interior lines.
        public void MigrateToWallLines()
        {
            if (WallCenter() != null) return;          // already the new model
            WallSpec oldA = WallLeft();
            if (oldA == null) return;                  // no eave to anchor
            WallSpec oldE = WallRight();
            List<BaySpec> oldABays = oldA.Bays;
            List<BaySpec> oldEBays = oldE?.Bays;

            double span = Span, hs = span / 2.0, vx = span / 4.0;
            var nA = WallSpec.NewDefault("A", false, vx,        BayRole.Eave);
            var nB = WallSpec.NewDefault("B", false, hs - vx,   BayRole.Vstrut);
            var nC = WallSpec.NewDefault("C", false, hs - vx,   BayRole.Center);
            var nD = WallSpec.NewDefault("D", false, vx,        BayRole.Vstrut);
            var nE = WallSpec.NewDefault("E", true,  0.0,       BayRole.Eave);
            Walls = new List<WallSpec> { nA, nB, nC, nD, nE };
            SyncBays();   // BayCount role-based bays per wall

            int bayN = BayCount;
            for (int i = 0; i < bayN; i++)
            {
                BaySpec a = i < oldABays.Count ? oldABays[i] : null;
                BaySpec e = oldEBays != null && i < oldEBays.Count ? oldEBays[i] : null;

                OverlayBay(nA.Bays[i], a);              // new A <- old A eave/floor/roof
                CopyRoofScalars(nA.Bays[i], a);
                OverlayBay(nE.Bays[i], e);              // new E <- old E eave/floor
                CopyRoofScalars(nE.Bays[i], a);         // E's roof config mirrors A (symmetric default)
                OverlayKey(nE.Bays[i], a, "Commons:X"); // and mirror A's commons/purlins on/off onto E
                OverlayKey(nE.Bays[i], a, "Purlins:X");
                OverlayKey(nC.Bays[i], a, "Ridge:R");   // the ridge moves to the Center line

                nA.Bays[i].SyncRoofType(); nE.Bays[i].SyncRoofType();
            }
            SyncWallRoles();   // correct B/D from the loaded bent type (queen / hammer / vstrut)
        }

        // The interior wall-line role implied by the (representative) bent type: QueenPost queens,
        // HammerBeam hammer posts, otherwise KingPost vstruts (addressing-only). Eave/Center lines are
        // type-independent, so only the interior lines are re-roled.
        private static BayRole InteriorRoleForType(string bentType)
        {
            switch (bentType)
            {
                case "QueenPost": case "QueenPostTruss": return BayRole.Queen;
                case "HammerBeam":                       return BayRole.Hammer;
                default:                                 return BayRole.Vstrut;   // KingPost(Truss) / unset
            }
        }

        // Interior wall LINES per side implied by a bent: HammerBeam divisor 6 stacks two hammer-post
        // tiers (two lines per side -> A-G), everything else has one (A-E). Free Assembly bents emit no
        // parametric verticals, so they contribute none.
        private static int PerSideInteriorCount(BentSpec b)
        {
            if (b == null || b.IsFreeAssembly) return 0;
            return (b.BentType == "HammerBeam" && b.HBDivisor >= 6) ? 2 : 1;
        }

        // The bent that drives the canonical line set: the non-Free bent with the MOST interior lines per
        // side (ties -> first), so any HammerBeam div6 forces the full A-G set. Falls back to the first.
        private BentSpec RepBentForLines()
        {
            BentSpec rep = null; int best = -1;
            foreach (BentSpec b in Bents)
            {
                if (b.IsFreeAssembly) continue;
                int n = PerSideInteriorCount(b);
                if (n > best) { best = n; rep = b; }
            }
            return rep ?? FirstBent();
        }

        // Current canonical (non-Free) interior lines per side = (#walls that aren't Eave/Center) / 2.
        private int CurrentInteriorPerSide()
        {
            int n = 0;
            foreach (WallSpec w in Walls)
                if (!w.FreeAssembly && w.Role != BayRole.Eave && w.Role != BayRole.Center) n++;
            return n / 2;
        }

        // Reconcile the longitudinal wall lines to the representative bent type: set the interior role
        // (queen / hammer / vstrut) AND the interior line COUNT per side (1 normally, 2 for HammerBeam
        // divisor 6 -> A-G). When the count already matches, only the role is re-derived in place (all
        // edits preserved). When it changes (5 <-> 7 walls), the canonical skeleton is rebuilt, carrying
        // the eave + center bay edits across. Call after a bent-type / divisor change or a load.
        public void SyncWallRoles()
        {
            BentSpec rep = RepBentForLines();
            BayRole interior = InteriorRoleForType(rep?.BentType);
            int perSide = System.Math.Max(1, PerSideInteriorCount(rep));

            // Fast path: line count unchanged -> just re-role the interior lines in place.
            if (CurrentInteriorPerSide() == perSide)
            {
                foreach (WallSpec w in Walls)
                {
                    if (w.Role == BayRole.Eave || w.Role == BayRole.Center || w.Role == interior) continue;
                    w.Role = interior;
                    foreach (BaySpec y in w.Bays) { y.Role = interior; y.RebuildTimbers(); }
                }
                return;
            }
            RebuildWallLines(perSide, interior);
        }

        // Rebuild the canonical wall skeleton to `perSide` interior lines per side with role `interior`,
        // preserving the eave (roof) + center (ridge) bay edits. Layout: [left eave] + perSide interior +
        // [center] + perSide interior + [right eave], lettered dense sequential, ridge on the center
        // line. The x's are placeholders (the grid derives the real positions); only the temp-grid lines
        // read them. Guard: if any Free Assembly (hand-placed) wall exists, the structure is left alone
        // (re-role only) so sub-walls aren't clobbered -- Insert/Remove-Wall reconciliation is deferred.
        private void RebuildWallLines(int perSide, BayRole interior)
        {
            foreach (WallSpec w in Walls)
                if (w.FreeAssembly)
                {
                    foreach (WallSpec iw in Walls)   // fall back to in-place re-role
                    {
                        if (iw.Role == BayRole.Eave || iw.Role == BayRole.Center || iw.Role == interior) continue;
                        iw.Role = interior;
                        foreach (BaySpec y in iw.Bays) { y.Role = interior; y.RebuildTimbers(); }
                    }
                    return;
                }

            // Snapshot the eave / center bays to carry roof + ridge edits across the rebuild.
            List<BaySpec> aBays = WallLeft()?.Bays;
            List<BaySpec> eBays = WallRight()?.Bays;
            List<BaySpec> cBays = WallCenter()?.Bays;

            const string Alpha = "ABCDEFGHJKLMNPQRSTUVWXYZ";
            double span = Span;
            double[] xs = CanonicalWallXs(span, perSide);   // 0 .. span, length = 2*perSide + 3
            int last = xs.Length - 1;

            var nw = new List<WallSpec>(xs.Length);
            for (int i = 0; i <= last; i++)
            {
                BayRole role = (i == 0 || i == last) ? BayRole.Eave
                             : (i == last / 2)        ? BayRole.Center
                             :                          interior;
                double sep = (i < last) ? xs[i + 1] - xs[i] : 0.0;
                string letter = Alpha[System.Math.Min(i, Alpha.Length - 1)].ToString();
                nw.Add(WallSpec.NewDefault(letter, right: (i == last), sep: sep, role: role));
            }
            Walls = nw;
            SyncBays();   // BayCount role-based bays per wall (catalog per role)

            // Overlay the preserved eave/center bays onto the matching new lines (by key).
            WallSpec na = WallLeft(), ne = WallRight(), nc = WallCenter();
            int bayN = BayCount;
            for (int i = 0; i < bayN; i++)
            {
                if (na != null && aBays != null && i < aBays.Count)
                { OverlayBay(na.Bays[i], aBays[i]); CopyRoofScalars(na.Bays[i], aBays[i]); na.Bays[i].SyncRoofType(); }
                if (ne != null && eBays != null && i < eBays.Count)
                { OverlayBay(ne.Bays[i], eBays[i]); CopyRoofScalars(ne.Bays[i], eBays[i]); ne.Bays[i].SyncRoofType(); }
                if (nc != null && cBays != null && i < cBays.Count)
                    OverlayKey(nc.Bays[i], cBays[i], "Ridge:R");
            }
        }

        // Placeholder across-span x's for the canonical lines: left eave 0; the perSide interior lines
        // on each side evenly spread between an eave and the center (hs*k/(perSide+1)); center hs; right
        // eave span. Length = 2*perSide + 3. For perSide=1 this is {0, span/4, hs, 3span/4, span} (the
        // existing seed spacing); for perSide=2, {0, span/6, span/3, hs, 2span/3, 5span/6, span}.
        private static double[] CanonicalWallXs(double span, int perSide)
        {
            double hs = span / 2.0;
            var xs = new double[2 * perSide + 3];
            int n = xs.Length, last = n - 1;
            xs[0] = 0.0; xs[last] = span; xs[last / 2] = hs;
            for (int k = 1; k <= perSide; k++)
            {
                double x = hs * k / (perSide + 1);
                xs[k] = x;               // left interior line k
                xs[last - k] = span - x; // mirrored right interior line
            }
            return xs;
        }

        // Overlay membership + size from a source bay onto a destination bay, by timber key (the eave/
        // floor/roof recipe is identical across rebuilds, so matching keys carry the user's edits).
        private static void OverlayBay(BaySpec dst, BaySpec src)
        {
            if (src == null) return;
            foreach (Timber c in dst.Timbers)
            {
                Timber t = src.Find(c.Key);
                if (t != null) { c.Enabled = t.Enabled; c.Size = t.Size.Clone(); }
            }
        }

        // Copy the flat commons/purlins schedule (stored on the bay, not as timbers) between bays.
        private static void CopyRoofScalars(BaySpec dst, BaySpec src)
        {
            if (src == null) return;
            dst.CommonMode = src.CommonMode; dst.CommonCount = src.CommonCount; dst.CommonSpacing = src.CommonSpacing;
            dst.CommonW = src.CommonW; dst.CommonD = src.CommonD;
            dst.PurlinMode = src.PurlinMode; dst.PurlinCount = src.PurlinCount; dst.PurlinSpacing = src.PurlinSpacing;
            dst.PurlinW = src.PurlinW; dst.PurlinD = src.PurlinD;
        }

        // Overlay a single timber (membership + size) by key between bays (used for the ridge).
        private static void OverlayKey(BaySpec dst, BaySpec src, string key)
        {
            Timber s = src?.Find(key); Timber d = dst.Find(key);
            if (s != null && d != null) { d.Enabled = s.Enabled; d.Size = s.Size.Clone(); }
        }
    }

    // How a thin in-bent member (brace/strut/vstrut) is justified across the wall thickness, measured
    // against the NARROWER of the two timbers it connects to. Default = inherit the frame's OffsetType.
    public enum Justify { Default, Back, Center, Front }

    // The frame-wide DEFAULT centering for braces/vstruts/struts (the value a member's Justify.Default
    // inherits). Mirrors OffsetType's int: Back=0, Center=1, Front=2.
    public enum Centering { Back, Center, Front }

    // Which longitudinal wall LINE a bay sits on -- selects its timber catalog (the eave girt+brace
    // recipe generalized per line). Derived from the vertical member at the line's x (the bent type):
    //   Eave   -- A/E: eave girt + braces, floor girt + braces, that side's commons/purlins.
    //   Center -- king-post/ridge line: the ridge + ridge->king-post braces.
    //   Queen  -- QueenPost B/D: queen-post girt + braces.
    //   Hammer -- HammerBeam interior lines: hammer-post girt + braces.
    //   Vstrut -- KingPost B/D: addressing-only (the vstrut sits here; nothing runs along it).
    // The non-eave kits are opt-in (seeded OFF). Lines materialize per bent geometry in step 2.
    public enum BayRole { Eave, Center, Queen, Hammer, Vstrut }

    // How a common-rafter TAIL (overhang past the eave girt) is cut: Plumb = a vertical fascia cut;
    // Square = perpendicular to the rafter. Per-eave-bay (on the Commons leaf).
    public enum TailCut { Plumb, Square }

    // The size of one timber. Only the fields relevant to the timber's role are used (a post
    // uses W/D; a brace adds Length/Angle; a strut adds Angle; the bent floor girt adds Ht).
    public class MemberSize
    {
        [DisplayName("Width")]  public double W { get; set; }
        [DisplayName("Depth")]  public double D { get; set; }
        [DisplayName("Length")] public double Length { get; set; }
        [DisplayName("Angle")]  public double Angle { get; set; }
        [DisplayName("Height")] public double Ht { get; set; }
        // Brace legs (the right-triangle the brace fills): Foot = VERTICAL leg down the post, Head =
        // HORIZONTAL leg along the girt; Angle from horizontal, tan(Angle) = Foot/Head. Two-of-three.
        [DisplayName("Foot")]   public double Foot { get; set; }
        [DisplayName("Head")]   public double Head { get; set; }
        // Per-member centering (brace/strut/vstrut only); Default inherits the frame's OffsetType.
        [DisplayName("Placement")] public Justify Place { get; set; } = Justify.Default;

        public MemberSize Clone() => new MemberSize { W = W, D = D, Length = Length, Angle = Angle, Ht = Ht, Foot = Foot, Head = Head, Place = Place };
    }

    // The universal spec-level timber: one member identified by Role + Designation (the
    // "Role:Designation" key the generator's AddEdge calls use). Each carries its OWN MemberSize.
    public class Timber
    {
        public string Role { get; set; }
        public string Designation { get; set; }
        public string Label { get; set; }          // display name on the tree leaf
        public bool Enabled { get; set; } = true;  // membership (the checkbox)
        public MemberSize Size { get; set; } = new MemberSize();

        [Browsable(false)] public string Key => Role + ":" + Designation;
    }

    // Base for a frame element (Bent / Wall / Bay).
    public abstract class FrameElement
    {
        [Category("0 Identity"), DisplayName("Name")]
        public string Name { get; set; } = "";
        [Browsable(false)] public abstract string Kind { get; }
    }

    // Common surface for an element that carries a universal timber list (bent or bay), so the
    // tree UI handles timbers uniformly.
    public interface IMemberOwner
    {
        string Name { get; }
        bool TypeIsSet { get; }
        List<Timber> Timbers { get; }
        void RebuildTimbers();                 // (re)emit the default timbers for the current type
        Timber Find(string key);               // by "Role:Designation"
        bool IsEnabled(string key);
        MemberSize SizeOf(string key);
    }

    // Type-set dropdowns for the PropertyGrid (exclusive = pick from list only).
    public class BentTypeConverter : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext c) => true;
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext c) => true;
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c)
            => new StandardValuesCollection(new[]
               { "", "KingPost", "QueenPost", "HammerBeam", "KingPostTruss", "QueenPostTruss" });
        // The unset bent type ("") shows + selects as "None".
        public override object ConvertTo(ITypeDescriptorContext c, System.Globalization.CultureInfo ci,
            object value, System.Type destType)
            => (destType == typeof(string) && string.IsNullOrEmpty(value as string)) ? "None"
               : base.ConvertTo(c, ci, value, destType);
        public override object ConvertFrom(ITypeDescriptorContext c, System.Globalization.CultureInfo ci,
            object value)
            => (value is string s && s == "None") ? "" : base.ConvertFrom(c, ci, value);
    }
    public class RoofTypeConverter : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext c) => true;
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext c) => true;
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c)
            => new StandardValuesCollection(new[] { "None", "Commons", "Purlins" });
    }

    // HBDivisor is stored as a full-span divisor (4 single tier, 6 stacked); the spec sheet shows
    // divisions per rafter slope = HBDivisor/2 -> "2" or "3". Exclusive dropdown; value unchanged.
    public class HBDivisorConverter : Int32Converter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext c) => true;
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext c) => true;
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c)
            => new StandardValuesCollection(new object[] { 4, 6 });

        public override object ConvertTo(ITypeDescriptorContext c, System.Globalization.CultureInfo ci,
            object value, System.Type destType)
            => (destType == typeof(string) && value is int i) ? (i / 2).ToString(ci)
               : base.ConvertTo(c, ci, value, destType);

        public override object ConvertFrom(ITypeDescriptorContext c, System.Globalization.CultureInfo ci,
            object value)
            => (value is string s && int.TryParse(s.Trim(), out int n))
               ? (object)(n <= 3 ? n * 2 : n)   // "2"->4, "3"->6; legacy "4"/"6" pass through
               : base.ConvertFrom(c, ci, value);
    }

    // One bent instance. BentType is unset ("") until the user picks it; Timbers is empty until
    // then, then RebuildTimbers() emits the default set. Separation = graph-Z distance to the NEXT
    // bent (the last bent's is unused).
    public class BentSpec : FrameElement, IMemberOwner
    {
        public override string Kind => "Bent";
        [Browsable(false)] public bool TypeIsSet =>
            BentType == "KingPost" || BentType == "QueenPost" || BentType == "HammerBeam"
            || BentType == "KingPostTruss" || BentType == "QueenPostTruss" || IsFreeAssembly;

        // A Free Assembly bent emits NO parametric geometry; its members are hand-placed managed
        // timbers assigned to it (TPlace + TAssign), listed drawing-derived in the tree.
        [Browsable(false)] public bool IsFreeAssembly => BentType == "Free Assembly";

        [Category("1 Type"), DisplayName("Bent Type"), TypeConverter(typeof(BentTypeConverter))]
        public string BentType { get; set; } = "";

        [Category("1 Layout"), DisplayName("Separation")]
        public double Separation { get; set; }

        // Non-size, type-global params (not per-timber).
        // Distance from the bent center to each queen post's INNER face (the face toward center), in
        // inches. Symmetric: the left inner face sits at Span/2 - QueenOffset (outer face one QueenD
        // further out), the right inner face at Span/2 + QueenOffset.
        [Category("9 Queen"), DisplayName("Center Offset")] public double QueenOffset { get; set; } = 48.0;
        // Tie elevation: the tie TOP sits this far below the rafter-foot line (TOH). 6" is the MINIMUM
        // (the heel/birdsmouth + king-post bearing need that clearance); the setter clamps to it. Raising
        // GirtDrop lowers the tie + everything riding on TOG/BOG (wall braces, king post, struts, vstruts).
        private double _girtDrop = 6.0;
        [Category("2 Tie"), DisplayName("Girt Offset (6\" min)")]
        public double GirtDrop
        {
            get => _girtDrop;
            set => _girtDrop = System.Math.Max(6.0, value);   // 6" is the minimum; sub-6 entries snap up
        }
        [Category("10 Rafter Divisions"), DisplayName("Divisions (2 or 3)"), TypeConverter(typeof(HBDivisorConverter))]
        public int HBDivisor { get; set; } = 4;

        // This bent's DEFAULT brace/strut/vstrut centering across the wall thickness (the value a member's
        // Justify.Default inherits). Was a single frame-wide setting; seeded from settings on NewDefault.
        [Browsable(false)] public int OffsetType { get; set; }
        [Category("3 Bracing"), DisplayName("Brace V/Strut Ctr")]
        public Centering BraceCentering { get => (Centering)OffsetType; set => OffsetType = (int)value; }

        [Browsable(false)] public List<Timber> Timbers { get; set; } = new List<Timber>();

        public Timber Find(string key)
        {
            foreach (Timber t in Timbers) if (t.Key == key) return t;
            return null;
        }
        public bool IsEnabled(string key) { Timber t = Find(key); return t != null && t.Enabled; }
        public MemberSize SizeOf(string key) => Find(key)?.Size;

        // FACTORY: the default timbers for the current BentType (every L/R pair split, all
        // enabled, each with its own MemberSize seeded from settings).
        public void RebuildTimbers()
        {
            Timbers = BuildDefaultTimbers(BentType);
        }

        public static List<Timber> BuildDefaultTimbers(string bentType)
        {
            KPBentParams p = Commands.ReadKPParams();
            var list = new List<Timber>();
            if (string.IsNullOrEmpty(bentType)) return list;

            // Size builders by role family.
            MemberSize WD(double w, double d) => new MemberSize { W = w, D = d };
            MemberSize WDLA(double w, double d, double len, double ang) => new MemberSize
                { W = w, D = d, Length = len, Angle = ang, Head = len, Foot = len * System.Math.Tan(ang * System.Math.PI / 180.0) };
            MemberSize WDA(double w, double d, double ang) => new MemberSize { W = w, D = d, Angle = ang };
            MemberSize WDH(double w, double d, double ht) => new MemberSize { W = w, D = d, Ht = ht };
            void Add(string role, string desig, string label, MemberSize size)
                => list.Add(new Timber { Role = role, Designation = desig, Label = label, Enabled = true, Size = size });

            bool wallPosts = bentType == "KingPost" || bentType == "QueenPost" || bentType == "HammerBeam";

            // Wall posts (all but trusses).
            if (wallPosts)
            {
                Add("Post", "A", "Post L", WD(p.PostW, p.PostD));
                Add("Post", "E", "Post R", WD(p.PostW, p.PostD));
            }

            switch (bentType)
            {
                case "KingPost":
                    Add("Girt", "AE", "Girt", WD(p.GirtW, p.GirtD));
                    Add("Rafter", "A", "Rafter L", WD(p.RafterW, p.RafterD));
                    Add("Rafter", "E", "Rafter R", WD(p.RafterW, p.RafterD));
                    Add("KingPost", "C", "King Post", WD(p.KpostW, p.KpostD));
                    Add("Brace", "A", "Brace L", WDLA(p.BraceW, p.BraceD, p.BraceLength, p.BraceAngle));
                    Add("Brace", "E", "Brace R", WDLA(p.BraceW, p.BraceD, p.BraceLength, p.BraceAngle));
                    Add("Strut", "A", "Strut L", WDA(p.StrutW, p.StrutD, p.StrutAngle));
                    Add("Strut", "E", "Strut R", WDA(p.StrutW, p.StrutD, p.StrutAngle));
                    Add("VStrut", "A", "Vert Strut L", WD(p.VStrutW, p.VStrutD));
                    Add("VStrut", "E", "Vert Strut R", WD(p.VStrutW, p.VStrutD));
                    Add("FloorGirt", "FG", "Floor Girt", WDH(p.GirtW, p.GirtD, p.FloorGirtHt));
                    Add("FloorBrace", "A", "Floor Brace L", WDLA(p.BraceW, p.BraceD, p.BraceLength, p.BraceAngle));
                    Add("FloorBrace", "E", "Floor Brace R", WDLA(p.BraceW, p.BraceD, p.BraceLength, p.BraceAngle));
                    break;

                case "QueenPost":
                    Add("Girt", "AE", "Girt (tie)", WD(p.GirtW, p.GirtD));
                    Add("Rafter", "A", "Rafter L", WD(p.RafterW, p.RafterD));
                    Add("Rafter", "E", "Rafter R", WD(p.RafterW, p.RafterD));
                    Add("Queen", "B", "Queen Post L", WD(p.PostW, p.PostD));
                    Add("Queen", "D", "Queen Post R", WD(p.PostW, p.PostD));
                    Add("UpperGirt", "BD", "Straining Beam", WD(p.GirtW, p.GirtD));
                    Add("Strut", "A", "Queen Strut L", WDA(p.StrutW, p.StrutD, p.StrutAngle));
                    Add("Strut", "E", "Queen Strut R", WDA(p.StrutW, p.StrutD, p.StrutAngle));
                    Add("Brace", "A", "Wall Brace L", WDLA(p.BraceW, p.BraceD, p.BraceLength, p.BraceAngle));
                    Add("Brace", "E", "Wall Brace R", WDLA(p.BraceW, p.BraceD, p.BraceLength, p.BraceAngle));
                    Add("QueenBrace", "B", "Queen Brace L", WDLA(p.BraceW, p.BraceD, p.BraceLength, p.BraceAngle));
                    Add("QueenBrace", "D", "Queen Brace R", WDLA(p.BraceW, p.BraceD, p.BraceLength, p.BraceAngle));
                    Add("FloorGirt", "FG", "Floor Girt", WDH(p.GirtW, p.GirtD, p.FloorGirtHt));
                    Add("FloorBrace", "A", "Floor Brace L", WDLA(p.BraceW, p.BraceD, p.BraceLength, p.BraceAngle));
                    Add("FloorBrace", "E", "Floor Brace R", WDLA(p.BraceW, p.BraceD, p.BraceLength, p.BraceAngle));
                    break;

                case "HammerBeam":
                    Add("Rafter", "A", "Rafter L", WD(p.RafterW, p.RafterD));
                    Add("Rafter", "E", "Rafter R", WD(p.RafterW, p.RafterD));
                    Add("HBeam", "A", "Hammer Beam L", WD(p.GirtW, p.GirtD));
                    Add("HBeam", "E", "Hammer Beam R", WD(p.GirtW, p.GirtD));
                    Add("HPost", "A", "Hammer Post L", WD(p.PostW, p.PostD));
                    Add("HPost", "E", "Hammer Post R", WD(p.PostW, p.PostD));
                    Add("Collar", "AE", "Collar", WD(p.GirtW, p.GirtD));
                    Add("KingPost", "C", "King Post", WD(p.KpostW, p.KpostD));
                    Add("Brace", "A", "Wall Brace L", WDLA(p.BraceW, p.BraceD, p.BraceLength, p.BraceAngle));
                    Add("Brace", "E", "Wall Brace R", WDLA(p.BraceW, p.BraceD, p.BraceLength, p.BraceAngle));
                    Add("CollarBrace", "A", "Collar Brace L", WDLA(p.BraceW, p.BraceD, p.BraceLength, p.BraceAngle));
                    Add("CollarBrace", "E", "Collar Brace R", WDLA(p.BraceW, p.BraceD, p.BraceLength, p.BraceAngle));
                    Add("FloorGirt", "FG", "Floor Girt", WDH(p.GirtW, p.GirtD, p.FloorGirtHt));
                    Add("FloorBrace", "A", "Floor Brace L", WDLA(p.BraceW, p.BraceD, p.BraceLength, p.BraceAngle));
                    Add("FloorBrace", "E", "Floor Brace R", WDLA(p.BraceW, p.BraceD, p.BraceLength, p.BraceAngle));
                    break;

                case "KingPostTruss":
                    Add("Girt", "AE", "Tie", WD(p.GirtW, p.GirtD));
                    Add("Rafter", "A", "Rafter L", WD(p.RafterW, p.RafterD));
                    Add("Rafter", "E", "Rafter R", WD(p.RafterW, p.RafterD));
                    Add("KingPost", "C", "King Post", WD(p.KpostW, p.KpostD));
                    Add("Strut", "A", "Strut L", WDA(p.StrutW, p.StrutD, p.StrutAngle));
                    Add("Strut", "E", "Strut R", WDA(p.StrutW, p.StrutD, p.StrutAngle));
                    Add("VStrut", "A", "Vert Strut L", WD(p.VStrutW, p.VStrutD));
                    Add("VStrut", "E", "Vert Strut R", WD(p.VStrutW, p.VStrutD));
                    break;

                case "QueenPostTruss":
                    Add("Girt", "AE", "Tie", WD(p.GirtW, p.GirtD));
                    Add("Rafter", "A", "Rafter L", WD(p.RafterW, p.RafterD));
                    Add("Rafter", "E", "Rafter R", WD(p.RafterW, p.RafterD));
                    Add("Queen", "B", "Queen Post L", WD(p.PostW, p.PostD));
                    Add("Queen", "D", "Queen Post R", WD(p.PostW, p.PostD));
                    Add("UpperGirt", "BD", "Straining Beam", WD(p.GirtW, p.GirtD));
                    Add("Strut", "A", "Queen Strut L", WDA(p.StrutW, p.StrutD, p.StrutAngle));
                    Add("Strut", "E", "Queen Strut R", WDA(p.StrutW, p.StrutD, p.StrutAngle));
                    Add("QueenBrace", "B", "Queen Brace L", WDLA(p.BraceW, p.BraceD, p.BraceLength, p.BraceAngle));
                    Add("QueenBrace", "D", "Queen Brace R", WDLA(p.BraceW, p.BraceD, p.BraceLength, p.BraceAngle));
                    break;
            }
            return list;
        }

        public static BentSpec NewDefault()
        {
            KPBentParams p = Commands.ReadKPParams();
            double sep = (p.BaySpacings != null && p.BaySpacings.Length > 0) ? p.BaySpacings[0] : 96.0;
            return new BentSpec { BentType = "", Separation = sep, QueenOffset = 48.0, HBDivisor = 4, GirtDrop = 6.0, OffsetType = p.OffsetType };
        }
    }

    // One longitudinal wall (A left / B right). Carries Separation = graph-X distance to the NEXT
    // wall (across the span; the last wall's is unused) and a parallel list of bay cells -- one per
    // inter-bent interval. A wall-bay owns this wall's longitudinal members for its interval.
    public class WallSpec : FrameElement
    {
        public override string Kind => "Wall";

        [Category("1 Identity"), DisplayName("Wall Letter")] public string Letter { get; set; } = "A";
        [Browsable(false)] public bool RightSide { get; set; }
        [Category("1 Layout"), DisplayName("Separation")] public double Separation { get; set; }
        // Interior walls are hosts for hand-placed (TPlace + TAssign) timbers -- no parametric emission.
        // DERIVED by position in RenumberWalls (eaves = false, interior = true); not user-editable.
        [Browsable(false)] public bool FreeAssembly { get; set; }

        // The line's role -- selects the longitudinal catalog its bays carry (eave girt/roof, ridge,
        // queen/hammer girt, or addressing-only vstrut). Anchored to the bent's vertical member at this
        // line's x. Default Eave (legacy two-wall frames are both eaves).
        [Browsable(false)] public BayRole Role { get; set; } = BayRole.Eave;

        [Browsable(false)] public List<BaySpec> Bays { get; set; } = new List<BaySpec>();

        public static WallSpec NewDefault(string letter, bool right, double sep, BayRole role = BayRole.Eave)
            => new WallSpec { Letter = letter, Name = letter, RightSide = right, Separation = sep, Role = role };
    }

    // One bay CELL: the derived interval between two adjacent bents, on ONE wall. It owns that wall's
    // longitudinal members for the interval (SINGLE-SIDED -- the side comes from the owning wall, so
    // keys are neutral "Role:S"). The ridge + roof (commons/purlins) live ONLY on Wall A's bay (the
    // central members, like FrameWalls' ridge->A). Spacing now lives on the bents, not here.
    public class BaySpec : FrameElement, IMemberOwner
    {
        public override string Kind => "Bay";
        // A bay ALWAYS carries its full (single-sided) catalog; RoofType is DERIVED from which roof
        // checkbox is on (SyncRoofType).
        [Browsable(false)] public bool TypeIsSet => true;

        // True when owned by Wall A (left) -- only then does the catalog include the ridge + roof.
        [Browsable(false)] public bool OwnerIsWallA { get; set; } = true;

        // Which wall LINE this bay sits on -- selects its longitudinal catalog (BuildDefaultTimbers).
        // Default Eave; the Center/Queen/Hammer/Vstrut lines are assigned when step 2 materializes the
        // A-E lines from bent geometry. Until then every bay is an Eave (the OwnerIsWallA path).
        [Browsable(false)] public BayRole Role { get; set; } = BayRole.Eave;

        [Browsable(false)] public string RoofType { get; set; } = "None";

        // Bay-span hook (future joists): the inclusive interval range a member covers. -1 = single-bay
        // (its own interval). Members are single-bay this pass; the shop label is "<wall> <startRoman>-<endRoman>".
        [Browsable(false)] public int BayStart { get; set; } = -1;
        [Browsable(false)] public int BayEnd { get; set; } = -1;

        // This bay's DEFAULT brace centering across the wall thickness (its eave/floor braces; the value a
        // member's Justify.Default inherits). Was a single frame-wide setting; seeded from settings on NewDefault.
        [Browsable(false)] public int OffsetType { get; set; }
        [Category("2 Bracing"), DisplayName("Brace V/Strut Ctr")]
        public Centering BraceCentering { get => (Centering)OffsetType; set => OffsetType = (int)value; }

        // Derive RoofType from the mutually-exclusive roof checkboxes (the single source of truth).
        public void SyncRoofType()
            => RoofType = IsEnabled("Commons:X") ? "Commons" : IsEnabled("Purlins:X") ? "Purlins" : "None";

        // Short roof tag for the tree label: "Common" / "Purlin" / "None".
        public string RoofShort()
            => RoofType == "Commons" ? "Common" : RoofType == "Purlins" ? "Purlin" : "None";

        [Category("3 Commons"), DisplayName("Common Mode (0 count,1 spacing)")] public int CommonMode { get; set; }
        [Category("3 Commons"), DisplayName("Common Count")]   public int CommonCount { get; set; }
        [Category("3 Commons"), DisplayName("Common Spacing")] public double CommonSpacing { get; set; }
        [Category("3 Commons"), DisplayName("Common W")]       public double CommonW { get; set; }
        [Category("3 Commons"), DisplayName("Common D")]       public double CommonD { get; set; }
        // Common-rafter TAIL: horizontal overhang run past the eave girt outer face (0 = no tail), and how
        // its end is cut. Per eave bay (each slope's commons own their overhang).
        [Category("3 Commons"), DisplayName("Tail")]           public double CommonTail { get; set; }
        [Category("3 Commons"), DisplayName("Tail Cut")]       public TailCut CommonTailCut { get; set; } = TailCut.Plumb;

        [Category("4 Purlins"), DisplayName("Purlin Mode (0 count,1 spacing)")] public int PurlinMode { get; set; }
        [Category("4 Purlins"), DisplayName("Purlin Count")]   public int PurlinCount { get; set; }
        [Category("4 Purlins"), DisplayName("Purlin Spacing")] public double PurlinSpacing { get; set; }
        [Category("4 Purlins"), DisplayName("Purlin W")]       public double PurlinW { get; set; }
        [Category("4 Purlins"), DisplayName("Purlin D")]       public double PurlinD { get; set; }

        [Browsable(false)] public List<Timber> Timbers { get; set; } = new List<Timber>();

        public Timber Find(string key)
        {
            foreach (Timber t in Timbers) if (t.Key == key) return t;
            return null;
        }
        public bool IsEnabled(string key) { Timber t = Find(key); return t != null && t.Enabled; }
        public MemberSize SizeOf(string key) => Find(key)?.Size;

        public void RebuildTimbers()
        {
            Timbers = BuildDefaultTimbers(Role);
        }

        // Backward-compat: an older bay stored ONE eave/floor brace per wall ("Role:S"). Split each
        // into the two bay-end timbers (":L" low-Y / ":R" high-Y), cloning the old size onto both ends
        // and carrying its Enabled. Idempotent -- a no-op once already split.
        public void NormalizeBraces()
        {
            if (Timbers == null) return;
            void Split(string role)
            {
                Timber s = Find(role + ":S");
                if (s == null || Find(role + ":L") != null || Find(role + ":R") != null) return;
                Timbers.Remove(s);
                string baseLabel = role == "EaveBrace" ? "Eave Brace" : "Floor Girt Brace";
                Timbers.Add(new Timber { Role = role, Designation = "L", Label = baseLabel + " L",
                    Enabled = s.Enabled, Size = s.Size.Clone() });
                Timbers.Add(new Timber { Role = role, Designation = "R", Label = baseLabel + " R",
                    Enabled = s.Enabled, Size = s.Size.Clone() });
            }
            Split("EaveBrace");
            Split("FloorBrace");
        }

        // The per-line longitudinal catalog (SINGLE-SIDED -- keys are neutral, the owning wall supplies
        // L/R + side at generation). One kit per BayRole; the eave girt+brace recipe is generalized to
        // every line. New members (ridge/queen/hammer braces + girts) seed OFF, like commons/purlins.
        public static List<Timber> BuildDefaultTimbers(BayRole role)
        {
            KPBentParams p = Commands.ReadKPParams();
            var list = new List<Timber>();

            MemberSize WD(double w, double d) => new MemberSize { W = w, D = d };
            MemberSize WDH(double w, double d, double ht) => new MemberSize { W = w, D = d, Ht = ht };
            MemberSize WDLA(double w, double d, double len, double ang) => new MemberSize
                { W = w, D = d, Length = len, Angle = ang, Head = len, Foot = len * System.Math.Tan(ang * System.Math.PI / 180.0) };
            MemberSize Brace() => WDLA(p.BraceW, p.BraceD, p.BraceLength, p.BraceAngle);
            void Add(string role_, string desig, string label, MemberSize size, bool enabled = true)
                => list.Add(new Timber { Role = role_, Designation = desig, Label = label, Enabled = enabled, Size = size });

            switch (role)
            {
                case BayRole.Eave:
                    // Eave girt carries its OWN top elevation (Ht), seeded to EaveHt + 1 (the 1" reveal).
                    // Lowering it drops the girt; the rafter birdsmouth + eave braces follow. Uniform with
                    // the floor girt (top = Ht). Per eave bay, per side.
                    Add("EaveGirt", "S", "Eave Girt", WDH(p.GirtW, p.GirtD, p.EaveHt + 1.0));
                    // Eave/floor braces split into the bay's two ends (L = low-Y bent, R = high-Y bent),
                    // each independently sized/enabled/placed. Both seeded identically from settings.
                    Add("EaveBrace", "L", "Eave Brace L", Brace());
                    Add("EaveBrace", "R", "Eave Brace R", Brace());
                    // Bay floor girt carries its OWN elevation (Ht), defaulting to the bent floor girt's
                    // height so it aligns unless the user steps this bay's floor line.
                    Add("FloorGirt", "S", "Floor Girt", WDH(p.GirtW, p.GirtD, p.FloorGirtHt));
                    Add("FloorBrace", "L", "Floor Girt Brace L", Brace());
                    Add("FloorBrace", "R", "Floor Girt Brace R", Brace());
                    // Each eave carries ITS slope's commons/purlins (default OFF, mutually exclusive);
                    // the owning wall's side picks the slope at generation.
                    Add("Commons", "X", "Commons", WD(p.CommonW, p.CommonD), enabled: false);
                    Add("Purlins", "X", "Purlins", WD(p.PurlinW, p.PurlinD), enabled: false);
                    break;

                case BayRole.Center:
                    Add("Ridge", "R", "Ridge", WD(p.RidgeW, p.RidgeD));
                    Add("RidgeBrace", "L", "Ridge Brace L", Brace(), enabled: false);
                    Add("RidgeBrace", "R", "Ridge Brace R", Brace(), enabled: false);
                    break;

                case BayRole.Queen:
                    Add("QueenGirt", "S", "Queen Girt", WD(p.GirtW, p.GirtD), enabled: false);
                    Add("QueenGirtBrace", "L", "Queen Girt Brace L", Brace(), enabled: false);
                    Add("QueenGirtBrace", "R", "Queen Girt Brace R", Brace(), enabled: false);
                    break;

                case BayRole.Hammer:
                    Add("HPostGirt", "S", "Hammer Post Girt", WD(p.GirtW, p.GirtD), enabled: false);
                    Add("HPostGirtBrace", "L", "Hammer Post Girt Brace L", Brace(), enabled: false);
                    Add("HPostGirtBrace", "R", "Hammer Post Girt Brace R", Brace(), enabled: false);
                    break;

                case BayRole.Vstrut:
                    break;   // addressing-only: the vstrut sits on the line, nothing runs along it
            }
            return list;
        }

        // LEGACY/migration compat: the old left/right-eave bool maps to the Eave catalog, and the LEFT
        // eave additionally carries the ridge -- so a two-wall frame (no Center line) still emits the
        // ridge from Wall A via Merge. New (five-wall) frames don't use this path: their walls build
        // from BayRole, so the ridge lives on the Center line. Both eaves carry their own commons/purlins.
        public static List<Timber> BuildDefaultTimbers(bool isWallA)
        {
            List<Timber> list = BuildDefaultTimbers(BayRole.Eave);
            if (isWallA) list.AddRange(BuildDefaultTimbers(BayRole.Center));
            return list;
        }

        // New (role-based) bay: catalog per the owning line's role; the caller (SyncBays/InsertBent)
        // sets OwnerIsWallA to mark the left-eave bay. Roof scalars are seeded for every bay (used only
        // by eave bays, harmless elsewhere).
        public static BaySpec NewDefault(BayRole role)
        {
            KPBentParams p = Commands.ReadKPParams();
            return new BaySpec
            {
                Role = role, OwnerIsWallA = (role == BayRole.Eave), RoofType = "None", OffsetType = p.OffsetType,
                CommonMode = p.CommonMode, CommonCount = p.CommonCount, CommonSpacing = p.CommonSpacing,
                CommonW = p.CommonW, CommonD = p.CommonD,
                PurlinMode = p.PurlinMode, PurlinCount = p.PurlinCount, PurlinSpacing = p.PurlinSpacing,
                PurlinW = p.PurlinW, PurlinD = p.PurlinD,
                Timbers = BuildDefaultTimbers(role)
            };
        }

        public static BaySpec NewDefault(bool isWallA)
        {
            KPBentParams p = Commands.ReadKPParams();
            return new BaySpec
            {
                OwnerIsWallA = isWallA, RoofType = "None", OffsetType = p.OffsetType,
                CommonMode = p.CommonMode, CommonCount = p.CommonCount, CommonSpacing = p.CommonSpacing,
                CommonW = p.CommonW, CommonD = p.CommonD,
                PurlinMode = p.PurlinMode, PurlinCount = p.PurlinCount, PurlinSpacing = p.PurlinSpacing,
                PurlinW = p.PurlinW, PurlinD = p.PurlinD,
                Timbers = BuildDefaultTimbers(isWallA)
            };
        }

        // Build a transient LEGACY-shaped bay (full L/R catalog) for the generator from the aligned
        // wall-bays of one interval: Wall A's single-sided members map to ":L", Wall E's to ":R". The
        // ridge comes from the CENTER line's bay (`center`); when absent (legacy two-wall frames) it
        // falls back to Wall A. Commons/roof config still come from Wall A (Step 3 splits per side).
        // The existing side-gated builders consume this unchanged; sizes are shared by reference.
        public static BaySpec Merge(BaySpec a, BaySpec b, BaySpec center = null)
        {
            BaySpec ridgeSrc = center ?? a;
            var m = new BaySpec { OwnerIsWallA = true, OffsetType = a?.OffsetType ?? 0 };
            if (a != null)
            {
                m.RoofType = a.RoofType;
                m.CommonMode = a.CommonMode; m.CommonCount = a.CommonCount; m.CommonSpacing = a.CommonSpacing;
                m.CommonW = a.CommonW; m.CommonD = a.CommonD;
                m.PurlinMode = a.PurlinMode; m.PurlinCount = a.PurlinCount; m.PurlinSpacing = a.PurlinSpacing;
                m.PurlinW = a.PurlinW; m.PurlinD = a.PurlinD;
            }
            m.Timbers = new List<Timber>();
            void Map(BaySpec src, string neutralKey, string role, string desig, string label)
            {
                Timber t = src?.Find(neutralKey);
                if (t == null) return;
                MemberSize sz = t.Size;
                // Bake the SOURCE wall-bay's centering into a Default-placed brace, so the flattened merged bay
                // (one OffsetType) still honors each wall-bay's own setting; explicit per-member overrides are
                // kept (OffsetType 0/1/2 -> Justify.Back/Center/Front). The merged bay is transient, so cloning
                // the size here is safe.
                if (role.EndsWith("Brace") && sz.Place == Justify.Default)
                    { sz = sz.Clone(); sz.Place = (Justify)(src.OffsetType + 1); }
                m.Timbers.Add(new Timber { Role = role, Designation = desig, Label = label, Enabled = t.Enabled, Size = sz });
            }
            Map(a, "EaveGirt:S",  "EaveGirt",  "L", "Eave Girt L");
            Map(b, "EaveGirt:S",  "EaveGirt",  "R", "Eave Girt R");
            // Braces are split per bay-end (L low-Y / R high-Y) on EACH wall: merged designation =
            // wall (A left / E right) + end (L/R), so a bay has up to four of each brace.
            Map(a, "EaveBrace:L", "EaveBrace", "AL", "Eave Brace A-L");
            Map(a, "EaveBrace:R", "EaveBrace", "AR", "Eave Brace A-R");
            Map(b, "EaveBrace:L", "EaveBrace", "EL", "Eave Brace E-L");
            Map(b, "EaveBrace:R", "EaveBrace", "ER", "Eave Brace E-R");
            Map(a, "FloorGirt:S", "FloorGirt", "L", "Floor Girt L");
            Map(b, "FloorGirt:S", "FloorGirt", "R", "Floor Girt R");
            Map(a, "FloorBrace:L","FloorBrace","AL", "Floor Brace A-L");
            Map(a, "FloorBrace:R","FloorBrace","AR", "Floor Brace A-R");
            Map(b, "FloorBrace:L","FloorBrace","EL", "Floor Brace E-L");
            Map(b, "FloorBrace:R","FloorBrace","ER", "Floor Brace E-R");
            Map(ridgeSrc, "Ridge:R", "Ridge",   "R", "Ridge");
            Map(a, "Commons:X",   "Commons",   "X", "Commons");
            Map(a, "Purlins:X",   "Purlins",   "X", "Purlins");
            return m;
        }
    }
}
