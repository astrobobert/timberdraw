using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TimberDraw
{
    // Serializes a FrameSpec (the source-of-truth instance model) to / from JSON. Manual JSON in
    // the same style as FrameStore. The frame is TWO axes: a "bents" array (each with a separation
    // to the next bent) and a "walls" array (A/B, each with a separation + a "bays" array of cells).
    // Each bent/bay carries a "timbers" array of universal Timbers (role/desig/label/enabled + size).
    //
    // MIGRATION: the previous ordered "elements" list (alternating bent/bay) still loads -- bents
    // become the Bents axis (separation taken from the following bay's old spacing), and each old bay
    // is SPLIT into a Wall A cell (its :L members + ridge/roof) and a Wall B cell (its :R members),
    // using the new neutral ":S" keys. Pre-Timber flat JSON (a "members" array) is read best-effort.
    public static class FrameSpecStore
    {
        public static string ToJson(FrameSpec s)
        {
            var ic = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendFormat(ic,
                "{{\"frameTag\":\"{5}\",\"span\":{0},\"eaveHt\":{1},\"pitch\":{2},\"make3D\":{3},\"offsetType\":{4},\"bents\":[",
                s.Span, s.EaveHt, s.Pitch, s.Make3D ? "true" : "false", s.OffsetType, Esc(s.FrameTag));
            for (int i = 0; i < s.Bents.Count; i++)
            {
                if (i > 0) sb.Append(',');
                AppendBent(sb, ic, s.Bents[i]);
            }
            sb.Append("],\"walls\":[");
            for (int i = 0; i < s.Walls.Count; i++)
            {
                if (i > 0) sb.Append(',');
                AppendWall(sb, ic, s.Walls[i]);
            }
            sb.Append("]}");
            return sb.ToString();
        }

        // ---- single-element round-trip (TypeDefaults templates) ----------------------------
        public static string ElementToJson(FrameElement el)
        {
            var ic = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            if (el is BentSpec b) AppendBent(sb, ic, b);
            else if (el is BaySpec y) AppendBay(sb, ic, y);
            return sb.ToString();
        }

        public static FrameElement ElementFromJson(string json)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement e = doc.RootElement;
            return Str(e, "kind", "") == "Bay" ? (FrameElement)ReadBay(e) : ReadBent(e);
        }

        private static void AppendBent(StringBuilder sb, CultureInfo ic, BentSpec b)
        {
            sb.AppendFormat(ic,
                "{{\"kind\":\"Bent\",\"name\":\"{0}\",\"type\":\"{1}\",\"separation\":{4},\"queenOffset\":{2},\"hbDivisor\":{3},\"girtDrop\":{5},\"offsetType\":{6},\"timbers\":[",
                Esc(b.Name), Esc(b.BentType), b.QueenOffset, b.HBDivisor, b.Separation, b.GirtDrop, b.OffsetType);
            AppendTimbers(sb, ic, b.Timbers);
            sb.Append("]}");
        }

        private static void AppendWall(StringBuilder sb, CultureInfo ic, WallSpec w)
        {
            sb.AppendFormat(ic,
                "{{\"kind\":\"Wall\",\"name\":\"{0}\",\"letter\":\"{1}\",\"rightSide\":{2},\"separation\":{3},\"freeAssembly\":{4},\"role\":\"{5}\",\"bays\":[",
                Esc(w.Name), Esc(w.Letter), w.RightSide ? "true" : "false", w.Separation,
                w.FreeAssembly ? "true" : "false", w.Role);
            for (int i = 0; i < w.Bays.Count; i++)
            {
                if (i > 0) sb.Append(',');
                AppendBay(sb, ic, w.Bays[i]);
            }
            sb.Append("]}");
        }

        private static void AppendBay(StringBuilder sb, CultureInfo ic, BaySpec y)
        {
            sb.AppendFormat(ic,
                "{{\"kind\":\"Bay\",\"name\":\"{0}\",\"ownerIsWallA\":{1},\"roofType\":\"{2}\",\"bayStart\":{13},\"bayEnd\":{14},\"offsetType\":{17}," +
                "\"commonMode\":{3},\"commonCount\":{4},\"commonSpacing\":{5},\"commonW\":{6},\"commonD\":{7}," +
                "\"commonTail\":{15},\"commonTailCut\":{16}," +
                "\"purlinMode\":{8},\"purlinCount\":{9},\"purlinSpacing\":{10},\"purlinW\":{11},\"purlinD\":{12},\"timbers\":[",
                Esc(y.Name), y.OwnerIsWallA ? "true" : "false", Esc(y.RoofType),
                y.CommonMode, y.CommonCount, y.CommonSpacing, y.CommonW, y.CommonD,
                y.PurlinMode, y.PurlinCount, y.PurlinSpacing, y.PurlinW, y.PurlinD,
                y.BayStart, y.BayEnd, y.CommonTail, (int)y.CommonTailCut, y.OffsetType);
            AppendTimbers(sb, ic, y.Timbers);
            sb.Append("]}");
        }

        private static void AppendTimbers(StringBuilder sb, CultureInfo ic, List<Timber> timbers)
        {
            for (int i = 0; i < timbers.Count; i++)
            {
                Timber t = timbers[i];
                if (i > 0) sb.Append(',');
                sb.AppendFormat(ic,
                    "{{\"role\":\"{0}\",\"desig\":\"{1}\",\"label\":\"{2}\",\"enabled\":{3}," +
                    "\"w\":{4},\"d\":{5},\"length\":{6},\"angle\":{7},\"ht\":{8},\"foot\":{9},\"head\":{10},\"place\":{11}}}",
                    Esc(t.Role), Esc(t.Designation), Esc(t.Label), t.Enabled ? "true" : "false",
                    t.Size.W, t.Size.D, t.Size.Length, t.Size.Angle, t.Size.Ht, t.Size.Foot, t.Size.Head, (int)t.Size.Place);
            }
        }

        public static FrameSpec FromJson(string json)
        {
            var s = new FrameSpec();
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement r = doc.RootElement;

            s.FrameTag = Str(r, "frameTag", "A");
            s.EaveHt = D(r, "eaveHt");
            s.Pitch = D(r, "pitch");
            s.Make3D = B(r, "make3D");
            s.OffsetType = I(r, "offsetType");
            // Legacy frame keys "eaveGirtOffset" / "rafterTail" are intentionally ignored now: the eave
            // girt carries its own Height, and the rafter tail lives on each Commons leaf (per bay).
            double span = D(r, "span");
            s.Span = span;   // seed (walls empty here -> stores _span)

            if (r.TryGetProperty("bents", out JsonElement bents) || r.TryGetProperty("walls", out _))
            {
                if (r.TryGetProperty("bents", out JsonElement bs))
                    foreach (JsonElement e in bs.EnumerateArray()) s.Bents.Add(ReadBent(e, span, s.OffsetType));
                if (r.TryGetProperty("walls", out JsonElement ws))
                    foreach (JsonElement e in ws.EnumerateArray()) s.Walls.Add(ReadWall(e, s.OffsetType));
                EnsureWalls(s, span);
                s.SyncBays();   // make each wall's bay list match the bent intervals (defensive)
            }
            else if (r.TryGetProperty("elements", out JsonElement els))
            {
                MigrateElements(s, els, span);
            }
            else
            {
                EnsureWalls(s, span);
            }
            s.MigrateToWallLines();   // legacy two-wall frame -> materialized A-E lines (no-op if already new)
            s.SyncWallRoles();        // reconcile line count to the bent type (HammerBeam div6 -> A-G); no-op when matched
            return s;
        }

        // Guarantee the two primary walls A/B exist (older saves / partial JSON).
        private static void EnsureWalls(FrameSpec s, double span)
        {
            if (s.WallLeft() == null)  s.Walls.Insert(0, WallSpec.NewDefault("A", false, span));
            if (s.WallRight() == null) s.Walls.Add(WallSpec.NewDefault("E", true, 0.0));
        }

        // `span` is the frame span, used only to migrate legacy "queenFraction" into the absolute
        // "queenOffset" (distance from bent center to the queen post's INNER face). Templates read via
        // ElementFromJson have no span (0) -> a fraction-only legacy bent falls back to the default offset.
        private static BentSpec ReadBent(JsonElement e, double span = 0, int frameOffset = 0)
        {
            var b = new BentSpec
            {
                Name = Str(e, "name", "Bent"),
                BentType = Str(e, "type", ""),
                Separation = D(e, "separation"),
                HBDivisor = e.TryGetProperty("hbDivisor", out _) ? I(e, "hbDivisor") : 4,
                // Tie drop; legacy saves (no key) default to the 6" minimum -- the setter clamps anyway.
                GirtDrop = e.TryGetProperty("girtDrop", out _) ? D(e, "girtDrop") : 6.0,
                // Per-bent centering; OLD saves (no key) inherit the frame-wide value (migration).
                OffsetType = e.TryGetProperty("offsetType", out _) ? I(e, "offsetType") : frameOffset
            };
            if (e.TryGetProperty("timbers", out JsonElement ts))
                b.Timbers = ReadTimbers(ts);
            else
                b.Timbers = MigrateBentTimbers(e, b.BentType);   // pre-Timber JSON
            b.EnsureSill();   // saves that predate floor systems phase 3 (adds the leaf, OFF)

            // Queen offset precedence: explicit queenOffset -> migrate legacy queenFraction -> default.
            // Legacy queenFraction dimensioned the OUTER face from the left edge (qLo = fraction*Span), so
            // the old inner face was at fraction*Span + QueenD; convert to center-to-inner here. QueenD is
            // read from the bent's queen timber (above), with a literal fallback when absent.
            if (e.TryGetProperty("queenOffset", out _))
                b.QueenOffset = D(e, "queenOffset");
            else if (e.TryGetProperty("queenFraction", out _) && span > 0)
            {
                double queenD = b.SizeOf("Queen:B")?.D ?? 6.0;   // queen reuses the post section
                b.QueenOffset = span * (0.5 - D(e, "queenFraction")) - queenD;
            }
            else b.QueenOffset = 48.0;
            return b;
        }

        private static WallSpec ReadWall(JsonElement e, int frameOffset = 0)
        {
            var w = new WallSpec
            {
                Name = Str(e, "name", "Wall"),
                Letter = Str(e, "letter", "A"),
                RightSide = B(e, "rightSide"),
                Separation = D(e, "separation"),
                FreeAssembly = B(e, "freeAssembly"),
                // Absent on legacy two-wall saves -> Eave (their ridge stays on Wall A, no Center line).
                Role = System.Enum.TryParse(Str(e, "role", "Eave"), out BayRole r) ? r : BayRole.Eave
            };
            if (e.TryGetProperty("bays", out JsonElement bys))
                foreach (JsonElement by in bys.EnumerateArray())
                {
                    BaySpec bay = ReadBay(by, frameOffset);
                    bay.Role = w.Role;   // a bay's catalog role follows its line
                    bay.EnsureSill();    // saves that predate floor systems phase 3 (eave lines only)
                    w.Bays.Add(bay);
                }
            return w;
        }

        private static BaySpec ReadBay(JsonElement e, int frameOffset = 0)
        {
            bool isA = e.TryGetProperty("ownerIsWallA", out _) ? B(e, "ownerIsWallA") : true;
            var y = new BaySpec
            {
                Name = Str(e, "name", "Bay"),
                OwnerIsWallA = isA,
                RoofType = Str(e, "roofType", "None"),
                // Per-bay centering; OLD saves (no key) inherit the frame-wide value (migration).
                OffsetType = e.TryGetProperty("offsetType", out _) ? I(e, "offsetType") : frameOffset,
                BayStart = e.TryGetProperty("bayStart", out _) ? I(e, "bayStart") : -1,
                BayEnd = e.TryGetProperty("bayEnd", out _) ? I(e, "bayEnd") : -1,
                CommonMode = I(e, "commonMode"), CommonCount = I(e, "commonCount"),
                CommonSpacing = D(e, "commonSpacing"), CommonW = D(e, "commonW"), CommonD = D(e, "commonD"),
                CommonTail = e.TryGetProperty("commonTail", out _) ? D(e, "commonTail") : 0.0,
                CommonTailCut = e.TryGetProperty("commonTailCut", out _) ? (TailCut)I(e, "commonTailCut") : TailCut.Plumb,
                PurlinMode = I(e, "purlinMode"), PurlinCount = I(e, "purlinCount"),
                PurlinSpacing = D(e, "purlinSpacing"), PurlinW = D(e, "purlinW"), PurlinD = D(e, "purlinD")
            };
            if (e.TryGetProperty("timbers", out JsonElement ts))
                y.Timbers = ReadTimbers(ts);
            else
                y.Timbers = BaySpec.BuildDefaultTimbers(isA);    // pre-Timber bay -> single-sided defaults
            y.NormalizeBraces();   // split any legacy single eave/floor brace (":S") into L/R bay ends
            return y;
        }

        private static List<Timber> ReadTimbers(JsonElement ts)
        {
            var list = new List<Timber>();
            foreach (JsonElement t in ts.EnumerateArray())
                list.Add(new Timber
                {
                    Role = Str(t, "role", ""),
                    Designation = Str(t, "desig", ""),
                    Label = Str(t, "label", ""),
                    Enabled = B(t, "enabled"),
                    Size = ReadSize(t)
                });
            return list;
        }

        // Read a timber's size; MIGRATE pre-Foot/Head JSON (legs absent) to a triangle derived from the
        // old single run + angle: Foot = Length*tan(Angle), Head = Length.
        private static MemberSize ReadSize(JsonElement t)
        {
            var sz = new MemberSize
            {
                W = D(t, "w"), D = D(t, "d"), Length = D(t, "length"),
                Angle = D(t, "angle"), Ht = D(t, "ht"),
                Foot = D(t, "foot"), Head = D(t, "head"),
                Place = (Justify)I(t, "place")   // absent -> 0 = Default (inherit frame OffsetType)
            };
            if (sz.Foot <= 0 && sz.Head <= 0 && sz.Length > 0)   // legacy single-run brace
            {
                sz.Head = sz.Length;
                sz.Foot = sz.Length * System.Math.Tan(sz.Angle * System.Math.PI / 180.0);
            }
            return sz;
        }

        // ---- Migration from the ordered "elements" (alternating bent/bay) format -----------

        private static void MigrateElements(FrameSpec s, JsonElement els, double span)
        {
            // Read the old elements in order, collecting bents + the bays that follow each bent.
            var oldBents = new List<BentSpec>();
            var oldBayJson = new List<JsonElement?>();   // bay AFTER each bent (interior interval); null when none
            BentSpec pendingBent = null;
            bool sawBentForPending = false;

            foreach (JsonElement e in els.EnumerateArray())
            {
                string kind = Str(e, "kind", "");
                if (kind == "Bay")
                {
                    if (pendingBent != null && !sawBentForPending)
                    {
                        pendingBent.Separation = D(e, "spacing");
                        oldBayJson[oldBents.Count - 1] = e;   // bay following the last bent
                        sawBentForPending = true;
                    }
                    // leading / consecutive bays are dropped (no two-bent interval to own them)
                }
                else
                {
                    pendingBent = ReadBent(e, span, s.OffsetType);
                    oldBents.Add(pendingBent);
                    oldBayJson.Add(null);
                    sawBentForPending = false;
                }
            }

            foreach (BentSpec b in oldBents) s.Bents.Add(b);

            var wallA = WallSpec.NewDefault("A", false, span);
            var wallB = WallSpec.NewDefault("E", true, 0.0);
            // One interval per gap between consecutive bents -> split the old bay into A/B cells.
            for (int i = 0; i + 1 < oldBents.Count; i++)
            {
                JsonElement? oldBay = oldBayJson[i];
                SplitOldBay(oldBay, out BaySpec aBay, out BaySpec bBay);
                aBay.OffsetType = bBay.OffsetType = s.OffsetType;   // legacy frames had one frame-wide centering
                wallA.Bays.Add(aBay);
                wallB.Bays.Add(bBay);
            }
            s.Walls.Add(wallA);
            s.Walls.Add(wallB);
            s.SyncBays();
            s.Renumber();
        }

        // Split one old (L/R) bay into a Wall A cell (its :L members + ridge/roof) and a Wall B cell
        // (its :R members), using the new neutral ":S" keys. A null oldBay yields plain defaults.
        private static void SplitOldBay(JsonElement? oldBay, out BaySpec aBay, out BaySpec bBay)
        {
            aBay = BaySpec.NewDefault(true);
            bBay = BaySpec.NewDefault(false);
            if (oldBay == null) return;
            JsonElement e = oldBay.Value;

            aBay.RoofType = Str(e, "roofType", "None");
            aBay.CommonMode = I(e, "commonMode"); aBay.CommonCount = I(e, "commonCount");
            aBay.CommonSpacing = D(e, "commonSpacing"); aBay.CommonW = D(e, "commonW"); aBay.CommonD = D(e, "commonD");
            aBay.PurlinMode = I(e, "purlinMode"); aBay.PurlinCount = I(e, "purlinCount");
            aBay.PurlinSpacing = D(e, "purlinSpacing"); aBay.PurlinW = D(e, "purlinW"); aBay.PurlinD = D(e, "purlinD");

            List<Timber> old = e.TryGetProperty("timbers", out JsonElement ts) ? ReadTimbers(ts) : new List<Timber>();
            void Copy(BaySpec dst, string oldKey, string newKey)
            {
                Timber src = old.Find(t => t.Key == oldKey);
                Timber d = dst.Find(newKey);
                if (src != null && d != null) { d.Enabled = src.Enabled; d.Size = src.Size.Clone(); }
            }
            // The legacy bay carried ONE brace per wall side (symmetric near/far). Seed BOTH new
            // bay-end timbers (":L"/":R") of each wall from that single old per-wall brace.
            void CopyBoth(BaySpec dst, string oldKey, string role)
            {
                Copy(dst, oldKey, role + ":L");
                Copy(dst, oldKey, role + ":R");
            }
            Copy(aBay, "EaveGirt:L", "EaveGirt:S"); Copy(bBay, "EaveGirt:R", "EaveGirt:S");
            CopyBoth(aBay, "EaveBrace:L", "EaveBrace"); CopyBoth(bBay, "EaveBrace:R", "EaveBrace");
            Copy(aBay, "FloorGirt:L", "FloorGirt:S"); Copy(bBay, "FloorGirt:R", "FloorGirt:S");
            CopyBoth(aBay, "FloorBrace:L", "FloorBrace"); CopyBoth(bBay, "FloorBrace:R", "FloorBrace");
            Copy(aBay, "Ridge:R", "Ridge:R");
            Copy(aBay, "Commons:X", "Commons:X");
            Copy(aBay, "Purlins:X", "Purlins:X");
            aBay.SyncRoofType();
        }

        // ---- Pre-Timber flat bent migration (unchanged) ------------------------------------

        private static List<Timber> MigrateBentTimbers(JsonElement e, string type)
        {
            List<Timber> timbers = BentSpec.BuildDefaultTimbers(type);
            HashSet<string> on = LegacyEnabled(e);
            foreach (Timber t in timbers)
            {
                if (on.Count > 0) t.Enabled = on.Contains(t.Key);
                SeedBentSizeFromLegacy(t, e);
            }
            return timbers;
        }

        // Old "members" keys mapped to the new "Role:Designation" keys (combined -> L/R).
        private static HashSet<string> LegacyEnabled(JsonElement e)
        {
            var on = new HashSet<string>();
            if (!e.TryGetProperty("members", out JsonElement m)) return on;
            foreach (JsonElement k in m.EnumerateArray())
            {
                string key = k.GetString();
                switch (key)
                {
                    case "Brace:AB":       on.Add("Brace:A"); on.Add("Brace:E"); break;
                    case "Strut:S":        on.Add("Strut:A"); on.Add("Strut:E"); break;
                    case "VStrut:V":       on.Add("VStrut:A"); on.Add("VStrut:E"); break;
                    case "QueenBrace:BD":  on.Add("QueenBrace:B"); on.Add("QueenBrace:D"); break;
                    case "CollarBrace:AE": on.Add("CollarBrace:A"); on.Add("CollarBrace:E"); break;
                    case "FloorBrace:FB":  on.Add("FloorBrace:A"); on.Add("FloorBrace:E"); break;
                    case "Ridge":          on.Add("Ridge:R"); break;
                    case "EaveGirtL":      on.Add("EaveGirt:L"); break;
                    case "EaveGirtR":      on.Add("EaveGirt:R"); break;
                    case "FloorGirtL":     on.Add("FloorGirt:L"); break;
                    case "FloorGirtR":     on.Add("FloorGirt:R"); break;
                    case "Commons":        on.Add("Commons:X"); break;
                    case "Purlins":        on.Add("Purlins:X"); break;
                    default:               on.Add(key); break;
                }
            }
            return on;
        }

        private static void SeedBentSizeFromLegacy(Timber t, JsonElement e)
        {
            switch (t.Role)
            {
                case "Post":      t.Size.W = D(e, "postW");   t.Size.D = D(e, "postD");   break;
                case "Girt":      t.Size.W = D(e, "girtW");   t.Size.D = D(e, "girtD");   break;
                case "Rafter":    t.Size.W = D(e, "rafterW"); t.Size.D = D(e, "rafterD"); break;
                case "KingPost":  t.Size.W = D(e, "kpostW");  t.Size.D = D(e, "kpostD");  break;
                case "Queen":     t.Size.W = D(e, "queenW");  t.Size.D = D(e, "queenD");  break;
                case "UpperGirt": t.Size.W = D(e, "upperGirtW"); t.Size.D = D(e, "upperGirtD"); break;
                case "HBeam":     t.Size.W = D(e, "hbeamW");  t.Size.D = D(e, "hbeamD");  break;
                case "HPost":     t.Size.W = D(e, "hpostW");  t.Size.D = D(e, "hpostD");  break;
                case "Collar":    t.Size.W = D(e, "collarW"); t.Size.D = D(e, "collarD"); break;
                case "Brace":
                case "QueenBrace":
                case "CollarBrace":
                case "FloorBrace":
                    t.Size.W = D(e, "braceW"); t.Size.D = D(e, "braceD");
                    t.Size.Length = D(e, "braceLength"); t.Size.Angle = D(e, "braceAngle");
                    t.Size.Head = t.Size.Length;
                    t.Size.Foot = t.Size.Length * System.Math.Tan(t.Size.Angle * System.Math.PI / 180.0);
                    break;
                case "Strut":
                    t.Size.W = D(e, "strutW"); t.Size.D = D(e, "strutD"); t.Size.Angle = D(e, "strutAngle"); break;
                case "VStrut":
                    t.Size.W = D(e, "vstrutW"); t.Size.D = D(e, "vstrutD"); break;
                case "FloorGirt":
                    t.Size.W = D(e, "floorGirtW"); t.Size.D = D(e, "floorGirtD"); t.Size.Ht = D(e, "floorGirtHt"); break;
            }
        }

        // ---- JSON readers ------------------------------------------------------------------

        private static double D(JsonElement e, string name)
            => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0.0;
        private static int I(JsonElement e, string name)
            => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
        private static bool B(JsonElement e, string name)
            => e.TryGetProperty(name, out JsonElement v) && (v.ValueKind == JsonValueKind.True);
        private static string Str(JsonElement e, string name, string dflt)
            => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : dflt;

        private static string Esc(string s)
            => string.IsNullOrEmpty(s) ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
