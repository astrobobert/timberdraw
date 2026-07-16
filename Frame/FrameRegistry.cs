using System.Text.Json;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace TimberDraw
{
    // Per-frame record: the generator's recipe (seed params) + the freeze gate, stored on the FRAME
    // (not on any one timber). There is no frame-group entity, so the record lives in the drawing's
    // NamedObjectsDictionary under "TM_FRAME_<tag>" -- which persists across save/reload, as a frozen
    // gate must (JSON in a single-Text Xrecord).
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
        public string SpecJson;        // the tree's FrameSpec at the last Draw (recall-on-open,
                                       // batch-3 #3) -- null on records from before the stamp
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
                catch (System.Exception ex)
                {
                    // A corrupt record reads as "no record" -- which silently DISENGAGES the freeze
                    // gate for the frame. Surface it; the fallback behavior is unchanged.
                    Diag.Warn("FrameRegistry.Load", "frame " + frameTag
                        + " record unreadable (treated as unfrozen): " + ex.Message);
                    return null;
                }
            }
        }

        // The first stored recipe among the drawing's frame records, with its tag -- the
        // recall-on-open source (batch-3 #3: opening a drawing refills the tree with ITS frame).
        // Tags scan in NOD order; null when no record carries a spec (pre-stamp drawings).
        public static string FirstSpecJson(Database db)
        {
            if (db == null) return null;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return null;

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry e in nod)
                {
                    if (!e.Key.StartsWith(KeyPrefix)) continue;
                    var xrec = tr.GetObject(e.Value, OpenMode.ForRead, false) as Xrecord;
                    TypedValue[] vals = xrec?.Data?.AsArray();
                    string json = vals != null && vals.Length > 0 ? vals[0].Value?.ToString() : null;
                    if (string.IsNullOrEmpty(json)) continue;
                    try
                    {
                        FrameRecord rec = JsonSerializer.Deserialize<FrameRecord>(json, JsonOpts);
                        if (!string.IsNullOrEmpty(rec?.SpecJson)) { tr.Commit(); return rec.SpecJson; }
                    }
                    catch (System.Exception ex)
                    { Diag.Warn("FrameRegistry.FirstSpecJson", e.Key + " record unreadable, skipped: " + ex.Message); }
                }
                tr.Commit();
                return null;
            }
        }

        // Every frame tag recorded in the drawing (the TM_FRAME_* keys, NOD order). ONE FRAME PER
        // DRAWING is the convention (Robert's call, 2026-07-16) -- this exists so the Draw path can
        // WARN when a renamed spec would add a second frame beside an existing one, and so a legacy
        // multi-frame drawing can still be navigated.
        public static System.Collections.Generic.List<string> Tags(Database db)
        {
            var tags = new System.Collections.Generic.List<string>();
            if (db == null) return tags;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return tags;

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry e in nod)
                    if (e.Key.StartsWith(KeyPrefix)) tags.Add(e.Key.Substring(KeyPrefix.Length));
                tr.Commit();
            }
            return tags;
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
