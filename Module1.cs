using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Geometry;
namespace TimberDraw
{

	public static class Module1
	{
		public static double EaveHt;
		public static double Span;
		public static double Prise;
		public static double Prun;
		public static double BayWidth;
		//public static double FlrGirtHt;
		public const  int Back = 0;
		public const  int Centered = 1;
		public const  int Front = 2;
		public static int OffsetType;

		public static bool GenerateBayMembers;
		//King Post Truss

		public static bool KingPostTruss = false;
		//Queen Post Truss

		public static bool QueenPostTruss = false;
		//Roof Loads
		public static double RoofLoad;
		public static double Emod;

		public static double AllowableDeflection;
		//Callout

		public static int BentWallNumber;
		//General
		public static Point3d StartPoint;
		public static double BOG;
		public static double TOG;
		public static double TOH;
		public static double Pitch;
		public static double Beta;
		public static double PlumbLength;
		public static double R;
		public static double B;
		public static double C;
		public static bool Make3D = false;
        // ShowEndMarkers: when true, "N"/"F" DBText labels are drawn at the near/far
        // face of every timber on the TF_EndMarkers layer. Freeze that layer to hide them.
        public static bool ShowEndMarkers = false;
		public static int TrussType;
		// HasJoinery: always true in parametric model -- joinery is always generated.
		// Retained for backward compat during transition; will be removed in Phase 2.
		public static bool HasJoinery = true;
		public static bool DeletePolylines;

        public static List<(ObjectId MortiseId, ObjectId SourceTimberId)> PendingLeftPostMortises = new();
        public static List<(ObjectId MortiseId, ObjectId SourceTimberId)> PendingRightPostMortises = new();
        public static List<(ObjectId MortiseId, ObjectId SourceTimberId)> PendingLeftRafterMortises = new();
        public static List<(ObjectId MortiseId, ObjectId SourceTimberId)> PendingRightRafterMortises = new();
        public static List<(ObjectId MortiseId, ObjectId SourceTimberId)> PendingKPostMortises = new();

        // The five cross-bent mortise queues are GENERATION-TIME SCAFFOLDING, not a persistent
        // connectivity engine (see CLAUDE.md Architecture Direction). Each generator run starts them
        // clean so stale handles from a prior run can never leak in. The managed/emitter path does not
        // populate them, so this is a safety/clarity reset there; the legacy TDraw path relied on them
        // surviving across calls within one parametric run only.
        public static void ResetCrossBentQueues()
        {
            PendingLeftPostMortises.Clear();
            PendingRightPostMortises.Clear();
            PendingLeftRafterMortises.Clear();
            PendingRightRafterMortises.Clear();
            PendingKPostMortises.Clear();
        }

		private static void EnsureLayer(Database db, Transaction tr, string name)
		{
			LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
			if (!lt.Has(name)) {
				lt.UpgradeOpen();
                LayerTableRecord ltr = new() { Name = name };
                lt.Add(ltr);
				tr.AddNewlyCreatedDBObject(ltr, true);
			}
		}

		private static void ApplyTransforms(Entity entity, Editor ed, double yAngle, double zAngle, double xAngle, Point3d rpt)
		{
			entity.TransformBy(Matrix3d.Rotation(rad(yAngle), new Vector3d(0, 1, 0), rpt));
			entity.TransformBy(Matrix3d.Rotation(rad(zAngle), new Vector3d(0, 0, 1), rpt));
			entity.TransformBy(Matrix3d.Rotation(rad(xAngle), new Vector3d(1, 0, 0), rpt));
			entity.TransformBy(Matrix3d.Displacement(StartPoint.GetAsVector()));
			entity.TransformBy(ed.CurrentUserCoordinateSystem);
		}

		private static ObjectId ExtrudeAndCommit(Entity profile, BlockTableRecord btr, Transaction tr, double exDepth, string type, string bentNumber, string designation)
		{
			ObjectId objId = btr.AppendEntity(profile);
			tr.AddNewlyCreatedDBObject(profile, true);
			if (!Make3D) return objId;

			Solid3d solid = new();
			try {
				DBObjectCollection rescol = Region.CreateFromCurves(new DBObjectCollection { profile });
				solid.Extrude((Region)rescol[0], exDepth, 0);
				objId = btr.AppendEntity(solid);
				tr.AddNewlyCreatedDBObject(solid, true);
				solid = null;
				if (DeletePolylines) profile.Erase();
			} catch (System.Exception ex) {
				solid?.Dispose();
				System.Windows.Forms.MessageBox.Show(
					$"Error in {type} {bentNumber}{designation}: {ex.Message}",
					"TimberDraw", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
			}
			return objId;
		}

		public static ObjectId DrawElement(Point3dCollection pts, double exDepth, string type, string bentNumber, string designation, string size = "", double yAngle = 0, double zAngle = 0, double x = 0, double y = 0, double z = 0, double xAngle = 0, string jointNear = "", string jointFar = "")
		{
			Point3d rpt = new(x, y, z);
			Document doc = Application.DocumentManager.MdiActiveDocument;
			Database db = doc.Database;
			ObjectId objId;
			using (doc.LockDocument()) {
                using Transaction tr = db.TransactionManager.StartTransaction();
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite);
                Polyline3d pline = new(Poly3dType.SimplePoly, pts, true);
                ApplyTransforms(pline, doc.Editor, yAngle, zAngle, xAngle, rpt);
                objId = ExtrudeAndCommit(pline, btr, tr, exDepth, type, bentNumber, designation);
                tr.Commit();
            }
            // Derive Width/Depth/Length from size string; use exDepth for width
            ParseSizeString(size, out double dimW, out double dimD, out double dimL);
            double w = (dimW > 0) ? dimW : exDepth;
			return SetXdata(objId, new DataStructure(type, bentNumber, designation, size, "0", 0, 0, 0, w, dimD, dimL, jointNear, jointFar, false));
		}

		public static ObjectId DrawPeg(Point3d pt, double radius, double exDepth, string type, string bentNumber, string designation, string size = "", double yAngle = 0, double zAngle = 0, double x = 0, double y = 0, double z = 0, double xAngle = 0)
		{
			Point3d rpt = new(x, y, z);
			Document doc = Application.DocumentManager.MdiActiveDocument;
			Database db = doc.Database;
			ObjectId objId;
			using (doc.LockDocument()) {
                using Transaction tr = db.TransactionManager.StartTransaction();
                EnsureLayer(db, tr, "pegs");
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite);
                Circle circle = new(pt, new Vector3d(0.0, 0.0, 1.0), radius);
                ApplyTransforms(circle, doc.Editor, yAngle, zAngle, xAngle, rpt);
                objId = ExtrudeAndCommit(circle, btr, tr, exDepth, type, bentNumber, designation);
                ((Entity)tr.GetObject(objId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite)).Layer = "pegs";
                tr.Commit();
            }
            // Pegs have no joinery endpoints -- Width/Depth/Length/JointNear/JointFar all empty
			return SetXdata(objId, new DataStructure(type, bentNumber, designation, size, "0", 0, 0, 0));
		}

