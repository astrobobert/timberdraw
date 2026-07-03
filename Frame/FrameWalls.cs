namespace TimberDraw
{
    // The structural-grid ownership rules: numbered BENTS (transverse) x lettered WALLS (longitudinal).
    // Every timber is owned by exactly ONE bent or ONE wall (so a future per-bent / per-wall 2D shop
    // drawing shows each member once + contextual neighbors). Derived from what the grouping layer
    // already stamped (BentTag / BayTag) plus the member's role + designation -- ONE source of truth so
    // the Browser lens (and later the shop-drawing pass) agree.
    //
    // Ownership: a member with a BentTag is in the bent cross-section (posts -- incl. the shared
    // grid-intersection "Post 1A" -- tie, principal rafters, king/queen post, struts, braces, floor
    // tie/brace) -> owned by that bent. A longitudinal member (no BentTag) runs along a wall line ->
    // owned by that wall: eave/floor girt, wall braces, and the bay roof timbers by side (left commons/
    // purlins -> their wall, right -> theirs); the ridge -> the first wall (A).
    public static class FrameWalls
    {
        // Owner group label for a timber: "Bent N" (bent-owned) or "Wall X" (wall-owned). An explicit
        // wallTag (assigned by TAssign to a free timber) wins over the derived wall letter; emitted
        // longitudinal members leave wallTag blank and derive their wall from role+side.
        public static string Owner(string bentTag, string wallTag, string role, string desig)
        {
            if (!string.IsNullOrEmpty(bentTag)) return "Bent " + bentTag;
            return "Wall " + EffectiveWall(wallTag, role, desig);
        }

        // The wall letter to display/sort by: the explicit assignment if present, else derived.
        public static string EffectiveWall(string wallTag, string role, string desig)
            => !string.IsNullOrEmpty(wallTag) ? wallTag : WallLetter(role, desig);

        // LEGACY FALLBACK for an un-tagged longitudinal member. The authoritative wall letter is now the
        // grid-derived WallTag the emitter stamps (FrameGrid.ColLetter against the full A-E line set --
        // the ridge reads the center letter C, eaves A/E), so EffectiveWall prefers WallTag and this
        // fires only when a member carries none. Kept as a rough L/R guess; can't know the center letter
        // (which floats with the interior-line count) without the grid, so the ridge guesses "A".
        public static string WallLetter(string role, string desig)
        {
            if (role == "Ridge") return "A";                 // fallback only -- grid stamps the center letter (C)
            switch (desig)
            {
                case "EL": case "FL": case "CA": case "PL": return "A";   // eave/floor girt, common, purlin -- left
                case "ER": case "FR": case "CE": case "PR": return "E";   // -- right (eaves anchored A..E)
                default: return "A";
            }
        }

        // Side letter for a BENT member's designation (the wall line it meets), for the grid ref:
        // A/left -> "A", E/right -> "B". Interior bent members (queen "B"/"D", king "C", ties) sit on
        // no wall line -> "".
        public static string SideLetter(string desig)
        {
            switch (desig)
            {
                case "A": case "L": case "EL": case "FL": return "A";
                case "E": case "R": case "ER": case "FR": return "E";   // right eave anchored "E"
                default: return "";
            }
        }

        // Parse a Roman numeral (inverse of KingPostBentGraph.Roman) for bay sort order. 0 on empty/
        // unparseable so bent members (no bay) sort first within their group.
        public static int RomanToInt(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int total = 0, prev = 0;
            for (int i = s.Length - 1; i >= 0; i--)
            {
                int v = Digit(s[i]);
                if (v == 0) return 0;            // not a clean Roman numeral
                if (v < prev) total -= v; else { total += v; prev = v; }
            }
            return total;
        }

        private static int Digit(char c)
        {
            switch (char.ToUpperInvariant(c))
            {
                case 'I': return 1;
                case 'V': return 5;
                case 'X': return 10;
                case 'L': return 50;
                case 'C': return 100;
                case 'D': return 500;
                case 'M': return 1000;
                default: return 0;
            }
        }
    }
}
