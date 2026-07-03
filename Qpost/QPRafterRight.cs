using System.Collections.Generic;
using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{

	public class QPRafterRight
	{
		public Point3d StartPoint;
		public double Width;
		public double Depth;
		public double postDepth;
		public string BentNumber;
		public string Designation;
		public string Type;
		public ObjectId TimberId;
		public ObjectId Tenon;              // near (foot/eave) tenon into right post
		public ObjectId SeatPeakId;         // far (peak/ridge) housing polygon solid
		public JointParams NearJointParamsDrawn;  // params used to draw Tenon
		public JointParams FarJointParamsDrawn;   // params used to draw SeatPeakId

		// "Tenon" draws the foot tenon (default); "Butt" skips it.
		// "Polygon" draws the ridge housing polygon (default); "Butt" skips it.
		// "Shoulder" is kept as a compat alias for "Polygon" during regen of old DrawContexts.
		public string NearJointType = "Tenon";
		public string FarJointType  = "Polygon";

		// sitDepth: repurposed as housing perpendicular depth (was shoulder sit depth = 3.0).
		// Stored in DrawContext as "sitDepth"; overrideable via FarParams["hdepth"].
		public double sitDepth = 1.0;

		// Housing polygon pts and width -- stored for BentNetwork registration in QPBent.
		public Point3d[] FarTenonPts;
		public double    FarTenonWidth;

		// Per-end joint params: near = foot/eave (Tenon), far = peak/ridge (Shoulder).
		public Dictionary<string, double> NearParams = new();
		public Dictionary<string, double> FarParams  = new();

		public List<ObjectId> PegCol = new();

		public void Draw()
		{
			// Body: 4-pt plumb-cut parallelogram, face-to-face with posts.
			// Right rafter: xFoot > xPeak, so vertices are reversed for CCW +Z extrusion.
			double xFoot    = Module1.Span - postDepth;
			double xPeak    = Module1.Span / 2.0;
			double yTopFoot = Module1.EaveHt + postDepth * Module1.Pitch;
			double yTopPeak = Module1.EaveHt + (Module1.Span / 2.0) * Module1.Pitch;
			double yBotFoot = yTopFoot - Module1.PlumbLength;
			double yBotPeak = yTopPeak - Module1.PlumbLength;

			// Peak body: top face ends at the shared corner with QPRafterLeft's peak-bottom.
			// xPeakTop = Span/2 + PlumbLength/(2*Pitch), same formula as QPRafterLeft's xPeakBot.
			// yPeakTopLap uses the left-rafter-bottom formula to express the shared corner explicitly.
			double lapExt      = Module1.PlumbLength / (2.0 * Module1.Pitch);
			double xPeakTop    = xPeak + lapExt;
			double yPeakTopLap = Module1.EaveHt + xPeakTop * Module1.Pitch - Module1.PlumbLength;

			// [foot-top, peak-top, peak-bot, foot-bot] -- CCW when xFoot > xPeak
			Point3dCollection pts = new()
			{
				new Point3d(xFoot,    yTopFoot,    0),   // [0] foot top
				new Point3d(xPeakTop, yPeakTopLap, 0),   // [1] peak top (shared corner with QPRafterLeft)
				new Point3d(xPeak,    yBotPeak,    0),   // [2] peak bottom (at centerline Span/2)
				new Point3d(xFoot,    yBotFoot,    0)    // [3] foot bottom
			};
			string sizeStr = (int)Width + "x" + (int)Depth + "x" + Module1.BuyLongFeet((Module1.Span / 2.0) / Math.Cos(Module1.Beta));
			TimberId = Module1.DrawElement(pts, Width, Type, BentNumber, Designation, sizeStr,
				jointNear: NearJointType, jointFar: FarJointType);

			double tenonZ = Module1.Make3D ? StartPoint.Z + ((Width - 2) / 2) : 0;
			var ic = System.Globalization.CultureInfo.InvariantCulture;

			// Near end (foot/eave): tenon + housing into right post.
			string nearParamsJson = "{}";
			if (NearJointType == "Tenon")
			{
				double nearTenonWidth   = NearParams.TryGetValue("tenonWidth",   out var ntw) ? ntw : 2.0;
				double nearTopRelish    = NearParams.TryGetValue("tenonRelish",  out var ntr) ? ntr : 0.0;
				double nearHousingDepth = NearParams.TryGetValue("housingDepth", out var nhd) ? nhd : 1.5;
				var nearP = new JointParams(Module1.JointType.Tenon,
					new Point3d(xFoot, yBotFoot, tenonZ),
					new Vector3d(1, 0, 0),    // FaceNormal: into right post
					new Vector3d(0, 1, 0),    // LateralDir: up the plumb face
					Width, Module1.PlumbLength, nearTenonWidth,
					BentNumber, Designation,
					nearTopRelish, 0.0, true, nearHousingDepth,
					pitch: Module1.Pitch);
				var nearRes = JointFactory.CreateWithPegs(Module1.JointType.Tenon, nearP);
				Tenon = nearRes.JointId;
				NearJointParamsDrawn = nearP;
				PegCol.AddRange(nearRes.Pegs);
				Module1.AddJoint(TimberId, Tenon, Module1.Joint.Tenon);
				Module1.DeleteJoint(Tenon);   // standalone solid no longer needed; geometry is in rafter body
				nearParamsJson = string.Format(ic,
					"{{\"tenonWidth\":{0},\"tenonRelish\":{1},\"housingDepth\":{2}}}",
					nearTenonWidth, nearTopRelish, nearHousingDepth);
			}

			// Far end (peak/ridge): full-width housing polygon, 1" deep perpendicular to rafter.
			// QPRafterRight is the housing giver; SeatPeakId is applied to QPRafterLeft in QPBent.
			// Do NOT AddJoint(Tenon) here -- housing is a mortise for QPRafterLeft, not this rafter.
			string farParamsJson = "{}";
			double hDepth = FarParams.TryGetValue("hdepth", out var fhd) ? fhd : sitDepth;
			if (FarJointType == "Polygon" || FarJointType == "Shoulder")
			{
				double lap_   = Module1.PlumbLength / (2.0 * Module1.Pitch);
				double sinB   = Math.Sin(Module1.Beta), cosB = Math.Cos(Module1.Beta);
				// Full-width housing: extrusion must start at timber base Z, not tenon center.
				double baseZ  = Module1.Make3D ? StartPoint.Z : 0.0;
				// v0 = right rafter peak-bottom corner (plumb face, at xPeak = Span/2)
				Point3d v0 = new Point3d(xPeak, yBotPeak, baseZ);
				// v1 = v0 moved hDepth inches perpendicular to rafter surface (into QPRafterLeft body)
				Point3d v1 = new Point3d(xPeak - sinB * hDepth, yBotPeak + cosB * hDepth, baseZ);
				// v3 = shared corner: QPRafterRight peak-top = QPRafterLeft peak-bottom
				double  xv3 = xPeak + lap_;
				double  yv3 = yBotPeak + lap_ * Module1.Pitch;
				Point3d v3  = new Point3d(xv3, yv3, baseZ);
				// v2 = intersection of (slope+Pitch through v1) and (slope-Pitch through v3)
				double  xv2 = (yv3 + Module1.Pitch * xv3 - v1.Y + Module1.Pitch * v1.X)
				              / (2.0 * Module1.Pitch);
				Point3d v2  = new Point3d(xv2, v1.Y + Module1.Pitch * (xv2 - v1.X), baseZ);
				// Reverse order: v0,v1,v2,v3 is CW in XY plane; DrawElement requires CCW for +Z extrusion.
			FarTenonPts   = new[] { v3, v2, v1, v0 };
				FarTenonWidth = Width;
				var farP = JointParams.ForPolygon(FarTenonPts, Width, BentNumber, Designation);
				FarJointParamsDrawn = farP;
				var farRes = JointFactory.CreateWithPegs(Module1.JointType.Polygon, farP);
				SeatPeakId = farRes.JointId;
				if (!SeatPeakId.IsNull)
					Module1.AddJoint(TimberId, SeatPeakId, Module1.Joint.Tenon);  // unite housing step into right rafter body
				farParamsJson = string.Format(ic, "{{\"hdepth\":{0}}}", hDepth);
			}

			var xd = Module1.GetXdata(TimberId);
			xd.JointNearParams = nearParamsJson;
			xd.JointFarParams  = farParamsJson;
			Module1.SetXdata(TimberId, xd);

			Module1.PersistPegHandles(TimberId, PegCol);
			Module1.SaveDrawContext(TimberId, BuildContextJson());
            // End markers: "N" at near (foot/eave) face center, "F" at far (peak/ridge) face center.
            if (Module1.ShowEndMarkers) {
                var em = new System.Collections.Generic.List<ObjectId>();
                em.Add(Module1.DrawEndMarker(new Point3d(xFoot, (yBotFoot + yTopFoot) / 2, tenonZ), "N"));
                em.Add(Module1.DrawEndMarker(new Point3d(xPeak, (yBotPeak + yTopPeak) / 2, tenonZ), "F"));
                Module1.PersistEndMarkerHandles(TimberId, em);
            }
		}

		private string BuildContextJson()
		{
			var c = System.Globalization.CultureInfo.InvariantCulture;
			return string.Format(c,
				"{{\"class\":\"QPRafterRight\"" +
				",\"startX\":{0},\"startY\":{1},\"startZ\":{2}" +
				",\"postDepth\":{3},\"sitDepth\":{4}" +
				",\"span\":{5},\"eaveHt\":{6},\"pitch\":{7},\"beta\":{8},\"toh\":{9}" +
				",\"make3D\":{10}}}",
				StartPoint.X, StartPoint.Y, StartPoint.Z,
				postDepth, sitDepth,
				Module1.Span, Module1.EaveHt, Module1.Pitch, Module1.Beta, Module1.TOH,
				Module1.Make3D ? "true" : "false");
		}

		public void AddMortise(ObjectId MortiseId)
		{
			Module1.AddJoint(TimberId, MortiseId, Module1.Joint.Mortise);
			Module1.DeleteJoint(MortiseId);
		}
	}
}
