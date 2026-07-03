using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{

	public class QPTRafterLeft
	{
		private double TenonWidth = 2;
		public Point3d StartPoint;
		public double Width;
		public double Depth;
		public string BentNumber;
		public string Designation;
		public string Type;
		public ObjectId TimberId;

		public ObjectId TenonId;
		public void Draw()
		{
			Point3dCollection pts = new()
            {
                StartPoint,
                new Point3d((Depth - 3) / Math.Sin(Module1.Beta), Module1.EaveHt, 0),
                new Point3d(((Depth - 3) / Math.Sin(Module1.Beta)) + (3 * Math.Sin(Module1.Beta)), Module1.EaveHt - (3 * Math.Cos(Module1.Beta)), 0),
                new Point3d(Module1.Span / 2, Module1.EaveHt + (((Module1.Span / 2) - (Depth / Math.Sin(Module1.Beta))) * Module1.Pitch), 0)
            };
			pts.Add(Module1.AtPoint(pts[3], -(1 * Math.Sin(Module1.Beta)), 1 * Math.Cos(Module1.Beta), 0));
			pts.Add(Module1.AtPoint(pts[4], ((((Depth / Math.Cos(Module1.Beta)) - (1 / Math.Cos(Module1.Beta))) / 2) / Module1.Pitch) + (1 * Math.Sin(Module1.Beta)), (((Depth / Math.Cos(Module1.Beta)) - (1 / Math.Cos(Module1.Beta))) / 2) + ((1 * Math.Sin(Module1.Beta)) * Module1.Pitch), 0));
			pts.Add(new Point3d((Module1.Span / 2), Module1.EaveHt + ((Module1.Span / 2) * Module1.Pitch), 0));
			string sizeStr = (int)Width + "x" + (int)Depth + "x" + Module1.BuyLongFeet((Module1.Span / 2.0) / Math.Cos(Module1.Beta));
			TimberId = Module1.DrawElement(pts, Width, Type, BentNumber, Designation, sizeStr, jointNear: "Butt", jointFar: "Tenon");
            Point3d _nearBodyPt = pts[0]; Point3d _farBodyPt = pts[pts.Count - 1];
			if (Module1.HasJoinery) {
				double tenonZ = 0;
				if (Module1.Make3D)
					tenonZ = StartPoint.Z + ((Width - 2) / 2);
				else
					tenonZ = 0;
				pts.Clear();
				pts.Add(Module1.AtPoint(StartPoint, 4, 0, tenonZ));
				pts.Add(Module1.AtPoint(pts[0], 0, -4, 0));
				pts.Add(Module1.AtPoint(pts[0], ((10 / Math.Sin(Module1.Beta)) - 4) - (4 / Math.Tan(Module1.Beta)), -4, 0));
				pts.Add(new Point3d((7 / Math.Sin(Module1.Beta)) + (3 * Math.Sin(Module1.Beta)), Module1.EaveHt - (3 * Math.Cos(Module1.Beta)), tenonZ));
				pts.Add(new Point3d(7 / Math.Sin(Module1.Beta), Module1.EaveHt, tenonZ));
				TenonId = Module1.DrawElement(pts, TenonWidth, "Tenon", "9", "");
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
                "{{\"class\":\"QPTRafterLeft\",\"startX\":{0},\"startY\":{1},\"startZ\":{2},\"span\":{0},\"eaveHt\":{1},\"pitch\":{2},\"beta\":{3},\"make3D\":{4}}}",
                StartPoint.X, StartPoint.Y, StartPoint.Z, Module1.Span, Module1.EaveHt, Module1.Pitch, Module1.Beta, Module1.Make3D ? "true" : "false");
        }
		public void AddMortise(ObjectId MortiseId)
		{
            Module1.AddJoint(TimberId, MortiseId, Module1.Joint.Mortise);
			Module1.DeleteJoint(MortiseId);
		}
	}
}
