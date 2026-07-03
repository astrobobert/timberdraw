using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{

	public class Ridge
	{
		public double HousingDepth = 1;
		public ObjectId TimberId;
		public ObjectId RidgeExtension1_ID;
		public ObjectId RidgeExtension2Id;
		public ObjectId Housing1Id;
		public ObjectId Housing1aId;
		public ObjectId Housing2Id;
        public ObjectId Housing2aId;
		public double Depth;
		public double Width;
		public double postWidth;
		public double Length;
		public bool Make3d;
		public void Draw()
		{
			double z = 0;
			if (Make3d) {
				z = postWidth;
			} else {
				z = 0;
			}
			Point3dCollection ridge_pts = new()
            {
                new Point3d((Module1.Span / 2) - (Width / 2), Module1.EaveHt + (((Module1.Span / 2) - (Width / 2)) * Module1.Pitch), z),
                new Point3d((Module1.Span / 2) - (Width / 2), (Module1.EaveHt + (((Module1.Span / 2) - (Width / 2)) * Module1.Pitch)) - (Depth - 1), z),
                new Point3d((Module1.Span / 2) + (Width / 2), (Module1.EaveHt + (((Module1.Span / 2) - (Width / 2)) * Module1.Pitch)) - (Depth - 1), z),
                new Point3d((Module1.Span / 2) + (Width / 2), Module1.EaveHt + (((Module1.Span / 2) - (Width / 2)) * Module1.Pitch), z),
                new Point3d((Module1.Span / 2) + (Width / 2) - (1 / Module1.Pitch), (Module1.EaveHt + 1) + (((Module1.Span / 2) - (Width / 2)) * Module1.Pitch), z),
                new Point3d((Module1.Span / 2) - (Width / 2) + (1 / Module1.Pitch), (Module1.EaveHt + 1) + (((Module1.Span / 2) - (Width / 2)) * Module1.Pitch), z)
            };
			string sizeStr = (int)Width + "x" + (int)Depth + "x" + Module1.BuyLongFeet(Length);
			TimberId = Module1.DrawElement(ridge_pts, Length, "Ridge", Convert.ToString(Module1.Arabic2roman(Properties.Settings.Default.BentNumber)), "", sizeStr, jointNear: "Housing", jointFar: "Housing");
            // Capture near/far body endpoints (cross-section corner, extrusion axis = Z).
            Point3d _nearBodyPt = ridge_pts[0]; Point3d _farBodyPt = ridge_pts[2];

			if (Module1.HasJoinery) {
				Point3dCollection ridge_extension1_pts = new()
                {
                    new Point3d((Module1.Span / 2) - (Width / 2), Module1.EaveHt + (((Module1.Span / 2) - (Width / 2)) * Module1.Pitch), z),
                    new Point3d((Module1.Span / 2) - (Width / 2), (Module1.EaveHt + (((Module1.Span / 2) - (Width / 2)) * Module1.Pitch)) - (Depth - 1), z),
                    new Point3d((Module1.Span / 2) + (Width / 2), (Module1.EaveHt + (((Module1.Span / 2) - (Width / 2)) * Module1.Pitch)) - (Depth - 1), z),
                    new Point3d((Module1.Span / 2) + (Width / 2), Module1.EaveHt + (((Module1.Span / 2) - (Width / 2)) * Module1.Pitch), z),
                    new Point3d((Module1.Span / 2) + (Width / 2) - (1 / Module1.Pitch), (Module1.EaveHt + 1) + (((Module1.Span / 2) - (Width / 2)) * Module1.Pitch), z),
                    new Point3d((Module1.Span / 2) - (Width / 2) + (1 / Module1.Pitch), (Module1.EaveHt + 1) + (((Module1.Span / 2) - (Width / 2)) * Module1.Pitch), z)
                };
				RidgeExtension1_ID = Module1.DrawElement(ridge_extension1_pts, -HousingDepth, "Tenon", "1", "");
				Module1.AddJoint(TimberId, RidgeExtension1_ID, Module1.Joint.Tenon);
				
				Point3dCollection ridge_housing1_pts = new()
                {
                    new Point3d((Module1.Span / 2) - (Width / 2), Module1.EaveHt + (((Module1.Span / 2) - (Width / 2)) * Module1.Pitch), z),
                    new Point3d((Module1.Span / 2) - (Width / 2), (Module1.EaveHt + (((Module1.Span / 2) - (Width / 2)) * Module1.Pitch)) - (Depth - 1), z),
                    new Point3d((Module1.Span / 2) + (Width / 2), (Module1.EaveHt + (((Module1.Span / 2) - (Width / 2)) * Module1.Pitch)) - (Depth - 1), z),
                    new Point3d((Module1.Span / 2) + (Width / 2), Module1.EaveHt + (((Module1.Span / 2) - (Width / 2)) * Module1.Pitch), z),
                    new Point3d((Module1.Span / 2), Module1.EaveHt + ((Module1.Span / 2) * Module1.Pitch), z)
                };
                Housing1Id = Module1.DrawElement(ridge_housing1_pts, -HousingDepth, "Mortise", "1", "");
				Housing1aId = Module1.DrawElement(ridge_housing1_pts, -HousingDepth, "Mortise", "1", "");
				
				Point3dCollection ridge_extension2_pts = new()
				{
					new Point3d((Module1.Span / 2) - (Width / 2), Module1.EaveHt + (((Module1.Span / 2) - (Width / 2)) * Module1.Pitch), Length + postWidth),
					new Point3d((Module1.Span / 2) - (Width / 2), (Module1.EaveHt + (((Module1.Span / 2) - (Width / 2)) * Module1.Pitch)) - (Depth - 1), Length + postWidth),
					new Point3d((Module1.Span / 2) + (Width / 2), (Module1.EaveHt + (((Module1.Span / 2) - (Width / 2)) * Module1.Pitch)) - (Depth - 1), Length + postWidth),
					new Point3d((Module1.Span / 2) + (Width / 2), Module1.EaveHt + (((Module1.Span / 2) - (Width / 2)) * Module1.Pitch), Length + postWidth),
					new Point3d((Module1.Span / 2) + (Width / 2) - (1 / Module1.Pitch), (Module1.EaveHt + 1) + (((Module1.Span / 2) - (Width / 2)) * Module1.Pitch), Length + postWidth),
					new Point3d((Module1.Span / 2) - (Width / 2) + (1 / Module1.Pitch), (Module1.EaveHt + 1) + (((Module1.Span / 2) - (Width / 2)) * Module1.Pitch), Length + postWidth)
				};
				RidgeExtension2Id = Module1.DrawElement(ridge_extension2_pts, HousingDepth, "Tenon", "2", "");
                Module1.AddJoint(TimberId, RidgeExtension2Id, Module1.Joint.Tenon);
				
				Point3dCollection ridge_housing2_pts = new()
				{
					new Point3d((Module1.Span / 2) - (Width / 2), Module1.EaveHt + (((Module1.Span / 2) - (Width / 2)) * Module1.Pitch), Length + postWidth),
					new Point3d((Module1.Span / 2) - (Width / 2), (Module1.EaveHt + (((Module1.Span / 2) - (Width / 2)) * Module1.Pitch)) - (Depth - 1), Length + postWidth),
					new Point3d((Module1.Span / 2) + (Width / 2), (Module1.EaveHt + (((Module1.Span / 2) - (Width / 2)) * Module1.Pitch)) - (Depth - 1), Length + postWidth),
					new Point3d((Module1.Span / 2) + (Width / 2), Module1.EaveHt + (((Module1.Span / 2) - (Width / 2)) * Module1.Pitch), Length + postWidth),
					new Point3d((Module1.Span / 2), Module1.EaveHt + ((Module1.Span / 2) * Module1.Pitch), Length + postWidth),
				};
				Housing2Id  = Module1.DrawElement(ridge_housing2_pts, HousingDepth, "Mortise", "2", "");
				Housing2aId = Module1.DrawElement(ridge_housing2_pts, HousingDepth, "Mortise", "3", "");
			}
			// Phase 2: persist regeneration data (Ridge has no pegs)
			Module1.SaveDrawContext(TimberId, BuildContextJson());
            // End markers: "N" near current bent, "F" toward far bent (Z direction).
            if (Module1.ShowEndMarkers) {
                double _midY = (_nearBodyPt.Y + _farBodyPt.Y) / 2;
                var em = new System.Collections.Generic.List<ObjectId>();
                em.Add(Module1.DrawEndMarker(new Point3d(_nearBodyPt.X, _midY, z), "N"));
                em.Add(Module1.DrawEndMarker(new Point3d(_farBodyPt.X, _midY, Module1.Make3D ? z + Length : 0), "F"));
                Module1.PersistEndMarkerHandles(TimberId, em);
            }
		}

        private string BuildContextJson()
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return string.Format(c,
                "{{\"class\":\"Ridge\",\"postWidth\":{0},\"length\":{1},\"make3d\":{2},\"span\":{3},\"eaveHt\":{4},\"pitch\":{5}}}",
                postWidth, Length, Make3d ? "true" : "false",
                Module1.Span, Module1.EaveHt, Module1.Pitch);
        }
		public void AddMortise(ObjectId MortiseId)
		{
            Module1.AddJoint(TimberId, MortiseId, Module1.Joint.Mortise);
			Module1.DeleteJoint(MortiseId);
		}

	}
}
