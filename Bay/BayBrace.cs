using System.Collections.Generic;
using System;
//using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using TimberFrameSuite.Standards;

namespace TimberDraw
{

	public class BayBrace
	{
		public enum Hand
		{
			Left = 0,
			Right = 1
		}
		private double TenonWidth = 1.5;
		private double SinBeta = Math.Sqrt(2) / 2;
		public Point3d StartPoint;
		public double Width;
		public double Depth;
		public double Length;
		public double Peg1Length;
		public double Peg1Z;
		public double Peg2Length;
		public double Peg2Z;
		public double ZAngle;
		public double YAngle;
		public double XAngle;
		public string Designation = "";
		public ObjectId Timber;
		public ObjectId TenonUp;
		public ObjectId TenonDown;

		public List<ObjectId> PegCol = new();
		public void Draw()
		{
			if (Length > 0) {
				Point3dCollection pts = new();
				DoubleCollection bulge = new();
                //double tenonZ;
				pts.Add(new Point3d(StartPoint.X, StartPoint.Y - Length, StartPoint.Z));
				bulge.Add(0);
				pts.Add(new Point3d(StartPoint.X, StartPoint.Y - Length + (Depth / SinBeta), StartPoint.Z));
				bulge.Add(0);
				pts.Add(new Point3d(StartPoint.X + Length - (Depth / SinBeta), StartPoint.Y, StartPoint.Z));
				bulge.Add(0);
				pts.Add(new Point3d(StartPoint.X + Length, StartPoint.Y, StartPoint.Z));
				bulge.Add(0);
				pts.Add(new Point3d(StartPoint.X + Length - (Depth * SinBeta), StartPoint.Y - (Depth * SinBeta), StartPoint.Z));
				bulge.Add(0.0635);
				pts.Add(new Point3d(StartPoint.X + (Depth * SinBeta), StartPoint.Y - Length + (Depth * SinBeta), StartPoint.Z));
				bulge.Add(0);
				string sizeStr = (int)Width + "x" + (int)Depth + "x" + Module1.BuyLongFeet(Length);
				Timber = Module1.DrawBrace(Width, Depth, Length, pts, bulge, StartPoint.Z, Width, "Brace", "", "*",
				sizeStr, YAngle, ZAngle, StartPoint.X, StartPoint.Y, StartPoint.Z, XAngle, jointNear: "Tenon", jointFar: "Tenon");
				if (Module1.HasJoinery) {
                    //if (Module1.Make3D)
                    //{
                    //    tenonZ = 1.5;
                    //}
                    //else
                    //{
                    //    tenonZ = 0;
                    //}
					Point3dCollection tenonPts = new()
                    {
                        pts[0]
                    };
					tenonPts.Add(Module1.AtPoint(tenonPts[0], 0, (Depth / SinBeta), 0));
					tenonPts.Add(Module1.AtPoint(tenonPts[1], -4, -4, 0));
					tenonPts.Add(Module1.AtPoint(tenonPts[0], -4, 0, 0));
					TenonUp = Module1.DrawElement(tenonPts, TenonWidth, "Tenon", "Up", "", "", YAngle, ZAngle, StartPoint.X, StartPoint.Y,
					StartPoint.Z, XAngle);
                    Module1.AddJoint(Timber, TenonUp, Module1.Joint.Tenon);

					double pegR = TFGPegStandards.GetPresetForTenonThickness(TenonWidth).DiameterInches / 2;
					Point3d pegStartPt = Module1.AtPoint(tenonPts[0], -1.75, 3, 0);
					pegStartPt = new Point3d(pegStartPt.X, pegStartPt.Y, Peg1Z);
					PegCol.Add(Module1.DrawPeg(pegStartPt, pegR, Peg1Length, "Peg", "1", Designation, "", YAngle, ZAngle, StartPoint.X,
					StartPoint.Y, StartPoint.Z, XAngle));

					tenonPts.Clear();
					tenonPts.Add(pts[2]);
					tenonPts.Add(pts[3]);
					tenonPts.Add(Module1.AtPoint(pts[3], 0, 4, 0));
					tenonPts.Add(Module1.AtPoint(pts[2], 4, 4, 0));
					TenonDown = Module1.DrawElement(tenonPts, TenonWidth, "Tenon", "Down", "", "", YAngle, ZAngle, StartPoint.X, StartPoint.Y,
					StartPoint.Z, XAngle);
					Module1.AddJoint(Timber, TenonDown, Module1.Joint.Tenon);

					pegStartPt = Module1.AtPoint(tenonPts[1], -3, 1.75, 0);
					pegStartPt = new Point3d(pegStartPt.X, pegStartPt.Y, Peg2Z);
					PegCol.Add(Module1.DrawPeg(pegStartPt, pegR, Peg2Length, "Peg", "2", Designation, "", YAngle, ZAngle, StartPoint.X,
					StartPoint.Y, StartPoint.Z, XAngle));

				}
				// Phase 2: persist regeneration data
				Module1.PersistPegHandles(Timber, PegCol);
				Module1.SaveDrawContext(Timber, BuildContextJson());
			}
		}

		private string BuildContextJson()
		{
			var c = System.Globalization.CultureInfo.InvariantCulture;
			// Escape designation for JSON (replace backslash then double-quote)
			string desig = (Designation ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
			return string.Format(c,
				"{{\"class\":\"BayBrace\"" +
				",\"startX\":{0},\"startY\":{1},\"startZ\":{2}" +
				",\"width\":{3},\"depth\":{4},\"length\":{5}" +
				",\"peg1Length\":{6},\"peg1Z\":{7},\"peg2Length\":{8},\"peg2Z\":{9}" +
				",\"zAngle\":{10},\"yAngle\":{11},\"xAngle\":{12}" +
				",\"designation\":\"{13}\",\"make3D\":{14}}}",
				StartPoint.X, StartPoint.Y, StartPoint.Z,
				Width, Depth, Length,
				Peg1Length, Peg1Z, Peg2Length, Peg2Z,
				ZAngle, YAngle, XAngle,
				desig, Module1.Make3D ? "true" : "false");
		}

	}
}
