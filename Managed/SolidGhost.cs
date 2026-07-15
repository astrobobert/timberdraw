using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{
    // Shared SOLID placement preview (Robert's call, 2026-07-15: no transient/wireframe ghosts --
    // a preview is a real model-space Solid3d, blue ACI 5, rendered in whatever visual style is
    // current). The owning command Update()s it on every screen pick / palette change and erases
    // it when the loop ends (Dispose); the accepted timber is then drawn by the normal Draw* path,
    // so the preview never carries xdata or a frame xrecord (it is NOT a managed timber -- TScan /
    // TBom ignore it, and a crash at worst leaves one plain blue solid to erase by hand).
    //
    // PALETTE LIVENESS: a palette handler holds no document lock, so it must never write the db.
    // Its Changed handler calls Nudge(), which queues the hidden NudgeKeyword into the pending
    // prompt; the command loop catches the keyword and rebuilds IN COMMAND CONTEXT. If the loop
    // has just ended (race), the queued word falls through to the bare command line, where it runs
    // the no-op TMNUDGE command (PlaceCommands.cs) instead of erroring.
    public sealed class SolidGhost : System.IDisposable
    {
        public const string NudgeKeyword = "TMNUDGE";

        private ObjectId _id = ObjectId.Null;

        // Queue a preview rebuild into the pending prompt. Safe from any palette/UI event.
        public static void Nudge()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            doc?.SendStringToExecute(NudgeKeyword + " ", true, false, false);
        }

        // The hidden keyword every ghost-loop prompt carries (never shown in the prompt text).
        public static void AddNudge(KeywordCollection kws)
            => kws.Add(NudgeKeyword, NudgeKeyword, NudgeKeyword, false, true);

        // A plain square-ended box frame for previews (TPlace / TSpan). Args by ROLE, like DrawBox.
        public static ManagedTimber.TFrame BoxFrame(Point3d originWcs, Vector3d lengthAxis,
            Vector3d depthAxis, Vector3d widthAxis, double length, double depth, double width)
            => new ManagedTimber.TFrame
            {
                O = originWcs, X = widthAxis, Y = depthAxis, Z = lengthAxis,
                L = length, D = depth, W = width,
                NearN = -lengthAxis, FarN = lengthAxis
            };

        // Rebuild the preview from the frame: erase the old solid, append the new one. COMMAND
        // context only (needs the document lock -- palette handlers go through Nudge()).
        public void Update(ManagedTimber.TFrame f)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                EraseIn(tr);
                Solid3d sol = ManagedTimber.BuildFramedSolid(f);
                sol.ColorIndex = 5;   // blue -- the ghost colour (colour-blind safe)
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                _id = btr.AppendEntity(sol);
                tr.AddNewlyCreatedDBObject(sol, true);
                sol.RecordHistory = false;   // uniform with every managed solid (save safety)
                sol.ShowHistory = false;
                tr.Commit();
            }
            // Show the new solid NOW -- the loop is usually parked on a prompt, and a palette-
            // nudged rebuild must be visible before any further drawing input.
            doc.TransactionManager.QueueForGraphicsFlush();
            doc.Editor.UpdateScreen();
        }

        public void Erase()
        {
            if (_id.IsNull) return;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            using (doc.LockDocument())
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                EraseIn(tr);
                tr.Commit();
            }
        }

        private void EraseIn(Transaction tr)
        {
            if (_id.IsNull) return;
            if (!_id.IsErased)
                ((Entity)tr.GetObject(_id, OpenMode.ForWrite)).Erase();
            _id = ObjectId.Null;
        }

        public void Dispose() => Erase();
    }
}