		public static ObjectId DrawBrace(double width, double depth, double length, Point3dCollection pts, DoubleCollection bulge, double elevation, double exDepth, string type, string bentNumber, string designation,
		string size, double yRotationAngle, double zRotationAngle = 0, double x = 0, double y = 0, double z = 0, double xRotationAngle = 0, string jointNear = "", string jointFar = "")
		{
			Point3d rpt = new(x, y, z);
			Document doc = Application.DocumentManager.MdiActiveDocument;
			Database db = doc.Database;
			ObjectId objId;
			using (doc.LockDocument()) {
                using Transaction tr = db.TransactionManager.StartTransaction();
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite);
                Polyline2d pline = new(Poly2dType.SimplePoly, pts, elevation, true, 0, 0, bulge);
                ApplyTransforms(pline, doc.Editor, yRotationAngle, zRotationAngle, xRotationAngle, rpt);
                objId = ExtrudeAndCommit(pline, btr, tr, exDepth, type, bentNumber, designation);
                tr.Commit();
            }
            // Braces: width/depth/length are passed explicitly by caller
			return SetXdata(objId, new DataStructure(type, bentNumber, designation, size, "0", 0, 0, 0, width, depth, length, jointNear, jointFar, false));
		}

		public static Point3d PolarPoint(Point3d basepoint, double angle, double distance)
		{
			return new Point3d(basepoint.X + (distance * Math.Cos(angle)), basepoint.Y + (distance * Math.Sin(angle)), basepoint.Z);
		}

		public static Point3d AtPoint(Point3d basePoint, double deltaX, double deltaY, double deltaZ)
		{
			return new Point3d(basePoint.X + deltaX, basePoint.Y + deltaY, basePoint.Z + deltaZ);
		}

		public static double rad(double degree)
		{
			return degree * (Math.PI / 180);
		}

		// Buy-long rounding: matches TimberTag.Timber.GetSize() convention.
		// Returns the stock length in feet needed to cut a timber of lengthInches.
		public static int BuyLongFeet(double lengthInches)
		{
			int ft = (int)(lengthInches / 12);
			double rem = lengthInches - ft * 12.0;
			return rem >= 3.0 ? ft + 2 : ft + 1;
		}

        //[DllImport("acad.exe", CallingConvention = CallingConvention.Cdecl, EntryPoint = "acedTrans")]
        //private static int acedTrans(double[] point, IntPtr fromRb, IntPtr toRb, int disp, double[] result)
        //{
        //}

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
            // Used by ApplyCascade to reconstruct the OLD tenon geometry for DeltaSwapJoint,
            // even when TimberTag has already overwritten JointNearParams with new user values.
            public string JointNearParamsDrawn;
            public string JointFarParamsDrawn;
            // Grouping layer (managed timbers): which frame / bay this timber belongs to. The bent
            // is the existing BentNumber field. FrameTag enables MULTIPLE managed frames in one
            // drawing (per-frame redraw clears only its own tag); BayTag is the Roman bay numeral.
            // WallTag is the EXPLICIT wall letter assigned to a free timber by TAssign (emitted
            // longitudinal members leave it blank and derive their wall from role+side). Empty on
            // legacy / non-grouped timbers.
            public string FrameTag;
            public string BayTag;
            public string WallTag;
            // The installer label derived from the structural grid: a VERTICAL member's column ("1A"),
            // a SPANNING member's two columns ("1BC"). Stamped at emit; blank on free/legacy timbers.
            public string GridLabel;
            public DataStructure() { JointNear = ""; JointFar = ""; JointNearParams = ""; JointFarParams = ""; JointNearParamsDrawn = ""; JointFarParamsDrawn = ""; FrameTag = ""; BayTag = ""; WallTag = ""; GridLabel = ""; }
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

        // Parses "WxDxL" size string (L in buy-long feet) into Width/Depth/Length(inches).
        private static void ParseSizeString(string size, out double w, out double d, out double l)
        {
            w = 0; d = 0; l = 0;
            if (string.IsNullOrEmpty(size)) return;
            string[] parts = size.Split('x');
            if (parts.Length != 3) return;
            double.TryParse(parts[0], out w);
            double.TryParse(parts[1], out d);
            double lft;
            double.TryParse(parts[2], out lft);
            l = lft * 12.0;
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
                    data.GridLabel   = ReadTextField(extDict, tr, "GridLabel");
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
                // Captures the params that were physically drawn -- used as "old params" by
                // ApplyCascade when the user edits params in TimberTag then triggers Regen.
                WriteTextField(extDict,   tr, "JointNearParamsDrawn", data.JointNearParams ?? "");
                WriteTextField(extDict,   tr, "JointFarParamsDrawn",  data.JointFarParams  ?? "");
                WriteTextField(extDict,   tr, "FrameTag",             data.FrameTag ?? "");
                WriteTextField(extDict,   tr, "BayTag",               data.BayTag ?? "");
                WriteTextField(extDict,   tr, "WallTag",              data.WallTag ?? "");
                WriteTextField(extDict,   tr, "GridLabel",            data.GridLabel ?? "");
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

        // -----------------------------------------------------------------------
        // Phase 2: parametric regeneration helpers
        // -----------------------------------------------------------------------

        // Appends the handle of entityId to a comma-separated handle list in
        // the timber's xdata (used for TenonHandles and PegHandles tracking).
        public static void AppendHandleToField(ObjectId timberId, string key, ObjectId entityId)
        {
            Database db = HostApplicationServices.WorkingDatabase;
            using (Application.DocumentManager.MdiActiveDocument.LockDocument()) {
                using Transaction tr = db.TransactionManager.StartTransaction();
                Entity ent = (Entity)tr.GetObject(timberId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
                if (ent.ExtensionDictionary.IsNull) { ent.UpgradeOpen(); ent.CreateExtensionDictionary(); }
                DBDictionary extDict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite, false);
                string newHandle = entityId.Handle.ToString();
                if (extDict.Contains(key)) {
                    Xrecord xrec = (Xrecord)tr.GetObject(extDict.GetAt(key), Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite);
                    TypedValue[] vals = xrec.Data.AsArray();
                    string existing = vals.Length > 0 ? vals[0].Value.ToString() : "";
                    xrec.Data = new ResultBuffer(new TypedValue((int)DxfCode.Text,
                        string.IsNullOrEmpty(existing) ? newHandle : existing + "," + newHandle));
                } else {
                    Xrecord xrec = new Xrecord();
                    xrec.Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, newHandle));
                    extDict.SetAt(key, xrec);
                    tr.AddNewlyCreatedDBObject(xrec, true);
                }
                tr.Commit();
            }
        }

        // Returns all handles stored in a comma-separated handle list field.
        public static Handle[] GetHandlesFromField(ObjectId timberId, string key)
        {
            Database db = HostApplicationServices.WorkingDatabase;
            using (Application.DocumentManager.MdiActiveDocument.LockDocument()) {
                using Transaction tr = db.TransactionManager.StartTransaction();
                Entity ent = (Entity)tr.GetObject(timberId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, false, true);
                if (ent.ExtensionDictionary.IsNull) { tr.Commit(); return new Handle[0]; }
                DBDictionary extDict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, false);
                if (!extDict.Contains(key)) { tr.Commit(); return new Handle[0]; }
                Xrecord xrec = (Xrecord)tr.GetObject(extDict.GetAt(key), Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, false);
                TypedValue[] vals = xrec.Data.AsArray();
                tr.Commit();
                if (vals.Length == 0 || string.IsNullOrEmpty(vals[0].Value?.ToString()))
                    return new Handle[0];
                string[] parts = vals[0].Value.ToString().Split(',');
                Handle[] handles = new Handle[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                    handles[i] = StringToHandle(parts[i].Trim());
                return handles;
            }
        }

        // Writes the peg entity handle list to "PegHandles" xdata on timberId.
        public static void PersistPegHandles(ObjectId timberId, List<ObjectId> pegIds)
        {
            if (pegIds == null || pegIds.Count == 0) return;
            Database db = HostApplicationServices.WorkingDatabase;
            using (Application.DocumentManager.MdiActiveDocument.LockDocument()) {
                using Transaction tr = db.TransactionManager.StartTransaction();
                Entity ent = (Entity)tr.GetObject(timberId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
                if (ent.ExtensionDictionary.IsNull) { ent.UpgradeOpen(); ent.CreateExtensionDictionary(); }
                DBDictionary extDict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite, false);
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < pegIds.Count; i++) {
                    if (i > 0) sb.Append(',');
                    sb.Append(pegIds[i].Handle.ToString());
                }
                Xrecord xrec;
                if (extDict.Contains("PegHandles")) {
                    xrec = (Xrecord)tr.GetObject(extDict.GetAt("PegHandles"), Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite);
                } else {
                    xrec = new Xrecord();
                    extDict.SetAt("PegHandles", xrec);
                    tr.AddNewlyCreatedDBObject(xrec, true);
                }
                xrec.Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, sb.ToString()));
                tr.Commit();
            }
        }

        // Draws a single DBText label (e.g. "N" or "F") at pt on the TF_EndMarkers layer.
        // Called by member Draw() methods when Module1.ShowEndMarkers is true.
        // Returns the new entity's ObjectId (Null when ShowEndMarkers is false).
        public static ObjectId DrawEndMarker(Point3d pt, string label, double height = 1.5)
        {
            Database db = HostApplicationServices.WorkingDatabase;
            using (Application.DocumentManager.MdiActiveDocument.LockDocument()) {
                using Transaction tr = db.TransactionManager.StartTransaction();
                EnsureLayer(db, tr, "TF_EndMarkers");
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite);
                DBText txt = new DBText {
                    Position   = pt,
                    TextString = label,
                    Height     = height,
                    Layer      = "TF_EndMarkers"
                };
                ObjectId id = btr.AppendEntity(txt);
                tr.AddNewlyCreatedDBObject(txt, true);
                tr.Commit();
                return id;
            }
        }

        // Writes the end-marker entity handle list to "EndMarkerHandles" on timberId.
        // Mirrors PersistPegHandles; erased during EraseAndMarkStale in TimberFactory.
        public static void PersistEndMarkerHandles(ObjectId timberId, List<ObjectId> ids)
        {
            if (ids == null || ids.Count == 0) return;
            Database db = HostApplicationServices.WorkingDatabase;
            using (Application.DocumentManager.MdiActiveDocument.LockDocument()) {
                using Transaction tr = db.TransactionManager.StartTransaction();
                Entity ent = (Entity)tr.GetObject(timberId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
                if (ent.ExtensionDictionary.IsNull) { ent.UpgradeOpen(); ent.CreateExtensionDictionary(); }
                DBDictionary extDict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite, false);
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < ids.Count; i++) {
                    if (i > 0) sb.Append(',');
                    sb.Append(ids[i].Handle.ToString());
                }
                Xrecord xrec;
                if (extDict.Contains("EndMarkerHandles")) {
                    xrec = (Xrecord)tr.GetObject(extDict.GetAt("EndMarkerHandles"), Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite);
                } else {
                    xrec = new Xrecord();
                    extDict.SetAt("EndMarkerHandles", xrec);
                    tr.AddNewlyCreatedDBObject(xrec, true);
                }
                xrec.Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, sb.ToString()));
                tr.Commit();
            }
        }

        // Saves a JSON snapshot of the Module1 global state + member-specific parameters
        // needed to regenerate this timber. Called at the end of each member Draw().
        // Assumption: WCS was active at draw time (UCS = WCS). Non-WCS drawings will need
        // an additional matrix field added here in a future revision.
        public static void SaveDrawContext(ObjectId timberId, string contextJson)
        {
            Database db = HostApplicationServices.WorkingDatabase;
            using (Application.DocumentManager.MdiActiveDocument.LockDocument()) {
                using Transaction tr = db.TransactionManager.StartTransaction();
                Entity ent = (Entity)tr.GetObject(timberId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
                if (ent.ExtensionDictionary.IsNull) { ent.UpgradeOpen(); ent.CreateExtensionDictionary(); }
                DBDictionary extDict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite, false);
                Xrecord xrec;
                if (extDict.Contains("DrawContext")) {
                    xrec = (Xrecord)tr.GetObject(extDict.GetAt("DrawContext"), Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite);
                } else {
                    xrec = new Xrecord();
                    extDict.SetAt("DrawContext", xrec);
                    tr.AddNewlyCreatedDBObject(xrec, true);
                }
                xrec.Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, contextJson));
                tr.Commit();
            }
        }

        // Reads the DrawContext JSON string from xdata. Returns "{}" if not present.
        public static string LoadDrawContext(ObjectId timberId)
        {
            Database db = HostApplicationServices.WorkingDatabase;
            using (Application.DocumentManager.MdiActiveDocument.LockDocument()) {
                using Transaction tr = db.TransactionManager.StartTransaction();
                Entity ent = (Entity)tr.GetObject(timberId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, false, true);
                if (ent.ExtensionDictionary.IsNull) { tr.Commit(); return "{}"; }
                DBDictionary extDict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, false);
                if (!extDict.Contains("DrawContext")) { tr.Commit(); return "{}"; }
                Xrecord xrec = (Xrecord)tr.GetObject(extDict.GetAt("DrawContext"), Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, false);
                TypedValue[] vals = xrec.Data.AsArray();
                tr.Commit();
                return vals.Length > 0 ? vals[0].Value?.ToString() ?? "{}" : "{}";
            }
        }

        // Sets IsStale = true on timberId's xdata. Called on neighbours when a connected
        // timber changes size or position so TimberTag can show the stale indicator.
        public static void MarkStale(ObjectId timberId)
        {
            DataStructure data = GetXdata(timberId);
            data.IsStale = true;
            SetXdata(timberId, data);
        }

        // Returns the ObjectId for a drawing Handle. Returns ObjectId.Null if not found.
        public static ObjectId GetObjectIdFromHandle(Handle h)
        {
            Database db = HostApplicationServices.WorkingDatabase;
            db.TryGetObjectId(h, out ObjectId id);
            return id;
        }

        // Erases entities identified by their persistent Handles. Silently skips
        // handles that no longer resolve (already erased or belong to another drawing).
        public static void EraseEntities(Handle[] handles)
        {
            if (handles == null || handles.Length == 0) return;
            Database db = HostApplicationServices.WorkingDatabase;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            using (doc.LockDocument()) {
                using Transaction tr = db.TransactionManager.StartTransaction();
                foreach (Handle h in handles) {
                    if (!db.TryGetObjectId(h, out ObjectId id)) continue;
                    if (id.IsNull || id.IsErased) continue;
                    try {
                        Entity ent = (Entity)tr.GetObject(id, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite, true, true);
                        if (!ent.IsErased) ent.Erase(true);
                    } catch { }
                }
                tr.Commit();
            }
        }

        // Erases a single entity by ObjectId. Used by Regenerate() to remove the
        // old timber solid after tenons and pegs have been erased.
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

		public static string Arabic2roman(int number)
		{
			if (number <= 0) return number.ToString();
			int[] values  = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
			string[] syms = {  "M","CM", "D","CD", "C","XC","L","XL","X","IX","V","IV","I" };
			var result = new System.Text.StringBuilder();
			for (int i = 0; i < values.Length; i++)
				while (number >= values[i]) { result.Append(syms[i]); number -= values[i]; }
			return result.ToString();
		}

		public static double ImperialtoDecimal(string str)
		{
			return Converter.StringToDistance(str);
		}

		public static double MaxDeflection(double W, double E, double i, double l)
		{
			return (5.0 / 384.0) * ((W * (Math.Pow(l, 3))) / (E * i));
		}

		public static void AddJoint(ObjectId timberId, ObjectId jointId, Joint jointType)
		{
            Extents3d mortiseExt = default;
			if (Make3D) {
				Solid3d timber3d = null;
				DataStructure timberData = GetXdata(timberId);
				Document doc = Application.DocumentManager.MdiActiveDocument;
				Database db = doc.Database;
				using (doc.LockDocument()) {
                    using Transaction tr = db.TransactionManager.StartTransaction();
                    timber3d = (Solid3d)tr.GetObject(timberId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite);
                    Solid3d joint3d = (Solid3d)tr.GetObject(jointId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite, true, true);
                    switch (jointType)
                    {
                        case Joint.Mortise:
                            // Capture bounding box before commit so we can persist to IncomingMortises
                            // after the transaction closes (avoids nested-transaction ordering issues).
                            mortiseExt = joint3d.GeometricExtents;
                            timber3d.BooleanOperation(BooleanOperationType.BoolSubtract, (Solid3d)joint3d.Clone());
                            timberData.MortiseCnt += 1;
                            break;
                        case Joint.Tenon:
                            timber3d.BooleanOperation(BooleanOperationType.BoolUnite, (Solid3d)joint3d.Clone());
                            timberData.TenonCnt += 1;
                            break;
                        case Joint.Fill:
                            // Restore a previously subtracted void (delta-swap: undo old mortise).
                            // No counts updated; IncomingJoints managed by the caller (DeltaSwapJoint).
                            timber3d.BooleanOperation(BooleanOperationType.BoolUnite, (Solid3d)joint3d.Clone());
                            break;
                    }
                    tr.Commit();
                }
				SetXdata(timber3d.ObjectId, timberData);
			}
            // Phase 2: persist tenon handle for Regenerate() in both 2D and 3D mode.
            // The standalone tenon solid/polyline at jointId must be erasable when regenerating.
            if (jointType == Joint.Tenon)
                AppendHandleToField(timberId, "TenonHandles", jointId);

            // Phase 3: persist incoming mortise geometry on the receiver.
            // When a JF-based PrepareIncomingJointRecord call precedes AddJoint, the exact
            // JointParams are already saved and bbox write is suppressed for that mortise.
            // Bbox is still written for DrawElement-based mortises (oblique struts, braces).
            if (jointType == Joint.Mortise && Make3D)
            {
                if (!_suppressNextMortiseBbox)
                    SaveIncomingMortise(timberId, mortiseExt);
                _suppressNextMortiseBbox = false;   // always reset
            }
		}

        // When set, the next AddJoint(Mortise) call skips the bbox write to IncomingMortises.
        // Set by PrepareIncomingJointRecord (JF connections) and SuppressNextMortiseBbox
        // (JF re-cuts in TimberFactory).  Always reset by AddJoint after reading.
        private static bool _suppressNextMortiseBbox = false;

        // Call this BEFORE AddMortise for every JF-generated tenon connection.
        // Saves exact JointParams to IncomingJoints on the receiver (keyed by giverHandle)
        // and suppresses the bbox write so IncomingMortises stays DrawElement-only.
        public static void PrepareIncomingJointRecord(ObjectId receiverId, Handle giverHandle, JointParams jp)
        {
            SaveIncomingJoint(receiverId, giverHandle, jp);
            _suppressNextMortiseBbox = true;
        }

        // Suppresses the next bbox write without saving params.
        // Used by TimberFactory.ApplyIncomingJointsAndMortises during JF re-cuts.
        public static void SuppressNextMortiseBbox() { _suppressNextMortiseBbox = true; }

        // Returns all (giverHandle, JointParams) entries from the IncomingJoints xrecord.
        // Empty when the xrecord is absent (old drawing -- bbox fallback handles those).
        public static (Handle GiverHandle, JointParams Params)[] LoadAllIncomingJoints(ObjectId receiverId)
        {
            Database db = HostApplicationServices.WorkingDatabase;
            using (Application.DocumentManager.MdiActiveDocument.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Entity ent = (Entity)tr.GetObject(receiverId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, false, true);
                if (ent == null || ent.IsErased || ent.ExtensionDictionary.IsNull)
                    { tr.Commit(); return System.Array.Empty<(Handle, JointParams)>(); }
                DBDictionary extDict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, false);
                const string key = "IncomingJoints";
                if (!extDict.Contains(key))
                    { tr.Commit(); return System.Array.Empty<(Handle, JointParams)>(); }
                Xrecord xrec = (Xrecord)tr.GetObject(extDict.GetAt(key), Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, false);
                TypedValue[] vals = xrec.Data.AsArray();
                tr.Commit();
                var result = new System.Collections.Generic.List<(Handle, JointParams)>();
                for (int i = 0; i + IncomingJointEntrySize <= vals.Length; i += IncomingJointEntrySize)
                {
                    Handle bh = StringToHandle((string)vals[i].Value);
                    result.Add((bh, ParseIncomingJointEntry(vals, i)));
                }
                return result.ToArray();
            }
        }

        // Appends one bounding-box mortise record to the receiver's "IncomingMortises" xrecord.
        // Called automatically from AddJoint(Mortise) -- covers every mortise cut in the codebase.
        // Storage: pairs of TypedValue (code 10 = minPt, code 11 = maxPt).
        public static void SaveIncomingMortise(ObjectId timberId, Extents3d ext)
        {
            Database db = HostApplicationServices.WorkingDatabase;
            using (Application.DocumentManager.MdiActiveDocument.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Entity ent = (Entity)tr.GetObject(timberId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, false, true);
                if (ent == null || ent.IsErased) { tr.Commit(); return; }
                if (ent.ExtensionDictionary.IsNull) { ent.UpgradeOpen(); ent.CreateExtensionDictionary(); }
                DBDictionary extDict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite, false);
                const string key = "IncomingMortises";
                Xrecord xrec;
                List<TypedValue> vals = new List<TypedValue>();
                if (extDict.Contains(key))
                {
                    xrec = (Xrecord)tr.GetObject(extDict.GetAt(key), Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite);
                    vals.AddRange(xrec.Data.AsArray());
                }
                else
                {
                    xrec = new Xrecord();
                    extDict.SetAt(key, xrec);
                    tr.AddNewlyCreatedDBObject(xrec, true);
                }
                vals.Add(new TypedValue(10, ext.MinPoint));   // code 10 = min corner
                vals.Add(new TypedValue(11, ext.MaxPoint));   // code 11 = max corner
                xrec.Data = new ResultBuffer(vals.ToArray());
                tr.Commit();
            }
        }

        // Reads all bounding-box mortise records from the entity's "IncomingMortises" xrecord.
        // Returns empty array if the xrecord is absent (old drawing -- cascade handles those).
        public static Extents3d[] LoadIncomingMortises(ObjectId timberId)
        {
            Database db = HostApplicationServices.WorkingDatabase;
            using (Application.DocumentManager.MdiActiveDocument.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Entity ent = (Entity)tr.GetObject(timberId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, false, true);
                if (ent == null || ent.IsErased || ent.ExtensionDictionary.IsNull) { tr.Commit(); return new Extents3d[0]; }
                DBDictionary extDict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, false);
                const string key = "IncomingMortises";
                if (!extDict.Contains(key)) { tr.Commit(); return new Extents3d[0]; }
                Xrecord xrec = (Xrecord)tr.GetObject(extDict.GetAt(key), Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, false);
                TypedValue[] vals = xrec.Data.AsArray();
                tr.Commit();
                // Values are stored in (code-10, code-11) pairs: minPt, maxPt
                var result = new List<Extents3d>();
                for (int i = 0; i + 1 < vals.Length; i += 2)
                {
                    if (vals[i].TypeCode == 10 && vals[i + 1].TypeCode == 11)
                        result.Add(new Extents3d((Point3d)vals[i].Value, (Point3d)vals[i + 1].Value));
                }
                return result.ToArray();
            }
        }

        // -----------------------------------------------------------------------
        // IncomingJoints xrecord -- stores full JointParams per incoming connection,
        // keyed by the giver timber's handle.  Used by DeltaSwapJoint in TimberFactory
        // to fill the old void (JointFactory.Create(oldParams) -> BoolAdd) and cut
        // the new void (actual new tenon solid -> BoolSubtract) without touching any
        // other mortises on the receiver.
        //
        // Storage: fixed 16-TypedValue blocks, one block per incoming joint.
        //   [0]  (5,  Handle)  giver handle
        //   [1]  (70, short)   JointType enum value
        //   [2]  (10, Point3d) Origin
        //   [3..8]  (40, double) x6  FaceNormal.X/Y/Z  LateralDir.X/Y/Z
        //   [9..15] (40, double) x7  Width Depth TenonWidth TopRelish ShoulderDepth HousingDepth Pitch
        // -----------------------------------------------------------------------
        private const int IncomingJointEntrySize = 16;

        // Appends (or replaces) a JointParams record for giverHandle on the receiver.
        public static void SaveIncomingJoint(ObjectId receiverId, Handle giverHandle, JointParams jp)
        {
            Database db = HostApplicationServices.WorkingDatabase;
            using (Application.DocumentManager.MdiActiveDocument.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Entity ent = (Entity)tr.GetObject(receiverId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, false, true);
                if (ent == null || ent.IsErased) { tr.Commit(); return; }
                if (ent.ExtensionDictionary.IsNull) { ent.UpgradeOpen(); ent.CreateExtensionDictionary(); }
                DBDictionary extDict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite, false);
                const string key = "IncomingJoints";
                Xrecord xrec;
                List<TypedValue> vals = new List<TypedValue>();
                if (extDict.Contains(key))
                {
                    xrec = (Xrecord)tr.GetObject(extDict.GetAt(key), Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite);
                    TypedValue[] existing = xrec.Data.AsArray();
                    // Copy all entries except any existing entry for giverHandle.
                    for (int i = 0; i + IncomingJointEntrySize <= existing.Length; i += IncomingJointEntrySize)
                    {
                        Handle bh = StringToHandle((string)existing[i].Value);
                        if (bh != giverHandle)
                            for (int k = 0; k < IncomingJointEntrySize; k++)
                                vals.Add(existing[i + k]);
                    }
                }
                else
                {
                    xrec = new Xrecord();
                    extDict.SetAt(key, xrec);
                    tr.AddNewlyCreatedDBObject(xrec, true);
                }
                // Append new entry for giverHandle.
                vals.Add(new TypedValue((int)DxfCode.Handle, giverHandle));
                vals.Add(new TypedValue(70, (short)jp.JointType));
                vals.Add(new TypedValue(10, jp.Origin));
                vals.Add(new TypedValue(40, jp.FaceNormal.X));
                vals.Add(new TypedValue(40, jp.FaceNormal.Y));
                vals.Add(new TypedValue(40, jp.FaceNormal.Z));
                vals.Add(new TypedValue(40, jp.LateralDir.X));
                vals.Add(new TypedValue(40, jp.LateralDir.Y));
                vals.Add(new TypedValue(40, jp.LateralDir.Z));
                vals.Add(new TypedValue(40, jp.Width));
                vals.Add(new TypedValue(40, jp.Depth));
                vals.Add(new TypedValue(40, jp.TenonWidth));
                vals.Add(new TypedValue(40, jp.TopRelish));
                vals.Add(new TypedValue(40, jp.ShoulderDepth));
                vals.Add(new TypedValue(40, jp.HousingDepth));
                vals.Add(new TypedValue(40, jp.Pitch));
                xrec.Data = new ResultBuffer(vals.ToArray());
                tr.Commit();
            }
        }

        // Returns the stored JointParams for giverHandle on receiverId.
        // Returns default(JointParams) (Origin = 0,0,0) if no entry exists.
        public static JointParams LoadIncomingJoint(ObjectId receiverId, Handle giverHandle)
        {
            Database db = HostApplicationServices.WorkingDatabase;
            using (Application.DocumentManager.MdiActiveDocument.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Entity ent = (Entity)tr.GetObject(receiverId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, false, true);
                if (ent == null || ent.IsErased || ent.ExtensionDictionary.IsNull) { tr.Commit(); return default; }
                DBDictionary extDict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, false);
                const string key = "IncomingJoints";
                if (!extDict.Contains(key)) { tr.Commit(); return default; }
                Xrecord xrec = (Xrecord)tr.GetObject(extDict.GetAt(key), Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, false);
                TypedValue[] vals = xrec.Data.AsArray();
                tr.Commit();
                for (int i = 0; i + IncomingJointEntrySize <= vals.Length; i += IncomingJointEntrySize)
                {
                    Handle bh = StringToHandle((string)vals[i].Value);
                    if (bh != giverHandle) continue;
                    return ParseIncomingJointEntry(vals, i);
                }
                return default;
            }
        }

        private static JointParams ParseIncomingJointEntry(TypedValue[] vals, int i)
        {
            var jt  = (JointType)(short)vals[i + 1].Value;
            var org = (Point3d)vals[i + 2].Value;
            double fnX = (double)vals[i +  3].Value, fnY = (double)vals[i +  4].Value, fnZ = (double)vals[i +  5].Value;
            double ldX = (double)vals[i +  6].Value, ldY = (double)vals[i +  7].Value, ldZ = (double)vals[i +  8].Value;
            double w   = (double)vals[i +  9].Value, d   = (double)vals[i + 10].Value, tw  = (double)vals[i + 11].Value;
            double rel = (double)vals[i + 12].Value, sh  = (double)vals[i + 13].Value, hd  = (double)vals[i + 14].Value;
            double pit = (double)vals[i + 15].Value;
            return new JointParams(jt, org,
                new Vector3d(fnX, fnY, fnZ), new Vector3d(ldX, ldY, ldZ),
                w, d, tw, "", "",
                rel, sh, false, hd, pit);
        }

		public static void DeleteJoint(ObjectId objId)
		{
			if (Make3D) {
				Document doc = Application.DocumentManager.MdiActiveDocument;
				Database db = doc.Database;
				using (doc.LockDocument()) {
                    using Transaction tr = db.TransactionManager.StartTransaction();
                    Solid3d ent = (Solid3d)tr.GetObject(objId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite);
                    ent.Erase(true);
                    tr.Commit();
                }
			}
		}

        public enum Joint
        {
            Tenon   = 0,
            Mortise = 1,
            Fill    = 2    // BoolUnite to restore a previously subtracted void (delta-swap undo)
        }

        public enum JointEnd
        {
            Up = 0,
            Down = 1
        }

		public enum JointType
		{
            None = 0,           // no joint / receiver side (post receiving a tenon)
			Tenon = 1,
			Mortise = -1,
			Butt = 2,
			ButtHousing = -2,
			Dovetail = 3,
			DovetailHousing = -3,
			Birdmouth = 4,
			BirdmouthHousing = -4,
			ScarfA = 5,
			ScarfB = -5,
			Spline = 6,
			SplineHousing = -6,
			Shoulder = 7,       // triangular bearing solid aligned with member axis; no tenon stub
            Polygon = 8         // arbitrary polygon profile -- geometry supplied via JointParams.CustomPts
		}

		public class ConnectedMember
		{
			public Handle Hndl{get; set;}
			public JointType JType{get; set;}
			public short Clr{get; set;}
            // Phase 1: endpoint info -- which end of each timber is at this joint
            // 0 = Near (bottom/left/start), 1 = Far (top/right/end)
            public short ThisEnd{get; set;}
            public short OtherEnd{get; set;}
            // Legacy 3-param constructor -- Clr kept for backward compat reading
            public ConnectedMember(Handle h, JointType t, short c)
            {
                Hndl = h;
                JType = t;
                Clr = c;
                ThisEnd = 0;
                OtherEnd = 0;
            }
            // Full constructor with endpoint info
            public ConnectedMember(Handle h, JointType t, short c, short thisEnd, short otherEnd)
            {
                Hndl = h;
                JType = t;
                Clr = c;
                ThisEnd = thisEnd;
                OtherEnd = otherEnd;
            }

			public override string ToString()
			{
				return JType.ToString() + "\t" + Hndl.ToString();
			}
		}

        // Endpoint position constants for Connection.ThisEnd / Connection.OtherEnd
        public static class End
        {
            public const short Near = 0;   // bottom / left / start end
            public const short Far  = 1;   // top / right / finish end
            public const short Body = 2;   // mid-body (mortise bored into body, not at an end)
        }

        // Rich per-connection record stored in the "Connections" xrecord.
        // Written by AddConnectionFull(); read by GetConnections() and ApplyCascade().
        //
        // ThisJoint = the joint type THIS timber contributes at this connection.
        //   Tenon, Dovetail, Scarf, etc. for geometry-producing senders.
        //   None (0) for receivers (post body receiving a tenon from a girt).
        //   JointType.Mortise may appear from old AddConnection() calls but is
        //   semantically equivalent to None -- the mortise is the void from the tenon.
        //
        // TenonHandles = handles of the standalone tenon solid(s) on THIS side.
        //   Set at draw time by AddConnectionFull(); used by ApplyCascade() to cut
        //   fresh mortises into regenerated connected members.
        //   Empty for receiver connections and old drawings (cascade uses geometric fallback).
        //   Multi-solid joints (future dovetail, scarf): all constituent solids listed here.
        public struct Connection
        {
            public Handle    ConnHandle;
            public short     ThisEnd;
            public short     OtherEnd;
            public JointType ThisJoint;
            public Handle[]  TenonHandles;
        }

		public static Handle StringToHandle(string str)
		{
			return new Handle(Convert.ToInt64(str, 16));
		}

        // Returns the Handle of every timber connected to timberId via the
        // ConnectedMembers xrecord. Used by TimberFactory to mark neighbours stale.
        public static Handle[] GetConnectedHandles(ObjectId timberId)
        {
            Database db = HostApplicationServices.WorkingDatabase;
            using (Application.DocumentManager.MdiActiveDocument.LockDocument()) {
                using Transaction tr = db.TransactionManager.StartTransaction();
                Entity ent = (Entity)tr.GetObject(timberId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, false, true);
                if (ent.ExtensionDictionary.IsNull) { tr.Commit(); return new Handle[0]; }
                DBDictionary extDict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, false);
                if (!extDict.Contains("ConnectedMembers")) { tr.Commit(); return new Handle[0]; }
                Xrecord xrec = (Xrecord)tr.GetObject(extDict.GetAt("ConnectedMembers"), Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, false);
                var members = new List<Handle>();
                Handle h = default(Handle);
                foreach (TypedValue tv in xrec.Data) {
                    if (tv.TypeCode == (short)DxfCode.Handle)
                        h = StringToHandle((string)tv.Value);
                    else if (tv.TypeCode == (short)DxfCode.Color)
                        members.Add(h);
                }
                tr.Commit();
                return members.ToArray();
            }
        }

        // Parses a comma-separated hex handle string (e.g. "2A4,2B1") into Handle[].
        private static Handle[] ParseHandleList(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return new Handle[0];
            string[] parts = csv.Split(',');
            var list = new List<Handle>();
            foreach (string p in parts)
            {
                string s = p.Trim();
                if (!string.IsNullOrEmpty(s))
                    list.Add(new Handle(Convert.ToInt64(s, 16)));
            }
            return list.ToArray();
        }

        // Formats Handle[] as a comma-separated hex string for xrecord Text storage.
        private static string FormatHandleList(Handle[] handles)
        {
            if (handles == null || handles.Length == 0) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < handles.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(handles[i].ToString());
            }
            return sb.ToString();
        }

        // Returns all Connection records for timberId.
        //
        // Reads the "Connections" xrecord (new format, 6-value blocks) first.
        // Falls back to "ConnectedMembers" (old format) if Connections is absent;
        // in that case TenonHandles is empty and ThisEnd/OtherEnd default to Near --
        // ApplyCascade will use the geometric fallback path for those timbers.
        public static Connection[] GetConnections(ObjectId timberId)
        {
            Database db = HostApplicationServices.WorkingDatabase;
            using (Application.DocumentManager.MdiActiveDocument.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Entity ent = (Entity)tr.GetObject(timberId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, false, true);
                if (ent.ExtensionDictionary.IsNull) { tr.Commit(); return new Connection[0]; }
                DBDictionary extDict = (DBDictionary)tr.GetObject(
                    ent.ExtensionDictionary, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, false);

                // New format: "Connections" xrecord with 6-value blocks
                // Block: Handle | Int16(ThisEnd) | Int16(OtherEnd) | Int16(JointType) | Text(TenonHandles) | Color(256)
                if (extDict.Contains("Connections"))
                {
                    Xrecord xrec = (Xrecord)tr.GetObject(
                        extDict.GetAt("Connections"), Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, false);
                    TypedValue[] vals = xrec.Data.AsArray();
                    var result = new List<Connection>();
                    int idx = 0;
                    while (idx + 5 <= vals.Length - 1)
                    {
                        if (vals[idx].TypeCode != (int)DxfCode.Handle) { idx++; continue; }
                        result.Add(new Connection
                        {
                            ConnHandle   = StringToHandle((string)vals[idx].Value),
                            ThisEnd      = (short)vals[idx + 1].Value,
                            OtherEnd     = (short)vals[idx + 2].Value,
                            ThisJoint    = (JointType)(short)vals[idx + 3].Value,
                            TenonHandles = ParseHandleList((string)vals[idx + 4].Value)
                        });
                        idx += 6;
                    }
                    tr.Commit();
                    return result.ToArray();
                }

                // Fallback: old ConnectedMembers triplet format (no endpoint or tenon data)
                if (extDict.Contains("ConnectedMembers"))
                {
                    Xrecord xrec = (Xrecord)tr.GetObject(
                        extDict.GetAt("ConnectedMembers"), Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, false);
                    var result = new List<Connection>();
                    Handle h = default(Handle);
                    JointType jt = JointType.None;
                    foreach (TypedValue tv in xrec.Data)
                    {
                        if (tv.TypeCode == (short)DxfCode.Handle)
                            h = StringToHandle((string)tv.Value);
                        else if (tv.TypeCode == (int)DxfCode.ExtendedDataInteger16)
                            jt = (JointType)(short)tv.Value;
                        else if (tv.TypeCode == (short)DxfCode.Color)
                            result.Add(new Connection
                            {
                                ConnHandle   = h,
                                ThisEnd      = End.Near,
                                OtherEnd     = End.Near,
                                ThisJoint    = jt,
                                TenonHandles = new Handle[0]
                            });
                    }
                    tr.Commit();
                    return result.ToArray();
                }

                tr.Commit();
                return new Connection[0];
            }
        }

        // Thin wrapper -- builds a Connection with empty TenonHandles and calls AddConnectionFull.
        // All existing callers that do not have per-connection tenon solid handles use this path.
        // Backward compat: same signature as before; no behavior change for existing callers.
        public static void AddConnection(ObjectId timberId, ObjectId connectedId, JointType connection, short thisEnd = 0, short otherEnd = 0)
        {
            AddConnectionFull(timberId, new Connection {
                ConnHandle   = connectedId.Handle,
                ThisEnd      = thisEnd,
                OtherEnd     = otherEnd,
                ThisJoint    = connection,
                TenonHandles = new Handle[0]
            });
        }

        // Full connection writer.  Writes three xrecords on timberId's extension dictionary:
        //
        //   "Connections"         (new) -- 6-value block per connection:
        //                                  Handle | Int16(ThisEnd) | Int16(OtherEnd) |
        //                                  Int16(ThisJoint) | Text(TenonHandles) | Color(256)
        //   "ConnectedMembers"    (compat) -- triplet: Handle | Int16(JType) | Color(256)
        //   "ConnectionEndpoints" (compat) -- quad:   Handle | Int16(Te) | Int16(Oe) | Color(0)
        //
        // Appends to existing data in all three records (update-in-place; never erases xrecords).
        public static void AddConnectionFull(ObjectId timberId, Connection conn)
        {
            Database db = HostApplicationServices.WorkingDatabase;
            using (Application.DocumentManager.MdiActiveDocument.LockDocument())
            {
                using Transaction tr = db.TransactionManager.StartTransaction();
                Entity locTimber = (Entity)tr.GetObject(timberId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite, false, true);
                if (locTimber.ExtensionDictionary.IsNull)
                    locTimber.CreateExtensionDictionary();
                DBDictionary extDict = (DBDictionary)tr.GetObject(
                    locTimber.ExtensionDictionary, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite, false);

                // ---- "Connections" xrecord (new format, 6-value blocks) ----
                {
                    string tenonStr = FormatHandleList(conn.TenonHandles);
                    if (extDict.Contains("Connections"))
                    {
                        Xrecord xrec = (Xrecord)tr.GetObject(
                            extDict.GetAt("Connections"), Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite);
                        TypedValue[] existing = xrec.Data.AsArray();
                        ResultBuffer rb = new();
                        foreach (TypedValue tv in existing) rb.Add(tv);
                        rb.Add(new TypedValue((int)DxfCode.Handle,                 conn.ConnHandle));
                        rb.Add(new TypedValue((int)DxfCode.ExtendedDataInteger16,  conn.ThisEnd));
                        rb.Add(new TypedValue((int)DxfCode.ExtendedDataInteger16,  conn.OtherEnd));
                        rb.Add(new TypedValue((int)DxfCode.ExtendedDataInteger16,  (short)conn.ThisJoint));
                        rb.Add(new TypedValue((int)DxfCode.Text,                   tenonStr));
                        rb.Add(new TypedValue((int)DxfCode.Color,                  256));
                        xrec.Data = rb;
                    }
                    else
                    {
                        ResultBuffer rb = new();
                        rb.Add(new TypedValue((int)DxfCode.Handle,                conn.ConnHandle));
                        rb.Add(new TypedValue((int)DxfCode.ExtendedDataInteger16, conn.ThisEnd));
                        rb.Add(new TypedValue((int)DxfCode.ExtendedDataInteger16, conn.OtherEnd));
                        rb.Add(new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)conn.ThisJoint));
                        rb.Add(new TypedValue((int)DxfCode.Text,                  tenonStr));
                        rb.Add(new TypedValue((int)DxfCode.Color,                 256));
                        Xrecord newRec = new() { Data = rb };
                        extDict.SetAt("Connections", newRec);
                        tr.AddNewlyCreatedDBObject(newRec, true);
                    }
                }

                // ---- "ConnectedMembers" xrecord (backward-compat triplet format) ----
                {
                    List<ConnectedMember> connectedMembers = new();
                    if (extDict.Contains("ConnectedMembers"))
                    {
                        Xrecord existingRec = (Xrecord)tr.GetObject(
                            extDict.GetAt("ConnectedMembers"), Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite);
                        ResultBufferEnumerator resultEnum = existingRec.Data.GetEnumerator();
                        Handle h = default(Handle); JointType t = 0; short c = 0;
                        while (resultEnum.MoveNext())
                        {
                            if (resultEnum.Current.TypeCode == (short)DxfCode.Handle)
                                h = StringToHandle((string)resultEnum.Current.Value);
                            if (resultEnum.Current.TypeCode == (int)DxfCode.ExtendedDataInteger16)
                                t = (JointType)(short)resultEnum.Current.Value;
                            if (resultEnum.Current.TypeCode == (short)DxfCode.Color)
                            { c = (short)resultEnum.Current.Value; connectedMembers.Add(new ConnectedMember(h, t, c)); }
                        }
                        ResultBuffer rb = new();
                        foreach (ConnectedMember m in connectedMembers)
                        {
                            rb.Add(new TypedValue((int)DxfCode.Handle,                m.Hndl));
                            rb.Add(new TypedValue((int)DxfCode.ExtendedDataInteger16, m.JType));
                            rb.Add(new TypedValue((int)DxfCode.Color,                 m.Clr));
                        }
                        rb.Add(new TypedValue((int)DxfCode.Handle,                conn.ConnHandle));
                        rb.Add(new TypedValue((int)DxfCode.ExtendedDataInteger16, conn.ThisJoint));
                        rb.Add(new TypedValue((int)DxfCode.Color,                 256));
                        existingRec.Data = rb;
                    }
                    else
                    {
                        ResultBuffer rb = new();
                        rb.Add(new TypedValue((int)DxfCode.Handle,                conn.ConnHandle));
                        rb.Add(new TypedValue((int)DxfCode.ExtendedDataInteger16, conn.ThisJoint));
                        rb.Add(new TypedValue((int)DxfCode.Color,                 256));
                        Xrecord newRec = new() { Data = rb };
                        extDict.SetAt("ConnectedMembers", newRec);
                        tr.AddNewlyCreatedDBObject(newRec, true);
                    }
                }

                // ---- "ConnectionEndpoints" xrecord (Phase 1 compat: quad format) ----
                {
                    List<(Handle h, short te, short oe)> endpoints = new();
                    if (extDict.Contains("ConnectionEndpoints"))
                    {
                        Xrecord epRec = (Xrecord)tr.GetObject(
                            extDict.GetAt("ConnectionEndpoints"), Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite);
                        ResultBufferEnumerator epEnum = epRec.Data.GetEnumerator();
                        Handle eh = default(Handle); short ete = 0, eoe = 0; int i16 = 0;
                        while (epEnum.MoveNext())
                        {
                            if (epEnum.Current.TypeCode == (short)DxfCode.Handle)
                            { eh = StringToHandle((string)epEnum.Current.Value); i16 = 0; }
                            else if (epEnum.Current.TypeCode == (int)DxfCode.ExtendedDataInteger16)
                            { if (i16 == 0) ete = (short)epEnum.Current.Value; else eoe = (short)epEnum.Current.Value; i16++; }
                            else if (epEnum.Current.TypeCode == (short)DxfCode.Color)
                            { endpoints.Add((eh, ete, eoe)); }
                        }
                        ResultBuffer epRb = new();
                        foreach (var ep in endpoints)
                        {
                            epRb.Add(new TypedValue((int)DxfCode.Handle,                ep.h));
                            epRb.Add(new TypedValue((int)DxfCode.ExtendedDataInteger16, ep.te));
                            epRb.Add(new TypedValue((int)DxfCode.ExtendedDataInteger16, ep.oe));
                            epRb.Add(new TypedValue((int)DxfCode.Color,                 0));
                        }
                        epRb.Add(new TypedValue((int)DxfCode.Handle,                conn.ConnHandle));
                        epRb.Add(new TypedValue((int)DxfCode.ExtendedDataInteger16, conn.ThisEnd));
                        epRb.Add(new TypedValue((int)DxfCode.ExtendedDataInteger16, conn.OtherEnd));
                        epRb.Add(new TypedValue((int)DxfCode.Color,                 0));
                        epRec.Data = epRb;
                    }
                    else
                    {
                        ResultBuffer epRb = new();
                        epRb.Add(new TypedValue((int)DxfCode.Handle,                conn.ConnHandle));
                        epRb.Add(new TypedValue((int)DxfCode.ExtendedDataInteger16, conn.ThisEnd));
                        epRb.Add(new TypedValue((int)DxfCode.ExtendedDataInteger16, conn.OtherEnd));
                        epRb.Add(new TypedValue((int)DxfCode.Color,                 0));
                        Xrecord epNewRec = new() { Data = epRb };
                        extDict.SetAt("ConnectionEndpoints", epNewRec);
                        tr.AddNewlyCreatedDBObject(epNewRec, true);
                    }
                }

                tr.Commit();
            }
        }

        // Replaces all occurrences of oldHandle with newHandle across the three connection
        // xrecords on timberId.  Called by ApplyCascade after regenerating a connected member
        // so subsequent regenerations resolve the correct (live) entity handle.
        //
        // Updates all three records for consistency:
        //   "Connections"         -- primary format read by GetConnections / ApplyCascade
        //   "ConnectedMembers"    -- compat triplets read by TimberReactor stale-marking
        //   "ConnectionEndpoints" -- compat quads (endpoint data only; not stale-marking critical)
        public static void UpdateConnectionHandle(ObjectId timberId, Handle oldHandle, Handle newHandle)
        {
            if (oldHandle == newHandle) return;
            Database db = HostApplicationServices.WorkingDatabase;
            using (Application.DocumentManager.MdiActiveDocument.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Entity ent = (Entity)tr.GetObject(timberId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, false, true);
                if (ent == null || ent.IsErased || ent.ExtensionDictionary.IsNull) { tr.Commit(); return; }
                DBDictionary extDict = (DBDictionary)tr.GetObject(
                    ent.ExtensionDictionary, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead, false);

                string oldStr = oldHandle.ToString();
                string newStr = newHandle.ToString();

                // ---- "Connections" (6-value blocks: Handle | I16 | I16 | I16 | Text | Color) ----
                if (extDict.Contains("Connections"))
                {
                    Xrecord xrec = (Xrecord)tr.GetObject(
                        extDict.GetAt("Connections"), Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite);
                    TypedValue[] vals = xrec.Data.AsArray();
                    bool changed = false;
                    for (int i = 0; i < vals.Length; i++)
                    {
                        if (vals[i].TypeCode == (int)DxfCode.Handle &&
                            vals[i].Value?.ToString() == oldStr)
                        {
                            vals[i] = new TypedValue((int)DxfCode.Handle, newHandle);
                            // The TenonHandles Text field (offset +4) stays: the new tenon handles
                            // are set by AddConnectionFull immediately after this update.
                            changed = true;
                        }
                    }
                    if (changed) xrec.Data = new ResultBuffer(vals);
                }

                // ---- "ConnectedMembers" (triplets: Handle | I16(JType) | Color) ----
                if (extDict.Contains("ConnectedMembers"))
                {
                    Xrecord xrec = (Xrecord)tr.GetObject(
                        extDict.GetAt("ConnectedMembers"), Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite);
                    TypedValue[] vals = xrec.Data.AsArray();
                    bool changed = false;
                    for (int i = 0; i < vals.Length; i++)
                    {
                        if (vals[i].TypeCode == (int)DxfCode.Handle &&
                            vals[i].Value?.ToString() == oldStr)
                        { vals[i] = new TypedValue((int)DxfCode.Handle, newHandle); changed = true; }
                    }
                    if (changed) xrec.Data = new ResultBuffer(vals);
                }

                // ---- "ConnectionEndpoints" (quads: Handle | I16(Te) | I16(Oe) | Color) ----
                if (extDict.Contains("ConnectionEndpoints"))
                {
                    Xrecord xrec = (Xrecord)tr.GetObject(
                        extDict.GetAt("ConnectionEndpoints"), Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite);
                    TypedValue[] vals = xrec.Data.AsArray();
                    bool changed = false;
                    for (int i = 0; i < vals.Length; i++)
                    {
                        if (vals[i].TypeCode == (int)DxfCode.Handle &&
                            vals[i].Value?.ToString() == oldStr)
                        { vals[i] = new TypedValue((int)DxfCode.Handle, newHandle); changed = true; }
                    }
                    if (changed) xrec.Data = new ResultBuffer(vals);
                }

                tr.Commit();
            }
        }

	}
}



