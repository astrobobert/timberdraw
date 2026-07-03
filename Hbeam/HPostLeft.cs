using System.Collections.Generic;
using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using TimberFrameSuite.Standards;

namespace TimberDraw
{

	public class HPostLeft
	{
		public double hpLengthLong;
		private double TenonWidth = 2;
		public Point3d StartPoint;
		public double Width;
		public double Depth;
		public double postDepth;
		public double postWidth;
		public double KpostDepth;
		public double RafterWidth;
		public int HBDivisor;
		public string BentNumber;
		public string Designation;
		public string Type;
		public ObjectId TimberId;
		public ObjectId TenonUp;
		public ObjectId TenonDown;
		public JointParams NearJointParamsDrawn;   // params used to draw TenonDown (foot into HBeamLeft)
		public Point3d[]   UpTenonPts;             // oblique rafter-contact polygon pts for BentNetwork
		public double      UpTenonWidth;

		public List<ObjectId> PegCol = new();
		public void Draw()
		{
			Point3dCollection pts = new();
			double hbLength = ((Module1.Span - ((postDepth * 2) + KpostDepth)) / HBDivisor);
			double hpLengthShort = (hbLength - Depth) * Module1.Pitch;
			hpLengthLong = (hbLength * Module1.Pitch) + 6;
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
			pts.Add(Module1.AtPoint(pts[0], 0, hpLengthLong, 0));
			pts.Add(Module1.AtPoint(pts[0], -Depth, hpLengthShort + 6, 0));
			pts.Add(Module1.AtPoint(pts[0], -Depth, 0, 0));
			string sizeStr = (int)Width + "x" + (int)Depth + "x" + Module1.BuyLongFeet(hpLengthLong);
			TimberId = Module1.DrawElement(pts, Width, Type, BentNumber, Designation, sizeStr, jointNear: "Tenon", jointFar: "Tenon");
            // Capture near/far body endpoints before pts is reused for far (rafter-contact) tenon geometry.
            Point3d _nearBodyPt = pts[0];
            Point3d _farBodyPt  = pts[1];
			if (Module1.HasJoinery) {
				double tenonZ = 0;
				if (Module1.Make3D)
					tenonZ = StartPoint.Z + ((Width - 2) / 2);
				else
					tenonZ = 0;
				// Near-end tenon (at foot): Origin at StartPoint, projects -Y (into plate), spans -X (Depth toward center).
				Point3d tenonDownOrigin = new Point3d(StartPoint.X, StartPoint.Y, tenonZ);
				var nearP = new JointParams(Module1.JointType.Tenon,
                    tenonDownOrigin,
                    new Vector3d(0, -1, 0),  // FaceNormal: into HBeamLeft below
                    new Vector3d(-1, 0, 0),  // LateralDir: along Depth (-X, toward center)
                    Width, Depth, TenonWidth, BentNumber, Designation);
				NearJointParamsDrawn = nearP;
				TenonDown = JointFactory.Create(Module1.JointType.Tenon, nearP);
                Module1.AddJoint(TimberId, TenonDown, Module1.Joint.Tenon);
				double ps = TFGPegStandards.GetPresetForTenonThickness(TenonWidth).CalculatedSpacingInches;
				double r  = TFGPegStandards.GetPresetForTenonThickness(TenonWidth).DiameterInches / 2;
				double deltaX = (Depth - ps) / 2;
				PegCol.Add(Module1.DrawPeg(Module1.AtPoint(tenonDownOrigin, -deltaX, -1.75, -(tenonZ + 0.75)), r, Width + 1.5, "Peg", "", ""));
				PegCol.Add(Module1.DrawPeg(Module1.AtPoint(tenonDownOrigin, -(deltaX + ps), -1.75, -(tenonZ + 0.75)), r, Width + 1.5, "Peg", "", ""));
				pts.Clear();
				pts.Add(Module1.AtPoint(new Point3d(StartPoint.X, StartPoint.Y, tenonZ), -Depth, hpLengthShort + 6, StartPoint.Z));
				pts.Add(Module1.AtPoint(pts[0], Depth, Depth * Module1.Pitch, 0));
				pts.Add(Module1.PolarPoint(pts[1], Module1.Beta + (Math.PI / 2), 4));
				pts.Add(Module1.PolarPoint(pts[0], Module1.rad(90), 4 / Math.Cos(Module1.Beta)));
				TenonUp = Module1.DrawElement(pts, TenonWidth, "Tenon", "1", "");
                Module1.AddJoint(TimberId, TenonUp, Module1.Joint.Tenon);
				UpTenonPts   = new Point3d[] { pts[0], pts[1], pts[2], pts[3] };
				UpTenonWidth = TenonWidth;
				double angle = 0;
				double offset = 0;
				if (Depth < 7){angle = 1.0427 + Math.Atan(Module1.Pitch) + 1.5708;offset = 3.47312;}
				else{angle = 1.15839 + Math.Atan(Module1.Pitch) + 1.5708;offset = 4.36607;}
				Point3d pt = default(Point3d);
				pt = Module1.PolarPoint(pts[1], angle, offset);
				pt = Module1.AtPoint(pt, 0, 0, -(0.75 + tenonZ));
				PegCol.Add(Module1.DrawPeg(pt, r, RafterWidth + 1.5, "Peg", "", "", ""));
			}
			// Phase 2: persist regeneration data
			Module1.PersistPegHandles(TimberId, PegCol);
			Module1.SaveDrawContext(TimberId, BuildContextJson());
            // End markers: "N" at near (foot/bottom) body corner, "F" at far (rafter-contact/top) body corner.
            if (Module1.ShowEndMarkers) {
                double _mz = Module1.Make3D ? StartPoint.Z + (Width / 2) : 0;
                var em = new System.Collections.Generic.List<ObjectId>();
                em.Add(Module1.DrawEndMarker(new Point3d(_nearBodyPt.X, _nearBodyPt.Y, _mz), "N"));
                em.Add(Module1.DrawEndMarker(new Point3d(_farBodyPt.X,  _farBodyPt.Y,  _mz), "F"));
                Module1.PersistEndMarkerHandles(TimberId, em);
            }
		}

        private string BuildContextJson()
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return string.Format(c,
                "{{\"class\":\"HPostLeft\",\"startX\":{0},\"startY\":{1},\"startZ\":{2},\"postDepth\":{3},\"postWidth\":{4},\"kpostDepth\":{5},\"rafterWidth\":{6},\"hbDivisor\":{7},\"span\":{8},\"pitch\":{9},\"beta\":{10},\"make3D\":{11}}}",
                StartPoint.X, StartPoint.Y, StartPoint.Z,postDepth, postWidth, KpostDepth, RafterWidth, HBDivisor,Module1.Span, Module1.Pitch, Module1.Beta, Module1.Make3D ? "true" : "false");
        }
		public void AddMortise(ObjectId MortiseId)
		{
            Module1.AddJoint(TimberId, MortiseId, Module1.Joint.Mortise);
			Module1.DeleteJoint(MortiseId);
		}
	}
}
