using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{

	public class QPTPostRight
	{
		private double TenonWidth = 2;
		public Point3d StartPoint;
		public double Width;
		public double Depth;
		public double RafterDepth;
		public double RafterWidth;
		public double QpostDepth;
		public string BentNumber;
		public string Designation;
		public string Type;
		public ObjectId TimberId;
		public ObjectId TenonUpId;

		public ObjectId TenonDownId;
		public void Draw()
		{
			Point3dCollection pts = new();
			double thirdSpan = Module1.Span / 3;
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
			pts.Add(new Point3d((thirdSpan * 2) - Depth, Module1.EaveHt, StartPoint.Z));
			pts.Add(new Point3d(thirdSpan * 2, Module1.EaveHt, StartPoint.Z));
			pts.Add(new Point3d(thirdSpan * 2, Module1.EaveHt + ((thirdSpan - (RafterDepth / Math.Sin(Module1.Beta))) * Module1.Pitch), StartPoint.Z));
			pts.Add(Module1.AtPoint(pts[2], -Depth, Depth * Math.Tan(Module1.Beta), 0));
			double postLen = Math.Sqrt(Math.Pow(pts[3].X - pts[0].X, 2) + Math.Pow(pts[3].Y - pts[0].Y, 2));
			string sizeStr = (int)Width + "x" + (int)Depth + "x" + Module1.BuyLongFeet(postLen);
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
				pts.Add(Module1.AtPoint(pts[0], 0, -4, 0));
				pts.Add(Module1.AtPoint(pts[1], Depth, 0, 0));
				pts.Add(Module1.AtPoint(pts[2], 0, 4, 0));
				TenonDownId = Module1.DrawElement(pts, 2, "Tenon", "10", "");
                Module1.AddJoint(TimberId, TenonDownId, Module1.Joint.Tenon);
				pts.Clear();
				pts.Add(new Point3d((thirdSpan * 2) - QpostDepth, Module1.EaveHt + ((thirdSpan - (RafterDepth / Math.Sin(Module1.Beta))) + QpostDepth) * Module1.Pitch, tenonZ));
				pts.Add(Module1.AtPoint(pts[0], Depth, -(Depth * Module1.Pitch), 0));
				pts.Add(Module1.PolarPoint(pts[1], Module1.rad(90), 4 / Math.Cos(Module1.Beta)));
				pts.Add(Module1.PolarPoint(pts[0], Math.Atan(Module1.Prun / Module1.Prise), 4));
				TenonUpId = Module1.DrawElement(pts, TenonWidth, "Tenon", "11", "");
                Module1.AddJoint(TimberId, TenonUpId, Module1.Joint.Tenon);
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
                "{{\"class\":\"QPTPostRight\",\"startX\":{0},\"startY\":{1},\"startZ\":{2},\"rafterDepth\":{3},\"rafterWidth\":{4},\"qpostDepth\":{5},\"offsetType\":{6},\"span\":{0},\"eaveHt\":{1},\"pitch\":{2},\"beta\":{3},\"make3D\":{4}}}",
                StartPoint.X, StartPoint.Y, StartPoint.Z, RafterDepth, RafterWidth, QpostDepth, Module1.OffsetType, Module1.Span, Module1.EaveHt, Module1.Pitch, Module1.Beta, Module1.Make3D ? "true" : "false");
        }
		public void AddMortise(ObjectId MortiseId)
		{
            Module1.AddJoint(TimberId, MortiseId, Module1.Joint.Mortise);
			Module1.DeleteJoint(MortiseId);
		}

	}
}
