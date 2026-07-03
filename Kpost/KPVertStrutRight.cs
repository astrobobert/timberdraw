using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{
	public class KPVertStrutRight
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
		public ObjectId TenonDownId;  // near end: foot / plate below
		public ObjectId TenonUpId;    // far end:  rafter contact
		public JointParams NearJointParamsDrawn;   // params used to draw TenonDownId (JF)
		public string NearJointType = "Tenon";     // "Butt" skips foot tenon BoolUnite
		public string FarJointType  = "Tenon";     // "Butt" skips oblique rafter tenon
		public Dictionary<string, double> NearParams = new();
		public Dictionary<string, double> FarParams  = new();
		public List<ObjectId> PegCol = new();
		// Pts captured during Draw() for BentNetwork Polygon edge registration.
		public Point3d[] FarTenonPts;    // 4 local-coordinate pts of the rafter-contact tenon
		public double    FarTenonWidth;  // extrusion depth (tenonWidth scalar)

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
			StartPoint = new Point3d(StartPoint.X, StartPoint.Y, z);
			pts.Add(StartPoint);
			pts.Add(new Point3d(((Span - ((postDepth * 2) + KPostDepth)) * 0.75) + ((postDepth + KPostDepth) + Depth), Module1.TOG, StartPoint.Z));
			pts.Add(new Point3d(((Span - ((postDepth * 2) + KPostDepth)) * 0.75) + ((postDepth + KPostDepth) + Depth), Module1.TOH + (((halfSpan - (postDepth + (KPostDepth / 2))) / 2) * Module1.Pitch) - (Depth * Module1.Pitch), StartPoint.Z));
			pts.Add(new Point3d(((Span - ((postDepth * 2) + KPostDepth)) * 0.75) + (postDepth + KPostDepth), Module1.TOH + (((halfSpan - (postDepth + (KPostDepth / 2))) / 2) * Module1.Pitch), StartPoint.Z));
			double strutLen = Math.Sqrt(Math.Pow(pts[3].X - pts[0].X, 2) + Math.Pow(pts[3].Y - pts[0].Y, 2));
			string sizeStr = (int)Width + "x" + (int)Depth + "x" + Module1.BuyLongFeet(strutLen);
			TimberId = Module1.DrawElement(pts, Width, Type, BentNumber, Designation, sizeStr,
				jointNear: NearJointType, jointFar: FarJointType);
            // Capture near/far body endpoints before pts is reused for tenon geometry.
            Point3d _nearBodyPt = pts[0];
            Point3d _farBodyPt  = pts[3];

			double tenonZ = Module1.Make3D ? StartPoint.Z + ((Width - 2) / 2) : 0;

			// Far-end params (oblique rafter tenon, only tenonWidth used)
			double farTW   = FarParams.TryGetValue("tenonWidth",  out var ftw) ? ftw : 2.0;
			double farRel  = FarParams.TryGetValue("tenonRelish", out var frl) ? frl : 0.0;
			var ic = System.Globalization.CultureInfo.InvariantCulture;
			string nearParamsJson = "{}";

			// Near (Down) tenon: only when NearJointType="Tenon".
			if (NearJointType == "Tenon")
			{
				double nearTW  = NearParams.TryGetValue("tenonWidth",   out var ntw) ? ntw : 2.0;
				double nearRel = NearParams.TryGetValue("tenonRelish",  out var nrl) ? nrl : 0.0;
				double nearHD  = NearParams.TryGetValue("housingDepth", out var nhd) ? nhd : 0.0;
				var nearP = new JointParams(Module1.JointType.Tenon,
					new Point3d(StartPoint.X, StartPoint.Y, tenonZ),
					new Vector3d(0, -1, 0),   // FaceNormal: into plate below
					new Vector3d(1,  0, 0),   // LateralDir: along Depth (+X)
					Width, Depth, nearTW, BentNumber, Designation,
					nearRel, 0.0, true, nearHD);
				var nearRes = JointFactory.CreateWithPegs(Module1.JointType.Tenon, nearP);
				TenonDownId = nearRes.JointId;
				NearJointParamsDrawn = nearP;
				PegCol.AddRange(nearRes.Pegs);
				Module1.AddJoint(TimberId, TenonDownId, Module1.Joint.Tenon);
				nearParamsJson = string.Format(ic,
					"{{\"tenonWidth\":{0},\"tenonRelish\":{1},\"housingDepth\":{2}}}",
					nearTW, nearRel, nearHD);
			}

			// Far (Up) tenon: oblique rafter contact face. Keep DrawElement geometry.
			if (FarJointType == "Tenon") {
				pts.Clear();
				var fp0 = Module1.AtPoint(new Point3d(StartPoint.X, StartPoint.Y, tenonZ), 0, (((halfSpan - (postDepth + 5)) / 2) * Module1.Pitch) + 6, 0);
				var fp1 = Module1.AtPoint(fp0, Depth, -(Depth * Module1.Pitch), 0);
				var fp2 = Module1.PolarPoint(fp1, Module1.rad(90), 4 / Math.Cos(Module1.Beta));
				var fp3 = Module1.PolarPoint(fp0, Math.Atan(Module1.Prun / Module1.Prise), 4);
				pts.Add(fp0); pts.Add(fp1); pts.Add(fp2); pts.Add(fp3);
				FarTenonPts   = new[] { fp0, fp1, fp2, fp3 };
				FarTenonWidth = farTW;
				TenonUpId = Module1.DrawElement(pts, farTW, "Tenon", "10", "");
				Module1.AddJoint(TimberId, TenonUpId, Module1.Joint.Tenon);
				// Peg for far (Up) end:
				{
					double angle, offset;
					if (Depth < 7) { angle = (Math.PI * 1.5) + 0.52807 + (1.5708 - Math.Atan(Module1.Pitch)); offset = 3.47312; }
					else           { angle = (Math.PI * 1.5) + 0.41241 + (1.5708 - Math.Atan(Module1.Pitch)); offset = 4.36607; }
					Point3d pegPt = Module1.PolarPoint(pts[0], angle, offset);
					pegPt = new Point3d(pegPt.X, pegPt.Y, -0.75);
					PegCol.Add(Module1.DrawPeg(pegPt, 0.5, KPostWidth + 1.5, "Peg", "", "", ""));
				}
			}

			// Persist
			Module1.PersistPegHandles(TimberId, PegCol);
			Module1.SaveDrawContext(TimberId, BuildContextJson());
			var xd = Module1.GetXdata(TimberId);
			xd.JointNearParams = nearParamsJson;
			xd.JointFarParams = string.Format(ic,
				"{{\"tenonWidth\":{0},\"tenonRelish\":{1}}}",
				farTW, farRel);
			Module1.SetXdata(TimberId, xd);
            // End markers: "N" at near (foot) body corner, "F" at far (rafter-contact) body corner.
            if (Module1.ShowEndMarkers) {
                var em = new List<ObjectId>();
                em.Add(Module1.DrawEndMarker(new Point3d(_nearBodyPt.X, _nearBodyPt.Y, tenonZ), "N"));
                em.Add(Module1.DrawEndMarker(new Point3d(_farBodyPt.X,  _farBodyPt.Y,  tenonZ), "F"));
                Module1.PersistEndMarkerHandles(TimberId, em);
            }
		}

		private string BuildContextJson()
		{
			var c = System.Globalization.CultureInfo.InvariantCulture;
			return string.Format(c,
				"{{\"class\":\"KPVertStrutRight\"" +
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
