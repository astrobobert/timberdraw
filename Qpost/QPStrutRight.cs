using System.Collections.Generic;
using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using TimberFrameSuite.Standards;

namespace TimberDraw
{

	public class QPStrutRight
	{
		private double TenonWidth = 2;
		public Point3d StartPoint;
		public double Width;
		public double Depth;
		public double QPRafterWidth;
		public string BentNumber;
		public string Designation;
		public string Type;
		public ObjectId TimberId;
		public ObjectId TenonUp;
		public ObjectId TenonDown;
		public Point3d[] UpTenonPts;
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
                new Point3d((2 * thirdSpan) + Module1.B, Module1.TOH + Module1.B, StartPoint.Z),
                new Point3d(((2 * thirdSpan) + Module1.B) - (8.4853 - ((Module1.B * 8.4853) / (thirdSpan - 10))), Module1.TOH + Module1.B + (Math.Sin(Math.Atan(Module1.Pitch)) * (2 * (8.4853 / (2 * Math.Sin(Math.PI - (Math.Atan(Module1.Pitch) + Math.Atan(1)))))) * Math.Sin(Math.Atan(1))), StartPoint.Z),
                new Point3d(thirdSpan * 2, Module1.TOH + 8.4853, StartPoint.Z)
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
				pts.Add(Module1.PolarPoint(pts[0], (Math.PI / 2), Depth / Math.Cos((Math.PI / 4))));
				pts.Add(Module1.PolarPoint(pts[1], Math.PI * 1.25, 4 / Math.Cos(Math.PI / 4)));
				pts.Add(Module1.PolarPoint(pts[0], Math.PI, 4));
				TenonDown = Module1.DrawElement(pts, TenonWidth, "Tenon", "3", "");
                Module1.AddJoint(TimberId, TenonDown, Module1.Joint.Tenon);
				DownTenonPts   = new Point3d[] { pts[0], pts[1], pts[2], pts[3] };
				DownTenonWidth = TenonWidth;
				PegIt(pts[0], (int)Module1.JointEnd.Up);
				pts.Clear();
				pts.Add(new Point3d(((2 * thirdSpan) + Module1.B) - (8.4853 - ((Module1.B * 8.4853) / (thirdSpan - 10))), Module1.TOH + Module1.B + (Math.Sin(Math.Atan(Module1.Pitch)) * (2 * (8.4853 / (2 * Math.Sin(Math.PI - (Math.Atan(Module1.Pitch) + Math.Atan(1)))))) * Math.Sin(Math.Atan(1))), tenonZ));
				pts.Add(new Point3d((2 * thirdSpan) + Module1.B, Module1.TOH + Module1.B, tenonZ));
				pts.Add(Module1.PolarPoint(pts[1], (Math.PI / 2) - Module1.Beta, 4));
				pts.Add(Module1.PolarPoint(pts[0], Math.PI / 4, 4 / Math.Cos(((Math.PI / 2) - (Module1.Beta * 2)) / 2)));
				TenonUp = Module1.DrawElement(pts, TenonWidth, "Tenon", "4", "");
                Module1.AddJoint(TimberId, TenonUp, Module1.Joint.Tenon);
				UpTenonPts   = new Point3d[] { pts[0], pts[1], pts[2], pts[3] };
				UpTenonWidth = TenonWidth;
				PegIt(pts[1], (int)Module1.JointEnd.Down);
			}
			// Phase 2: persist regeneration data
			Module1.PersistPegHandles(TimberId, PegCol);
			Module1.SaveDrawContext(TimberId, BuildContextJson());
            // End markers: "N" at near (rafter contact) end, "F" at far (QP face) end.
            if (Module1.ShowEndMarkers) {
                double _mz = Module1.Make3D ? StartPoint.Z + (Width / 2) : 0;
                var em = new System.Collections.Generic.List<ObjectId>();
                em.Add(Module1.DrawEndMarker(new Point3d(StartPoint.X, StartPoint.Y, _mz), "N"));
                em.Add(Module1.DrawEndMarker(new Point3d((2 * thirdSpan) + Module1.B, Module1.TOH + Module1.B, _mz), "F"));
                Module1.PersistEndMarkerHandles(TimberId, em);
            }
		}

        private string BuildContextJson()
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return string.Format(c,
                "{{\"class\":\"QPStrutRight\",\"startX\":{0},\"startY\":{1},\"qpRafterWidth\":{2},\"span\":{3},\"make3D\":{4},\"offsetType\":{5},\"toh\":{6},\"pitch\":{7},\"beta\":{8},\"b\":{9}}}",
                StartPoint.X, StartPoint.Y, QPRafterWidth, Module1.Span, Module1.Make3D ? "true" : "false", Module1.OffsetType, Module1.TOH, Module1.Pitch, Module1.Beta, Module1.B);
        }
		private void PegIt(Point3d Pt, int WhichEnd)
		{
			double r = TFGPegStandards.GetPresetForTenonThickness(TenonWidth).DiameterInches / 2;
			switch (WhichEnd) {
				case (int)Module1.JointEnd.Up:
					double angle = 0;
					double offset = 0;
					if (Depth < 7){angle = Math.PI - 0.52807 - Math.Atan(Module1.Pitch);offset = 3.47312;}
					else{angle = Math.PI - 0.41241 - Math.Atan(Module1.Pitch);offset = 4.36607;}
					Pt = Module1.PolarPoint(Pt, angle, offset);
					Pt = Module1.AtPoint(Pt, 0, 0, -(tenonZ + 0.75));
					PegCol.Add(Module1.DrawPeg(Pt, r, QPRafterWidth + 1.5, "Peg", "", "", ""));
					break;
				case (int)Module1.JointEnd.Down:
					if (Depth < 7)
						Pt = Module1.AtPoint(Pt, -1.75, 3, -(tenonZ + 0.75));
					else
						Pt = Module1.AtPoint(Pt, -1.75, 4, -(tenonZ + 0.75));
					PegCol.Add(Module1.DrawPeg(Pt, r, QPRafterWidth + 1.5, "Peg", "", "", ""));
					break;
			}
		}
	}
}
