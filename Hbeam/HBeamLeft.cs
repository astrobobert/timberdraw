using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using TimberFrameSuite.Standards;
namespace TimberDraw
{

	public class HBeamLeft
	{
		public double hbLength;
		private double TenonWidth = 2;
		public Point3d StartPoint;
		public double Width;
		public double Depth;
		public double postWidth;
		public double KpostDepth;
		public int HBDivisor;
		public string BentNumber;
		public string Designation;
		public string Type;
		public ObjectId TimberId;
		public ObjectId Tenon;
		public JointParams FarJointParamsDrawn;   // params used to draw Tenon (into left post)

		public List<ObjectId> PegCol = new();
		public void Draw()
		{
			Point3dCollection pts = new()
            {
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
                StartPoint
            };
			pts.Add(Module1.PolarPoint(pts[0], Module1.rad(0), hbLength + 4));
			pts.Add(Module1.PolarPoint(pts[1], Module1.rad(90), Depth));
			pts.Add(Module1.PolarPoint(pts[0], Module1.rad(90), Depth));
			string sizeStr = (int)Width + "x" + (int)Depth + "x" + Module1.BuyLongFeet(hbLength + 4);
			TimberId = Module1.DrawElement(pts, Width, Type, BentNumber, Designation, sizeStr, jointNear: "Butt", jointFar: "Tenon");
			if (Module1.HasJoinery) {
				double tenonZ = 0;
				if (Module1.Make3D)
					tenonZ = StartPoint.Z + ((Width - 2) / 2);
				else
					tenonZ = 0;
				// Tenon at left-post end: Origin at StartPoint, projects -X (into left wall post), spans +Y (Depth).
				Point3d tenonOrigin = new Point3d(StartPoint.X, StartPoint.Y, tenonZ);
				var farP = new JointParams(Module1.JointType.Tenon,
                    tenonOrigin,
                    new Vector3d(-1, 0, 0),  // FaceNormal: into left wall post
                    new Vector3d(0, 1, 0),   // LateralDir: along Depth (+Y)
                    Width, Depth, TenonWidth, BentNumber, Designation);
				FarJointParamsDrawn = farP;
				Tenon = JointFactory.Create(Module1.JointType.Tenon, farP);
                Module1.AddJoint(TimberId, Tenon, Module1.Joint.Tenon);
				double ps = TFGPegStandards.GetPresetForTenonThickness(TenonWidth).CalculatedSpacingInches;
				double r  = TFGPegStandards.GetPresetForTenonThickness(TenonWidth).DiameterInches / 2;
				double deltaY = (Depth - ps) / 2;
				PegCol.Add(Module1.DrawPeg(Module1.AtPoint(tenonOrigin, -1.75, deltaY, -(tenonZ + 0.75)), r, postWidth + 1.5, "Peg", "", ""));
				PegCol.Add(Module1.DrawPeg(Module1.AtPoint(tenonOrigin, -1.75, deltaY + ps, -(tenonZ + 0.75)), r, postWidth + 1.5, "Peg", "", ""));
			}
			// Phase 2: persist regeneration data
			Module1.PersistPegHandles(TimberId, PegCol);
			Module1.SaveDrawContext(TimberId, BuildContextJson());
            // End markers: "N" at near (center/inner) face center, "F" at far (post) face center.
            if (Module1.ShowEndMarkers) {
                double _mz = Module1.Make3D ? StartPoint.Z + (Width / 2) : 0;
                var em = new System.Collections.Generic.List<ObjectId>();
                em.Add(Module1.DrawEndMarker(new Point3d(StartPoint.X + hbLength + 4, StartPoint.Y + Depth / 2, _mz), "N"));
                em.Add(Module1.DrawEndMarker(new Point3d(StartPoint.X, StartPoint.Y + Depth / 2, _mz), "F"));
                Module1.PersistEndMarkerHandles(TimberId, em);
            }
		}

        private string BuildContextJson()
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return string.Format(c,
                "{{\"class\":\"HBeamLeft\",\"startX\":{0},\"startY\":{1},\"startZ\":{2},\"hbLength\":{3},\"postWidth\":{4},\"kpostDepth\":{5},\"hbDivisor\":{6},\"make3D\":{7}}}",
                StartPoint.X, StartPoint.Y, StartPoint.Z, hbLength, postWidth, KpostDepth, HBDivisor, Module1.Make3D ? "true" : "false");
        }
		public void AddMortise(ObjectId MortiseId)
		{
            Module1.AddJoint(TimberId, MortiseId, Module1.Joint.Mortise);
			Module1.DeleteJoint(MortiseId);
		}
	}
}
