using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace TimberDraw
{
    // The timber XDATA layer -- all that survives of the legacy parametric pipeline (deep-purged
    // 2026-07-06, Phase C: the bent/bay member classes, cross-bent queues, draw helpers, cascade
    // machinery, and connection xrecords went with it; see git history). What remains is the
    // extension-dictionary schema every managed timber carries (DataStructure + GetXdata/SetXdata --
    // field names and TypedValue codes are the ON-DISK FORMAT, do not touch), plus the two small
    // helpers the managed path shares (BuyLongFeet, EraseEntity).
    public static class Module1
    {
        // Buy-long rounding: matches TimberTag.Timber.GetSize() convention.
        // Returns the stock length in feet needed to cut a timber of lengthInches.
        public static int BuyLongFeet(double lengthInches)
        {
            int ft = (int)(lengthInches / 12);
            double rem = lengthInches - ft * 12.0;
            return rem >= 3.0 ? ft + 2 : ft + 1;
        }

        public class DataStructure
        {
            public string Type;
            public string BentNumber;
            public string Designation;
            public string Size;
            public string TagHandle;
            public int TenonCnt;
            public int MortiseCnt;
            public int PegCnt;
            // Phase 1: parametric fields (stored in extension dictionary)
            public double Width;    // section width (extrusion depth)
            public double Depth;    // section depth (profile height)
            public double Length;   // nominal length in inches
            public string JointNear;       // JointType name at near/bottom/left end
            public string JointFar;        // JointType name at far/top/right end
            public bool IsStale;           // true when a connected timber has changed
            // JSON extra-params for each joint end, set by TimberTag Timber Tab.
            // Empty string = schema defaults apply; preserved through regen.
            public string JointNearParams; // e.g. {"tenonWidth":1.5}
            public string JointFarParams;  // e.g. {"taperAngle":0.125,"tenonLength":4.0}
            // Snapshot of the params that were physically drawn last time (written only by
            // Module1.SetXdata -- TimberTag's Timber.SetXdata never writes these).
            public string JointNearParamsDrawn;
            public string JointFarParamsDrawn;
            // Grouping layer (managed timbers): which frame / bay this timber belongs to. The bent
            // is the existing BentNumber field. FrameTag enables MULTIPLE managed frames in one
            // drawing (per-frame redraw clears only its own tag); BayTag is the Roman bay numeral.
            // WallTag is the wall letter (stamped at emit for longitudinal/bay members, or assigned
            // to a free timber by TAssign). Empty on legacy / non-grouped timbers.
            public string FrameTag;
            public string BayTag;
            public string WallTag;
            // Floor LEVEL a floor-system member (joist/summer) belongs to, digits bottom-up ("1", "2");
            // blank on everything else. Owner hierarchy: Frame -> Bent | Wall -> Bay | Floor.
            public string FloorTag;
            // The installer label derived from the structural grid: a VERTICAL member's column ("1A"),
            // a SPANNING member's two columns ("1BC"). Stamped at emit; blank on free/legacy timbers.
            public string GridLabel;
            // "1" on FREE-ASSEMBLY timbers (editor-created: TPlace/TSpan/TJoin/TJoist), stamped at
            // creation and preserved across rebuilds. The generator's regenerate erases only its own
            // skeleton (Free != "1"), so hand-placed timbers survive a re-Generate -- assigned or not.
            // Blank on emitted skeleton members and on legacy timbers (additive field, absent reads "").
            public string Free;
            public DataStructure() { JointNear = ""; JointFar = ""; JointNearParams = ""; JointFarParams = ""; JointNearParamsDrawn = ""; JointFarParamsDrawn = ""; FrameTag = ""; BayTag = ""; WallTag = ""; FloorTag = ""; GridLabel = ""; Free = ""; }
            public DataStructure(string type, string bentNumber, string designation, string size,
                string tagHandle, int tenonCnt, int mortiseCnt, int pegCnt,
                double width = 0, double depth = 0, double length = 0,
                string jointNear = "", string jointFar = "", bool isStale = false)
            {
                Type = type;
                BentNumber = bentNumber;
                Designation = designation;
                Size = size;
                TagHandle = tagHandle;
                TenonCnt = tenonCnt;
                MortiseCnt = mortiseCnt;
                PegCnt = pegCnt;
                Width = width;
                Depth = depth;
                Length = length;
                JointNear = jointNear ?? "";
                JointFar = jointFar ?? "";
                IsStale = isStale;
            }
        }

        public static DataStructure GetXdata(ObjectId objId)
        {
            DataStructure data = new();
            Database db = HostApplicationServices.WorkingDatabase;
            using (Application.DocumentManager.MdiActiveDocument.LockDocument()) {
                using Transaction tr = db.TransactionManager.StartTransaction();
                Solid3d locSolid = (Solid3d)tr.GetObject(objId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, false, true);
                if (!locSolid.ExtensionDictionary.IsNull)
                {
                    DBDictionary extDict = (DBDictionary)tr.GetObject(locSolid.ExtensionDictionary, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, false);
                    data.Type        = ReadTextField(extDict, tr, "Type");
                    data.BentNumber  = ReadTextField(extDict, tr, "BentNumber");
                    data.Designation = ReadTextField(extDict, tr, "Designation");
                    data.Size        = ReadTextField(extDict, tr, "Size");
                    data.TagHandle   = ReadTextField(extDict, tr, "TagHandle");
                    data.TenonCnt    = ReadIntField(extDict, tr, "TenonCnt");
                    data.MortiseCnt  = ReadIntField(extDict, tr, "MortiseCnt");
                    data.PegCnt      = ReadIntField(extDict, tr, "PegCnt");
                    // Phase 1: parametric fields (return defaults if not yet written)
                    data.Width       = ReadDoubleField(extDict, tr, "Width");
                    data.Depth       = ReadDoubleField(extDict, tr, "Depth");
                    data.Length      = ReadDoubleField(extDict, tr, "Length");
                    data.JointNear       = ReadTextField(extDict, tr, "JointNear");
                    data.JointFar        = ReadTextField(extDict, tr, "JointFar");
                    data.IsStale         = ReadBoolField(extDict, tr, "IsStale");
                    data.JointNearParams      = ReadTextField(extDict, tr, "JointNearParams");
                    data.JointFarParams       = ReadTextField(extDict, tr, "JointFarParams");
                    data.JointNearParamsDrawn = ReadTextField(extDict, tr, "JointNearParamsDrawn");
                    data.JointFarParamsDrawn  = ReadTextField(extDict, tr, "JointFarParamsDrawn");
                    data.FrameTag    = ReadTextField(extDict, tr, "FrameTag");
                    data.BayTag      = ReadTextField(extDict, tr, "BayTag");
                    data.WallTag     = ReadTextField(extDict, tr, "WallTag");
                    data.FloorTag    = ReadTextField(extDict, tr, "FloorTag");
                    data.GridLabel   = ReadTextField(extDict, tr, "GridLabel");
                    data.Free        = ReadTextField(extDict, tr, "Free");
                }
                tr.Commit();
            }
            return data;
        }

        private static string ReadTextField(DBDictionary dict, Transaction tr, string key)
        {
            if (!dict.Contains(key)) return string.Empty;
            Xrecord xrec = (Xrecord)tr.GetObject(dict.GetAt(key), Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, false);
            TypedValue[] vals = xrec.Data.AsArray();
            return vals.Length > 0 ? vals[0].Value.ToString() : string.Empty;
        }

        private static int ReadIntField(DBDictionary dict, Transaction tr, string key)
        {
            if (!dict.Contains(key)) return 0;
            Xrecord xrec = (Xrecord)tr.GetObject(dict.GetAt(key), Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, false);
            TypedValue[] vals = xrec.Data.AsArray();
            return vals.Length > 0 ? Convert.ToInt32(vals[0].Value) : 0;
        }

        private static double ReadDoubleField(DBDictionary dict, Transaction tr, string key)
        {
            if (!dict.Contains(key)) return 0.0;
            Xrecord xrec = (Xrecord)tr.GetObject(dict.GetAt(key), Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, false);
            TypedValue[] vals = xrec.Data.AsArray();
            return vals.Length > 0 ? Convert.ToDouble(vals[0].Value) : 0.0;
        }

        private static bool ReadBoolField(DBDictionary dict, Transaction tr, string key)
        {
            if (!dict.Contains(key)) return false;
            Xrecord xrec = (Xrecord)tr.GetObject(dict.GetAt(key), Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, false);
            TypedValue[] vals = xrec.Data.AsArray();
            return vals.Length > 0 && Convert.ToInt16(vals[0].Value) != 0;
        }

        public static ObjectId SetXdata(ObjectId objId, DataStructure data)
        {
            Database db = HostApplicationServices.WorkingDatabase;
            using (Application.DocumentManager.MdiActiveDocument.LockDocument()) {
                using Transaction tr = db.TransactionManager.StartTransaction();
                Entity ent = (Entity)tr.GetObject(objId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
                if (ent.ExtensionDictionary.IsNull)
                {
                    ent.UpgradeOpen();
                    ent.CreateExtensionDictionary();
                }
                DBDictionary extDict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite, false);
                WriteTextField(extDict,   tr, "Type",        data.Type);
                WriteTextField(extDict,   tr, "BentNumber",  data.BentNumber);
                WriteTextField(extDict,   tr, "Designation", data.Designation);
                WriteTextField(extDict,   tr, "Size",        data.Size);
                WriteTextField(extDict,   tr, "TagHandle",   data.TagHandle);
                WriteIntField(extDict,    tr, "TenonCnt",    data.TenonCnt);
                WriteIntField(extDict,    tr, "MortiseCnt",  data.MortiseCnt);
                WriteIntField(extDict,    tr, "PegCnt",      data.PegCnt);
                // Phase 1: parametric fields
                WriteDoubleField(extDict, tr, "Width",       data.Width);
                WriteDoubleField(extDict, tr, "Depth",       data.Depth);
                WriteDoubleField(extDict, tr, "Length",      data.Length);
                WriteTextField(extDict,   tr, "JointNear",       data.JointNear);
                WriteTextField(extDict,   tr, "JointFar",        data.JointFar);
                WriteBoolField(extDict,   tr, "IsStale",         data.IsStale);
                WriteTextField(extDict,   tr, "JointNearParams",      data.JointNearParams ?? "");
                WriteTextField(extDict,   tr, "JointFarParams",       data.JointFarParams  ?? "");
                // Snapshot: written only here (TimberDraw), never by TimberTag.SetXdata().
                WriteTextField(extDict,   tr, "JointNearParamsDrawn", data.JointNearParams ?? "");
                WriteTextField(extDict,   tr, "JointFarParamsDrawn",  data.JointFarParams  ?? "");
                WriteTextField(extDict,   tr, "FrameTag",             data.FrameTag ?? "");
                WriteTextField(extDict,   tr, "BayTag",               data.BayTag ?? "");
                WriteTextField(extDict,   tr, "WallTag",              data.WallTag ?? "");
                WriteTextField(extDict,   tr, "FloorTag",             data.FloorTag ?? "");
                WriteTextField(extDict,   tr, "GridLabel",            data.GridLabel ?? "");
                WriteTextField(extDict,   tr, "Free",                 data.Free ?? "");
                tr.Commit();
            }
            return objId;
        }

        private static void WriteTextField(DBDictionary dict, Transaction tr, string key, string value)
        {
            Xrecord xrec;
            if (dict.Contains(key)) {
                xrec = (Xrecord)tr.GetObject(dict.GetAt(key), Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite);
            } else {
                xrec = new Xrecord();
                dict.SetAt(key, xrec);
                tr.AddNewlyCreatedDBObject(xrec, true);
            }
            xrec.Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, value));
        }

        private static void WriteIntField(DBDictionary dict, Transaction tr, string key, int value)
        {
            Xrecord xrec;
            if (dict.Contains(key)) {
                xrec = (Xrecord)tr.GetObject(dict.GetAt(key), Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite);
            } else {
                xrec = new Xrecord();
                dict.SetAt(key, xrec);
                tr.AddNewlyCreatedDBObject(xrec, true);
            }
            xrec.Data = new ResultBuffer(new TypedValue((int)DxfCode.Int16, (short)value));
        }

        private static void WriteDoubleField(DBDictionary dict, Transaction tr, string key, double value)
        {
            Xrecord xrec;
            if (dict.Contains(key)) {
                xrec = (Xrecord)tr.GetObject(dict.GetAt(key), Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite);
            } else {
                xrec = new Xrecord();
                dict.SetAt(key, xrec);
                tr.AddNewlyCreatedDBObject(xrec, true);
            }
            xrec.Data = new ResultBuffer(new TypedValue((int)DxfCode.Real, value));
        }

        private static void WriteBoolField(DBDictionary dict, Transaction tr, string key, bool value)
        {
            Xrecord xrec;
            if (dict.Contains(key)) {
                xrec = (Xrecord)tr.GetObject(dict.GetAt(key), Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite);
            } else {
                xrec = new Xrecord();
                dict.SetAt(key, xrec);
                tr.AddNewlyCreatedDBObject(xrec, true);
            }
            xrec.Data = new ResultBuffer(new TypedValue((int)DxfCode.Int16, (short)(value ? 1 : 0)));
        }

        // Erases a single entity by ObjectId (used by the managed re-section / rebuild paths).
        public static void EraseEntity(ObjectId id)
        {
            if (id.IsNull || id.IsErased) return;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            using (doc.LockDocument()) {
                using Transaction tr = doc.Database.TransactionManager.StartTransaction();
                try {
                    Entity ent = (Entity)tr.GetObject(id, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite, true, true);
                    if (!ent.IsErased) ent.Erase(true);
                } catch { }
                tr.Commit();
            }
        }
    }
}
