using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{

	public class QPTUpperGirt
	{
		private double TenonWidth = 2;
		public Point3d StartPoint;
		public double Width;
		public double Depth;
		public double QpostDepth;
		public string BentNumber;
		public string Designation;
		public string Type;
		public ObjectId TimberId;
		public ObjectId TenonLeftId;

		public ObjectId TenonRightId;
		public void Draw()
		{
			Point3dCollection pts = new();
			double z = 0;
			if (Module1.Make3D) {
				switch (Module1.OffsetType) {
					case Module1.Back:
						z = 0;
						break;
					case Module1.Centered:
						z = (QpostDepth - Width) / 2;
						break;
					case Module1.Front:
						z = (QpostDepth - Width);
						break;
				}
			}
			StartPoint = new Point3d(StartPoint.X, StartPoint.Y, z);
			double thirdSpan = Module1.Span / 3;
			pts.Add(StartPoint);
			pts.Add(Module1.AtPoint(pts[0], thirdSpan - (QpostDepth * 2), 0, 0));
			pts.Add(Module1.AtPoint(pts[1], 0, Depth, 0));
			pts.Add(Module1.AtPoint(pts[2], -(thirdSpan - (QpostDepth * 2)), 0, 0));
			double girtLen = Module1.Span / 3 - (QpostDepth * 2);
			string sizeStr = (int)Width + "x" + (int)Depth + "x" + Module1.BuyLongFeet(girtLen);
			TimberId = Module1.DrawElement(pts, Width, Type, BentNumber, Designation, sizeStr, jointNear: "Tenon", jointFar: "Tenon");
            Point3d _nearBodyPt = pts[0]; Point3d _farBodyPt = pts[pts.Count - 1];
			if (Module1.HasJoinery) {
				double tenonZ = 0;
				if (Module1.Make3D)
					tenonZ = StartPoint.Z + ((Width - 2) / 2);
				else
					tenonZ = 0;
				// Left tenon: Origin at girt start, projects -X (into left queen post), spans +Y (Depth).
				// Note: original code passed tenonZ as extrusion width (bug); corrected to TenonWidth here.
				TenonLeftId = JointFactory.Create(Module1.JointType.Tenon, new JointParams(
                    Module1.JointType.Tenon,
                    new Point3d(StartPoint.X, StartPoint.Y, tenonZ),
                    new Vector3d(-1, 0, 0),  // FaceNormal: into left queen post
                    new Vector3d(0, 1, 0),   // LateralDir: along Depth (+Y)
                    Width, Depth, TenonWidth, BentNumber, Designation));
				Module1.AddJoint(TimberId, TenonLeftId, Module1.Joint.Tenon);
				// Right tenon: Origin at girt end, projects +X (into right queen post), spans +Y (Depth).
				TenonRightId = JointFactory.Create(Module1.JointType.Tenon, new JointParams(
                    Module1.JointType.Tenon,
                    new Point3d((thirdSpan * 2) - QpostDepth, StartPoint.Y, tenonZ),
                    new Vector3d(1, 0, 0),   // FaceNormal: into right queen post
                    new Vector3d(0, 1, 0),   // LateralDir: along Depth (+Y)
                    Width, Depth, TenonWidth, BentNumber, Designation));
                Module1.AddJoint(TimberId, TenonRightId, Module1.Joint.Tenon);
			}
			// Phase 2: persist regeneration data (no pegs in QpostTruss)
			Module1.SaveDrawContext(TimberId, BuildContextJson());
            if (Module1.ShowEndMarkers) {
                double _mz = Module1.Make3D ? StartPoint.Z + (Width / 2) : 0;
                var em = new System.Collections.Generic.List<ObjectId>();
                em.Add(Module1.DrawEndMarker(new Point3d(_nearBodyPt.X, _nearBodyPt.Y, _mz), "N"));
                em.Add(Module1.DrawEndMarker(new Point3d(_farBodyPt.X, _farBodyPt.Y, _mz), "F"));
                Module1.PersistEndMarkerHandles(TimberId, em);
            }
		}

        private string BuildContextJson()
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return string.Format(c,
                "{{\"class\":\"QPTUpperGirt\",\"startX\":{0},\"startY\":{1},\"startZ\":{2},\"qpostDepth\":{3},\"span\":{4},\"make3D\":{5},\"offsetType\":{6}}}",
                StartPoint.X, StartPoint.Y, StartPoint.Z, QpostDepth,Module1.Span, Module1.Make3D ? "true" : "false", Module1.OffsetType);
        }
		public void AddMortise(ObjectId MortiseId)
		{
			Module1.AddJoint(TimberId, MortiseId, Module1.Joint.Mortise);
			Module1.DeleteJoint(MortiseId);
		}
	}
}
