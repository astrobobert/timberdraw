using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{
    // Shop-drawing MODEL layer (no AutoCAD commands): partition the managed timbers into 2D "assembly
    // maps" -- one per BENT, one per WALL, plus a PLAN -- and hold each map's orthographic projection
    // basis. Drawing the maps into model space + wrapping them in paper-space layouts lives in
    // ShopMaps.Draw* (phase 1+) and ShopLayouts (phase 3); this file is grouping + geometry only.
    //
    // Model basis (baked by ManagedFrameEmitter.ModelBasis): the frame stands Z-up in WCS -- building
    // length -> world X, across-the-bent -> world Y, height -> world +Z. So a BENT lies in the world YZ
    // plane (view along X), a WALL lies in the world XZ plane (view along Y), the PLAN is the world XY
    // top view (look down Z). Grouping is therefore by world station: bent = fixed world X, wall = fixed
    // world Y.
    public static class ShopMaps
    {
        private const double CrossPlaneDot = 0.35;  // |length-axis . world X| above this = spans the building (wall/roof)
        private const double StationTol    = 2.0;   // inches: timbers "on the same bent / wall line" cluster

        public enum MapKind { Bent, Wall, Plan }

        // One assembly map: a named group of timbers + the orthographic projection basis to flatten them.
        // A WCS point P projects to the 2D map coordinate (P.DotProduct(U), P.DotProduct(V)); W = U x V is
        // the look direction (used as the drawn geometry's normal).
        public sealed class ShopMap
        {
            public MapKind Kind;
            public string  Name;                 // "Bent 1", "Wall A", "Plan"
            public Vector3d U, V, W;             // projection basis (horizontal, vertical, look)
            public List<ManagedTimber.ShopInfo> Members = new List<ManagedTimber.ShopInfo>();

            // Connected neighbors drawn as faded context (no labels): the timbers that touch a primary
            // member. Filled by BuildMaps from face-adjacency. Empty on the Plan (all timbers are primary).
            public List<ManagedTimber.ShopInfo> Context = new List<ManagedTimber.ShopInfo>();

            // Model-space placement of the drawn (flattened) map, set by Draw: the map's 2D min-corner
            // lands at RegionOrigin on the world XY plane, extending RegionW x RegionH. Consumed by the
            // paper-space wrapper (ShopLayouts, phase 3) to frame each viewport.
            public Point3d RegionOrigin;
            public double  RegionW, RegionH;
        }

        // Which map a timber belongs to, as (kind, key). PRIMARY signal is the emitter's stamped GROUP
        // LAYER (TM_<frame>_Bent<n> / _Wall<x>) -- the app's own bent/wall grouping, which correctly files
        // even a face-offset bay brace (its layer is its side wall) with the girts + commons. Falls back to
        // the XData tags (BentNumber -> bent, WallTag -> wall), then geometry (a length axis with a real
        // component along the building, world X, spans bays -> wall; else it sits in a YZ plane -> bent).
        // Geometry alone can't tell a COMMON rafter (roof) from a PRINCIPAL rafter, so the layer/tags lead.
        public static (MapKind kind, string key) ClassifyGroup(ManagedTimber.ShopInfo t)
        {
            string lyr = t.Layer ?? "";
            int iB = lyr.IndexOf("_Bent", StringComparison.Ordinal);
            if (iB >= 0) return (MapKind.Bent, lyr.Substring(iB + 5));
            int iW = lyr.IndexOf("_Wall", StringComparison.Ordinal);
            if (iW >= 0) return (MapKind.Wall, lyr.Substring(iW + 5));

            if (!string.IsNullOrEmpty(t.BentTag)) return (MapKind.Bent, t.BentTag);
            if (!string.IsNullOrEmpty(t.WallTag)) return (MapKind.Wall, t.WallTag);
            return Math.Abs(t.F.Z.DotProduct(Vector3d.XAxis)) > CrossPlaneDot
                 ? (MapKind.Wall, "") : (MapKind.Bent, "");
        }

        // Box centroid = near-end face center O advanced half the length along the axis (X/Y are
        // symmetric about the axis, so this is the geometric center of the nominal box).
        public static Point3d Centroid(ManagedTimber.TFrame f) => f.O + f.Z * (f.L * 0.5);

        // Build every assembly map for the drawing's managed timbers: a bent map per world-X station
        // (cross members), a wall map per world-Y station (longitudinal members), and one plan (all).
        public static List<ShopMap> BuildMaps(Autodesk.AutoCAD.DatabaseServices.Database db)
            => BuildMaps(ManagedTimber.EnumerateForShop(db));

        public static List<ShopMap> BuildMaps(List<ManagedTimber.ShopInfo> timbers)
        {
            var maps = new List<ShopMap>();
            if (timbers == null || timbers.Count == 0) return maps;

            // Partition by the emitter's group (layer-derived): bents keyed by number, walls by letter.
            var classified = timbers.Select(t => (g: ClassifyGroup(t), t)).ToList();
            var bentMembers = classified.Where(c => c.g.kind == MapKind.Bent).ToList();
            var wallMembers = classified.Where(c => c.g.kind == MapKind.Wall).ToList();

            // BENT maps: group by bent key (a face-offset bent brace keeps its bent), ordered by world-X
            // station. VIEW DIRECTION follows the timber-framing convention: the exterior END bents are
            // viewed from OUTSIDE -- the first (min X) looking +X, the LAST (max X) looking -X -- while
            // intermediate bents follow the FIRST (+X). So only the last bent flips (U flips with W; V=+Z up).
            var bentGroups = GroupByKeyThenStation(bentMembers, t => Centroid(t.F).X);
            for (int i = 0; i < bentGroups.Count; i++)
            {
                var (tag, members) = bentGroups[i];
                bool flip = i == bentGroups.Count - 1 && bentGroups.Count > 1;   // far exterior bent
                maps.Add(new ShopMap
                {
                    Kind = MapKind.Bent,
                    Name = "Bent " + (string.IsNullOrEmpty(tag) ? (i + 1).ToString() : tag),
                    U = flip ? Vector3d.YAxis : Vector3d.YAxis.Negate(),
                    V = Vector3d.ZAxis,
                    W = flip ? Vector3d.XAxis.Negate() : Vector3d.XAxis,
                    Members = members
                });
            }

            // WALL maps: group by wall key, ordered by world-Y station. Same convention: the exterior EAVE
            // walls are viewed from OUTSIDE -- first (min Y) looking +Y, LAST (max Y) looking -Y -- while
            // interior walls (ridge etc.) follow the FIRST (+Y). Only the last wall flips.
            var wallGroups = GroupByKeyThenStation(wallMembers, t => Centroid(t.F).Y);
            for (int i = 0; i < wallGroups.Count; i++)
            {
                var (tag, members) = wallGroups[i];
                bool flip = i == wallGroups.Count - 1 && wallGroups.Count > 1;   // far exterior eave
                maps.Add(new ShopMap
                {
                    Kind = MapKind.Wall,
                    Name = "Wall " + (string.IsNullOrEmpty(tag) ? LetterFor(i) : tag),
                    U = flip ? Vector3d.XAxis.Negate() : Vector3d.XAxis,
                    V = Vector3d.ZAxis,
                    W = flip ? Vector3d.YAxis.Negate() : Vector3d.YAxis,
                    Members = members
                });
            }

            // No PLAN drawing (it duplicated every timber). Post footprints are drawn in place on the
            // structural grid instead -- see ShopMaps.Draw / DrawFootprints.
            AttachContext(maps, timbers);
            return maps;
        }

        // Fill each map's Context with its connected neighbors (face-adjacent to a primary, not primary).
        // For a WALL map, an adjacent BENT member pulls in its WHOLE bent (a rafter joins a post, not the
        // eave girt, so it is 2 hops from the wall -- expanding the bent surfaces the posts/rafters/ridge
        // the user wants). Bent maps stay strict 1-hop (just the crossing longitudinals). Plan gets none.
        private static void AttachContext(List<ShopMap> maps, List<ManagedTimber.ShopInfo> timbers)
        {
            var adj = BuildAdjacency(timbers);
            var byId = timbers.ToDictionary(t => t.Id);
            var bentByKey = new Dictionary<string, List<ManagedTimber.ShopInfo>>();
            foreach (var t in timbers)
            {
                var g = ClassifyGroup(t);
                if (g.kind != MapKind.Bent) continue;
                if (!bentByKey.TryGetValue(g.key, out var l)) bentByKey[g.key] = l = new List<ManagedTimber.ShopInfo>();
                l.Add(t);
            }

            foreach (ShopMap m in maps)
            {
                if (m.Kind == MapKind.Plan) continue;
                var primary = new HashSet<ObjectId>(m.Members.Select(t => t.Id));
                var ctx = new Dictionary<ObjectId, ManagedTimber.ShopInfo>();
                foreach (var p in m.Members)
                {
                    if (!adj.TryGetValue(p.Id, out var nbrs)) continue;
                    foreach (var nId in nbrs)
                    {
                        if (primary.Contains(nId) || !byId.TryGetValue(nId, out var n)) continue;
                        var g = ClassifyGroup(n);
                        if (m.Kind == MapKind.Wall && g.kind == MapKind.Bent && bentByKey.TryGetValue(g.key, out var bent))
                        {
                            foreach (var b in bent) if (!primary.Contains(b.Id)) ctx[b.Id] = b;
                        }
                        else ctx[nId] = n;
                    }
                }
                m.Context = ctx.Values.ToList();
            }
        }

        // Face-adjacency of all timbers (mirrors ManagedTimber.ComputeNodes): two timbers are adjacent when
        // any of their analytic faces MATE (opposing + coplanar + overlapping). O(N^2), computed once.
        private static Dictionary<ObjectId, HashSet<ObjectId>> BuildAdjacency(List<ManagedTimber.ShopInfo> timbers)
        {
            const double tol = 0.05;
            var faces = timbers.Select(t => ManagedTimber.Faces(t.F)).ToList();
            var adj = new Dictionary<ObjectId, HashSet<ObjectId>>();
            foreach (var t in timbers) adj[t.Id] = new HashSet<ObjectId>();

            for (int i = 0; i < timbers.Count; i++)
                for (int j = i + 1; j < timbers.Count; j++)
                {
                    bool mate = false;
                    foreach (var a in faces[i]) { foreach (var b in faces[j]) if (ManagedTimber.FacesMate(a, b, tol, out _)) { mate = true; break; } if (mate) break; }
                    if (mate) { adj[timbers[i].Id].Add(timbers[j].Id); adj[timbers[j].Id].Add(timbers[i].Id); }
                }
            return adj;
        }

        // Group timbers by their classified group KEY (bent number / wall letter, from ClassifyGroup),
        // falling back to 1-D station clustering (world X / Y) for members with a BLANK key (free-placed
        // timbers with no group layer/tag): each joins the nearest existing group within StationTol, else
        // opens its own. Ordered ascending by station so bents number 1,2,3.. and walls letter A,B,C.. .
        private static List<(string key, List<ManagedTimber.ShopInfo> members)> GroupByKeyThenStation(
            List<((MapKind kind, string key) g, ManagedTimber.ShopInfo t)> src,
            Func<ManagedTimber.ShopInfo, double> coordSel)
        {
            var groups = new List<(string key, double station, List<ManagedTimber.ShopInfo> members)>();

            foreach (var (g, t) in src)   // keyed first, so blank-key members can merge into a station
            {
                if (string.IsNullOrEmpty(g.key)) continue;
                int hit = groups.FindIndex(x => x.key == g.key);
                if (hit < 0) groups.Add((g.key, coordSel(t), new List<ManagedTimber.ShopInfo> { t }));
                else groups[hit].members.Add(t);
            }
            foreach (var (g, t) in src)   // blank key: nearest station within tol, else a new group
            {
                if (!string.IsNullOrEmpty(g.key)) continue;
                double c = coordSel(t);
                int best = -1; double bd = StationTol;
                for (int i = 0; i < groups.Count; i++)
                {
                    double d = Math.Abs(groups[i].station - c);
                    if (d <= bd) { bd = d; best = i; }
                }
                if (best >= 0) groups[best].members.Add(t);
                else groups.Add(("", c, new List<ManagedTimber.ShopInfo> { t }));
            }
            return groups.OrderBy(x => x.members.Average(coordSel))
                         .Select(x => (x.key, x.members)).ToList();
        }

        // A..H, J..N, P..Z (skipping I and O, matching FrameGrid.LetterFor), then AA.. for the wall
        // fallback letter when a station carries no WallTag.
        private const string Alpha = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        private static string LetterFor(int i)
        {
            if (i < 0) i = 0;
            if (i < Alpha.Length) return Alpha[i].ToString();
            return LetterFor(i / Alpha.Length - 1) + Alpha[i % Alpha.Length];
        }

        // ---- drawing (flatten each map onto world XY, off to the side of the frame) ----------------

        private const double Gutter      = 96.0;  // model inches between the frame + between map regions (spread so viewports don't bleed)
        private const double GridMargin  = 60.0;  // pad around the frame for the column-grid viewport (grid lines + bubbles)
        private const double LabelHeight = 6.0;   // model inches (~1/4" at the 1:24 sheet scale)
        private const double BubbleR     = 9.0;   // note-bubble radius (matches FrameGrid)
        private const double BubbleTextH = 8.0;   // bubble text height
        private const double BubbleGap   = 24.0;  // drop below the drawing to the bubble row (model inches)
        private const double MarkerR     = 9.0;   // grid-bubble radius (arrow starts at the bubble edge)
        private const double MarkerShaft = 20.0;  // thin shaft length
        private const double MarkerHeadL = 14.0;  // solid arrowhead length
        private const double MarkerHeadW = 11.0;  // solid arrowhead base width

        private const double TitleGap   = 12.0;   // gap above the drawing to the title (model inches)
        private const double TitleTextH = 14.0;   // model-space drawing title height

        // Vertical spans reserved BELOW (note bubbles) and ABOVE (title) each drawing -- so the paper-space
        // viewport (ShopLayouts) frames the whole thing. Read from each map's RegionOrigin.
        public const double BubbleReserve = BubbleGap + BubbleR + BubbleTextH;
        public const double TitleReserve  = TitleGap + TitleTextH;

        // BYLAYER color (256): every shop entity inherits the TM_SHOP layer color (blue ACI 5).
        private static readonly Color ByLayer = Color.FromColorIndex(ColorMethod.ByLayer, 256);

        // The order maps stack in rows (one row per kind): bents, then walls, then the plan on top.
        private static readonly MapKind[] RowOrder = { MapKind.Bent, MapKind.Wall, MapKind.Plan };

        // Draw the given maps into model space, flattened onto the world XY plane and tiled just to the
        // RIGHT of the frame (so they never overlap the live 3D model). Maps are laid out in ROWS by kind
        // -- bents on the bottom row, walls above, the plan on top -- each row left->right in station order,
        // sharing a common bottom. Every entity is tagged (TMShop) so EraseShop clears+redraws only the
        // maps. Each map's RegionOrigin/RegionW/RegionH are filled in for the paper-space wrapper (phase 3).
        public static void Draw(Database db, List<ShopMap> mapsToDraw)
        {
            if (mapsToDraw == null || mapsToDraw.Count == 0) return;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            // Park the block of maps clear of the frame: to the right of the drawn members' XY extent.
            var members = mapsToDraw.SelectMany(m => m.Members).ToList();
            ModelExtentXY(members, out double minX, out double minY, out double maxX, out double maxY);
            double baseX = maxX + GridMargin + Gutter;   // clear the in-place column-grid region (maxX + GridMargin)
            double rowBottom = minY;

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectId layer    = EnsureLayer(tr, db, ManagedTimber.ShopLayer, 5);      // primary: blue
                ObjectId ctxLayer = EnsureLayer(tr, db, ManagedTimber.ShopCtxLayer, 8);   // context: gray
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                foreach (MapKind kind in RowOrder)
                {
                    var row = mapsToDraw.Where(m => m.Kind == kind).ToList();
                    if (row.Count == 0) continue;

                    double cursorX = baseX, rowH = 0.0;
                    foreach (ShopMap m in row)
                    {
                        MapBBox(m, out Point2d min, out double w, out double h);
                        m.RegionOrigin = new Point3d(cursorX, rowBottom, 0.0);
                        m.RegionW = w; m.RegionH = h;
                        DrawMap(tr, btr, db, layer, ctxLayer, m, min);
                        cursorX += w + Gutter;
                        if (h > rowH) rowH = h;
                    }
                    rowBottom += rowH + Gutter;
                }

                DrawFootprints(tr, btr, layer, mapsToDraw);   // post footprints in place on the grid
                DrawElevationMarkers(tr, btr, db, layer, mapsToDraw, minX, minY);  // per-drawing view-direction markers

                // A COLUMN-GRID "plan": a Plan-kind map framing the frame's REAL location (the structural
                // grid + the post feet). ShopLayouts gives it its own viewport with the 3D timber layers
                // frozen, so it reads as a clean column/foundation plan. Added after the row flow so it is
                // not drawn as an offset map; its title is drawn here in place above the frame.
                var gridMap = new ShopMap
                {
                    Kind = MapKind.Plan, Name = "Column Grid",
                    U = Vector3d.XAxis, V = Vector3d.YAxis, W = Vector3d.ZAxis,
                    RegionOrigin = new Point3d(minX - GridMargin, minY - GridMargin, 0.0),
                    RegionW = (maxX - minX) + 2.0 * GridMargin,
                    RegionH = (maxY - minY) + 2.0 * GridMargin
                };
                DrawTitle(tr, btr, db, layer, gridMap);
                mapsToDraw.Add(gridMap);

                tr.Commit();
            }
        }

        // ELEVATION DIRECTION ARROWS on the column-grid plan. The grid BUBBLES (A-E, 1-2) already exist on
        // TM_GRID (drawn by FrameGrid) -- so this reads those bubble circles and adds only an ARROW at each,
        // pointing that elevation's DIRECTION OF SIGHT (the map's W): a bent bubble (below the frame) gets a
        // horizontal +/-X arrow, a wall bubble (left of the frame) a vertical +/-Y arrow. So the plan keys
        // every elevation + shows which way it is viewed (the exterior end bent/wall arrows point inward).
        private static void DrawElevationMarkers(Transaction tr, BlockTableRecord btr, Database db,
                                                 ObjectId layer, List<ShopMap> maps, double minX, double minY)
        {
            // The grid labels (bubble text -> center) already on TM_GRID. Match each map to its bubble by
            // its KEY string (bent number / wall letter) -- robust vs the map's mean Y (which roof members
            // pull up the slope). Collect first (don't append while enumerating the block).
            var labels = new List<(string s, Point3d c)>();
            foreach (ObjectId id in btr)
                if (tr.GetObject(id, OpenMode.ForRead) is DBText tx && tx.Layer == ManagedTimber.GridLayer)
                    labels.Add((tx.TextString.Trim(), tx.AlignmentPoint));

            foreach (ShopMap m in maps)
            {
                if (m.Members.Count == 0) continue;
                string key = Token(m.Name);
                if (m.Kind == MapKind.Bent)
                {
                    var dir = new Vector3d(Math.Sign(m.W.X), 0, 0);                 // horizontal (+/-X)
                    foreach (var (s, c) in labels)
                        if (s == key && c.Y < minY - 1.0) { DrawArrow(tr, btr, layer, c, dir); break; }
                }
                else if (m.Kind == MapKind.Wall)
                {
                    var dir = new Vector3d(0, Math.Sign(m.W.Y), 0);                 // vertical (+/-Y)
                    foreach (var (s, c) in labels)
                        if (s == key && c.X < minX - 1.0) { DrawArrow(tr, btr, layer, c, dir); break; }
                }
            }
        }

        // A direction arrow emanating from a grid bubble (edge) along the unit `dir` -- a thin shaft with a
        // SOLID (filled) arrowhead, drawn as one polyline whose head segment tapers from full width to zero
        // (the standard leader/section-arrow look). On the shop layer.
        private static void DrawArrow(Transaction tr, BlockTableRecord btr, ObjectId layer, Point3d bubble, Vector3d dir)
        {
            Point3d start = bubble + dir * MarkerR;
            Point3d headBase = start + dir * MarkerShaft;
            Point3d tip = headBase + dir * MarkerHeadL;

            var pl = new Polyline(3);
            pl.AddVertexAt(0, new Point2d(start.X, start.Y), 0.0, 0.0, 0.0);          // shaft (hairline)
            pl.AddVertexAt(1, new Point2d(headBase.X, headBase.Y), 0.0, MarkerHeadW, 0.0);  // head: width -> 0
            pl.AddVertexAt(2, new Point2d(tip.X, tip.Y), 0.0, 0.0, 0.0);
            pl.LayerId = layer; pl.Color = ByLayer;
            btr.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
            ManagedTimber.WriteShopTag(tr, pl);
        }

        // The drawing's short key: "Bent 1" -> "1", "Wall A" -> "A" (matches the grid bubble text).
        private static string Token(string name)
        {
            int sp = name.LastIndexOf(' ');
            return sp >= 0 ? name.Substring(sp + 1) : name;
        }

        // A projection stand-in for the top-down footprints (look down world Z; U=+X, V=+Y).
        private static readonly ShopMap PlanProjection = new ShopMap
        { Kind = MapKind.Plan, U = Vector3d.XAxis, V = Vector3d.YAxis, W = Vector3d.ZAxis };

        // Draw each floor POST's actual FOOTPRINT (top-down projection of its solid) IN PLACE at its real
        // WCS X,Y on the floor plane -- annotating the structural grid, on the blue TM_SHOP layer (replaces
        // the dropped plan drawing). Floor-meeting verticals only (elevated king posts on a tie are skipped).
        private static void DrawFootprints(Transaction tr, BlockTableRecord btr, ObjectId layer, List<ShopMap> maps)
        {
            Point3d Ident(Point2d q) => new Point3d(q.X, q.Y, 0.0);

            var verticals = maps.SelectMany(m => m.Members)
                                .GroupBy(t => t.Id).Select(g => g.First())
                                .Where(t => Math.Abs(t.F.Z.DotProduct(Vector3d.ZAxis)) >= 0.9)
                                .ToList();
            if (verticals.Count == 0) return;

            double FootZ(ManagedTimber.ShopInfo t) => Math.Min(t.F.O.Z, (t.F.O + t.F.Z * t.F.L).Z);
            double baseZ = verticals.Min(FootZ);
            foreach (ManagedTimber.ShopInfo t in verticals)
                if (FootZ(t) <= baseZ + 12.0)                 // reaches the floor (post), not elevated (king post)
                    DrawShape(tr, btr, layer, PlanProjection, t, Ident);
        }

        // Draw one map: each timber's projected nominal-box outline (convex hull of its 8 corners) as a
        // closed polyline + a centered label at its projected centroid, re-embedded flat on world XY at
        // the map's region origin.
        private static void DrawMap(Transaction tr, BlockTableRecord btr, Database db,
                                    ObjectId layer, ObjectId ctxLayer, ShopMap m, Point2d min)
        {
            Point3d Embed(Point2d q) =>
                m.RegionOrigin + Vector3d.XAxis * (q.X - min.X) + Vector3d.YAxis * (q.Y - min.Y);

            // Context (connected neighbors) first, faded + unlabeled, so the primary members draw on top.
            // A BENT map marks each end-on context member with X (projects toward the viewer, -X) or + (away,
            // +X) relative to the bent's world-X station; a WALL map ghosts the connected frame as outlines.
            double bentX = (m.Kind == MapKind.Bent && m.Members.Count > 0)
                         ? m.Members.Average(t => Centroid(t.F).X) : 0.0;
            foreach (ManagedTimber.ShopInfo c in m.Context)
            {
                DrawShape(tr, btr, ctxLayer, m, c, Embed);
                if (m.Kind == MapKind.Bent)
                {
                    Point3d p = Embed(Project(Centroid(c.F), m));
                    double along = (Centroid(c.F).X - bentX) * m.W.X;    // + = along the look dir (away from viewer)
                    string mark = along < 0 ? "X" : "+";                 // toward viewer / away
                    AddMark(tr, btr, db, ctxLayer, mark, p);
                }
            }

            // One label per distinct string on this drawing: identical repeats (same-symbol braces, equal
            // joists) draw the outline but no text -- the framer keys the count to the BOM.
            var labelled = new HashSet<string>();

            foreach (ManagedTimber.ShopInfo t in m.Members)
            {
                DrawShape(tr, btr, layer, m, t, Embed);

                string label = FirstNonEmpty(t.GridLabel, t.Designation, t.Id.Handle.ToString());
                if (string.IsNullOrEmpty(label) || !labelled.Add(label)) continue;   // already drawn

                Point3d pos = Embed(Project(Centroid(t.F), m));
                var txt = new DBText
                {
                    TextString = label, Height = LabelHeight,
                    HorizontalMode = TextHorizontalMode.TextMid,
                    VerticalMode   = TextVerticalMode.TextVerticalMid,
                    Position = pos, AlignmentPoint = pos,
                    LayerId = layer, Color = ByLayer
                };
                btr.AppendEntity(txt); tr.AddNewlyCreatedDBObject(txt, true);
                txt.AdjustAlignment(db);
                ManagedTimber.WriteShopTag(tr, txt);
            }

            DrawBubbles(tr, btr, db, layer, m, min);
            DrawTitle(tr, btr, db, layer, m);
        }

        // The drawing's TITLE (map name) in model space, centered above the drawing, so it reads through
        // the single paper-space viewport (ShopLayouts frames drawing + bubbles + title).
        private static void DrawTitle(Transaction tr, BlockTableRecord btr, Database db, ObjectId layer, ShopMap m)
        {
            Point3d pos = m.RegionOrigin + Vector3d.XAxis * (m.RegionW / 2.0)
                                         + Vector3d.YAxis * (m.RegionH + TitleGap + TitleTextH / 2.0);
            var t = new DBText
            {
                TextString = m.Name, Height = TitleTextH,
                HorizontalMode = TextHorizontalMode.TextMid,
                VerticalMode   = TextVerticalMode.TextVerticalMid,
                Position = pos, AlignmentPoint = pos,
                LayerId = layer, Color = ByLayer
            };
            btr.AppendEntity(t); tr.AddNewlyCreatedDBObject(t, true);
            t.AdjustAlignment(db);
            ManagedTimber.WriteShopTag(tr, t);
        }

        // Column / bent NOTE BUBBLES in a row below the drawing (bent + wall only). Under a BENT: a lettered
        // bubble at each vertical member's (post/king) horizontal position, from the column letter in its
        // grid label. Under a WALL: a numbered bubble at each connected bent's station. FrameGrid bubble style.
        private static void DrawBubbles(Transaction tr, BlockTableRecord btr, Database db, ObjectId layer,
                                        ShopMap m, Point2d min)
        {
            if (m.Kind == MapKind.Plan) return;
            double vRow = min.Y - BubbleGap;   // projected V of the bubble row, below the drawing
            Point3d Embed(Point2d q) =>
                m.RegionOrigin + Vector3d.XAxis * (q.X - min.X) + Vector3d.YAxis * (q.Y - min.Y);
            var seen = new HashSet<string>();

            if (m.Kind == MapKind.Bent)
            {
                foreach (ManagedTimber.ShopInfo t in m.Members)
                {
                    if (Math.Abs(t.F.Z.DotProduct(Vector3d.ZAxis)) < 0.9) continue;   // not a vertical (post/king)
                    string letter = ColumnLetter(t.GridLabel);
                    if (string.IsNullOrEmpty(letter) || !seen.Add(letter)) continue;
                    double u = Project(Centroid(t.F), m).X;
                    DrawBubble(tr, btr, db, layer, Embed(new Point2d(u, vRow)), letter);
                }
            }
            else   // Wall: a bubble per connected bent, numbered
            {
                foreach (ManagedTimber.ShopInfo c in m.Context)
                {
                    var g = ClassifyGroup(c);
                    if (g.kind != MapKind.Bent) continue;
                    string num = string.IsNullOrEmpty(g.key) ? c.BentTag : g.key;
                    if (string.IsNullOrEmpty(num) || !seen.Add(num)) continue;
                    double u = Project(Centroid(c.F), m).X;   // wall horizontal axis = +X
                    DrawBubble(tr, btr, db, layer, Embed(new Point2d(u, vRow)), num);
                }
            }
        }

        private static void DrawBubble(Transaction tr, BlockTableRecord btr, Database db, ObjectId layer,
                                       Point3d center, string text)
        {
            var circ = new Circle(center, Vector3d.ZAxis, BubbleR) { LayerId = layer, Color = ByLayer };
            btr.AppendEntity(circ); tr.AddNewlyCreatedDBObject(circ, true);
            ManagedTimber.WriteShopTag(tr, circ);

            var t = new DBText
            {
                TextString = text, Height = BubbleTextH,
                HorizontalMode = TextHorizontalMode.TextMid,
                VerticalMode   = TextVerticalMode.TextVerticalMid,
                Position = center, AlignmentPoint = center,
                LayerId = layer, Color = ByLayer
            };
            btr.AppendEntity(t); tr.AddNewlyCreatedDBObject(t, true);
            t.AdjustAlignment(db);
            ManagedTimber.WriteShopTag(tr, t);
        }

        // The column letter trailing a bent grid label ("1A"->"A", "12C"->"C"); "" for a group symbol ("*")
        // or a family label ("EG-A-I") -- only a 1-2 letter suffix after a leading bent number counts.
        private static string ColumnLetter(string gridLabel)
        {
            if (string.IsNullOrEmpty(gridLabel)) return "";
            int i = gridLabel.Length;
            while (i > 0 && char.IsLetter(gridLabel[i - 1])) i--;
            if (i == 0 || i == gridLabel.Length) return "";       // all letters (no bent number) or none
            string tail = gridLabel.Substring(i);
            return tail.Length <= 2 ? tail : "";
        }

        // Draw one timber's ACTUAL projected shape on `layer` (BYLAYER color). Projects the real Solid3d's
        // edges (so joinery -- birdsmouths, tenons, tapers, gable cuts -- reads), falling back to the
        // nominal-box convex hull if the Brep is unavailable.
        private static void DrawShape(Transaction tr, BlockTableRecord btr, ObjectId layer,
                                      ShopMap m, ManagedTimber.ShopInfo t, Func<Point2d, Point3d> embed)
        {
            if (!TryDrawSolidEdges(tr, btr, layer, m, t.Id, embed))
                DrawHull(tr, btr, layer, m, t.F, embed);
        }

        // Project every edge of the timber's Solid3d onto the map plane and draw each as a polyline.
        // Edges parallel to the view direction collapse to ~a point and are skipped, so an axis-aligned
        // box reads as a clean outline. Returns false (=> hull fallback) if the Brep yields no drawable edge.
        private static bool TryDrawSolidEdges(Transaction tr, BlockTableRecord btr, ObjectId layer,
                                              ShopMap m, ObjectId id, Func<Point2d, Point3d> embed)
        {
            if (!(tr.GetObject(id, OpenMode.ForRead) is Solid3d sol)) return false;
            int drawn = 0;
            try
            {
                using (var brep = new Autodesk.AutoCAD.BoundaryRepresentation.Brep(sol))
                {
                    foreach (Autodesk.AutoCAD.BoundaryRepresentation.Edge edge in brep.Edges)
                    {
                        List<Point3d> pts = SampleEdge(edge);
                        if (pts == null || pts.Count < 2) continue;
                        var proj = pts.Select(p => Project(p, m)).ToList();
                        if (Extent2d(proj) < 1e-6) continue;      // edge runs along the view direction
                        DrawPolyline(tr, btr, layer, proj, embed);
                        drawn++;
                    }
                }
            }
            catch { return false; }
            return drawn > 0;
        }

        // Sample one Brep edge into WCS points: a straight edge -> its 2 endpoints; a curved edge (arched
        // brace) -> evenly along its parameter interval. Null on any geometry hiccup (that edge is skipped).
        private static List<Point3d> SampleEdge(Autodesk.AutoCAD.BoundaryRepresentation.Edge edge)
        {
            try
            {
                Curve3d gc = edge.Curve;
                Interval iv = gc.GetInterval();
                double lo = iv.LowerBound, hi = iv.UpperBound;
                Point3d a = gc.EvaluatePoint(lo), b = gc.EvaluatePoint(hi);
                Point3d mid = gc.EvaluatePoint((lo + hi) / 2.0);
                Point3d lin = new Point3d((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0, (a.Z + b.Z) / 2.0);
                if (mid.DistanceTo(lin) < 1e-6) return new List<Point3d> { a, b };   // straight
                const int n = 16;
                var pts = new List<Point3d>(n + 1);
                for (int k = 0; k <= n; k++) pts.Add(gc.EvaluatePoint(lo + (hi - lo) * k / n));
                return pts;
            }
            catch { return null; }
        }

        // Draw a projected (open) polyline, embedding each 2D map point back onto the world XY plane.
        private static void DrawPolyline(Transaction tr, BlockTableRecord btr, ObjectId layer,
                                         List<Point2d> proj, Func<Point2d, Point3d> embed)
        {
            var pl = new Polyline();
            for (int i = 0; i < proj.Count; i++)
            {
                Point3d e = embed(proj[i]);
                pl.AddVertexAt(i, new Point2d(e.X, e.Y), 0, 0, 0);
            }
            pl.LayerId = layer; pl.Color = ByLayer;
            btr.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
            ManagedTimber.WriteShopTag(tr, pl);
        }

        // Fallback: the nominal-box convex-hull outline (8 corners), when the Brep is unavailable.
        private static void DrawHull(Transaction tr, BlockTableRecord btr, ObjectId layer,
                                     ShopMap m, ManagedTimber.TFrame f, Func<Point2d, Point3d> embed)
        {
            var proj = Corners8(f).Select(c => Project(c, m)).ToList();
            List<Point2d> hull = Hull(proj);
            if (hull.Count < 2) return;
            var pl = new Polyline();
            for (int i = 0; i < hull.Count; i++)
            {
                Point3d e = embed(hull[i]);
                pl.AddVertexAt(i, new Point2d(e.X, e.Y), 0, 0, 0);
            }
            pl.Closed = hull.Count >= 3;
            pl.LayerId = layer; pl.Color = ByLayer;
            btr.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
            ManagedTimber.WriteShopTag(tr, pl);
        }

        // Larger of the width/height of a set of 2D points (0 when they collapse to a point).
        private static double Extent2d(List<Point2d> pts)
        {
            double minx = double.MaxValue, miny = double.MaxValue, maxx = double.MinValue, maxy = double.MinValue;
            foreach (Point2d p in pts)
            {
                if (p.X < minx) minx = p.X; if (p.X > maxx) maxx = p.X;
                if (p.Y < miny) miny = p.Y; if (p.Y > maxy) maxy = p.Y;
            }
            return Math.Max(maxx - minx, maxy - miny);
        }

        // Project a WCS point onto the map plane: (P.U, P.V).
        private static Point2d Project(Point3d p, ShopMap m)
            => new Point2d(p.GetAsVector().DotProduct(m.U), p.GetAsVector().DotProduct(m.V));

        // The 8 corners of a timber's nominal box in WCS.
        private static IEnumerable<Point3d> Corners8(ManagedTimber.TFrame f)
        {
            double[] sx = { -f.W / 2.0, f.W / 2.0 };
            double[] sy = { -f.D / 2.0, f.D / 2.0 };
            double[] sz = { 0.0, f.L };
            foreach (double x in sx)
                foreach (double y in sy)
                    foreach (double z in sz)
                        yield return f.O + f.X * x + f.Y * y + f.Z * z;
        }

        // 2D convex hull (Andrew's monotone chain). Returns CCW hull vertices, collinear points dropped;
        // a rectangle collapses to its 4 corners, an oblique brace to its parallelogram/hexagon.
        private static List<Point2d> Hull(List<Point2d> pts)
        {
            var p = pts.OrderBy(q => q.X).ThenBy(q => q.Y).ToList();
            if (p.Count < 3) return p;
            var h = new List<Point2d>();
            for (int i = 0; i < p.Count; i++)   // lower hull
            {
                while (h.Count >= 2 && Cross(h[h.Count - 2], h[h.Count - 1], p[i]) <= 0) h.RemoveAt(h.Count - 1);
                h.Add(p[i]);
            }
            int lower = h.Count + 1;
            for (int i = p.Count - 2; i >= 0; i--)   // upper hull
            {
                while (h.Count >= lower && Cross(h[h.Count - 2], h[h.Count - 1], p[i]) <= 0) h.RemoveAt(h.Count - 1);
                h.Add(p[i]);
            }
            h.RemoveAt(h.Count - 1);   // last == first
            return h;
        }

        private static double Cross(Point2d o, Point2d a, Point2d b)
            => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

        // The map's 2D bounding box over every primary AND context member's projected corners (so the
        // region -- and the paper-space viewport -- frames the ghosted neighbors too).
        private static void MapBBox(ShopMap m, out Point2d min, out double w, out double h)
        {
            double minx = double.MaxValue, miny = double.MaxValue, maxx = double.MinValue, maxy = double.MinValue;
            foreach (ManagedTimber.ShopInfo t in m.Members.Concat(m.Context))
                foreach (Point3d c in Corners8(t.F))
                {
                    Point2d q = Project(c, m);
                    if (q.X < minx) minx = q.X; if (q.X > maxx) maxx = q.X;
                    if (q.Y < miny) miny = q.Y; if (q.Y > maxy) maxy = q.Y;
                }
            min = new Point2d(minx, miny);
            w = maxx - minx; h = maxy - miny;
        }

        // The XY extent (world) of a set of timbers -- used to park the map row clear of the frame.
        private static void ModelExtentXY(List<ManagedTimber.ShopInfo> members,
                                          out double minX, out double minY, out double maxX, out double maxY)
        {
            minX = minY = double.MaxValue; maxX = maxY = double.MinValue;
            foreach (ManagedTimber.ShopInfo t in members)
                foreach (Point3d c in Corners8(t.F))
                {
                    if (c.X < minX) minX = c.X; if (c.X > maxX) maxX = c.X;
                    if (c.Y < minY) minY = c.Y; if (c.Y > maxY) maxY = c.Y;
                }
            if (minX > maxX) { minX = minY = maxX = maxY = 0.0; }   // empty guard
        }

        // Ensure a shop layer exists with the given ACI color (5 = blue primary, 8 = gray context; never
        // green -- the user is red/green colorblind). Returns its id.
        private static ObjectId EnsureLayer(Transaction tr, Database db, string name, int aci)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return lt[name];
            lt.UpgradeOpen();
            var ltr = new LayerTableRecord
            {
                Name = name,
                Color = Color.FromColorIndex(ColorMethod.ByAci, (short)aci)
            };
            ObjectId id = lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
            return id;
        }

        // A direction mark ("X" toward viewer / "+" away) centered on an end-on context member's section.
        private static void AddMark(Transaction tr, BlockTableRecord btr, Database db, ObjectId layer,
                                    string mark, Point3d pos)
        {
            var t = new DBText
            {
                TextString = mark, Height = LabelHeight,
                HorizontalMode = TextHorizontalMode.TextMid,
                VerticalMode   = TextVerticalMode.TextVerticalMid,
                Position = pos, AlignmentPoint = pos,
                LayerId = layer, Color = ByLayer
            };
            btr.AppendEntity(t); tr.AddNewlyCreatedDBObject(t, true);
            t.AdjustAlignment(db);
            ManagedTimber.WriteShopTag(tr, t);
        }

        private static string FirstNonEmpty(params string[] vals)
        {
            foreach (string v in vals) if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            return "";
        }
    }
}
