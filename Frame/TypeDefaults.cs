using System.Collections.Generic;
using System.Text.Json;

namespace TimberDraw
{
    // Per-TYPE default templates for bents and bays. The user dials in one bent/bay, hits "Set as
    // Default" with it selected, and that element (member sizes + checkbox/Enabled state + the
    // type-global params) becomes the template for ITS type. When a NEW element of that type is typed
    // in the tree, Apply overlays the saved template onto the canonical timber list.
    //
    // Storage: ONE user-scoped Settings string (TypeDefaultsJson) holding a dictionary
    //   key("Bent:KingPost" / "Bay:Purlins") -> single-element frame JSON.
    // Each template is just a 1-element FrameSpec round-tripped through FrameSpecStore (no new
    // serializer); the nested JSON string is escaped/unescaped by System.Text.Json.
    public static class TypeDefaults
    {
        private static TimberDraw.Properties.Settings S => TimberDraw.Properties.Settings.Default;

        // The template key for an element, or null when its type isn't set yet (nothing to key on).
        public static string Key(FrameElement el)
        {
            if (el is BentSpec b && b.TypeIsSet) return "Bent:" + b.BentType;
            if (el is BaySpec y) { y.SyncRoofType(); return "Bay:" + y.RoofType; }  // roof from the checkbox
            return null;
        }

        // Save `el` as the default for its type. Returns the key, or null if the type isn't set.
        public static string Save(FrameElement el)
        {
            string key = Key(el);
            if (key == null) return null;
            var map = Load();
            map[key] = FrameSpecStore.ElementToJson(el);
            S.TypeDefaultsJson = JsonSerializer.Serialize(map);
            S.Save();
            return key;
        }

        // Overlay the saved template for `target`'s type onto it (no-op if none saved). The caller has
        // already RebuildTimbers()'d `target`, so it holds the canonical timber list for the type;
        // this copies the saved type-global params + each timber's Enabled + Size by Key. Canonical
        // structure always wins (a saved template missing a member keeps the factory default).
        public static void Apply(IMemberOwner target)
        {
            string key = Key((FrameElement)target);
            if (key == null) return;
            if (!TryGet(key, out FrameElement savedEl)) return;
            ApplyElement(target, savedEl);
        }

        // Overlay a SPECIFIC saved bent/bay onto `target` (already RebuildTimbers()'d, so it holds the
        // canonical timber list for the type): copies the type-global CONFIG + each timber's Enabled + Size by
        // Key. Canonical structure always wins; frame LAYOUT (a bent's Separation) and GLOBALS (span / eave /
        // pitch -- not on the element) never travel. Shared by Apply (the per-type default) AND the named
        // template library (TemplateLibrary).
        public static void ApplyElement(IMemberOwner target, FrameElement savedEl)
        {
            if (target is BentSpec tb && savedEl is BentSpec sb)
            {
                tb.QueenOffset = sb.QueenOffset;
                tb.HBDivisor = sb.HBDivisor;
                tb.OffsetType = sb.OffsetType;       // brace / strut / vstrut centering
                tb.GirtDrop = sb.GirtDrop;           // tie drop
                Overlay(tb.Timbers, sb.Timbers);
            }
            else if (target is BaySpec ty && savedEl is BaySpec sy)
            {
                ty.CommonMode = sy.CommonMode; ty.CommonCount = sy.CommonCount; ty.CommonSpacing = sy.CommonSpacing;
                ty.CommonW = sy.CommonW; ty.CommonD = sy.CommonD;
                ty.CommonTail = sy.CommonTail; ty.CommonTailCut = sy.CommonTailCut;
                ty.PurlinMode = sy.PurlinMode; ty.PurlinCount = sy.PurlinCount; ty.PurlinSpacing = sy.PurlinSpacing;
                ty.PurlinW = sy.PurlinW; ty.PurlinD = sy.PurlinD;
                ty.OffsetType = sy.OffsetType;       // bay brace centering
                Overlay(ty.Timbers, sy.Timbers);
                ty.SyncRoofType();   // keep the derived roof coherent with the applied checkboxes
            }
        }

        // ROOF-SCOPED overlay for a bay whose roof CHECKBOX changed: copy only the saved roof-type
        // params and the roof members' state -- the bay's floor girt / floor braces / eave girt keep
        // their checkboxes (the full overlay was silently unchecking them; Robert's bug).
        public static void ApplyRoofOnly(BaySpec bay)
        {
            string key = Key(bay);
            if (key == null) return;
            if (!TryGet(key, out FrameElement savedEl) || !(savedEl is BaySpec sy)) return;
            bay.CommonMode = sy.CommonMode; bay.CommonCount = sy.CommonCount; bay.CommonSpacing = sy.CommonSpacing;
            bay.CommonW = sy.CommonW; bay.CommonD = sy.CommonD;
            bay.CommonTail = sy.CommonTail; bay.CommonTailCut = sy.CommonTailCut;
            bay.PurlinMode = sy.PurlinMode; bay.PurlinCount = sy.PurlinCount; bay.PurlinSpacing = sy.PurlinSpacing;
            bay.PurlinW = sy.PurlinW; bay.PurlinD = sy.PurlinD;
            Overlay(bay.Timbers, sy.Timbers,
                k => k.StartsWith("Commons:") || k.StartsWith("Purlins:"));
            bay.SyncRoofType();
        }

        // Copy Enabled + Size from the saved timbers onto the canonical ones, matched by Key.
        // `take` limits which keys the overlay may touch (null = all).
        private static void Overlay(List<Timber> canonical, List<Timber> saved,
            System.Func<string, bool> take = null)
        {
            foreach (Timber c in canonical)
            {
                if (take != null && !take(c.Key)) continue;
                Timber s = saved.Find(t => t.Key == c.Key);
                if (s == null) continue;
                c.Enabled = s.Enabled;
                c.Size = s.Size.Clone();
            }
        }

        private static bool TryGet(string key, out FrameElement el)
        {
            el = null;
            var map = Load();
            if (!map.TryGetValue(key, out string json) || string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                el = FrameSpecStore.ElementFromJson(json);
                return el != null;
            }
            catch { return false; }
        }

        private static Dictionary<string, string> Load()
        {
            string json = S.TypeDefaultsJson;
            if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                       ?? new Dictionary<string, string>();
            }
            catch { return new Dictionary<string, string>(); }
        }
    }
}
