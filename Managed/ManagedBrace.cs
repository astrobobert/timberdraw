namespace TimberDraw
{
    // Sticky knee-brace spec set by the managed-timber palette (TPanel). When active, TJoin's angled
    // (knee-brace) branch takes the foot/head runs from here instead of prompting -- the user dials the
    // legs/angle once and braces many. Mirrors ManagedSection. Only the two leg runs are stored; TJoin
    // consumes runs, and the angle is purely a palette-side convenience for computing them.
    public static class ManagedBrace
    {
        public static bool HasCurrent;
        public static double FootRun;
        public static double HeadRun;

        public static void Set(double footRun, double headRun)
        {
            FootRun = footRun;
            HeadRun = headRun;
            HasCurrent = footRun > 0.0 && headRun > 0.0;
        }

        public static void Clear() { HasCurrent = false; }
    }
}
