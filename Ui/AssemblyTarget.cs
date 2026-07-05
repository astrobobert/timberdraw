using System;

namespace TimberDraw
{
    // The ONE assign-target state behind every assign surface (the Assembly tab's two-box and
    // the Browser's assign row): Frame / Kind / Owner / Bay. Each surface writes here on edit
    // and repaints on Changed (skipping its own echo via the source arg), so the two can never
    // diverge. TAssign still consumes the sticky ManagedAssembly -- Push() stamps this target
    // into it at Assign time. The command-line TAssign minting loop is untouched.
    internal static class AssemblyTarget
    {
        public static string Frame { get; private set; } = "A";
        public static string Kind  { get; private set; } = "Bent";
        public static string Owner { get; private set; } = "1";
        public static string Bay   { get; private set; } = "";

        // Raised after a real change; the arg is the source surface, so a subscriber can skip
        // the echo of its own edit.
        public static event Action<object> Changed;

        public static void Set(string frame, string kind, string owner, string bay, object source)
        {
            string f = (frame ?? "").Trim(), k = (kind ?? "").Trim();
            string o = (owner ?? "").Trim(), b = (bay ?? "").Trim();
            if (f == Frame && k == Kind && o == Owner && b == Bay) return;   // no-op -> no echo
            Frame = f; Kind = k; Owner = o; Bay = b;
            Changed?.Invoke(source);
        }

        // Stamp the target into the sticky ManagedAssembly (what TAssign consumes).
        public static void Push() => ManagedAssembly.Set(Frame, Kind, Owner, Bay);
    }
}
