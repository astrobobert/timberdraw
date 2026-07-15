namespace TimberDraw
{
    // Sticky knee-brace spec set by the managed-timber palette (TPanel). When active, TJoin's angled
    // (knee-brace) branch takes the foot/head runs from here instead of prompting -- the user dials the
    // legs/angle once and braces many. Mirrors ManagedSection. The angle is purely a palette-side
    // convenience for computing the runs. Placement (0 Back / 1 Center / 2 Front) registers the brace
    // against the NARROWER of the two picked timbers along the corner -- the same rule the frame
    // recipe's per-member Placement uses (Robert's call, 2026-07-15).
    public static class ManagedBrace
    {
        public static bool HasCurrent;
        public static double FootRun;
        public static double HeadRun;
        public static int Placement = 1;   // 0 Back / 1 Center / 2 Front

        // Fired on every Set -- the palette calls Set per edit, and a pending TJoin brace ghost
        // listens so it re-solves the INSTANT a value changes (a jig can't: its sampler only runs
        // on drawing-input events, so palette edits sat invisible until the cursor re-entered the
        // drawing -- Robert's catch). Raised on the UI thread.
        public static event System.Action Changed;

        public static void Set(double footRun, double headRun, int placement)
        {
            FootRun = footRun;
            HeadRun = headRun;
            Placement = placement < 0 ? 0 : placement > 2 ? 2 : placement;
            HasCurrent = footRun > 0.0 && headRun > 0.0;
            Changed?.Invoke();
        }

        public static void Clear() { HasCurrent = false; }
    }
}
