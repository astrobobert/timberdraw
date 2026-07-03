using System.Collections.Generic;
using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{

	public class KPRafterRight
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
		public string NearJointType = "Shoulder";  // "Butt" skips shoulder BoolUnite
		public string FarJointType  = "Tenon";     // "Butt" skips foot tenon BoolUnite

		// Per-end joint params: near = kingpost/peak, far = foot/eave.
		public Dictionary<string, double> NearParams = new();
		public Dictionary<string, double> FarParams  = new();

		public List<ObjectId> PegCol = new();

		public void Draw()
		{
			// Body: 4-pt plumb-cut parallelogram, right rafter.
			// Top slope: Y_top = EaveHt + (Span - X) * Pitch (symmetric with left rafter).
			// Simplified: at the foot X = Span-postDepth, Y_top = EaveHt+(postDepth)*Pitch.
			// At the peak X = halfSpan+KPostDepth/2, Y_top = EaveHt+(halfSpan-KPostDepth/2)*Pitch.
			double xFoot    = Module1.Span - postDepth;                    // face-to-face with right post
			double xPeak    = Module1.Span / 2.0 + KPostDepth / 2.0;
			double yTopFoot = Module1.EaveHt + postDepth * Module1.Pitch;   // symmetric formula
			double yTopPeak = Module1.EaveHt + (Module1.Span / 2.0 - KPostDepth / 2.0) * Module1.Pitch;
			double yBotFoot = yTopFoot - Module1.PlumbLength;
			double yBotPeak = yTopPeak - Module1.PlumbLength;

			// CCW order: top-right (foot) -> top-left (peak) -> bottom-left (peak) -> bottom-right (foot).
			Point3dCollection pts = new()
			{
				new Point3d(xFoot, yTopFoot, 0),   // [0] foot top (eave)
				new Point3d(xPeak, yTopPeak, 0),   // [1] peak top
				new Point3d(xPeak, yBotPeak, 0),   // [2] peak bottom
				new Point3d(xFoot, yBotFoot, 0)    // [3] foot bottom (eave)
			};
			string sizeStr = (int)Width + "x" + (int)Depth + "x" + Module1.BuyLongFeet((Module1.Span / 2.0) / Math.Cos(Module1.Beta));
			TimberId = Module1.DrawElement(pts, Width, Type, BentNumber, Designation, sizeStr,
				jointNear: NearJointType, jointFar: FarJointType);

			double tenonZ = Module1.Make3D ? StartPoint.Z + ((Width - 2) / 2) : 0;
			var ic = System.Globalization.CultureInfo.InvariantCulture;
			double nearShoulder = (NearJointType == "Shoulder") ? 1.0 : 0.0;
			double sitdepth = NearParams.TryGetValue("sitdepth", out var sd) ? sd : KpostRafterSitDepth;
			double nearShoulderDepth = nearShoulder > 0 ? sitdepth : 0.0;
			string farParamsJson = "{}";

			// -- Near end (peak/kingpost): shoulder drawn only when NearJointType="Shoulder".
			// Always build nearP for cascade metadata; only invoke JF when type is Shoulder.
			// A zero-depth Shoulder call produces a degenerate Polyline3d, not a Solid3d.
			var nearP = new JointParams(Module1.JointType.Shoulder,
				new Point3d(xPeak, yBotPeak, tenonZ),
				new Vector3d(-Math.Cos(Module1.Beta), Math.Sin(Module1.Beta), 0),
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
					new Vector3d(1, 0, 0),    // FaceNormal: into right post
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
				"{{\"class\":\"KPRafterRight\"" +
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
