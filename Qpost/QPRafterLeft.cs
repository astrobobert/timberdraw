using System.Collections.Generic;
using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{

	public class QPRafterLeft
	{
		public Point3d StartPoint;
		public double Width;
		public double Depth;
		public double postDepth;
		public string BentNumber;
		public string Designation;
		public string Type;
		public ObjectId TimberId;
		public ObjectId Tenon;              // far (foot/eave) tenon into left post
		public JointParams FarJointParamsDrawn;   // params used to draw Tenon

		// "Tenon" draws the foot tenon (default); "Butt" skips it.
		// Near (peak/center) is always Butt -- ridge housing arrives via AddMortise.
		public string NearJointType = "Butt";
		public string FarJointType  = "Tenon";

		// Per-end joint params: near = peak/center (Butt), far = foot/eave (Tenon).
		public Dictionary<string, double> NearParams = new();
		public Dictionary<string, double> FarParams  = new();

		public List<ObjectId> PegCol = new();

		public void Draw()
		{
			// Body: 4-pt plumb-cut parallelogram, face-to-face with posts.
			// Top and bottom edges parallel at slope Pitch; both end faces vertical (plumb).
			double xFoot    = postDepth;
			double xPeak    = Module1.Span / 2.0;
			double yTopFoot = Module1.EaveHt + xFoot * Module1.Pitch;
			double yTopPeak = Module1.EaveHt + xPeak * Module1.Pitch;
			double yBotFoot = yTopFoot - Module1.PlumbLength;
			double yBotPeak = yTopPeak - Module1.PlumbLength;

			// Peak body: bottom face extends past centerline to the shared corner with QPRafterRight.
			// xPeakBot = Span/2 + PlumbLength/(2*Pitch), derived from the intersection of the
			// peak cut line (slope -Pitch from peak-top) and this rafter's own bottom surface.
			double lapExt   = Module1.PlumbLength / (2.0 * Module1.Pitch);
			double xPeakBot = xPeak + lapExt;
			double yPeakBot = Module1.EaveHt + xPeakBot * Module1.Pitch - Module1.PlumbLength;

			Point3dCollection pts = new()
			{
				new Point3d(xFoot,    yBotFoot, 0),   // [0] foot bottom (near StartPoint)
				new Point3d(xPeakBot, yPeakBot, 0),   // [1] peak bottom (extends past centerline)
				new Point3d(xPeak,    yTopPeak, 0),   // [2] peak top (at centerline Span/2)
				new Point3d(xFoot,    yTopFoot, 0)    // [3] foot top
			};
			string sizeStr = (int)Width + "x" + (int)Depth + "x" + Module1.BuyLongFeet((Module1.Span / 2.0) / Math.Cos(Module1.Beta));
			TimberId = Module1.DrawElement(pts, Width, Type, BentNumber, Designation, sizeStr,
				jointNear: NearJointType, jointFar: FarJointType);

			double tenonZ = Module1.Make3D ? StartPoint.Z + ((Width - 2) / 2) : 0;
			var ic = System.Globalization.CultureInfo.InvariantCulture;

			// Near end (peak/center): Butt -- no tenon geometry drawn here.
			// Ridge housing from QPRafterRight.SeatPeakId arrives via AddMortise in QPBent.

			// Far end (foot/eave): tenon + housing into left post.
			string farParamsJson = "{}";
			if (FarJointType == "Tenon")
			{
				double farTenonWidth   = FarParams.TryGetValue("tenonWidth",   out var ftw) ? ftw : 2.0;
				double farTopRelish    = FarParams.TryGetValue("tenonRelish",  out var ftr) ? ftr : 0.0;
				double farHousingDepth = FarParams.TryGetValue("housingDepth", out var fhd) ? fhd : 1.5;
				var farP = new JointParams(Module1.JointType.Tenon,
					new Point3d(xFoot, yBotFoot, tenonZ),
					new Vector3d(-1, 0, 0),   // FaceNormal: into left post
					new Vector3d(0, 1, 0),    // LateralDir: up the plumb face
					Width, Module1.PlumbLength, farTenonWidth,
					BentNumber, Designation,
					farTopRelish, 0.0, true, farHousingDepth,
					pitch: Module1.Pitch);
				var farRes = JointFactory.CreateWithPegs(Module1.JointType.Tenon, farP);
				Tenon = farRes.JointId;
				FarJointParamsDrawn = farP;
				PegCol.AddRange(farRes.Pegs);
				Module1.AddJoint(TimberId, Tenon, Module1.Joint.Tenon);
				Module1.DeleteJoint(Tenon);   // standalone solid no longer needed; geometry is in rafter body
				farParamsJson = string.Format(ic,
					"{{\"tenonWidth\":{0},\"tenonRelish\":{1},\"housingDepth\":{2}}}",
					farTenonWidth, farTopRelish, farHousingDepth);
			}

			var xd = Module1.GetXdata(TimberId);
			xd.JointNearParams = "{\"tenonWidth\":0,\"tenonRelish\":0,\"housingDepth\":0}";
			xd.JointFarParams  = farParamsJson;
			Module1.SetXdata(TimberId, xd);

			Module1.PersistPegHandles(TimberId, PegCol);
			Module1.SaveDrawContext(TimberId, BuildContextJson());
            // End markers: "N" at near (peak/center) face center, "F" at far (foot/eave) face center.
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
				"{{\"class\":\"QPRafterLeft\"" +
				",\"startX\":{0},\"startY\":{1},\"startZ\":{2}" +
				",\"postDepth\":{3}" +
				",\"span\":{4},\"eaveHt\":{5},\"pitch\":{6},\"beta\":{7},\"toh\":{8}" +
				",\"make3D\":{9}}}",
				StartPoint.X, StartPoint.Y, StartPoint.Z,
				postDepth,
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
