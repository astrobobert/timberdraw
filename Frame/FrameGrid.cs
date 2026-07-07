using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{
    // The STRUCTURAL GRID of a frame: numbered BENT lines (transverse, spaced along the building) x
    // lettered COLUMN lines (longitudinal, across the span) at every vertical member's position. It is
    // the single source that controls each timber's installer label and the backbone drawn for shop
    // drawings.
    //
    // Built from the emitter's per-edge TFrames (graph coords: X = across span, Y = height, Z = along
    // building) -- NOT the raw graph nodes, because some reference lines are diagonal (the king post's
    // base->apex). The TFrame already un-skews: f.O is the true centerline point and f.Z the true length
    // axis. A VERTICAL member (|f.Z.Y| ~ 1) sits on a column line (its f.O.X) and marks a bent line
    // (its f.O.Z). Letters run A,B,C.. left->right (skipping I and O); numbers 1,2,3.. along the building.
    public class FrameGrid
    {
        public List<double> ColX = new List<double>();      // column-line across-span coords (sorted)
        public List<bool>   ColGround = new List<bool>();   // parallel to ColX: vertical reaches the floor
        public List<double> BentZ = new List<double>();     // bent-line building-length coords (sorted)
        private readonly List<double> _floorGirtZ = new List<double>();  // bents that carry a floor girt

        private const double Tol = 2.0;          // cluster tolerance (inches): members "on the same line"
        private const double VertDot = 0.9;      // |f.Z . up| above this = a vertical member
        private const double GroundTol = 1.0;    // a vertical foot within this of Y=0 reaches the floor

        // Column letters skip I and O to avoid confusion with 1 and 0.
        private const string Alpha = "ABCDEFGHJKLMNPQRSTUVWXYZ";

        // Build the grid from the emitter's graph-coordinate frames. EVERY vertical member defines a
        // column line (eave posts AND elevated vstruts / king posts / queens / hammer posts), plus the
        // ridge's center line -- so the columns are the full semantic A-E line set, lettered dense
        // sequential. The ground flag marks which reach the floor (posts) vs are elevated (for color +
        // the center clustering); BENT lines come from floor-meeting posts only.
        public static FrameGrid Build(List<(FrameEdge e, ManagedTimber.TFrame f)> frames)
        {
            var g = new FrameGrid();
            var cols  = new List<(double x, bool ground)>();
            var bents = new List<double>();
            foreach (var (e, f) in frames)
            {
                if (e.Longitudinal) continue;                    // runs along the building, not a column
                if (e.Role == "Girt" && e.Designation == "FG")
                    InsertZ(g._floorGirtZ, f.O.Z);               // this bent carries a floor girt (Dn/Up)
                if (System.Math.Abs(f.Z.Y) < VertDot) continue;  // not vertical -> not a column line
                double footY = System.Math.Min(f.O.Y, (f.O + f.Z * f.L).Y);
                bool ground = footY <= GroundTol;                // reaches the floor (post) vs elevated
                ClusterCol(cols, f.O.X, ground);                 // every vertical is a column line
                if (ground) InsertZ(bents, f.O.Z);               // bent lines from floor-meeting posts only
            }
            AddCenterCol(cols);                                  // the ridge/center line
            cols.Sort((p, q) => p.x.CompareTo(q.x));
            foreach (var c in cols) { g.ColX.Add(c.x); g.ColGround.Add(c.ground); }
            bents.Sort();
            g.BentZ.AddRange(bents);
            return g;
        }

        // Build the grid by SCANNING THE DRAWING: every managed timber stamped with `frameTag` (the
        // parametric-emitted ones AND sub timbers placed via TPlace + assigned via TAssign). Each WCS
        // frame is mapped back to graph coords by the inverse placement (graph X = across, Y = height,
        // Z = along-building), so the same floor-meeting test + clustering applies and adding/removing a
        // sub shifts the numbering. Assumes the frame was drawn at this placement (standard UCS=WCS).
        public static FrameGrid BuildFromDrawing(Database db, string frameTag, Matrix3d placement)
        {
            var g = new FrameGrid();
            Matrix3d inv = placement.Inverse();
            var cols  = new List<(double x, bool ground)>();
            var bents = new List<double>();
            foreach (ManagedTimber.TFrame wf in ManagedTimber.EnumerateFrameFrames(db, frameTag))
            {
                ManagedTimber.TFrame f = ManagedTimber.TransformFrame(wf, inv);   // WCS -> graph
                if (System.Math.Abs(f.Z.Y) < VertDot) continue;                   // not vertical (longitudinals skip)
                double footY = System.Math.Min(f.O.Y, (f.O + f.Z * f.L).Y);
                bool ground = footY <= GroundTol;                                 // post (floor) vs elevated vertical
                ClusterCol(cols, f.O.X, ground);                                  // every vertical is a column line
                if (ground) InsertZ(bents, f.O.Z);                                // bent lines from floor-meeting posts only
            }
            AddCenterCol(cols);                                                   // ridge/center line
            cols.Sort((p, q) => p.x.CompareTo(q.x));
            foreach (var c in cols) { g.ColX.Add(c.x); g.ColGround.Add(c.ground); }
            bents.Sort();
            g.BentZ.AddRange(bents);
            return g;
        }

        // The installer label for a bent member, TYPE-FIRST (user convention, 2026-07-03): family code
        // + the grid anchor -- vertical -> FAM-bent+column ("P-2A", "KP-2C"); spanning -> FAM-bent+the
        // two end columns ("RF-1AC", "TG-1AE"). The prefix groups the cut list by family AND keeps two
        // members at one anchor distinct (a free post beside the king post: P-2C vs KP-2C -- without it
        // the shop-map label dedup silently dropped one). Longitudinal members get no grid label here
        // (they keep the Wall/Bay grouping). Braces / commons / purlins are labeled by the emitter
        // (group symbol / consecutive number), not here. Empty when the grid has no columns.
        //
        // LEVEL QUALIFIER (user convention): when a bent carries a FLOOR girt, the two same-span girts
        // are told apart by level -- the floor girt reads "-Dn" (down) and the bent/tie girt "-Up".
        // A tie girt in a bent with no floor girt stays unqualified.
        public string LabelForEdge(FrameEdge e, ManagedTimber.TFrame f)
        {
            if (e.Longitudinal || ColX.Count == 0) return "";
            string fam = BentFamilyCode(e.Role, e.Designation);
            string pre = fam.Length > 0 ? fam + "-" : "";
            string bent = !string.IsNullOrEmpty(e.BentTag) ? e.BentTag : BentNumberAt(f.O.Z);
            if (System.Math.Abs(f.Z.Y) >= VertDot)
                return pre + bent + ColLetter(f.O.X);            // vertical -> single column

            double x0 = f.O.X, x1 = (f.O + f.Z * f.L).X;         // spanning -> the two end columns
            string a = ColLetter(System.Math.Min(x0, x1));
            string b = ColLetter(System.Math.Max(x0, x1));
            string label = pre + (a == b ? bent + a : bent + a + b);

            if (e.Role == "Girt")
            {
                if (e.Designation == "FG") return label + "-Dn";                     // the floor girt
                if (e.Designation == "AE" && NearAny(_floorGirtZ, f.O.Z))
                    return label + "-Up";                                            // tie above a floor girt
            }
            return label;
        }

        // Abbreviated family code LEADING a bent-member label (KP-2C), per the agreed label
        // conventions (structural-timber-label-conventions): P/KP/QP/ST/VS/HB/HP/TG/FG. Rafter is RF
        // here (the conventions' R already ships as the RIDGE's bay code, R-C-I). The girt splits by
        // designation (FG floor girt vs TG for the tie + other bent-plane girts). Unknown roles stay
        // bare (the anchor alone, the pre-2026-07-03 form).
        private static string BentFamilyCode(string role, string desig)
        {
            switch (role)
            {
                case "Post":      return "P";
                case "KingPost":  return "KP";
                case "QueenPost": return "QP";
                case "Rafter":    return "RF";
                case "Strut":     return "ST";
                case "VStrut":    return "VS";
                case "HBeam":     return "HB";
                case "HPost":     return "HP";
                case "Girt":      return desig == "FG" ? "FG" : "TG";
                // The transverse grade sill: "SL-1AE". The family code IS the level marker (the
                // design memo's "-Sl" qualifier predates type-first labels; TG-1AE / FG-1AE-Dn /
                // SL-1AE are already distinct).
                case "Sill":      return "SL";
                default:          return "";
            }
        }

        // ---- brace / commons / purlin labels (stamped by the emitter) ------------------------------

        // Per-emit state (the grid is rebuilt each Emit, so these reset with it).
        private readonly List<string> _braceSigs = new List<string>();
        private readonly Dictionary<string, int> _roofSeq = new Dictionary<string, int>();

        // A brace carries only a GROUP label: every brace of the same SIZE shares one symbol -- the first
        // group "*", the next "**", and so on (no bent / column letters). Grouped by CROSS-SECTION (W x D)
        // ONLY, so a bent knee brace and a wall/bay brace of the same size share a symbol (their nominal box
        // LENGTH differs by convention -- a bent brace stores its centerline, a wall FreeBox brace an
        // over-long padded length -- so length is deliberately left out of the key).
        public string BraceLabel(FrameEdge e, ManagedTimber.TFrame f)
        {
            string sig = Q(f.W) + "x" + Q(f.D);
            int idx = _braceSigs.IndexOf(sig);
            if (idx < 0) { _braceSigs.Add(sig); idx = _braceSigs.Count - 1; }
            return new string('*', idx + 1);
        }

        // A longitudinal WALL/BAY girt's locating label: FAM-WALL-BAY, family-first (groups the cut list by
        // family + distinguishes from the digit-first bent labels "1A"). One member per (family, wall, bay),
        // so no sequence needed -- unique + locating. WALL = the grid column letter (the ridge reads center
        // "C"); BAY = the Roman bay numeral (e.BayTag). Bay omitted only on a single-bent graph (blank tag).
        public string WallBayLabel(FrameEdge e, string wall)
        {
            string label = FamilyCode(e.Role) + "-" + wall;
            return string.IsNullOrEmpty(e.BayTag) ? label : label + "-" + e.BayTag;
        }

        // Commons / purlins: FAM-WALL-BAY-SEQ, the seq numbered 1..n WITHIN each (wall, bay) -- restarting
        // every bay. The wall is the eave-girt wall on the member's side (SideWall); bay = the Roman numeral.
        public string RoofLabel(string prefix, string wall, string bay)
        {
            string key = prefix + "|" + wall + "|" + bay;
            _roofSeq.TryGetValue(key, out int n); n++; _roofSeq[key] = n;
            string stem = prefix + "-" + wall + (string.IsNullOrEmpty(bay) ? "" : "-" + bay);
            return stem + "-" + n;
        }

        // Abbreviated family code that LEADS a wall/bay label (so the cut list groups by family). Unknown
        // roles fall back to the role string itself (still unique, just verbose).
        private static string FamilyCode(string role)
        {
            switch (role)
            {
                case "EaveGirt":  return "EG";
                case "FloorGirt": return "FG";
                case "Sill":      return "SL";   // longitudinal wall sill: "SL-A-I"
                case "Summer":    return "SB";   // recipe summer, center-owned: "SB-C-I"
                case "Ridge":     return "R";
                case "QueenGirt": return "QG";
                case "HPostGirt": return "HG";
                case "Common":    return "C";
                case "Purlin":    return "P";
                default: return string.IsNullOrEmpty(role) ? "M" : role;
            }
        }

        // The outer column letter (eave wall) on x's side of the span -- a roof timber's owning wall.
        public string SideWall(double x)
        {
            if (ColX.Count == 0) return "A";
            double mid = (ColX[0] + ColX[ColX.Count - 1]) / 2.0;
            return x <= mid ? ColLetterAt(0, ColX.Count) : ColLetterAt(ColX.Count - 1, ColX.Count);
        }

        // Column letter, DENSE SEQUENTIAL left->right: A,B,C.. (skipping I/O). The leftmost column is
        // "A" and the rightmost is whatever the count reaches -- E for the 5-line set (A-E, ridge C),
        // G for HammerBeam div6's 7 lines (A-G, ridge D). The two eaves fall on the ends naturally, so
        // a post still reads "1A" / "1<last>". (`count` is kept for the signature / the <=0 guard.)
        private static string ColLetterAt(int i, int count)
        {
            return LetterFor(i < 0 ? 0 : i);
        }

        private static int Q(double v) => (int)System.Math.Round(v * 4.0);   // quarter-inch signature bucket

        // The column letter nearest a given across-span coord (for wall-letter stamping + spans).
        public string ColLetter(double x)
        {
            if (ColX.Count == 0) return "A";
            int best = 0; double bd = double.MaxValue;
            for (int i = 0; i < ColX.Count; i++)
            {
                double d = System.Math.Abs(ColX[i] - x);
                if (d < bd) { bd = d; best = i; }
            }
            return ColLetterAt(best, ColX.Count);
        }

        // Index -> column letter over the I/O-skipping alphabet, rolling to AA, AB.. past 24.
        private static string LetterFor(int i)
        {
            if (i < 0) i = 0;
            if (i < Alpha.Length) return Alpha[i].ToString();
            return LetterFor(i / Alpha.Length - 1) + Alpha[i % Alpha.Length];
        }

        private string BentNumberAt(double z)
        {
            if (BentZ.Count == 0) return "1";
            int best = 0; double bd = double.MaxValue;
            for (int i = 0; i < BentZ.Count; i++)
            {
                double d = System.Math.Abs(BentZ[i] - z);
                if (d < bd) { bd = d; best = i; }
            }
            return (best + 1).ToString();
        }

        // Cluster an across-span coord into the column list, merging within Tol and ORing the ground flag.
        private static void ClusterCol(List<(double x, bool ground)> cols, double x, bool ground)
        {
            for (int i = 0; i < cols.Count; i++)
                if (System.Math.Abs(cols[i].x - x) <= Tol) { cols[i] = (cols[i].x, cols[i].ground || ground); return; }
            cols.Add((x, ground));
        }

        // The ridge/center line: a column at the across-span midpoint of the extremes (the two eaves).
        // For KingPost / HammerBeam it clusters with the king-post column already there; for QueenPost
        // (rafters lap, no king post) it materializes the center line the ridge sits on. Elevated
        // (ground = false): no vertical reaches the floor there.
        private static void AddCenterCol(List<(double x, bool ground)> cols)
        {
            if (cols.Count < 2) return;
            double min = cols[0].x, max = cols[0].x;
            foreach (var c in cols) { if (c.x < min) min = c.x; if (c.x > max) max = c.x; }
            ClusterCol(cols, (min + max) / 2.0, false);
        }

        // Insert a bent-line coord, merging values within Tol (one line per cluster). Sorted by the caller.
        private static void InsertZ(List<double> xs, double v)
        {
            for (int i = 0; i < xs.Count; i++)
                if (System.Math.Abs(xs[i] - v) <= Tol) return;   // already have this line
            xs.Add(v);
        }

        // ---- drawing -------------------------------------------------------------------------------

        private const double Margin = 36.0;   // line overrun past the frame extents (inches)
        private const double Bubble = 9.0;     // bubble radius
        private const double TextH  = 12.0;    // bubble text height

        // Draw the grid (lines + lettered/numbered bubbles) on the frame FLOOR (graph Y=0), placed by
        // `placement` so it sits UNDER the timbers. With the model basis baked into `placement` the
        // floor maps to the WCS XY plane (in a plan UCS) -- so the grid is flat AND under the frame;
        // in a rotated placement it follows the frame (the user rotated it). Across-span (graph X) and
        // along-building (the bent Z's) span the floor. ALL grid entities are color BYLAYER (the TM_GRID
        // layer). Column letters are anchored A (left eave) .. E (right eave) so a post reads "1A"/"1E";
        // interior columns (sub-walls) take B/C/D. Each entity is tagged with `frameTag` (a TMGrid
        // xrecord) so a redraw clears only this frame's grid. No-op if empty.
        public void Draw(Matrix3d placement, string frameTag)
        {
            if (ColX.Count == 0 || BentZ.Count == 0) return;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;

            double zLo = BentZ[0] - Margin, zHi = BentZ[BentZ.Count - 1] + Margin;  // along-building span
            double xLo = ColX[0] - Margin,  xHi = ColX[ColX.Count - 1] + Margin;    // across-span span
            // Floor-plane normal = image of graph Y (height): the floor is the graph XZ plane (Y=0), so
            // its normal is the placement's Y axis -- world Z (up) under the model basis. (Using the Z
            // axis here is the bug that split lines/text across +-Z.)
            Vector3d normal = placement.CoordinateSystem3d.Yaxis.GetNormal();

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectId layer = EnsureLayer(tr, db);
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                // Column lines run along the building at each across-span X; bubble at the low end.
                for (int i = 0; i < ColX.Count; i++)
                {
                    AddLine(tr, btr, layer, frameTag, placement, Floor(ColX[i], zLo), Floor(ColX[i], zHi));
                    AddBubble(tr, btr, layer, frameTag, placement, normal, db, Floor(ColX[i], zLo - Bubble), ColLetterAt(i, ColX.Count));
                }

                // Bent lines run across the span at each along-building Z; bubble at the low end.
                for (int j = 0; j < BentZ.Count; j++)
                {
                    AddLine(tr, btr, layer, frameTag, placement, Floor(xLo, BentZ[j]), Floor(xHi, BentZ[j]));
                    AddBubble(tr, btr, layer, frameTag, placement, normal, db, Floor(xLo - Bubble, BentZ[j]), (j + 1).ToString());
                }

                tr.Commit();
            }
        }

        // Draw PROVISIONAL (dashed, yellow) grid lines for Free Assembly elements that don't yet have a
        // derived line -- a reference for the user to align TPlace'd timbers to. A bent line (z,label)
        // runs across the span; a wall line (x,label) runs along the building. Any position already
        // matched by a derived line (a post landed there) is SKIPPED -- the solid grid line takes over.
        // Extents are graph coords (caller derives from the spec span + bent run). Tagged per frame so
        // the next redraw clears them along with the solid grid.
        public void DrawTempLines(Matrix3d placement, string frameTag,
            IEnumerable<(double coord, string label)> bentLines,
            IEnumerable<(double coord, string label)> wallLines,
            double xLo, double xHi, double zLo, double zHi)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            Vector3d normal = placement.CoordinateSystem3d.Yaxis.GetNormal();
            xLo -= Margin; xHi += Margin; zLo -= Margin; zHi += Margin;

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectId layer = EnsureTempLayer(tr, db);
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                foreach (var (z, label) in bentLines)
                {
                    if (NearAny(BentZ, z)) continue;                 // a post landed -> solid line wins
                    AddLine(tr, btr, layer, frameTag, placement, Floor(xLo, z), Floor(xHi, z));
                    AddBubble(tr, btr, layer, frameTag, placement, normal, db, Floor(xLo - Bubble, z), label);
                }
                foreach (var (x, label) in wallLines)
                {
                    if (NearAny(ColX, x)) continue;
                    AddLine(tr, btr, layer, frameTag, placement, Floor(x, zLo), Floor(x, zHi));
                    AddBubble(tr, btr, layer, frameTag, placement, normal, db, Floor(x, zLo - Bubble), label);
                }
                tr.Commit();
            }
        }

        private static bool NearAny(List<double> xs, double v)
        {
            foreach (double x in xs) if (System.Math.Abs(x - v) <= Tol) return true;
            return false;
        }

        // A point on the frame floor (graph Y=0) at across-span x, along-building z -- in GRAPH coords,
        // to be transformed by the placement.
        private static Point3d Floor(double across, double along) => new Point3d(across, 0.0, along);

        // BYLAYER color (256): every grid entity inherits the TM_GRID layer color.
        private static readonly Color ByLayer = Color.FromColorIndex(ColorMethod.ByLayer, 256);

        private static void AddLine(Transaction tr, BlockTableRecord btr, ObjectId layer, string frameTag,
            Matrix3d m, Point3d a, Point3d b)
        {
            var ln = new Line(a.TransformBy(m), b.TransformBy(m)) { LayerId = layer, Color = ByLayer };
            btr.AppendEntity(ln); tr.AddNewlyCreatedDBObject(ln, true);
            ManagedTimber.WriteGridTag(tr, ln, frameTag);
        }

        private static void AddBubble(Transaction tr, BlockTableRecord btr, ObjectId layer, string frameTag,
            Matrix3d m, Vector3d normal, Database db, Point3d centerGraph, string label)
        {
            Point3d center = centerGraph.TransformBy(m);
            var circ = new Circle(center, normal, Bubble) { LayerId = layer, Color = ByLayer };
            btr.AppendEntity(circ); tr.AddNewlyCreatedDBObject(circ, true);
            ManagedTimber.WriteGridTag(tr, circ, frameTag);

            var t = new DBText
            {
                TextString = label, Height = TextH, LayerId = layer, Color = ByLayer, Normal = normal,
                HorizontalMode = TextHorizontalMode.TextMid, VerticalMode = TextVerticalMode.TextVerticalMid,
                Position = center, AlignmentPoint = center
            };
            btr.AppendEntity(t); tr.AddNewlyCreatedDBObject(t, true);
            t.AdjustAlignment(db);
            ManagedTimber.WriteGridTag(tr, t, frameTag);
        }

        // Ensure the TM_GRID layer exists (blue, ACI 5 -- never green; the user is red/green colorblind).
        private static ObjectId EnsureLayer(Transaction tr, Database db)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(ManagedTimber.GridLayer)) return lt[ManagedTimber.GridLayer];
            lt.UpgradeOpen();
            var ltr = new LayerTableRecord { Name = ManagedTimber.GridLayer, Color = Color.FromColorIndex(ColorMethod.ByAci, 5) };
            ObjectId id = lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
            return id;
        }

        // Ensure the TM_GRID_TEMP layer exists: YELLOW (ACI 2) + DASHED, deliberately distinct from the
        // solid blue grid so provisional lines read as temporary (yellow/blue is colorblind-safe).
        private static ObjectId EnsureTempLayer(Transaction tr, Database db)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(ManagedTimber.GridTempLayer)) return lt[ManagedTimber.GridTempLayer];
            ObjectId dashed = LoadDashed(tr, db);
            lt.UpgradeOpen();
            var ltr = new LayerTableRecord { Name = ManagedTimber.GridTempLayer, Color = Color.FromColorIndex(ColorMethod.ByAci, 2) };
            if (!dashed.IsNull) ltr.LinetypeObjectId = dashed;
            ObjectId id = lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
            return id;
        }

        // The DASHED linetype id, loading it from acad.lin if absent; ObjectId.Null if unavailable
        // (the layer then stays continuous -- the yellow color still distinguishes it).
        private static ObjectId LoadDashed(Transaction tr, Database db)
        {
            var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (ltt.Has("DASHED")) return ltt["DASHED"];
            try { db.LoadLineTypeFile("DASHED", "acad.lin"); } catch { return ObjectId.Null; }
            ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            return ltt.Has("DASHED") ? ltt["DASHED"] : ObjectId.Null;
        }
    }
}
