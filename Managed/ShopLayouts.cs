using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{
    // Paper-space wrapper for the shop maps: ONE layout ("TM Shop") holding a SEPARATE floating viewport for
    // EACH bent + wall drawing, every viewport framing its own map's model region at a fixed scale
    // (3/8" = 1'-0"). Viewports are flowed left->right (wrapping into rows) on the sheet. Titles + note
    // bubbles are drawn in model space by ShopMaps, so they read through each viewport. The ObjectARX
    // layout/viewport gotchas live here (isolated from the geometry):
    //  - A fresh layout ALREADY comes with a floating viewport -> reuse it for the first drawing (skip the
    //    "overall" paper viewport, which is first in the paper space), create the rest, erase any leftover.
    //  - A viewport's On flag only takes effect in an ACTIVE layout -> the layout is made current first;
    //    the caller's prior layout is restored at the end.
    //  - CustomScale is paper-units / model-units: 3/8" = 1'-0" -> 0.375/12 = 1/32.
    //  - CreateLayout throws if the name exists (or contains : ; < > / \ " ? * | , =) -> colon-free name,
    //    same-named layout deleted first.
    public static class ShopLayouts
    {
        public const string Prefix = "TM ";          // ownership marker (colon-free); shop layouts start with this
        private const string SheetName = Prefix + "Shop";

        private const double Scale       = 1.0 / 32.0; // 3/8" = 1'-0"
        private const double Margin      = 1.0;        // paper inches from the sheet corner
        private const double VpPad       = 0.5;        // paper-inch margin inside each viewport around its drawing
        private const double VpGutter    = 1.0;        // paper inches between viewports
        private const double MaxRowWidth = 32.0;       // wrap the viewport flow past this paper width

        // Create/refresh the single "TM Shop" layout with one viewport per drawn bent/wall map, each at
        // 3/8"=1'-0". Assumes ShopMaps.Draw already ran (RegionOrigin/RegionW/RegionH set). Restores layout.
        public static void Create(Database db, List<ShopMaps.ShopMap> maps)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            // Include the Plan-kind "Column Grid" map (framed in place with the 3D model frozen).
            var drawn = maps.Where(m => m.RegionW > 0).ToList();
            if (drawn.Count == 0) return;

            LayoutManager lm = LayoutManager.Current;
            string saved = lm.CurrentLayout;

            using (doc.LockDocument())
            {
                if (lm.GetLayoutId(SheetName).IsValid) lm.DeleteLayout(SheetName);
                ObjectId layoutId = lm.CreateLayout(SheetName);
                lm.CurrentLayout = SheetName;                       // activate so the viewports turn On

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    var lay = (Layout)tr.GetObject(layoutId, OpenMode.ForRead);
                    var ps  = (BlockTableRecord)tr.GetObject(lay.BlockTableRecordId, OpenMode.ForWrite);
                    var pool = new Queue<Viewport>(FloatingViewports(tr, ps));   // reuse the built-in vp(s)

                    double cursorX = Margin, rowBottom = Margin, rowH = 0.0;
                    foreach (ShopMaps.ShopMap m in drawn)
                    {
                        FramedRect(m, out double mcx, out double mcy, out double fh, out double vpW, out double vpH);
                        if (cursorX > Margin && cursorX + vpW > Margin + MaxRowWidth)   // wrap to a new row
                        {
                            cursorX = Margin; rowBottom += rowH + VpGutter; rowH = 0.0;
                        }
                        Viewport vp = pool.Count > 0 ? pool.Dequeue() : NewViewport(tr, ps);
                        Frame(vp, mcx, mcy, fh, vpW, vpH, cursorX + vpW / 2.0, rowBottom + vpH / 2.0);
                        // The column-grid plan frames the real frame location -> hide the 3D timbers in it,
                        // leaving only the structural grid + post feet.
                        if (m.Kind == ShopMaps.MapKind.Plan) FreezeModelInViewport(tr, db, vp);
                        cursorX += vpW + VpGutter;
                        rowH = Math.Max(rowH, vpH);
                    }
                    while (pool.Count > 0) pool.Dequeue().Erase();   // no drawing left for a leftover default vp

                    tr.Commit();
                }
            }

            try { lm.CurrentLayout = saved; } catch { }
            doc.Editor.Regen();
        }

        // Delete the shop layout(s) ("TM *"). Returns the count. Switches to Model first (the current layout
        // can't be deleted). Used by TShopClear + at the top of a regenerate.
        public static int DeleteAll(Database db)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return 0;
            var names = new List<string>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var dict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry e in dict)
                    if (e.Key.StartsWith(Prefix, StringComparison.Ordinal)) names.Add(e.Key);
                tr.Commit();
            }
            if (names.Count == 0) return 0;

            LayoutManager lm = LayoutManager.Current;
            using (doc.LockDocument())
            {
                try { lm.CurrentLayout = "Model"; } catch { }
                foreach (string n in names) { try { lm.DeleteLayout(n); } catch { } }
            }
            return names.Count;
        }

        // The floating viewports a fresh layout already carries (skip the FIRST paper-space viewport -- the
        // "overall" one). One reusable default is the norm; more (if a template added them) get reused/erased.
        private static List<Viewport> FloatingViewports(Transaction tr, BlockTableRecord ps)
        {
            var list = new List<Viewport>();
            bool skippedOverall = false;
            foreach (ObjectId id in ps)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is Viewport)) continue;
                if (!skippedOverall) { skippedOverall = true; continue; }   // the overall paper viewport
                list.Add((Viewport)tr.GetObject(id, OpenMode.ForWrite));
            }
            return list;
        }

        private static Viewport NewViewport(Transaction tr, BlockTableRecord ps)
        {
            var vp = new Viewport();
            ps.AppendEntity(vp);
            tr.AddNewlyCreatedDBObject(vp, true);
            return vp;
        }

        // Freeze every layer in this viewport EXCEPT the structural grid + shop layers, so the column-grid
        // plan shows only the grid and the post feet (not the 3D timbers seen from above).
        private static void FreezeModelInViewport(Transaction tr, Database db, Viewport vp)
        {
            var keep = new HashSet<string>(new[]
            {
                ManagedTimber.GridLayer, ManagedTimber.GridTempLayer, ManagedTimber.ShopLayer
            }, StringComparer.OrdinalIgnoreCase);

            var freeze = new List<ObjectId>();
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId lid in lt)
            {
                var ltr = (LayerTableRecord)tr.GetObject(lid, OpenMode.ForRead);
                if (!keep.Contains(ltr.Name)) freeze.Add(lid);
            }
            try { if (freeze.Count > 0) vp.FreezeLayersInViewport(freeze.GetEnumerator()); } catch { }
        }

        // The framed model rect for a map (drawing + reserved bubble row below + title above; a FLOOR plan
        // also reserves a bubble COLUMN on the left for its wall letters) and the paper viewport size to
        // show it at Scale. Outputs the model-space center + model height + paper W/H.
        private static void FramedRect(ShopMaps.ShopMap m, out double mcx, out double mcy,
                                       out double fh, out double vpW, out double vpH)
        {
            double left = m.Kind == ShopMaps.MapKind.Floor ? ShopMaps.BubbleReserve : 0.0;
            double fx0 = m.RegionOrigin.X - left;
            double fy0 = m.RegionOrigin.Y - ShopMaps.BubbleReserve;
            double fw  = m.RegionW + left;
            fh = m.RegionH + ShopMaps.BubbleReserve + ShopMaps.TitleReserve;
            mcx = fx0 + fw / 2.0;
            mcy = fy0 + fh / 2.0;
            vpW = fw * Scale + 2.0 * VpPad;
            vpH = fh * Scale + 2.0 * VpPad;
        }

        // Size + place one viewport on the sheet and zoom it so its map reads at exactly 3/8"=1'-0".
        private static void Frame(Viewport vp, double mcx, double mcy, double fh,
                                  double vpW, double vpH, double px, double py)
        {
            vp.Width  = vpW;
            vp.Height = vpH;
            vp.CenterPoint   = new Point3d(px, py, 0.0);
            vp.ViewDirection = Vector3d.ZAxis;                    // plan view (maps lie flat on world XY)
            vp.ViewTarget    = Point3d.Origin;                    // reset any stale target on a reused viewport
            vp.ViewCenter    = new Point2d(mcx, mcy);             // pan this model point to the vp center
            vp.ViewHeight    = fh + 2.0 * VpPad / Scale;
            vp.CustomScale   = Scale;                             // enforce 3/8" = 1'-0"
            try { vp.On = true; } catch { }                      // needs the layout active (it is)
            vp.Locked = true;                                    // lock the scale against pan/zoom
        }
    }
}
