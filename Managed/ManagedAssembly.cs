using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;

namespace TimberDraw
{
    // Session-sticky assignment TARGET for TAssign -- the palette's Assembly group and the Frame
    // Browser push it and TAssign consumes it with no prompts (the ManagedSection pattern: the pane
    // IS the command's visibility). Kind picks the owner in the organization hierarchy
    //   Frame -> Bent -> members | Wall -> Bay -> members | Floor -> members.
    public static class ManagedAssembly
    {
        public static bool HasCurrent;
        public static string FrameTag;   // "A"
        public static string Kind;       // "Bent" | "Wall" | "Floor"
        public static string Owner;      // bent number / wall letter / floor number
        public static string Bay;        // second grid coordinate: a Wall's or Floor's bay numeral,
                                         // or a Bent's intersection wall letter (may be blank)

        public static void Set(string frameTag, string kind, string owner, string bay)
        {
            FrameTag = string.IsNullOrWhiteSpace(frameTag) ? "A" : frameTag.Trim();
            Kind = (kind ?? "").Trim();
            Owner = (owner ?? "").Trim();
            Bay = (bay ?? "").Trim();
            HasCurrent = Kind.Length > 0 && Owner.Length > 0;
        }

        public static void Clear() { HasCurrent = false; }

        // Target TIMBERS handed off by a pane (the Frame Browser's selected rows). TAssign consumes
        // them ONCE instead of prompting for a selection -- the JoinSession deferred-write pattern:
        // a palette handler holds no document lock, the command context does. A later console
        // TAssign (nothing stashed) prompts as always.
        private static List<ObjectId> _ids;
        public static void StashIds(IEnumerable<ObjectId> ids)
            => _ids = ids != null ? new List<ObjectId>(ids) : null;
        public static List<ObjectId> TakeIds()
        { var t = _ids; _ids = null; return t != null && t.Count > 0 ? t : null; }

        // Fired by TAssign when an assignment completes, so an open Frame Browser can refresh its
        // rows (labels/groups change). Fires on the AutoCAD UI thread.
        public static event System.Action Applied;
        public static void RaiseApplied() => Applied?.Invoke();
    }
}
