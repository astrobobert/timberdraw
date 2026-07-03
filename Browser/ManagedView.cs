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
                string type = "Timber", desig = "", frame = "", bent = "", bay = "", wall = "", label = "";
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
                    Frame = frame, Bent = bent, Bay = bay, Wall = wall, GridLabel = label
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

        // Select (grip-highlight) the row's timber and zoom the view to it -- the browser's
        // select-to-zoom payoff. ZOOM Object on the pickfirst selection frames the timber correctly
        // in ANY view (a WCS-extents window mis-frames it in iso/3D views).
        public static void ZoomAndHighlight(ObjectId id)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null || id.IsNull) return;
            doc.Editor.SetImpliedSelection(new[] { id });   // pickfirst set (grip feedback + ZOOM target)
            doc.SendStringToExecute("_.ZOOM _Object ", true, false, true);
        }

        // Re-section the timber (write-back from the browser's property editor). Returns the NEW
        // ObjectId (regen erases + redraws -> a new handle) so the caller can reselect it.
        public static ObjectId ApplySection(ObjectId id, double w, double d, string type)
            => ManagedTimber.RegenerateSection(id, w, d, type);
    }
}
