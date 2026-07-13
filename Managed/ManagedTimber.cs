using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;

[assembly: CommandClass(typeof(TimberDraw.ManagedCommands))]
namespace TimberDraw
{
    // Managed-timber assembly model (NEW direction, additive -- does not touch the TDraw/TFrame
    // pipeline). A timber is a managed solid: its SHAPE comes from attributes (W x D x L, type,
    // per-end cut), drawn as a box placed freely in WCS. Each managed timber also stores its
    // placement FRAME (origin + axes + L/D/W) in an xrecord, so its 6 faces can be computed
    // analytically -- no BRep. Nodes are NOT stored: they are derived from face coincidence by a
    // rescan (TScan). WCS is the source of truth; UCS is only an input convenience.
    public static partial class ManagedTimber
    {
        public const string FrameKey = "TMFrame";   // xrecord on the solid's extension dictionary
        public const string NodeLayer = "TM_NODES"; // transient coincidence markers
        public const string ScarfKey = "TMScarf";   // xrecord: a scarf splice point this timber carries
        public const string SeatKey  = "TMSeat";    // xrecord: explicit seat nodes a timber carries (brace seats -- oblique member cuts aren't analytic faces)
        public const string GridKey  = "TMGrid";    // xrecord on a structural-grid entity: its FrameTag
        public const string JointSpecsKey = "TMJointSpecs"; // xrecord: per joint id, the editor's OPAQUE spec state string (jid:Real, state:Text)*
        public const string GridLayer = "TM_GRID";  // layer for the drawn structural grid (lines + bubbles)
        public const string GridTempLayer = "TM_GRID_TEMP";  // provisional grid lines for Free Assembly elements
        public const string ShopKey     = "TMShop";      // xrecord on a shop-drawing entity (outline / label / context)
        public const string ShopLayer   = "TM_SHOP";     // layer for the drawn 2D assembly maps (primary outlines + labels)
        public const string ShopCtxLayer = "TM_SHOP_CTX"; // faded context (connected neighbors, arris sections, direction marks)
        public const string GroupPrefix = "TM_";     // per-group timber layers: TM_<frame>_Bent<n> / TM_<frame>_Wall<letter>

        // The per-group layer a timber belongs to -- a bent member -> its bent's layer, a longitudinal /
        // wall member -> its wall's layer. Used for tree-checkbox isolation (toggle the layer on/off).
        public static string GroupLayer(string frameTag, string bentTag, string wallTag, string floorTag = "")
        {
            string f = string.IsNullOrWhiteSpace(frameTag) ? "A" : frameTag.Trim();
            if (!string.IsNullOrEmpty(bentTag)) return GroupPrefix + f + "_Bent" + bentTag;
            if (!string.IsNullOrEmpty(wallTag)) return GroupPrefix + f + "_Wall" + wallTag;
            if (!string.IsNullOrEmpty(floorTag)) return GroupPrefix + f + "_Floor" + floorTag;
            return GroupPrefix + f;
        }

