using System.Collections.Generic;
using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using TimberFrameSuite.Standards;

namespace TimberDraw
{

	public class KPTPost
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
		public ObjectId Tenon;

		public List<ObjectId> PegCol = new();
		public void Draw()
		{
			Point3dCollection pts = new()
            {
                StartPoint,
                new Point3d((Module1.Span / 2) + (Depth / 2), Module1.EaveHt, 0),
                new Point3d((Module1.Span / 2) + (Depth / 2), (((Module1.Span / 2) - (KPRafterDepth / (Math.Sin(Module1.Beta)) + (Depth / 2))) * Module1.Pitch) + Module1.EaveHt, 0),
                new Point3d(((Module1.Span / 2) + (Depth / 2)) - (3 * Math.Sin(Module1.Beta)), (((Module1.Span / 2) - (Depth / 2)) * Module1.Pitch) - ((KPRafterDepth - 3) / Math.Cos(Module1.Beta)) - (3 * Math.Cos(Module1.Beta)) + Module1.EaveHt, 0),
                new Point3d((Module1.Span / 2) + (Depth / 2), (((Module1.Span / 2) - (Depth / 2)) * Module1.Pitch) - ((KPRafterDepth - 3) / Math.Cos(Module1.Beta)) + Module1.EaveHt, 0),
                new Point3d((Module1.Span / 2) + (Depth / 2), (((Module1.Span / 2) - (Depth / 2)) * Module1.Pitch) + Module1.EaveHt, 0),
                new Point3d(Module1.Span / 2, ((Module1.Span / 2) * Module1.Pitch) + Module1.EaveHt, 0),
                new Point3d((Module1.Span / 2) - (Depth / 2), (((Module1.Span / 2) - (Depth / 2)) * Module1.Pitch) + Module1.EaveHt, 0),
                new Point3d((Module1.Span / 2) - (Depth / 2), (((Module1.Span / 2) - (Depth / 2)) * Module1.Pitch) - ((KPRafterDepth - 3) / Math.Cos(Module1.Beta)) + Module1.EaveHt, 0),
                new Point3d(((Module1.Span / 2) - (Depth / 2)) + (Math.Sin(Module1.Beta) * 3), (((Module1.Span / 2) - (Depth / 2)) * Module1.Pitch) - ((KPRafterDepth - 3) / Math.Cos(Module1.Beta)) - (3 * Math.Cos(Module1.Beta)) + Module1.EaveHt, 0),
                new Point3d((Module1.Span / 2) - (Depth / 2), (((Module1.Span / 2) - (KPRafterDepth / (Math.Sin(Module1.Beta)) + (Depth / 2))) * Module1.Pitch) + Module1.EaveHt, 0)
            };
			double kpostHeight = (Module1.Span / 2.0) * Module1.Pitch;
			string sizeStr = (int)Width + "x" + (int)Depth + "x" + Module1.BuyLongFeet(kpostHeight);
			Timber = Module1.DrawElement(pts, Width, Type, BentNumber, Designation, sizeStr, jointNear: "Butt", jointFar: "Tenon");
            Point3d _nearBodyPt = pts[0]; Point3d _farBodyPt = pts[pts.Count - 1];
			if (Module1.HasJoinery) {
				double tenonZ = 0;
				if (Module1.Make3D)
					tenonZ = StartPoint.Z + ((Width - 2) / 2);
				else
					tenonZ = 0;
				// Far-end tenon (at ridge): Origin at StartPoint (ridge face), projects -Y (into ridge beam), spans +X (Depth).
				Point3d tenonOrigin = new Point3d(StartPoint.X, StartPoint.Y, tenonZ);
				Tenon = JointFactory.Create(Module1.JointType.Tenon, new JointParams(
                    Module1.JointType.Tenon,
                    tenonOrigin,
                    new Vector3d(0, -1, 0),  // FaceNormal: into ridge beam
                    new Vector3d(1, 0, 0),   // LateralDir: along Depth (+X)
                    Width, Depth, TenonWidth, BentNumber, Designation));
                Module1.AddJoint(Timber, Tenon, Module1.Joint.Tenon);
				var peg = TFGPegStandards.GetPresetForTenonThickness(TenonWidth);
				double r = peg.DiameterInches / 2;
				double spacing = peg.CalculatedSpacingInches;
				PegCol.Add(Module1.DrawPeg(Module1.AtPoint(tenonOrigin, Depth / 2, -1.75, -0.75), r, Width + 1.5, "Peg", "", "", ""));
				PegCol.Add(Module1.DrawPeg(Module1.AtPoint(tenonOrigin, (Depth / 2) - spacing, -1.75, -0.75), r, Width + 1.5, "Peg", "", "", ""));
				PegCol.Add(Module1.DrawPeg(Module1.AtPoint(tenonOrigin, (Depth / 2) + spacing, -1.75, -0.75), r, Width + 1.5, "Peg", "", "", ""));
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
                "{{\"class\":\"KPTPost\",\"startX\":{0},\"startY\":{1},\"startZ\":{2},\"kPostWidth\":{3},\"kPostDepth\":{4},\"kpRafterDepth\":{5},\"postWidth\":{6},\"span\":{0},\"eaveHt\":{1},\"pitch\":{2},\"beta\":{3},\"make3D\":{4}}}",
                StartPoint.X, StartPoint.Y, StartPoint.Z, KPostWidth, KPostDepth, KPRafterDepth, postWidth, Module1.Span, Module1.EaveHt, Module1.Pitch, Module1.Beta, Module1.Make3D ? "true" : "false");
        }
		public void AddMortise(ObjectId MortiseId)
		{
            Module1.AddJoint(Timber, MortiseId, Module1.Joint.Mortise);
			Module1.DeleteJoint(MortiseId);
		}
	}
}
