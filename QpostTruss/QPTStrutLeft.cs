using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{

	public class QPTStrutLeft
	{
		private double TenonWidth = 2;
		public Point3d StartPoint;
		public double Width;
		public double Depth;
		public double RafterWidth;
		public double RafterDepth;
		public string BentNumber;
		public string Designation;
		public string Type;
		public ObjectId TimberId;
		public ObjectId TenonUpId;

		public ObjectId TenonDownId;
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
						z = (RafterWidth - Width) / 2;
						break;
					case Module1.Front:
						z = (RafterWidth - Width);
						break;
				}
			}
			StartPoint = new Point3d(StartPoint.X, StartPoint.Y, z);
			pts.Add(StartPoint);
			pts.Add(new Point3d((Module1.Span / 3), Module1.EaveHt + Depth, StartPoint.Z));
			pts.Add(new Point3d((Module1.Span / 3), Module1.EaveHt + Depth + (Depth / (Math.Cos(Math.Atan(Module1.Pitch)))), StartPoint.Z));
			pts.Add(new Point3d((((Module1.Span / 3) - ((RafterDepth / (Math.Sin(Module1.Beta))) + (Depth / Module1.Pitch) + 0)) / 2) + ((RafterDepth / (Math.Sin(Module1.Beta))) + (Depth / Module1.Pitch)) + ((Depth / Math.Sin(Math.Atan(Module1.Pitch)) / 2)), ((((Module1.Span / 3) - ((RafterDepth / (Math.Sin(Module1.Beta))) + (Depth / Module1.Pitch) + 0)) / 2) * Module1.Pitch) + Module1.EaveHt + Depth + ((Depth / Math.Cos(Math.Atan(Module1.Pitch)) / 2)), StartPoint.Z));
			double strutLen = Math.Sqrt(Math.Pow(pts[3].X - pts[0].X, 2) + Math.Pow(pts[3].Y - pts[0].Y, 2));
			string sizeStr = (int)Width + "x" + (int)Depth + "x" + Module1.BuyLongFeet(strutLen);
			TimberId = Module1.DrawElement(pts, Width, Type, BentNumber, Designation, sizeStr, jointNear: "Tenon", jointFar: "Tenon");
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
				TenonUpId = Module1.DrawElement(pts, TenonWidth, "Tenon", "LStrut", "UP");
				Module1.AddJoint(TimberId, TenonUpId, Module1.Joint.Tenon);
				pts.Clear();
				pts.Add(new Point3d((Module1.Span / 3.0), Module1.EaveHt + 6, tenonZ));
				pts.Add(Module1.AtPoint(pts[0], 4, 0, 0));
				pts.Add(Module1.AtPoint(pts[0], 4, (6 / Math.Sin(Module1.Beta) - 4) * Math.Tan(Module1.Beta), 0));
				pts.Add(Module1.AtPoint(pts[0], 0, Depth / Math.Cos(Module1.Beta), 0));
				TenonDownId = Module1.DrawElement(pts, TenonWidth, "Tenon", "LStrut", "DN");
                Module1.AddJoint(TimberId, TenonDownId, Module1.Joint.Tenon);
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
                "{{\"class\":\"QPTStrutLeft\",\"startX\":{0},\"startY\":{1},\"rafterWidth\":{0},\"rafterDepth\":{1},\"span\":{2},\"eaveHt\":{3},\"pitch\":{4},\"beta\":{5},\"make3D\":{6},\"offsetType\":{7}}}",
                StartPoint.X, StartPoint.Y, RafterWidth, RafterDepth,Module1.Span, Module1.EaveHt, Module1.Pitch, Module1.Beta, Module1.Make3D ? "true" : "false", Module1.OffsetType);
        }
	}
}
