using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using TimberFrameSuite.Standards;

namespace TimberDraw
{

	public class QPUpperGirt
	{
		private double TenonWidth = 2;
		public Point3d StartPoint;
		public double Width;
		public double Depth;
		public double postDepth;
		public double QPRafterWidth;
		public double QPQpostDepth;
		public string BentNumber;
		public string Designation;
		public string Type;
		public ObjectId TimberId;
		public ObjectId TenonLeft;
		public ObjectId TenonRight;
		public JointParams NearJointParamsDrawn;   // params used to draw TenonLeft (into left QPost)
		public JointParams FarJointParamsDrawn;    // params used to draw TenonRight (into right QPost)

		public List<ObjectId> PegCol = new();
		public void Draw()
		{
			double thirdSpan = Module1.Span / 3;
			double z = 0;
			if (Module1.Make3D) {
				switch (Module1.OffsetType) {
					case Module1.Back:
						z = 0;
						break;
					case Module1.Centered:
						z = (QPRafterWidth - Width) / 2;
						break;
					case Module1.Front:
						z = (QPRafterWidth - Width);
						break;
				}
			}
			StartPoint = new Point3d(StartPoint.X, StartPoint.Y, z);
			Point3dCollection pts = new()
            {
                StartPoint
            };
			pts.Add(Module1.AtPoint(pts[0], thirdSpan - (QPQpostDepth * 2), 0, 0));
			pts.Add(Module1.AtPoint(pts[1], 0, Depth, 0));
			pts.Add(Module1.AtPoint(pts[2], -(thirdSpan - (QPQpostDepth * 2)), 0, 0));
			double girtLen = Module1.Span / 3 - (QPQpostDepth * 2);
			string sizeStr = (int)Width + "x" + (int)Depth + "x" + Module1.BuyLongFeet(girtLen);
			TimberId = Module1.DrawElement(pts, Width, Type, BentNumber, Designation, sizeStr, jointNear: "Tenon", jointFar: "Tenon");
			if (Module1.HasJoinery) {
				double tenonZ = 0;
				if (Module1.Make3D)
					tenonZ = StartPoint.Z + ((Width - 2) / 2);
				else
					tenonZ = 0;
				// Left tenon: Origin at girt start, projects -X (into left queen post).
				Point3d tenonLeftOrigin = new Point3d(StartPoint.X, StartPoint.Y, tenonZ);
				var leftP = new JointParams(Module1.JointType.Tenon,
					tenonLeftOrigin, new Vector3d(-1, 0, 0), new Vector3d(0, 1, 0),
					Width, Depth, TenonWidth, BentNumber, Designation);
				NearJointParamsDrawn = leftP;
				TenonLeft = JointFactory.Create(Module1.JointType.Tenon, leftP);
				Module1.AddJoint(TimberId, TenonLeft, Module1.Joint.Tenon);
				Module1.DeleteJoint(TenonLeft);   // standalone solid no longer needed; mortise created from params in QPBent
				double r = TFGPegStandards.GetPresetForTenonThickness(TenonWidth).DiameterInches / 2;
				Point3d pegCenterPt = new(tenonLeftOrigin.X, tenonLeftOrigin.Y, -0.75);
				PegCol.Add(Module1.DrawPeg(Module1.AtPoint(pegCenterPt, -1.75, Depth / 2, 0), r, Width + 1.5, "Peg", "", "", ""));
				// Right tenon: Origin at girt end, projects +X (into right queen post).
				Point3d tenonRightOrigin = new Point3d((thirdSpan * 2) - QPQpostDepth, Module1.TOH + ((thirdSpan - postDepth + QPQpostDepth) * Module1.Pitch) - (6 + Depth), tenonZ);
				var rightP = new JointParams(Module1.JointType.Tenon,
					tenonRightOrigin, new Vector3d(1, 0, 0), new Vector3d(0, 1, 0),
					Width, Depth, TenonWidth, BentNumber, Designation);
				FarJointParamsDrawn = rightP;
				TenonRight = JointFactory.Create(Module1.JointType.Tenon, rightP);
				Module1.AddJoint(TimberId, TenonRight, Module1.Joint.Tenon);
				Module1.DeleteJoint(TenonRight);  // standalone solid no longer needed
				pegCenterPt = new Point3d(tenonRightOrigin.X, tenonRightOrigin.Y, -0.75);
				PegCol.Add(Module1.DrawPeg(Module1.AtPoint(pegCenterPt, 1.75, Depth / 2, 0), r, Width + 1.5, "Peg", "", ""));
			}
			// Phase 2: persist regeneration data
			Module1.PersistPegHandles(TimberId, PegCol);
			Module1.SaveDrawContext(TimberId, BuildContextJson());
            // End markers: "N" at near (left/QP) face center, "F" at far (right/QP) face center.
            if (Module1.ShowEndMarkers) {
                double _mz = Module1.Make3D ? StartPoint.Z + (Width / 2) : 0;
                double _thirdSpan = Module1.Span / 3;
                var em = new System.Collections.Generic.List<ObjectId>();
                em.Add(Module1.DrawEndMarker(new Point3d(StartPoint.X, StartPoint.Y + Depth / 2, _mz), "N"));
                em.Add(Module1.DrawEndMarker(new Point3d(StartPoint.X + _thirdSpan - (QPQpostDepth * 2), StartPoint.Y + Depth / 2, _mz), "F"));
                Module1.PersistEndMarkerHandles(TimberId, em);
            }
		}

        private string BuildContextJson()
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return string.Format(c,
                "{{\"class\":\"QPUpperGirt\",\"startX\":{0},\"startY\":{1},\"qpRafterWidth\":{2},\"qpQpostDepth\":{3},\"postDepth\":{4},\"span\":{5},\"make3D\":{6},\"offsetType\":{7},\"toh\":{8},\"pitch\":{9}}}",
                StartPoint.X, StartPoint.Y,QPRafterWidth, QPQpostDepth, postDepth,Module1.Span, Module1.Make3D ? "true" : "false", Module1.OffsetType,Module1.TOH, Module1.Pitch);
        }
		public void AddMortise(ObjectId MortiseId)
		{
			Module1.AddJoint(TimberId, MortiseId, Module1.Joint.Mortise);
			Module1.DeleteJoint(MortiseId);
		}
	}
}
