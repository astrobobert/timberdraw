using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.DatabaseServices;

namespace TimberDraw
{
	public class BentBrace
	{
		public enum Hand { Left = 0, Right = 1 }
		private double SinBeta = Math.Sqrt(2) / 2;
		public Point3d StartPoint;
		public double Width;
		public double Depth;
		public double Length;
		public double postWidth;
		public double ZAngle;
		public double YAngle;
		public double XAngle;
		public ObjectId TimberId;
		public ObjectId TenonDown;   // lower/left tenon (which end this reaches varies by brace hand)
		public ObjectId TenonUp;     // upper/right tenon
		// SwapEnds=false (BBLeft, FBRight): NearJointType controls TenonDown (post), FarJointType controls TenonUp (girt).
		// SwapEnds=true  (BBRight, FBLeft): NearJointType controls TenonUp   (post), FarJointType controls TenonDown (girt).
		// Set by the orchestrator (KPBent) so that Near always means the post connection and Far means the girt.
		public bool SwapEnds = false;
		public string NearJointType = "Tenon";  // "Butt" removes the post-end tenon
		public string FarJointType  = "Tenon";  // "Butt" removes the girt-end tenon
		public Dictionary<string, double> NearParams = new();
		public Dictionary<string, double> FarParams  = new();
		public List<ObjectId> PegCol = new();

