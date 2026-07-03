using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using TimberFrameSuite.Standards;
namespace TimberDraw
{

	public class HBGirt
	{
		private double TenonWidth = 2;
		public Point3d StartPoint;
		public double Width;
		public double Depth;
		public double postDepth;
		public double postWidth;
		public double KpostDepth;
		public int HBDivisor;
		public string BentNumber;
		public string Designation;
		public string Type;
		public ObjectId Timber;
		public ObjectId TenonLeft;
		public ObjectId TenonRight;
		public JointParams NearJointParamsDrawn;   // params used to draw TenonLeft (into left HPpost)
		public JointParams FarJointParamsDrawn;    // params used to draw TenonRight (into right HPpost)

		public List<ObjectId> PegCol = new();
		public void Draw()
		{
			Point3dCollection pts = new();
			double hbLength = ((Module1.Span - ((postDepth * 2) + KpostDepth)) / HBDivisor * 2) + KpostDepth;
			double hpLengthShort = (hbLength - Depth) * Module1.Pitch;
			double hpLengthLong = hbLength * Module1.Pitch;
			//Dim z As Double = 0
			//If Make3D Then
			//    Select Case OffsetType
			//        Case Back
			//            z = 0
			//        Case Centered
			//            z = (postWidth - Width) / 2
			//        Case Front
			//            z = (postWidth - Width)
			//    End Select
			//End If
			//StartPoint = New Point3d(StartPoint.X, StartPoint.Y, z)
			pts.Add(StartPoint);
			pts.Add(Module1.AtPoint(pts[0], hbLength, 0, 0));
			pts.Add(Module1.AtPoint(pts[0], hbLength, Depth, 0));
			pts.Add(Module1.AtPoint(pts[0], 0, Depth, 0));
			string sizeStr = (int)Width + "x" + (int)Depth + "x" + Module1.BuyLongFeet(hbLength);
			Timber = Module1.DrawElement(pts, Width, Type, BentNumber, Designation, sizeStr, jointNear: "Tenon", jointFar: "Tenon");
			if (Module1.HasJoinery) {
				double tenonZ = 0;
				if (Module1.Make3D)
					tenonZ = StartPoint.Z + ((Width - 2) / 2);
				else
					tenonZ = 0;
				// Left tenon: Origin at girt start, projects -X (into left post), spans +Y (Depth).
				Point3d tenonLeftOrigin = new Point3d(StartPoint.X, StartPoint.Y, tenonZ);
				var nearP = new JointParams(Module1.JointType.Tenon,
                    tenonLeftOrigin,
                    new Vector3d(-1, 0, 0),  // FaceNormal: into left HPpost
                    new Vector3d(0, 1, 0),   // LateralDir: along Depth (+Y)
                    Width, Depth, TenonWidth, BentNumber, Designation);
				NearJointParamsDrawn = nearP;
				TenonLeft = JointFactory.Create(Module1.JointType.Tenon, nearP);
				Module1.AddJoint(Timber, TenonLeft, Module1.Joint.Tenon);
				double ps = TFGPegStandards.GetPresetForTenonThickness(TenonWidth).CalculatedSpacingInches;
				double r  = TFGPegStandards.GetPresetForTenonThickness(TenonWidth).DiameterInches / 2;
				double deltaY = (Depth - ps) / 2;
				PegCol.Add(Module1.DrawPeg(Module1.AtPoint(tenonLeftOrigin, -1.75, deltaY, -(tenonZ + 0.75)), r, Width + 1.5, "Peg", "", ""));
				PegCol.Add(Module1.DrawPeg(Module1.AtPoint(tenonLeftOrigin, -1.75, deltaY + ps, -(tenonZ + 0.75)), r, Width + 1.5, "Peg", "", ""));
				// Right tenon: Origin at girt end, projects +X (into right post), spans +Y (Depth).
				Point3d tenonRightOrigin = new Point3d(StartPoint.X + hbLength, StartPoint.Y, tenonZ);
				var farP = new JointParams(Module1.JointType.Tenon,
                    tenonRightOrigin,
                    new Vector3d(1, 0, 0),   // FaceNormal: into right HPpost
                    new Vector3d(0, 1, 0),   // LateralDir: along Depth (+Y)
                    Width, Depth, TenonWidth, BentNumber, Designation);
				FarJointParamsDrawn = farP;
				TenonRight = JointFactory.Create(Module1.JointType.Tenon, farP);
                Module1.AddJoint(Timber, TenonRight, Module1.Joint.Tenon);
				PegCol.Add(Module1.DrawPeg(Module1.AtPoint(tenonRightOrigin, 1.75, deltaY, -(tenonZ + 0.75)), r, Width + 1.5, "Peg", "", ""));
				PegCol.Add(Module1.DrawPeg(Module1.AtPoint(tenonRightOrigin, 1.75, deltaY + ps, -(tenonZ + 0.75)), r, Width + 1.5, "Peg", "", ""));
			}
			// Phase 2: persist regeneration data
			Module1.PersistPegHandles(Timber, PegCol);
			Module1.SaveDrawContext(Timber, BuildContextJson());
            // End markers: "N" at near (left/HPost) face center, "F" at far (right/HPost) face center.
            if (Module1.ShowEndMarkers) {
                double _mz = Module1.Make3D ? StartPoint.Z + (Width / 2) : 0;
                var em = new System.Collections.Generic.List<ObjectId>();
                em.Add(Module1.DrawEndMarker(new Point3d(StartPoint.X, StartPoint.Y + Depth / 2, _mz), "N"));
                em.Add(Module1.DrawEndMarker(new Point3d(StartPoint.X + hbLength, StartPoint.Y + Depth / 2, _mz), "F"));
                Module1.PersistEndMarkerHandles(Timber, em);
            }
		}

        private string BuildContextJson()
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return string.Format(c,
                "{{\"class\":\"HBGirt\",\"startX\":{0},\"startY\":{1},\"startZ\":{2},\"postDepth\":{3},\"postWidth\":{4},\"kpostDepth\":{5},\"hbDivisor\":{6},\"span\":{7},\"pitch\":{8},\"make3D\":{9}}}",
                StartPoint.X, StartPoint.Y, StartPoint.Z,postDepth, postWidth, KpostDepth, HBDivisor,Module1.Span, Module1.Pitch, Module1.Make3D ? "true" : "false");
        }
		public void AddMortise(ObjectId MortiseId)
		{
            Module1.AddJoint(Timber, MortiseId, Module1.Joint.Mortise);
			Module1.DeleteJoint(MortiseId);
		}
	}
}
