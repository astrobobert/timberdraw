using System.Collections.Generic;
using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{

	public class KPRafterLeft
	{
		public Point3d StartPoint;
		public double Width;
		public double Depth;
		public double KPostDepth;
		public double postDepth;
		public double KpostRafterSitDepth;
		public string BentNumber;
		public string Designation;
		public string Type;
		public ObjectId TimberId;
		public ObjectId Tenon;       // far (foot/eave) tenon -- kept for regen compat
		public ObjectId SeatPeakId;  // near (peak/kingpost) shoulder solid
		public JointParams NearJointParamsDrawn;   // params used to draw SeatPeakId
		public JointParams FarJointParamsDrawn;    // params used to draw Tenon
		// "Shoulder" (default) draws the shoulder at the king-post end; "Butt" skips it.
		// "Tenon" draws a plain tenon (shoulder omitted).
		public string NearJointType = "Shoulder";
		public string FarJointType  = "Tenon";     // "Butt" skips foot tenon BoolUnite

		// Per-end joint params: near = kingpost/peak, far = foot/eave.
		public Dictionary<string, double> NearParams = new();
		public Dictionary<string, double> FarParams  = new();

		public List<ObjectId> PegCol = new();

		public void Draw()
		{
			// Body: 4-pt plumb-cut parallelogram.
			// Top and bottom edges parallel at slope Pitch; both end faces vertical (plumb).
			// Slope formula: Y_top(x) = EaveHt + x * Pitch  (measured from X = 0).
			double xFoot    = postDepth;                                    // face-to-face with left post
			double xPeak    = Module1.Span / 2.0 - KPostDepth / 2.0;
			double yTopFoot = Module1.EaveHt + xFoot * Module1.Pitch;
			double yTopPeak = Module1.EaveHt + xPeak * Module1.Pitch;
			double yBotFoot = yTopFoot - Module1.PlumbLength;
			double yBotPeak = yTopPeak - Module1.PlumbLength;

			Point3dCollection pts = new()
			{
				new Point3d(xFoot, yBotFoot, 0),   // [0] foot bottom
				new Point3d(xPeak, yBotPeak, 0),   // [1] peak bottom
				new Point3d(xPeak, yTopPeak, 0),   // [2] peak top
				new Point3d(xFoot, yTopFoot, 0)    // [3] foot top
			};
			string sizeStr = (int)Width + "x" + (int)Depth + "x" + Module1.BuyLongFeet((Module1.Span / 2.0) / Math.Cos(Module1.Beta));
			TimberId = Module1.DrawElement(pts, Width, Type, BentNumber, Designation, sizeStr,
				jointNear: NearJointType, jointFar: FarJointType);

			double tenonZ = Module1.Make3D ? StartPoint.Z + ((Width - 2) / 2) : 0;
			var ic = System.Globalization.CultureInfo.InvariantCulture;
			// nearShoulder flag: 1 when the bearing seat is being drawn, 0 otherwise.
			double nearShoulder = (NearJointType == "Shoulder") ? 1.0 : 0.0;
			double sitdepth = NearParams.TryGetValue("sitdepth", out var sd) ? sd : KpostRafterSitDepth;
			double nearShoulderDepth = nearShoulder > 0 ? sitdepth : 0.0;
			string farParamsJson  = "{}";

			// -- Near end (peak/kingpost): shoulder bearing drawn only when NearJointType="Shoulder".
			// Always build the JointParams struct (needed for cascade delta-swap metadata),
			// but only call JF when the type is actually Shoulder -- a zero-depth call
			// produces a degenerate Polyline3d, not a Solid3d, which breaks AddJoint.
			var nearP = new JointParams(Module1.JointType.Shoulder,
				new Point3d(xPeak, yBotPeak, tenonZ),
				new Vector3d(Math.Cos(Module1.Beta), Math.Sin(Module1.Beta), 0),
				new Vector3d(0, 1, 0),
				Width, nearShoulderDepth / Math.Sin(Module1.Beta), 2.0,
				BentNumber, Designation,
				0, nearShoulderDepth, false, 0);
			NearJointParamsDrawn = nearP;
			if (NearJointType == "Shoulder")
			{
				var nearRes = JointFactory.CreateWithPegs(Module1.JointType.Shoulder, nearP);
				SeatPeakId = nearRes.JointId;
				if (!SeatPeakId.IsNull)
					Module1.AddJoint(TimberId, SeatPeakId, Module1.Joint.Tenon);  // unite into rafter
			}

			// -- Far end (foot/eave): tenon only when FarJointType="Tenon".
			if (FarJointType == "Tenon")
			{
				double farTenonWidth   = FarParams.TryGetValue("tenonWidth",   out var ftw) ? ftw : 2.0;
				double farTopRelish    = FarParams.TryGetValue("tenonRelish",  out var ftr) ? ftr : 0.0;
				const double farShoulder = 0.0;
				double farHousingDepth = FarParams.TryGetValue("housingDepth", out var fhd) ? fhd : 1.5;
				var farP = new JointParams(Module1.JointType.Tenon,
					new Point3d(xFoot, yBotFoot, tenonZ),
					new Vector3d(-1, 0, 0),   // FaceNormal: into left post
					new Vector3d(0, 1, 0),    // LateralDir: up the plumb face
					Width, Module1.PlumbLength, farTenonWidth,
					BentNumber, Designation,
					farTopRelish, farShoulder, true, farHousingDepth,
					pitch: Module1.Pitch);
				var farRes = JointFactory.CreateWithPegs(Module1.JointType.Tenon, farP);
				Tenon = farRes.JointId;
				FarJointParamsDrawn = farP;
				PegCol.AddRange(farRes.Pegs);
				Module1.AddJoint(TimberId, Tenon, Module1.Joint.Tenon);
				farParamsJson = string.Format(ic,
					"{{\"tenonWidth\":{0},\"tenonRelish\":{1},\"housingDepth\":{2}}}",
					farTenonWidth, farTopRelish, farHousingDepth);
			}

			var xd = Module1.GetXdata(TimberId);
			xd.JointNearParams = string.Format(ic,
				"{{\"tenonWidth\":0,\"tenonRelish\":0,\"housingDepth\":0,\"sitdepth\":{0}}}",
				sitdepth);
			xd.JointFarParams = farParamsJson;
			Module1.SetXdata(TimberId, xd);

			// Phase 2: persist regeneration data
			Module1.PersistPegHandles(TimberId, PegCol);
			Module1.SaveDrawContext(TimberId, BuildContextJson());
            // End markers: "N" at near (peak/kingpost) face center, "F" at far (foot/eave) face center.
            if (Module1.ShowEndMarkers) {
                var em = new System.Collections.Generic.List<ObjectId>();
                em.Add(Module1.DrawEndMarker(new Point3d(xPeak, (yBotPeak + yTopPeak) / 2, tenonZ), "N"));
                em.Add(Module1.DrawEndMarker(new Point3d(xFoot, (yBotFoot + yTopFoot) / 2, tenonZ), "F"));
                Module1.PersistEndMarkerHandles(TimberId, em);
            }
		}

		private string BuildContextJson()
		{
			var c = System.Globalization.CultureInfo.InvariantCulture;
			return string.Format(c,
				"{{\"class\":\"KPRafterLeft\"" +
				",\"startX\":{0},\"startY\":{1},\"startZ\":{2}" +
				",\"kPostDepth\":{3},\"postDepth\":{4},\"kpostRafterSitDepth\":{5}" +
				",\"span\":{6},\"eaveHt\":{7},\"pitch\":{8},\"beta\":{9},\"toh\":{10}" +
				",\"make3D\":{11}}}",
				StartPoint.X, StartPoint.Y, StartPoint.Z,
				KPostDepth, postDepth, KpostRafterSitDepth,
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
