using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TimberDraw
{
    // A NAMED library of bent/bay templates -- the user saves MANY configured bents/bays and applies them by
    // name. (TypeDefaults holds ONE auto-applied default per type; this is the named, many catalog.) Each
    // template is the same single-element FrameSpec JSON (FrameSpecStore.ElementToJson) tagged with the type it
    // came from (TypeDefaults.Key: "Bent:KingPost" / "Bay:Purlins"), so apply can be filtered to a compatible
    // target. Storage: ONE user-scoped Settings string (TemplateLibraryJson) holding name -> { Type, Json } --
    // per-user, reusable across drawings, like TypeDefaults. Geometry CONFIG only: TypeDefaults.ApplyElement
    // overlays sizes + Enabled + the type params and never the frame globals (span/eave/pitch) or a bent's
    // Separation.
    public static class TemplateLibrary
    {
        private static TimberDraw.Properties.Settings S => TimberDraw.Properties.Settings.Default;

        public class Entry { public string Type { get; set; } public string Json { get; set; } }

        // Save (or replace) `el` under `name`. False if the name is blank or the element's type isn't set.
        public static bool Save(string name, FrameElement el)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            string type = TypeDefaults.Key(el);
            if (type == null) return false;
            var map = Load();
            map[name.Trim()] = new Entry { Type = type, Json = FrameSpecStore.ElementToJson(el) };
            Store(map);
            return true;
        }

        public static bool TryGet(string name, out FrameElement el)
        {
            el = null;
            if (string.IsNullOrWhiteSpace(name)) return false;
            var map = Load();
            if (!map.TryGetValue(name.Trim(), out Entry e) || string.IsNullOrWhiteSpace(e?.Json)) return false;
            try { el = FrameSpecStore.ElementFromJson(e.Json); return el != null; }
            catch (System.Exception ex)
            {
                // The named template silently stops applying.
                Diag.Warn("TemplateLibrary.TryGet", name.Trim() + " unreadable: " + ex.Message);
                return false;
            }
        }

        public static void Remove(string name)
        {
            var map = Load();
            if (name != null && map.Remove(name.Trim())) Store(map);
        }

        public static bool Rename(string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName)) return false;
            oldName = oldName.Trim(); newName = newName.Trim();
            if (newName == oldName) return true;
            var map = Load();
            if (!map.TryGetValue(oldName, out Entry e) || map.ContainsKey(newName)) return false;
            map.Remove(oldName);
            map[newName] = e;
            Store(map);
            return true;
        }

        public static bool Exists(string name) => name != null && Load().ContainsKey(name.Trim());

        // All entries as (name, type), in name order.
        public static IEnumerable<(string Name, string Type)> All()
        {
            foreach (KeyValuePair<string, Entry> kv in Load().OrderBy(k => k.Key))
                yield return (kv.Key, kv.Value.Type);
        }

        // Names whose template type matches `target`'s type (so applying them is meaningful), in name
        // order. An UNTYPED target (a fresh bent, a bay with neither roof box checked -> "Bay:None")
        // matches EVERY template of its kind instead -- the template carries the type, and applying
        // it adopts that type first (Robert's ask: Apply Template shouldn't wait for the roof to be
        // set by hand). A typed element still lists only its own type (a KingPost bent never offers
        // a QueenPost overlay).
        public static IEnumerable<string> CompatibleNames(FrameElement target)
        {
            string key = TypeDefaults.Key(target);
            string prefix = key == null && target is BentSpec ? "Bent:"
                          : key == "Bay:None" ? "Bay:"
                          : null;
            if (key == null && prefix == null) yield break;
            foreach (KeyValuePair<string, Entry> kv in Load().OrderBy(k => k.Key))
                if (prefix != null
                        ? kv.Value.Type != null && kv.Value.Type.StartsWith(prefix)
                        : kv.Value.Type == key)
                    yield return kv.Key;
        }

        private static Dictionary<string, Entry> Load()
        {
            string json = S.TemplateLibraryJson;
            if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, Entry>();
            try { return JsonSerializer.Deserialize<Dictionary<string, Entry>>(json) ?? new Dictionary<string, Entry>(); }
            catch (System.Exception ex)
            {
                // The whole template library is unreadable: every saved template disappears from menus.
                Diag.Warn("TemplateLibrary.Load", "TemplateLibraryJson corrupt, starting empty: " + ex.Message);
                return new Dictionary<string, Entry>();
            }
        }

        private static void Store(Dictionary<string, Entry> map)
        {
            S.TemplateLibraryJson = JsonSerializer.Serialize(map);
            S.Save();
        }
    }
}
