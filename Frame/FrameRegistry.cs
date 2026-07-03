using System.Text.Json;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace TimberDraw
{
    // Per-frame record: the generator's recipe (seed params) + the freeze gate, stored on the FRAME
    // (not on any one timber). There is no frame-group entity, so the record lives in the drawing's
    // NamedObjectsDictionary under "TM_FRAME_<tag>" -- which persists across save/reload, as a frozen
    // gate must. Mirrors the JSON-in-Xrecord shape of Module1.SaveDrawContext, and the NOD read/write
    // shape of BentNetwork.
    //
    // The Frozen flag is the one-way break: pre-break the generator (TRoughIn) may re-emit the frame;
    // at the break (TFreeze) the parametric palette is locked for this frame and the skeleton timbers
    // carry on as the managed-editor truth. Type/Params/Placement are the seed kept so a future re-seed
    // can spawn a fresh frame from this recipe (re-seed itself is deferred).
    public sealed class FrameRecord
    {
        public string Type;            // bent type at emit (KingPost, QueenPost, ...)
        public KPBentParams Params;    // the seed recipe (may be null for a frozen legacy frame)
        public double[] Placement;     // graph->WCS placement matrix, Matrix3d.ToArray() (16 doubles)
        public bool Frozen;            // the break: true => parametric locked for this frame
    }

    public static class FrameRegistry
    {
        private const string KeyPrefix = "TM_FRAME_";

        // KPBentParams stores its data in public FIELDS (IncludeFields), and its derived levels are
        // computed get-only PROPERTIES (IgnoreReadOnlyProperties keeps them out of the JSON -- they
        // have no setter and would be ignored on read anyway).
        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            IncludeFields = true,
            IgnoreReadOnlyProperties = true
        };

        private static string Key(string frameTag) =>
            KeyPrefix + (string.IsNullOrWhiteSpace(frameTag) ? "A" : frameTag.Trim());

        // Write (overwrite) the record for a frame. JSON stored as a single DxfCode.Text value.
        public static void Save(Database db, string frameTag, FrameRecord rec)
        {
            if (db == null || rec == null) return;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            string key = Key(frameTag);
            string json = JsonSerializer.Serialize(rec, JsonOpts);

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
                Xrecord xrec;
                if (nod.Contains(key))
                {
                    xrec = (Xrecord)tr.GetObject(nod.GetAt(key), OpenMode.ForWrite);
                }
                else
                {
                    xrec = new Xrecord();
                    nod.SetAt(key, xrec);
                    tr.AddNewlyCreatedDBObject(xrec, true);
                }
                xrec.Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, json));
                tr.Commit();
            }
        }

        // Read the record for a frame, or null if there is none / it can't be parsed.
        public static FrameRecord Load(Database db, string frameTag)
        {
            if (db == null) return null;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return null;
            string key = Key(frameTag);

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                if (!nod.Contains(key)) { tr.Commit(); return null; }
                var xrec = (Xrecord)tr.GetObject(nod.GetAt(key), OpenMode.ForRead, false);
                TypedValue[] vals = xrec.Data?.AsArray();
                tr.Commit();
                string json = vals != null && vals.Length > 0 ? vals[0].Value?.ToString() : null;
                if (string.IsNullOrEmpty(json)) return null;
                try { return JsonSerializer.Deserialize<FrameRecord>(json, JsonOpts); }
                catch { return null; }
            }
        }

        // The freeze gate for a frame (false when there is no record).
        public static bool IsFrozen(Database db, string frameTag)
        {
            FrameRecord rec = Load(db, frameTag);
            return rec != null && rec.Frozen;
        }

        // Flip the gate, preserving the stored recipe. Creates a minimal record if none exists
        // (e.g. freezing a frame whose timbers predate this registry).
        public static void SetFrozen(Database db, string frameTag, bool frozen)
        {
            FrameRecord rec = Load(db, frameTag) ?? new FrameRecord();
            rec.Frozen = frozen;
            Save(db, frameTag, rec);
        }
    }
}