        // Ensure a layer exists (neutral color ACI 7; never green -- the user is red/green colorblind).
        // Returns its id. Call inside an open transaction.
        public static ObjectId EnsureFrameLayer(Transaction tr, Database db, string name)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return lt[name];
            lt.UpgradeOpen();
            var ltr = new LayerTableRecord
            {
                Name = name,
                Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 7)
            };
            ObjectId id = lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
            return id;
        }

        // Read/Write a group layer's visibility (On/Off) for tree-checkbox isolation. Visible = layer On.
        public static bool IsGroupVisible(Database db, string layerName)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                bool vis = !lt.Has(layerName) ||
                           !((LayerTableRecord)tr.GetObject(lt[layerName], OpenMode.ForRead)).IsOff;
                tr.Commit();
                return vis;
            }
        }

        public static void SetGroupVisible(Database db, string layerName, bool visible)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectId lid = EnsureFrameLayer(tr, db, layerName);
                var ltr = (LayerTableRecord)tr.GetObject(lid, OpenMode.ForWrite);
                ltr.IsOff = !visible;
                tr.Commit();
            }
        }

        // Erase every MANAGED timber in the current space (those carrying a TMFrame xrecord) -- the
        // tree editor's draw/redraw clears the prior frame before re-emitting, leaving non-managed
        // solids untouched. Returns the count erased. Reversible via AutoCAD UNDO. NOTE: today this
        // clears ALL managed timbers (one generated frame at a time); per-frame clearing (multiple
        // managed frames in one drawing) needs a frame tag on each timber -- see the grouping layer.
        public static int EraseAllManaged(Database db) => EraseFrame(db, null);

        // Erase managed timbers (those carrying a TMFrame xrecord). When `frameTag` is null, clears
        // EVERY managed timber (legacy whole-frame redraw). When `frameTag` is given, clears only the
        // GENERATOR'S OWN timbers carrying that FrameTag -- a regenerate never touches hand-placed
        // (free-assembly) work, assigned or not: a timber survives when it carries the Free marker
        // (stamped at editor creation) OR a FloorTag (floor-owned members are free by construction --
        // the generator never writes FloorTag, so this also protects joists/summers placed before the
        // marker existed). Survivors JOINTED TO the erased skeleton then get those joints' features
        // STRIPPED (and rebuild) -- the new mate is fresh uncut wood, so the half-joint is stale. The
        // per-joint recipe stamps are KEPT: TJointSync re-attaches them deliberately, or TJointAll /
        // the Joints pane cuts fresh. Non-managed solids untouched. Reversible via AutoCAD UNDO.
        // Returns the count erased.
        public static int EraseFrame(Database db, string frameTag)
        {
            int n = 0;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return 0;
            var erasedJoints = new HashSet<int>();   // joint ids the erased skeleton carried
            var survivors = new List<ObjectId>();    // managed timbers kept (free / floor-owned / other frames)
            using (doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                    foreach (ObjectId id in btr)
                    {
                        if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
                        if (!TryReadFrame(tr, ent, out TFrame f)) continue;
                        if (frameTag != null)
                        {
                            if (ReadXTextField(tr, ent, "FrameTag") != frameTag         // another frame's: keep
                                || ReadXTextField(tr, ent, "Free") == "1"               // hand-placed: keep
                                || ReadXTextField(tr, ent, "FloorTag") != "")           // floor-owned: keep
                            { survivors.Add(id); continue; }
                            foreach (int j in JointIds(f)) erasedJoints.Add(j);
                        }
                        ent.UpgradeOpen(); ent.Erase(); n++;
                    }
                    tr.Commit();
                }

                // The ORPHAN SWEEP: a joint id is pairwise, so an erased timber's id found on a survivor
                // is a half-joint whose mate just died. Strip those features and rebuild the survivor
                // plain (its recipe stamps ride across RebuildFromFrame untouched).
                if (erasedJoints.Count > 0)
                {
                    int strippedJoints = 0, strippedTimbers = 0;
                    foreach (ObjectId sid in survivors)
                    {
                        if (!TryReadFrame(db, sid, out TFrame f)) continue;
                        List<int> orphaned = null;
                        foreach (int j in JointIds(f))
                            if (erasedJoints.Contains(j)) (orphaned ??= new List<int>()).Add(j);
                        if (orphaned == null) continue;
                        foreach (int j in orphaned) StripJoint(ref f, j);
                        if (RebuildFromFrame(sid, f).IsNull) continue;
                        strippedTimbers++; strippedJoints += orphaned.Count;
                    }
                    if (strippedTimbers > 0)
                        doc.Editor.WriteMessage("\n  regen: stripped " + strippedJoints
                            + " orphaned joint(s) from " + strippedTimbers
                            + " surviving timber(s); recipes kept -- TJointSync re-attaches, or re-cut via TJointAll / the Joints pane.");
                }
            }
            return n;
        }

        // Erase the drawn structural-grid entities (lines + bubbles) carrying a TMGrid xrecord whose
        // value matches `frameTag` -- the per-frame grid redraw (mirrors EraseFrame). Returns the count.
        public static int EraseGrid(Database db, string frameTag)
        {
            int n = 0;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return 0;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
                    if (ReadGridTag(tr, ent) != frameTag) continue;
                    ent.UpgradeOpen(); ent.Erase(); n++;
                }
                tr.Commit();
            }
            return n;
        }

        // The FrameTag stored in an entity's TMGrid xrecord, or null if it carries none (not a grid entity).
        private static string ReadGridTag(Transaction tr, Entity ent)
        {
            if (ent.ExtensionDictionary.IsNull) return null;
            if (!(tr.GetObject(ent.ExtensionDictionary, OpenMode.ForRead) is DBDictionary dict)) return null;
            if (!dict.Contains(GridKey)) return null;
            if (!(tr.GetObject(dict.GetAt(GridKey), OpenMode.ForRead) is Xrecord xr)) return null;
            TypedValue[] v = xr.Data?.AsArray();
            return (v != null && v.Length > 0) ? v[0].Value.ToString() : "";
        }

        // Tag an entity as part of frame `frameTag`'s structural grid (a TMGrid xrecord on its extension
        // dictionary) so EraseGrid can clear it on redraw. Call inside an open transaction with the
        // entity open for write and newly appended.
        public static void WriteGridTag(Transaction tr, Entity ent, string frameTag)
        {
            ent.CreateExtensionDictionary();
            var dict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, OpenMode.ForWrite);
            var xr = new Xrecord { Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, frameTag ?? "")) };
            dict.SetAt(GridKey, xr);
            tr.AddNewlyCreatedDBObject(xr, true);
        }

        // Tag a shop-drawing entity (outline / label) with a TMShop xrecord so EraseShop can clear the 2D
        // assembly maps on redraw -- the shop counterpart of WriteGridTag, keyed separately (ShopKey) so
        // regenerating the maps never touches the structural grid. Call inside an open transaction with
        // the entity open for write and newly appended.
        public static void WriteShopTag(Transaction tr, Entity ent)
        {
            ent.CreateExtensionDictionary();
            var dict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, OpenMode.ForWrite);
            var xr = new Xrecord { Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, ShopLayer)) };
            dict.SetAt(ShopKey, xr);
            tr.AddNewlyCreatedDBObject(xr, true);
        }

        private static bool HasShopTag(Transaction tr, Entity ent)
        {
            if (ent.ExtensionDictionary.IsNull) return false;
            if (!(tr.GetObject(ent.ExtensionDictionary, OpenMode.ForRead) is DBDictionary dict)) return false;
            return dict.Contains(ShopKey);
        }

        // Erase every drawn shop-map entity (those carrying a TMShop xrecord) in the current space --
        // the 2D-map redraw primitive (mirrors EraseGrid). Returns the count erased. Reversible via UNDO.
        public static int EraseShop(Database db)
        {
            int n = 0;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return 0;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
                    if (!HasShopTag(tr, ent)) continue;
                    ent.UpgradeOpen(); ent.Erase(); n++;
                }
                tr.Commit();
            }
            return n;
        }

        // Read one text-valued XData field from an entity's extension dictionary inside an existing
        // transaction (Module1.GetXdata opens its own transaction, which we cannot nest in the erase
        // loop). Returns "" when the dictionary or key is absent.
        private static string ReadXTextField(Transaction tr, Entity ent, string key)
        {
            if (ent.ExtensionDictionary.IsNull) return "";
            if (!(tr.GetObject(ent.ExtensionDictionary, OpenMode.ForRead) is DBDictionary dict)) return "";
            if (!dict.Contains(key)) return "";
            if (!(tr.GetObject(dict.GetAt(key), OpenMode.ForRead) is Xrecord xr)) return "";
            TypedValue[] v = xr.Data?.AsArray();
            return (v != null && v.Length > 0) ? v[0].Value.ToString() : "";
        }
    }
}
