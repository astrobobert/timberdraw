using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{
	public class KPStrutRight
	{
		public Point3d StartPoint;
		public double Width;
		public double Depth;
		public double KPostWidth;
		public double KPostDepth;
		public double postDepth;
		public double EaveHt;
		public double Span;
		public double KpostRafterSitDepth;
		public string BentNumber;
		public string Designation;
		public string Type;
		public ObjectId TimberId;
		public ObjectId TenonUpId;    // near end: rafter contact face
		public ObjectId TenonDownId;  // far end:  king post right face
		public string NearJointType = "Tenon";  // "Butt" skips rafter-contact tenon
		public string FarJointType  = "Tenon";  // "Butt" skips king-post tenon
		public Dictionary<string, double> NearParams = new();
		public Dictionary<string, double> FarParams  = new();
		public List<ObjectId> PegCol = new();
		// Pts captured during Draw() for BentNetwork Polygon edge registration.
		public Point3d[] NearTenonPts;    // 4 local-coordinate pts of the rafter-contact tenon
		public double    NearTenonWidth;  // extrusion depth (tenonWidth scalar)

		public void Draw()
		{
			Point3dCollection pts = new();
			double halfSpan = Span / 2;
			double z = 0;
			if (Module1.Make3D) {
				switch (Module1.OffsetType) {
					case Module1.Back:     z = 0; break;
					case Module1.Centered: z = (KPostWidth - Width) / 2; break;
					case Module1.Front:    z = (KPostWidth - Width); break;
				}
			}
			Point3d _StartPoint = new Point3d(StartPoint.X, StartPoint.Y, z);
			pts.Add(_StartPoint);
			pts.Add(new Point3d((((Span - ((postDepth * 2) + KPostDepth)) * 0.75) + (postDepth + KPostDepth)) - ((Depth / Math.Sin(Math.Atan(Module1.Pitch)) / 2)), (((halfSpan - (postDepth + (KPostDepth / 2))) / 2) * Module1.Pitch) + Module1.TOH + ((Depth / Math.Cos(Math.Atan(Module1.Pitch)) / 2)), _StartPoint.Z));
			pts.Add(new Point3d(halfSpan + (KPostDepth / 2), Module1.TOH + (Depth / (Math.Cos(Math.Atan(Module1.Pitch)))), _StartPoint.Z));
			pts.Add(new Point3d(halfSpan + (KPostDepth / 2), Module1.TOH, _StartPoint.Z));
			double strutLen = Math.Sqrt(Math.Pow(pts[3].X - pts[0].X, 2) + Math.Pow(pts[3].Y - pts[0].Y, 2));
			string sizeStr = (int)Width + "x" + (int)Depth + "x" + Module1.BuyLongFeet(strutLen);
			TimberId = Module1.DrawElement(pts, Width, Type, BentNumber, Designation, sizeStr, jointNear: NearJointType, jointFar: FarJointType);

			// Per-end params (defaults: tenonWidth=2, relish=0)
			double nearTW  = NearParams.TryGetValue("tenonWidth",  out var ntw) ? ntw : 2.0;
			double nearRel = NearParams.TryGetValue("tenonRelish", out var nrl) ? nrl : 0.0;
			double farTW   = FarParams.TryGetValue("tenonWidth",   out var ftw) ? ftw : 2.0;
			double farRel  = FarParams.TryGetValue("tenonRelish",  out var frl) ? frl : 0.0;

			double tenonZ = Module1.Make3D ? _StartPoint.Z + ((Width - 2) / 2) : 0;
			const double pegR = 0.5;   // 1" peg for 2" tenon

			// Near (Up) tenon: oblique face at rafter contact (_StartPoint).
			if (NearJointType == "Tenon") {
				pts.Clear();
				var np0 = new Point3d(_StartPoint.X, _StartPoint.Y, tenonZ);
				var np1 = Module1.PolarPoint(np0, Math.Atan(Module1.Prun / Module1.Prise), 4);
				var np2 = Module1.PolarPoint(np1, Math.Atan(Module1.Prun / Module1.Prise) + (Math.PI / 2), Depth / Math.Cos((Math.PI / 2) - (Module1.Beta * 2)) - 4 * Math.Tan((Math.PI / 2) - (Module1.Beta * 2)));
				var np3 = Module1.PolarPoint(np0, Math.PI - Module1.Beta, Depth / Math.Cos((Math.PI / 2) - (Module1.Beta * 2)));
				pts.Add(np0); pts.Add(np1); pts.Add(np2); pts.Add(np3);
				NearTenonPts   = new[] { np0, np1, np2, np3 };
				NearTenonWidth = nearTW;
				TenonUpId = Module1.DrawElement(pts, nearTW, "Tenon", "3", "");
				Module1.AddJoint(TimberId, TenonUpId, Module1.Joint.Tenon);
				{
					double angle, offset;
					if (Depth < 7) { angle = Math.PI - 0.52807 - Math.Atan(Module1.Pitch); offset = 3.47312; }
					else           { angle = Math.PI - 0.41241 - Math.Atan(Module1.Pitch); offset = 4.36607; }
					Point3d pegPt = Module1.PolarPoint(pts[0], angle, offset);
					pegPt = Module1.AtPoint(pegPt, 0, 0, -(tenonZ + 0.75));
					PegCol.Add(Module1.DrawPeg(pegPt, pegR, KPostWidth + 1.5, "Peg", "", "", ""));
				}
			}

			// Far (Down) tenon: oblique face at king post right face.
			if (FarJointType == "Tenon") {
				pts.Clear();
				pts.Add(new Point3d(halfSpan + (KPostDepth / 2), Module1.TOH, tenonZ));
				pts.Add(Module1.AtPoint(pts[0], 0, Depth / Math.Cos(Module1.Beta), 0));
				pts.Add(Module1.AtPoint(pts[0], -4, (Depth / Math.Sin(Module1.Beta) - 4) * Math.Tan(Module1.Beta), 0));
				pts.Add(Module1.AtPoint(pts[0], -4, 0, 0));
				TenonDownId = Module1.DrawElement(pts, farTW, "Tenon", "4", "");
				Module1.AddJoint(TimberId, TenonDownId, Module1.Joint.Tenon);
				{
					Point3d pegPt = pts[0];
					if (Depth < 7) pegPt = Module1.AtPoint(pegPt, -1.75, 3, -(tenonZ + 0.75));
					else           pegPt = Module1.AtPoint(pegPt, -1.75, 4, -(tenonZ + 0.75));
					PegCol.Add(Module1.DrawPeg(pegPt, pegR, KPostWidth + 1.5, "Peg", "", "", ""));
				}
			}

			// Persist
			Module1.PersistPegHandles(TimberId, PegCol);
			Module1.SaveDrawContext(TimberId, BuildContextJson());
			var ic = System.Globalization.CultureInfo.InvariantCulture;
			var xd = Module1.GetXdata(TimberId);
			xd.JointNearParams = string.Format(ic, "{{\"tenonWidth\":{0},\"tenonRelish\":{1}}}", nearTW, nearRel);
			xd.JointFarParams  = string.Format(ic, "{{\"tenonWidth\":{0},\"tenonRelish\":{1}}}", farTW,  farRel);
			Module1.SetXdata(TimberId, xd);
            // End markers: "N" at near (rafter contact) face, "F" at far (king post right face).
            if (Module1.ShowEndMarkers) {
                var em = new List<ObjectId>();
                em.Add(Module1.DrawEndMarker(new Point3d(_StartPoint.X, _StartPoint.Y, tenonZ), "N"));
                em.Add(Module1.DrawEndMarker(new Point3d(halfSpan + (KPostDepth / 2), Module1.TOH, tenonZ), "F"));
                Module1.PersistEndMarkerHandles(TimberId, em);
            }
		}

		private string BuildContextJson()
		{
			var c = System.Globalization.CultureInfo.InvariantCulture;
			return string.Format(c,
				"{{\"class\":\"KPStrutRight\"" +
				",\"startX\":{0},\"startY\":{1}" +
				",\"kPostWidth\":{2},\"kPostDepth\":{3},\"postDepth\":{4}" +
				",\"span\":{5},\"kpostRafterSitDepth\":{6}" +
				",\"make3D\":{7},\"offsetType\":{8}" +
				",\"toh\":{9},\"pitch\":{10},\"beta\":{11}" +
				",\"prun\":{12},\"prise\":{13}}}",
				StartPoint.X, StartPoint.Y,
				KPostWidth, KPostDepth, postDepth,
				Span, KpostRafterSitDepth,
				Module1.Make3D ? "true" : "false", Module1.OffsetType,
				Module1.TOH, Module1.Pitch, Module1.Beta,
				Module1.Prun, Module1.Prise);
		}
	}
}
