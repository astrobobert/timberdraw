using System.Collections.Generic;
using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using TimberFrameSuite.Standards;

namespace TimberDraw
{

	public class KPTVertStrutLeft
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
			Point3dCollection pts = new();
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
			pts.Add(StartPoint);
			pts.Add(Module1.AtPoint(pts[0], Depth, 0, 0));
			pts.Add(Module1.AtPoint(pts[1], 0, (strutXLength * Module1.Pitch) + 6, 0));
			pts.Add(Module1.AtPoint(pts[2], -Depth, -(Depth * (Math.Tan(Module1.Beta))), 0));
			double strutLen = Math.Sqrt(Math.Pow(pts[3].X - pts[0].X, 2) + Math.Pow(pts[3].Y - pts[0].Y, 2));
			string sizeStr = (int)Width + "x" + (int)Depth + "x" + Module1.BuyLongFeet(strutLen);
			Timber = Module1.DrawElement(pts, Width, Type, BentNumber, Designation, sizeStr, jointNear: "Tenon", jointFar: "Tenon");
            Point3d _nearBodyPt = pts[0]; Point3d _farBodyPt = pts[pts.Count - 1];
			if (Module1.HasJoinery) {
				double tenonZ = 0;
				if (Module1.Make3D)
					tenonZ = StartPoint.Z + ((Width - 2) / 2);
				else
					tenonZ = 0;
				// Near-end tenon (at foot/plate): Origin at StartPoint, projects -Y (into plate), spans +X (Depth).
				Point3d tenonDownOrigin = new Point3d(StartPoint.X, StartPoint.Y, tenonZ);
				TenonDown = JointFactory.Create(Module1.JointType.Tenon, new JointParams(
                    Module1.JointType.Tenon,
                    tenonDownOrigin,
                    new Vector3d(0, -1, 0),  // FaceNormal: into plate/post below
                    new Vector3d(1, 0, 0),   // LateralDir: along Depth (+X)
                    Width, Depth, TenonWidth, BentNumber, Designation));
                Module1.AddJoint(Timber, TenonDown, Module1.Joint.Tenon);
				double r = TFGPegStandards.GetPresetForTenonThickness(TenonWidth).DiameterInches / 2;
				PegCol.Add(Module1.DrawPeg(Module1.AtPoint(tenonDownOrigin, Depth / 2, -1.75, -0.75), r, Width + 1.5, "Peg", "", "", ""));
				pts.Clear();
				pts.Add(Module1.AtPoint(StartPoint, 0, ((strutXLength * Module1.Pitch) + 6) - (Depth * (Math.Tan(Module1.Beta))), tenonZ));
				pts.Add(Module1.AtPoint(pts[0], Depth, Depth * Module1.Pitch, 0));
				pts.Add(Module1.PolarPoint(pts[1], Module1.Beta + (Math.PI / 2), 4));
				pts.Add(Module1.PolarPoint(pts[0], Module1.rad(90), 4 / Math.Cos(Module1.Beta)));
				TenonUp = Module1.DrawElement(pts, TenonWidth, "Tenon", "4", "");
                Module1.AddJoint(Timber, TenonUp, Module1.Joint.Tenon);

				double angle = 0;
				double offset = 0;
				if (Depth < 7){angle = 1.0427 + Math.Atan(Module1.Pitch) + 1.5708;offset = 3.47312;}
				else{angle = 1.15839 + Math.Atan(Module1.Pitch) + 1.5708;offset = 4.36607;}
				Point3d pt = pts[1];
				pt = Module1.PolarPoint(pt, angle, offset);
				pt = Module1.AtPoint(pt, 0, 0, -0.75);
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
                "{{\"class\":\"KPTVertStrutLeft\",\"startX\":{0},\"startY\":{1},\"kPostWidth\":{0},\"kPostDepth\":{1},\"kpRafterDepth\":{2},\"postWidth\":{3},\"span\":{4},\"eaveHt\":{5},\"pitch\":{6},\"beta\":{7},\"make3D\":{8},\"offsetType\":{9}}}",
                StartPoint.X, StartPoint.Y, KPostWidth, KPostDepth, KPRafterDepth, postWidth,Module1.Span, Module1.EaveHt, Module1.Pitch, Module1.Beta, Module1.Make3D ? "true" : "false", Module1.OffsetType);
        }
	}
}
