using System.Collections.Generic;
using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using TimberFrameSuite.Standards;

namespace TimberDraw
{

	public class KPTStrutLeft
	{
		private double TenonWidth = 2;
		public Point3d StartPoint;
		public double Width;
		public double Depth;
		public double KPostWidth;
		public double KPostDepth;
		public double KPRafterDepth;
		public double postWidth;
		public string BentNumber;
		public string Designation;
		public string Type;
		public ObjectId Timber;
		public ObjectId TenonUp;
		public ObjectId TenonDown;

		public List<ObjectId> PegCol = new();
		public void Draw()
		{
			double strutXLength = ((Module1.Span / 2) - (KPRafterDepth / (Math.Sin(Module1.Beta))) - (6 / Module1.Pitch) - (KPostDepth / 2)) / 2;
			double z = 0;
			if (Module1.Make3D) {
				switch (Module1.OffsetType) {
					case Module1.Back:
						z = 0;
						break;
					case Module1.Centered:
						z = (KPostWidth - Width) / 2;
						break;
					case Module1.Front:
						z = (KPostWidth - Width);
						break;
				}
			}
			StartPoint = new Point3d(StartPoint.X, StartPoint.Y, z);
			Point3dCollection pts = new()
            {
                StartPoint
            };
			pts.Add(Module1.AtPoint(pts[0], strutXLength, -(strutXLength * Math.Tan(Module1.Beta)), 0));
			pts.Add(Module1.AtPoint(pts[1], 0, Depth / Math.Cos(Module1.Beta), 0));
			pts.Add(Module1.AtPoint(StartPoint, ((Depth / Math.Cos(Module1.Beta)) / 2) / Math.Tan(Module1.Beta), (Depth / Math.Cos(Module1.Beta)) / 2, 0));
			double strutLen = Math.Sqrt(Math.Pow(pts[1].X - pts[0].X, 2) + Math.Pow(pts[1].Y - pts[0].Y, 2));
			string sizeStr = (int)Width + "x" + (int)Depth + "x" + Module1.BuyLongFeet(strutLen);
			Timber = Module1.DrawElement(pts, Width, Type, BentNumber, Designation, sizeStr, jointNear: "Tenon", jointFar: "Tenon");
            Point3d _nearBodyPt = pts[0]; Point3d _farBodyPt = pts[pts.Count - 1];
			if (Module1.HasJoinery) {
				double tenonZ = 0;
				if (Module1.Make3D)
					tenonZ = StartPoint.Z + ((Width - 2) / 2);
				else
					tenonZ = 0;
				pts.Clear();
				pts.Add(new Point3d(StartPoint.X, StartPoint.Y, tenonZ));
				pts.Add(Module1.PolarPoint(pts[0], Module1.Beta, Depth / Math.Cos((Math.PI / 2) - (Module1.Beta * 2))));
				pts.Add(Module1.PolarPoint(pts[1], Module1.Beta + (Math.PI / 2) + ((Math.PI / 2) - (Module1.Beta * 2)), 4 / Math.Cos((Math.PI / 2) - (Module1.Beta * 2))));
				pts.Add(Module1.PolarPoint(pts[0], Module1.Beta + (Math.PI / 2), 4));
				TenonUp = Module1.DrawElement(pts, TenonWidth, "Tenon", "7", "");
                Module1.AddJoint(Timber, TenonUp, Module1.Joint.Tenon);
				double r = TFGPegStandards.GetPresetForTenonThickness(TenonWidth).DiameterInches / 2;
				double angle = 0;
				double offset = 0;
				if (Depth < 7){angle = 0.52807 + Math.Atan(Module1.Pitch);offset = 3.47312;}
				else{angle = 0.41241 + Math.Atan(Module1.Pitch);offset = 4.36607;}
				Point3d pt = Module1.PolarPoint(StartPoint, angle, offset);
				pt = Module1.AtPoint(pt, 0, 0, -0.75);
				PegCol.Add(Module1.DrawPeg(pt, r, Width + 1.5, "Peg", "", "", ""));
				pts.Clear();
				pts.Add(new Point3d((Module1.Span / 2.0) - 5, Module1.EaveHt + 6, tenonZ));
				pts.Add(Module1.AtPoint(pts[0], 4, 0, 0));
				pts.Add(Module1.AtPoint(pts[0], 4, (Depth / Math.Sin(Module1.Beta) - 4) * Math.Tan(Module1.Beta), 0));
				pts.Add(Module1.AtPoint(pts[0], 0, Depth / Math.Cos(Module1.Beta), 0));
				TenonDown = Module1.DrawElement(pts, TenonWidth, "Tenon", "8", "");
                Module1.AddJoint(Timber, TenonDown, Module1.Joint.Tenon);
				pt = pts[0];
				if (Depth < 7)
					pt = Module1.AtPoint(pt, 1.75, 3, -0.75);
				else
					pt = Module1.AtPoint(pt, 1.75, 4, -0.75);
				PegCol.Add(Module1.DrawPeg(pt, r, Width + 1.5, "Peg", "", "", ""));
			}
			// Phase 2: persist regeneration data
			Module1.PersistPegHandles(Timber, PegCol);
			Module1.SaveDrawContext(Timber, BuildContextJson());
            if (Module1.ShowEndMarkers) {
                double _mz = Module1.Make3D ? StartPoint.Z + (Width / 2) : 0;
                var em = new System.Collections.Generic.List<ObjectId>();
                em.Add(Module1.DrawEndMarker(new Point3d(_nearBodyPt.X, _nearBodyPt.Y, _mz), "N"));
                em.Add(Module1.DrawEndMarker(new Point3d(_farBodyPt.X, _farBodyPt.Y, _mz), "F"));
                Module1.PersistEndMarkerHandles(Timber, em);
            }
		}

        private string BuildContextJson()
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return string.Format(c,
                "{{\"class\":\"KPTStrutLeft\",\"startX\":{0},\"startY\":{1},\"kPostWidth\":{0},\"kPostDepth\":{1},\"kpRafterDepth\":{2},\"postWidth\":{3},\"span\":{4},\"eaveHt\":{5},\"pitch\":{6},\"beta\":{7},\"make3D\":{8},\"offsetType\":{9}}}",
                StartPoint.X, StartPoint.Y, KPostWidth, KPostDepth, KPRafterDepth, postWidth,Module1.Span, Module1.EaveHt, Module1.Pitch, Module1.Beta, Module1.Make3D ? "true" : "false", Module1.OffsetType);
        }
	}
}
