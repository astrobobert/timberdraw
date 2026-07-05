using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace TimberDraw.Browser
{
    // AutoCAD interop for the Frame Browser, kept out of the view-model: read the managed timbers
    // (+ their XData type/size) into display rows, count the derived nodes, and select/zoom a row's
    // timber in the drawing. The view-model calls these; nothing here touches WPF.
    public static class ManagedView
    {
        // One display row per managed timber in the active drawing.
        public static List<FrameBrowserItem> LoadTimbers()
        {
            var items = new List<FrameBrowserItem>();
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return items;
            Database db = doc.Database;

            foreach ((ObjectId id, ManagedTimber.TFrame f) in ManagedTimber.Enumerate(db))
            {
                string type = "Timber", desig = "", frame = "", bent = "", bay = "", wall = "", floor = "", label = "";
                try
                {
                    Module1.DataStructure xd = Module1.GetXdata(id);
                    if (xd != null)
                    {
                        if (!string.IsNullOrEmpty(xd.Type)) type = xd.Type;
                        desig = xd.Designation ?? "";
                        frame = xd.FrameTag ?? "";
                        bent  = xd.BentNumber ?? "";
                        bay   = xd.BayTag ?? "";
                        wall  = xd.WallTag ?? "";
                        floor = xd.FloorTag ?? "";
                        label = xd.GridLabel ?? "";
                    }
                }
                catch { /* fall back to defaults */ }

                string size = (int)System.Math.Round(f.W) + "x" + (int)System.Math.Round(f.D)
                            + "x" + Module1.BuyLongFeet(f.L);
                items.Add(new FrameBrowserItem
                {
                    Id = id, Type = type, Designation = desig, Size = size,
                    Handle = id.Handle.ToString(), W = f.W, D = f.D,
                    Frame = frame, Bent = bent, Bay = bay, Wall = wall, Floor = floor, GridLabel = label
                });
            }
            return items;
        }

        // Count of derived coincidence nodes (same as TScan), for the header.
        public static int NodeCount()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return 0;
            return ManagedTimber.ComputeNodes(doc.Database).Count;
        }

        // Grip-highlight the rows' timbers in the drawing (pickfirst set) -- selection feedback
        // with NO view change (Robert's call: highlight on select, zoom only on demand). An empty
        // selection clears the highlight. Hardened: SetImpliedSelection silently fails (or throws
        // into the message pump, invisibly) while the editor is mid-command, so skip that pass --
        // the next quiescent click highlights normally. Same busy-skip philosophy as the BOM grid.
        public static void Highlight(List<ObjectId> ids)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            try
            {
                if (System.Convert.ToInt32(Application.GetSystemVariable("CMDACTIVE")) != 0) return;
                var live = new List<ObjectId>();
                if (ids != null)
                    foreach (ObjectId id in ids)
                        if (!id.IsNull && !id.IsErased) live.Add(id);
                doc.Editor.SetImpliedSelection(live.ToArray());
            }
            catch { /* editor busy / transient interop failure -- skip this highlight pass */ }
        }

        // Zoom the view to one timber (double-click) -- ZOOM Object on the pickfirst selection
        // frames it correctly in ANY view (a WCS-extents window mis-frames it in iso/3D views).
        public static void ZoomAndHighlight(ObjectId id)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null || id.IsNull || id.IsErased) return;
            doc.Editor.SetImpliedSelection(new[] { id });   // pickfirst set (grip feedback + ZOOM target)
            doc.SendStringToExecute("_.ZOOM _Object ", true, false, true);
        }

        // Re-section the timber (write-back from the browser's property editor). Returns the NEW
        // ObjectId (regen erases + redraws -> a new handle) so the caller can reselect it.
        public static ObjectId ApplySection(ObjectId id, double w, double d, string type)
            => ManagedTimber.RegenerateSection(id, w, d, type);

        // Assign the given timbers (the browser's selected rows) to Frame -> Bent / Wall+Bay /
        // Floor: stash the target + ids on ManagedAssembly and fire TAssign, which applies them
        // inside its own command context (document lock, label minting, layer moves) -- the
        // JoinSession deferred-write pattern. The browser refreshes on ManagedAssembly.Applied.
        public static void Assign(List<ObjectId> ids, string frame, string kind, string owner, string bay)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null || ids == null || ids.Count == 0) return;
            ManagedAssembly.Set(frame, kind, owner, bay);
            if (!ManagedAssembly.HasCurrent) return;   // no usable target -> nothing to send
            ManagedAssembly.StashIds(ids);
            doc.SendStringToExecute("TAssign ", true, false, true);
        }
    }
}