		public void Draw()
		{
			if (Length > 0) {
				double z = 0;
				if (Module1.Make3D) {
					switch (Module1.OffsetType) {
						case Module1.Back:     z = 0; break;
						case Module1.Centered: z = (postWidth - Width) / 2; break;
						case Module1.Front:
							z = postWidth;
							ZAngle += 90;
							YAngle += 180;
							break;
					}
				}
				StartPoint = new Point3d(StartPoint.X, StartPoint.Y, z);
				Point3dCollection pts = new();
				DoubleCollection bulge = new();
				pts.Add(new Point3d(StartPoint.X, StartPoint.Y - Length, StartPoint.Z));
				bulge.Add(0);
				pts.Add(new Point3d(StartPoint.X + (Depth * SinBeta), StartPoint.Y - Length + (Depth * SinBeta), StartPoint.Z));
				bulge.Add(-0.0635);
				pts.Add(new Point3d(StartPoint.X + Length - (Depth * SinBeta), StartPoint.Y - (Depth * SinBeta), StartPoint.Z));
				bulge.Add(0);
				pts.Add(new Point3d(StartPoint.X + Length, StartPoint.Y, StartPoint.Z));
				bulge.Add(0);
				pts.Add(new Point3d(StartPoint.X + Length - (Depth / SinBeta), StartPoint.Y, StartPoint.Z));
				bulge.Add(0);
				pts.Add(new Point3d(StartPoint.X, StartPoint.Y - Length + (Depth / SinBeta), StartPoint.Z));
				bulge.Add(0);
				string sizeStr = (int)Width + "x" + (int)Depth + "x" + Module1.BuyLongFeet(Length);
				TimberId = Module1.DrawBrace(Width, Depth, Length, pts, bulge, StartPoint.Z, Width, "Brace", "", "*", sizeStr, YAngle, ZAngle, StartPoint.X, StartPoint.Y, StartPoint.Z, jointNear: NearJointType, jointFar: FarJointType);

				// Per-end params (defaults: tenonWidth=1.5 for braces)
				double nearTW = NearParams.TryGetValue("tenonWidth", out var ntw) ? ntw : 1.5;
				double farTW  = FarParams.TryGetValue("tenonWidth",  out var ftw) ? ftw : 1.5;
				const double pegR = 0.375;   // 0.75" peg for 1.5" tenon

				double tenonZ = Module1.Make3D ? 1.5 : 0;
				double deltaZ = (Module1.OffsetType == Module1.Front) ? postWidth - 0.75 : -0.75;

				// SwapEnds determines which tenon is the post-end (Near) vs girt-end (Far).
				// SwapEnds=false: TenonDown=post(Near), TenonUp=girt(Far).
				// SwapEnds=true:  TenonUp=post(Near), TenonDown=girt(Far).
				string tenonDownJoint = SwapEnds ? FarJointType  : NearJointType;
				string tenonUpJoint   = SwapEnds ? NearJointType : FarJointType;

				// Near (Down) tenon.  Drawn when the controlling joint type is "Tenon".
				Point3dCollection tenonPts = new();
				if (tenonDownJoint == "Tenon") {
					tenonPts.Add(Module1.AtPoint(pts[0], 0, 0, tenonZ));
					tenonPts.Add(Module1.AtPoint(tenonPts[0], 0, (Depth / SinBeta), 0));
					tenonPts.Add(Module1.AtPoint(tenonPts[1], -4, -4, 0));
					tenonPts.Add(Module1.AtPoint(tenonPts[0], -4, 0, 0));
					TenonDown = Module1.DrawElement(tenonPts, nearTW, "Tenon", "Down", "", "", YAngle, ZAngle, StartPoint.X, StartPoint.Y, StartPoint.Z);
					Module1.AddJoint(TimberId, TenonDown, Module1.Joint.Tenon);
					{
						Point3d pegPt = Module1.AtPoint(tenonPts[0], -1.75, 3, 0);
						pegPt = new Point3d(pegPt.X, pegPt.Y, deltaZ);
						PegCol.Add(Module1.DrawPeg(pegPt, pegR, postWidth + 1.5, "Peg", "", "", "", YAngle, ZAngle, StartPoint.X, StartPoint.Y, StartPoint.Z));
					}
				}

				// Far (Up) tenon.
				tenonPts.Clear();
				if (tenonUpJoint == "Tenon") {
					tenonPts.Add(Module1.AtPoint(pts[4], 0, 0, tenonZ * 2));
					tenonPts.Add(Module1.AtPoint(tenonPts[0], 4, 4, 0));
					tenonPts.Add(Module1.AtPoint(tenonPts[0], (Depth / SinBeta), 4, 0));
					tenonPts.Add(Module1.AtPoint(tenonPts[0], (Depth / SinBeta), 0, 0));
					TenonUp = Module1.DrawElement(tenonPts, farTW, "Tenon", "Up", "", "", YAngle, ZAngle, StartPoint.X, StartPoint.Y, StartPoint.Z);
					Module1.AddJoint(TimberId, TenonUp, Module1.Joint.Tenon);
					{
						Point3d pegPt = Module1.AtPoint(tenonPts[3], -3, 1.75, -(0.75 + z));
						pegPt = new Point3d(pegPt.X, pegPt.Y, deltaZ);
						PegCol.Add(Module1.DrawPeg(pegPt, pegR, postWidth + 1.5, "Peg", "", "", "", YAngle, ZAngle, StartPoint.X, StartPoint.Y, StartPoint.Z));
					}
				}

				// Persist
				Module1.PersistPegHandles(TimberId, PegCol);
				Module1.SaveDrawContext(TimberId, BuildContextJson());
				var ic = System.Globalization.CultureInfo.InvariantCulture;
				var xd = Module1.GetXdata(TimberId);
				xd.JointNearParams = string.Format(ic, "{{\"tenonWidth\":{0}}}", nearTW);
				xd.JointFarParams  = string.Format(ic, "{{\"tenonWidth\":{0}}}", farTW);
				Module1.SetXdata(TimberId, xd);
			}
		}

		private string BuildContextJson()
		{
			var c = System.Globalization.CultureInfo.InvariantCulture;
			// ZAngle += 90 and YAngle += 180 occur inside Draw() when OffsetType == Front.
			// Store the pre-mutation originals so regen can call Draw() cleanly.
			double ctxZAngle = ZAngle;
			double ctxYAngle = YAngle;
			if (Module1.Make3D && Module1.OffsetType == Module1.Front) {
				ctxZAngle -= 90;
				ctxYAngle -= 180;
			}
			return string.Format(c,
				"{{\"class\":\"BentBrace\"" +
				",\"startX\":{0},\"startY\":{1}" +
				",\"width\":{2},\"depth\":{3},\"length\":{4},\"postWidth\":{5}" +
				",\"zAngle\":{6},\"yAngle\":{7},\"xAngle\":{8}" +
				",\"make3D\":{9},\"offsetType\":{10}" +
				",\"swapEnds\":{11}}}",
				StartPoint.X, StartPoint.Y,
				Width, Depth, Length, postWidth,
				ctxZAngle, ctxYAngle, XAngle,
				Module1.Make3D ? "true" : "false",
				Module1.OffsetType,
				SwapEnds ? "true" : "false");
		}
	}
}
