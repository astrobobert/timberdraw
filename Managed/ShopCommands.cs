using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

namespace TimberDraw
{
    // Shop drawings: automated 2D "assembly elevation maps" of the managed frame. Every timber is drawn
    // in its real place as a nominal box outline and labeled where it sits, so each stick is seen in
    // context of its neighbors -- one map per BENT, one per WALL, one FLOOR PLAN per floor level
    // (joists/summers + carrier context), plus Floor 0 (the in-place structural grid + post feet; sills
    // later). This is the command surface (thin); the grouping + geometry live in ShopMaps, the
    // paper-space wrapper in ShopLayouts.
    public partial class ManagedCommands
    {
        [CommandMethod("TShop")]
        public static void BuildShop()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            var maps = ShopMaps.BuildMaps(doc.Database);
            if (maps.Count == 0)
            {
                ed.WriteMessage("\nNo managed timbers found -- nothing to draw.");
                return;
            }

            // Clear any prior shop output first so re-runs regenerate cleanly, then draw the model regions
            // and wrap each in a paper-space layout.
            ManagedTimber.EraseShop(doc.Database);
            ShopLayouts.DeleteAll(doc.Database);
            ShopMaps.Draw(doc.Database, maps);
            ShopLayouts.Create(doc.Database, maps);

            int bents  = maps.Count(m => m.Kind == ShopMaps.MapKind.Bent);
            int walls  = maps.Count(m => m.Kind == ShopMaps.MapKind.Wall);
            int floors = maps.Count(m => m.Kind == ShopMaps.MapKind.Floor);
            ed.WriteMessage($"\nShop: {bents} bent + {walls} wall"
                + (floors > 0 ? $" + {floors} floor plan(s)" : "")
                + " + Floor 0 (grid) on the 'TM Shop' layout at 3/8\"=1'-0\".");
            foreach (var m in maps)
                ed.WriteMessage($"\n  {m.Name}: {m.Members.Count} members");
        }

        [CommandMethod("TShopClear")]
        public static void ClearShop()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            int n = ManagedTimber.EraseShop(doc.Database);
            int L = ShopLayouts.DeleteAll(doc.Database);
            doc.Editor.WriteMessage($"\nCleared {n} shop-map entities and {L} shop layout(s).");
        }
    }
}
