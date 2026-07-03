using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{

	public class TrussGirt
	{
		//private double TenonWidth = 2;
		public double Depth;
		public double Width;
		public double RafterDepth;
		public string Type;
		public string BentNumber;
		public string Designation;
		public ObjectId Timber;

		public ObjectId Mortise;
		public void Draw()
		{
			Point3dCollection pts = new()
            {
                new Point3d(0, Module1.EaveHt - Depth, 0),
                new Point3d(Module1.Span, Module1.EaveHt - Depth, 0),
                new Point3d(Module1.Span, Module1.EaveHt, 0),
                new Point3d(Module1.Span - ((RafterDepth - 3) / Math.Sin(Module1.Beta)), Module1.EaveHt, 0),
                new Point3d(Module1.Span - ((RafterDepth - 3) / Math.Sin(Module1.Beta)) - (3 * Math.Sin(Module1.Beta)), Module1.EaveHt - (3 * Math.Cos(Module1.Beta)), 0),
                new Point3d(Module1.Span - (RafterDepth / Math.Sin(Module1.Beta)), Module1.EaveHt, 0),
                new Point3d(RafterDepth / Math.Sin(Module1.Beta), Module1.EaveHt, 0),
                new Point3d(((RafterDepth - 3) / Math.Sin(Module1.Beta)) + (3 * Math.Sin(Module1.Beta)), Module1.EaveHt - (3 * Math.Cos(Module1.Beta)), 0),
                new Point3d((RafterDepth - 3) / Math.Sin(Module1.Beta), Module1.EaveHt, 0),
                new Point3d(0, Module1.EaveHt, 0)
            };
			string sizeStr = (int)Width + "x" + (int)Depth + "x" + Module1.BuyLongFeet(Module1.Span);
			Timber = Module1.DrawElement(pts, Width, Type, BentNumber, Designation, sizeStr, jointNear: "Butt", jointFar: "Butt");
			// Phase 2: persist regeneration data (TrussGirt has no pegs)
			Module1.SaveDrawContext(Timber, BuildContextJson());
            // End markers: "N" at near (left) face, "F" at far (right) face.
            // TrussGirt body spans from X=0 to X=Span at Y=EaveHt-Depth.
            if (Module1.ShowEndMarkers) {
                var em = new System.Collections.Generic.List<ObjectId>();
                em.Add(Module1.DrawEndMarker(new Point3d(0, Module1.EaveHt - Depth / 2, 0), "N"));
                em.Add(Module1.DrawEndMarker(new Point3d(Module1.Span, Module1.EaveHt - Depth / 2, 0), "F"));
                Module1.PersistEndMarkerHandles(Timber, em);
            }
		}

        private string BuildContextJson()
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return string.Format(c,
                "{{\"class\":\"TrussGirt\",\"depth\":{0},\"width\":{1},\"rafterDepth\":{2},\"span\":{3},\"eaveHt\":{4},\"beta\":{5}}}",
                Depth, Width, RafterDepth, Module1.Span, Module1.EaveHt, Module1.Beta);
        }
		public void AddMortise(ObjectId MortiseId)
		{
			Module1.AddJoint(Timber, MortiseId, Module1.Joint.Mortise);
			Module1.DeleteJoint(MortiseId);
		}

	}
}
