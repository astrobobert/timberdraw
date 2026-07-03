using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{
	public class QPPostRight
	{
		public Point3d StartPoint;
		public double Width;
		public double Depth;
		public double GirtWidth;
		public double postDepth;
		public string BentNumber;
		public string Designation;
		public string Type;
		public ObjectId TimberId;
		public ObjectId TenonUpId;    // far end: oblique rafter contact (DrawElement)
		public ObjectId TenonDownId;  // near end: foot tenon into tie beam (JF)
		public JointParams NearJointParamsDrawn;
		public Point3d[]   FarTenonPts;
		public double      FarTenonWidth;
		public string NearJointType = "Tenon";
		public Dictionary<string, double> NearParams = new();
		public Dictionary<string, double> FarParams  = new();
		public List<ObjectId> PegCol = new();

		public void Draw()
		{
			double thirdSpan = Module1.Span / 3;
			double z = 0;
			if (Module1.Make3D) {
				switch (Module1.OffsetType) {
					case Module1.Back:     z = 0; break;
					case Module1.Centered: z = (GirtWidth - Width) / 2; break;
					case Module1.Front:    z = (GirtWidth - Width); break;
				}
			}
			StartPoint = new Point3d(StartPoint.X, StartPoint.Y, z);
			Point3dCollection pts = new()
            {
                StartPoint,
                new Point3d(thirdSpan * 2, Module1.TOG, StartPoint.Z),
                new Point3d(thirdSpan * 2, Module1.TOH + ((thirdSpan - postDepth) * Module1.Pitch), StartPoint.Z),
                new Point3d((thirdSpan * 2) - Depth, Module1.TOH + ((thirdSpan - postDepth) * Module1.Pitch) + (Depth * Module1.Pitch), StartPoint.Z)
            };
			double postLen = Math.Sqrt(Math.Pow(pts[3].X - pts[0].X, 2) + Math.Pow(pts[3].Y - pts[0].Y, 2));
			string sizeStr = (int)Width + "x" + (int)Depth + "x" + Module1.BuyLongFeet(postLen);
			TimberId = Module1.DrawElement(pts, Width, Type, BentNumber, Designation, sizeStr,
				jointNear: NearJointType, jointFar: "Tenon");
            // Capture near/far body endpoints before pts is reused for tenon geometry.
            Point3d _nearBodyPt = pts[0];
            Point3d _farBodyPt  = pts[3];

			double tenonZ = Module1.Make3D ? StartPoint.Z + ((Width - 2) / 2) : 0;
			var ic = System.Globalization.CultureInfo.InvariantCulture;
			string nearParamsJson = "{}";

			// Near (Down) foot tenon: orthogonal, projects -Y into tie beam. JF handles pegs.
			if (NearJointType == "Tenon")
			{
				double nearTW  = NearParams.TryGetValue("tenonWidth",   out var ntw) ? ntw : 2.0;
				double nearRel = NearParams.TryGetValue("tenonRelish",  out var nrl) ? nrl : 0.0;
				double nearHD  = NearParams.TryGetValue("housingDepth", out var nhd) ? nhd : 0.0;
				var nearP = new JointParams(Module1.JointType.Tenon,
					new Point3d(StartPoint.X, StartPoint.Y, tenonZ),
					new Vector3d(0, -1, 0),   // FaceNormal: into tie beam
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

			// Far (Up) rafter-contact tenon: oblique face, keep DrawElement geometry.
			{
				double farTW = FarParams.TryGetValue("tenonWidth", out var ftw) ? ftw : 2.0;
				pts.Clear();
				pts.Add(new Point3d((thirdSpan * 2) - Depth, Module1.TOH + ((thirdSpan - postDepth) * Module1.Pitch) + (Depth * Module1.Pitch), tenonZ));
				pts.Add(Module1.AtPoint(pts[0], Depth, -(Depth * Module1.Pitch), 0));
				pts.Add(Module1.PolarPoint(pts[1], Module1.rad(90), 4 / Math.Cos(Module1.Beta)));
				pts.Add(Module1.PolarPoint(pts[0], (Math.PI / 2) - Module1.Beta, 4));
				TenonUpId = Module1.DrawElement(pts, farTW, "Tenon", "11", "");
				Module1.AddJoint(TimberId, TenonUpId, Module1.Joint.Tenon);
				FarTenonPts   = new Point3d[] { pts[0], pts[1], pts[2], pts[3] };
				FarTenonWidth = farTW;
				// Peg for far (Up) end:
				{
					double angle = Depth < 7
						? (Math.PI * 1.5) + 0.52807 + (1.5708 - Math.Atan(Module1.Pitch))
						: (Math.PI * 1.5) + 0.41241 + (1.5708 - Math.Atan(Module1.Pitch));
					double offset = Depth < 7 ? 3.47312 : 4.36607;
					Point3d pegPt = Module1.PolarPoint(pts[0], angle, offset);
					pegPt = new Point3d(pegPt.X, pegPt.Y, -0.75);
					PegCol.Add(Module1.DrawPeg(pegPt, 0.5, Width + 1.5, "Peg", "", "", ""));
				}
			}

			// Persist
			Module1.PersistPegHandles(TimberId, PegCol);
			Module1.SaveDrawContext(TimberId, BuildContextJson());
			var xd = Module1.GetXdata(TimberId);
			xd.JointNearParams = nearParamsJson;
			xd.JointFarParams  = "{}";
			Module1.SetXdata(TimberId, xd);
            // End markers: "N" at near (foot/tie beam) body corner, "F" at far (rafter-contact) body corner.
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
                "{{\"class\":\"QPPostRight\",\"startX\":{0},\"startY\":{1},\"girtWidth\":{2},\"postDepth\":{3},\"span\":{4},\"make3D\":{5},\"offsetType\":{6},\"tog\":{7},\"toh\":{8},\"pitch\":{9},\"beta\":{10}}}",
                StartPoint.X, StartPoint.Y, GirtWidth, postDepth, Module1.Span,
                Module1.Make3D ? "true" : "false", Module1.OffsetType,
                Module1.TOG, Module1.TOH, Module1.Pitch, Module1.Beta);
        }

		public void AddMortise(ObjectId MortiseId)
		{
            Module1.AddJoint(TimberId, MortiseId, Module1.Joint.Mortise);
			Module1.DeleteJoint(MortiseId);
		}
	}
}
