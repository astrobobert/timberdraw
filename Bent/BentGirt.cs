using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{

	public class BentGirt
	{
		public Point3d StartPoint;
		public double Width;
		public double Depth;
		public double postDepth;
		public string BentNumber;
		public string Designation;
		public string Type;
		public bool HasBentFloorGirt;
		public ObjectId TimberId;
		public ObjectId TenonLeftId;
		public ObjectId TenonRightId;
		public JointParams NearJointParamsDrawn;   // params used to draw TenonLeftId
		public JointParams FarJointParamsDrawn;    // params used to draw TenonRightId
		// Set by RegenerateBentGirt before Draw() so the body-BoolUnite is pre-empted
		// when the user requests a non-Tenon type. "Butt" skips that end entirely.
		public string NearJointType = "Tenon";
		public string FarJointType  = "Tenon";
		public ObjectId ShoulderPadLeftId;
		public ObjectId ShoulderPadRightId;
		public Dictionary<string, double> NearParams = new();
		public Dictionary<string, double> FarParams = new();

		public List<ObjectId> PegCol = new();
		public void Draw()
		{
			Point3dCollection pts = new()
            {
                StartPoint,
				Module1.AtPoint(StartPoint, Module1.Span - (postDepth * 2), 0, 0),
				Module1.AtPoint(StartPoint, Module1.Span - (postDepth * 2), Depth, 0),
				Module1.AtPoint(StartPoint, 0, Depth, 0)
            };
			if (HasBentFloorGirt) Designation += " UP";
			string sizeStr = (int)Width + "x" + (int)Depth + "x" + Module1.BuyLongFeet(Module1.Span - (postDepth * 2));
			TimberId = Module1.DrawElement(pts, Width, Type, BentNumber, Designation, sizeStr,
				jointNear: NearJointType, jointFar: FarJointType);

			double tenonZ = Module1.Make3D ? StartPoint.Z + ((Width - 2) / 2) : 0;
			var ic = System.Globalization.CultureInfo.InvariantCulture;
			string nearParamsJson = "{}";
			string farParamsJson  = "{}";

			// Left/near tenon: only drawn when NearJointType is "Tenon".
			// When "Butt", TenonLeftId stays Null -- no BoolUnite, body is clean.
			if (NearJointType == "Tenon")
			{
				double nearTenonWidth  = NearParams.TryGetValue("tenonWidth",   out var ntw) ? ntw : 2.0;
				double nearTopRelish   = NearParams.TryGetValue("tenonRelish",  out var ntr) ? ntr : 0.0;
				const double nearShoulder = 0.0;
				double nearHousingDepth = NearParams.TryGetValue("housingDepth", out var nhd) ? nhd : 0.0;
				var leftP = new JointParams(Module1.JointType.Tenon,
					new Point3d(StartPoint.X, StartPoint.Y, tenonZ),
					new Vector3d(-1, 0, 0),  new Vector3d(0, 1, 0),
					Width, Depth, nearTenonWidth, BentNumber, Designation,
					nearTopRelish, nearShoulder, true, nearHousingDepth);
				var leftRes = JointFactory.CreateWithPegs(Module1.JointType.Tenon, leftP);
				TenonLeftId = leftRes.JointId;
				NearJointParamsDrawn = leftP;
				if (leftRes.AdditionalSolids.Count > 0) ShoulderPadLeftId = leftRes.AdditionalSolids[0];
				PegCol.AddRange(leftRes.Pegs);
				nearParamsJson = string.Format(ic,
					"{{\"tenonWidth\":{0},\"tenonRelish\":{1},\"housingDepth\":{2}}}",
					nearTenonWidth, nearTopRelish, nearHousingDepth);
			}

			// Right/far tenon: only drawn when FarJointType is "Tenon".
			if (FarJointType == "Tenon")
			{
				double farTenonWidth   = FarParams.TryGetValue("tenonWidth",   out var ftw) ? ftw : 2.0;
				double farTopRelish    = FarParams.TryGetValue("tenonRelish",  out var ftr) ? ftr : 0.0;
				const double farShoulder = 0.0;
				double farHousingDepth  = FarParams.TryGetValue("housingDepth", out var fhd) ? fhd : 0.0;
				var rightP = new JointParams(Module1.JointType.Tenon,
					new Point3d(StartPoint.X + Module1.Span - (postDepth * 2), StartPoint.Y, tenonZ),
					new Vector3d(1, 0, 0),   new Vector3d(0, 1, 0),
					Width, Depth, farTenonWidth, BentNumber, Designation,
					farTopRelish, farShoulder, true, farHousingDepth);
				var rightRes = JointFactory.CreateWithPegs(Module1.JointType.Tenon, rightP);
				TenonRightId = rightRes.JointId;
				FarJointParamsDrawn = rightP;
				if (rightRes.AdditionalSolids.Count > 0) ShoulderPadRightId = rightRes.AdditionalSolids[0];
				PegCol.AddRange(rightRes.Pegs);
				farParamsJson = string.Format(ic,
					"{{\"tenonWidth\":{0},\"tenonRelish\":{1},\"housingDepth\":{2}}}",
					farTenonWidth, farTopRelish, farHousingDepth);
			}

			if (!TenonLeftId.IsNull)  Module1.AddJoint(TimberId, TenonLeftId,  Module1.Joint.Tenon);
			if (!TenonRightId.IsNull) Module1.AddJoint(TimberId, TenonRightId, Module1.Joint.Tenon);
			var xd = Module1.GetXdata(TimberId);
			xd.JointNearParams = nearParamsJson;
			xd.JointFarParams  = farParamsJson;
			Module1.SetXdata(TimberId, xd);
            // Phase 2: persist regeneration data so TimberFactory.Regenerate() can
            // reconstruct this timber without replaying the full bent session.
            Module1.PersistPegHandles(TimberId, PegCol);
            Module1.SaveDrawContext(TimberId, BuildContextJson());
            // End markers: "N" at near (left) face center, "F" at far (right) face center.
            if (Module1.ShowEndMarkers) {
                double mz = Module1.Make3D ? StartPoint.Z + (Width / 2) : 0;
                var em = new System.Collections.Generic.List<ObjectId>();
                em.Add(Module1.DrawEndMarker(new Point3d(StartPoint.X, StartPoint.Y + Depth / 2, mz), "N"));
                em.Add(Module1.DrawEndMarker(new Point3d(StartPoint.X + Module1.Span - (postDepth * 2), StartPoint.Y + Depth / 2, mz), "F"));
                Module1.PersistEndMarkerHandles(TimberId, em);
            }
		}

        // Builds a JSON string capturing the Module1 globals and member-specific params
        // needed to regenerate this BentGirt. Parsed by TimberFactory.RegenerateBentGirt().
        private string BuildContextJson()
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return string.Format(c,
                "{{\"class\":\"BentGirt\"" +
                ",\"startX\":{0},\"startY\":{1},\"startZ\":{2}" +
                ",\"span\":{3},\"bog\":{4}" +
                ",\"make3D\":{5}" +
                ",\"postDepth\":{6}}}",
                Module1.StartPoint.X, Module1.StartPoint.Y, Module1.StartPoint.Z,
                Module1.Span, Module1.BOG,
                Module1.Make3D ? "true" : "false",
                postDepth);
        }

		public void AddMortise(ObjectId MortiseId)
		{
            Module1.AddJoint(TimberId, MortiseId, Module1.Joint.Mortise);
			Module1.DeleteJoint(MortiseId);
		}
	}
}
