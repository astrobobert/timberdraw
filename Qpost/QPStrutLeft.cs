using System.Collections.Generic;
using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using TimberFrameSuite.Standards;

namespace TimberDraw
{

	public class QPStrutLeft
	{
		private double TenonWidth = 2;
		public Point3d StartPoint;
		public double Width;
		public double Depth;
		public double QPRafterWidth;
		public double postDepth;
		public string BentNumber;
		public string Designation;
		public string Type;
		public ObjectId TimberId;
		public ObjectId TenonUp;
		public ObjectId TenonDown;
		public Point3d[] UpTenonPts;    // captured after Draw() for BentNetwork polygon registration
		public double    UpTenonWidth;
		public Point3d[] DownTenonPts;
		public double    DownTenonWidth;
		public List<ObjectId> PegCol = new();

		private double tenonZ;
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
                StartPoint,
                new Point3d(thirdSpan, Module1.TOH, StartPoint.Z),
                new Point3d(thirdSpan, Module1.TOH + 8.4853, StartPoint.Z),
                new Point3d((thirdSpan - Module1.B) + ((6 / (Math.Sin(Math.Atan(1)))) / (Module1.Pitch + 1)), Module1.TOH + Module1.B + (Math.Sin(Math.Atan(Module1.Pitch)) * (2 * (8.4853 / (2 * Math.Sin(Math.PI - (Math.Atan(Module1.Pitch) + Math.Atan(1)))))) * Math.Sin(Math.Atan(1))), StartPoint.Z)
            };
			double strutLen = Math.Sqrt(Math.Pow(pts[3].X - pts[0].X, 2) + Math.Pow(pts[3].Y - pts[0].Y, 2));
			string sizeStr = (int)Width + "x" + (int)Depth + "x" + Module1.BuyLongFeet(strutLen);
			TimberId = Module1.DrawElement(pts, Width, Type, BentNumber, Designation, sizeStr, jointNear: "Tenon", jointFar: "Tenon");
			if (Module1.HasJoinery) {
				if (Module1.Make3D)
					tenonZ = StartPoint.Z + ((Width - 2) / 2);
				else
					tenonZ = 0;
				pts.Clear();
				pts.Add(new Point3d(StartPoint.X, StartPoint.Y, tenonZ));
				pts.Add(Module1.PolarPoint(pts[0], Module1.Beta, Depth / Math.Cos((Math.PI / 4) - Module1.Beta)));
				pts.Add(Module1.PolarPoint(pts[1], Math.PI * 0.75, 4 / Math.Cos((Math.PI / 2) - (Module1.Beta * 2))));
				pts.Add(Module1.PolarPoint(pts[0], Module1.Beta + (Math.PI / 2), 4));
				TenonUp = Module1.DrawElement(pts, TenonWidth, "Tenon", "5", "");
                Module1.AddJoint(TimberId, TenonUp, Module1.Joint.Tenon);
				UpTenonPts   = new Point3d[] { pts[0], pts[1], pts[2], pts[3] };
				UpTenonWidth = TenonWidth;
				PegIt(pts[0], (int)Module1.JointEnd.Up);
				pts.Clear();
				pts.Add(new Point3d(thirdSpan, Module1.TOH, tenonZ));
				pts.Add(Module1.AtPoint(pts[0], 4, 0, 0));
				pts.Add(Module1.AtPoint(new Point3d(thirdSpan, Module1.TOH + 8.4853, tenonZ), 4, -4, 0));
				pts.Add(Module1.AtPoint(pts[2], -4, 4, 0));
				TenonDown = Module1.DrawElement(pts, TenonWidth, "Tenon", "6", "");
                Module1.AddJoint(TimberId, TenonDown, Module1.Joint.Tenon);
				DownTenonPts   = new Point3d[] { pts[0], pts[1], pts[2], pts[3] };
				DownTenonWidth = TenonWidth;
				PegIt(pts[0], (int)Module1.JointEnd.Down);
			}
			// Phase 2: persist regeneration data
			Module1.PersistPegHandles(TimberId, PegCol);
			Module1.SaveDrawContext(TimberId, BuildContextJson());
            // End markers: "N" at near (rafter contact) end, "F" at far (QP face) end.
            if (Module1.ShowEndMarkers) {
                double _mz = Module1.Make3D ? StartPoint.Z + (Width / 2) : 0;
                var em = new System.Collections.Generic.List<ObjectId>();
                em.Add(Module1.DrawEndMarker(new Point3d(StartPoint.X, StartPoint.Y, _mz), "N"));
                em.Add(Module1.DrawEndMarker(new Point3d(thirdSpan, Module1.TOH, _mz), "F"));
                Module1.PersistEndMarkerHandles(TimberId, em);
            }
		}

        private string BuildContextJson()
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return string.Format(c,
                "{{\"class\":\"QPStrutLeft\",\"startX\":{0},\"startY\":{1},\"qpRafterWidth\":{2},\"postDepth\":{3},\"span\":{4},\"make3D\":{5},\"offsetType\":{6},\"toh\":{7},\"pitch\":{8},\"beta\":{9},\"b\":{10}}}",
                StartPoint.X, StartPoint.Y,QPRafterWidth, postDepth, Module1.Span,Module1.Make3D ? "true" : "false", Module1.OffsetType,Module1.TOH, Module1.Pitch, Module1.Beta, Module1.B);
        }
		private void PegIt(Point3d Pt, int WhichEnd)
		{
			double r = TFGPegStandards.GetPresetForTenonThickness(TenonWidth).DiameterInches / 2;
			switch (WhichEnd) {
				case (int)Module1.JointEnd.Up:
					double angle = 0;
					double offset = 0;
					if (Depth < 7){angle = 0.52807 + Math.Atan(Module1.Pitch);offset = 3.47312;}
					else{angle = 0.41241 + Math.Atan(Module1.Pitch);offset = 4.36607;}
					Pt = Module1.PolarPoint(Pt, angle, offset);
					Pt = Module1.AtPoint(Pt, 0, 0, -(tenonZ + 0.75));
					PegCol.Add(Module1.DrawPeg(Pt, r, QPRafterWidth + 1.5, "Peg", "", "", ""));
					break;
				case (int)Module1.JointEnd.Down:
					if (Depth < 7)
						Pt = Module1.AtPoint(Pt, 1.75, 3, -(tenonZ + 0.75));
					else
						Pt = Module1.AtPoint(Pt, 1.75, 4, -(tenonZ + 0.75));
					PegCol.Add(Module1.DrawPeg(Pt, r, QPRafterWidth + 1.5, "Peg", "", "", ""));
					break;
			}
		}
	}
}
