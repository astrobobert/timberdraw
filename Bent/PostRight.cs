using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{

	public class PostRight
	{
		public Point3d StartPoint;
		public double Width;
		public double Depth;
		public double FlrGirtHt;
		public double FlrGirtDepth;
		public string BentNumber;
		public string Designation;
		public string Type;
		public bool HasFlrGirt;

		public ObjectId TimberId;
		public ObjectId TenonNearId;
		public ObjectId TenonFarId;
		public string NearJointType = "Butt"; // end-condition type at near (bottom) end
		public string FarJointType  = "Butt"; // end-condition type at far  (top)   end
		public Dictionary<string, double> NearParams = new();
		public Dictionary<string, double> FarParams  = new();
		public List<ObjectId> PegCol = new();

		public void Draw()
		{
			Point3dCollection pts = new()
            {
                new Point3d(StartPoint.X + Module1.Span, StartPoint.Y, StartPoint.Z),
                new Point3d(StartPoint.X + Module1.Span, StartPoint.Y + Module1.EaveHt, StartPoint.Z),
                new Point3d(StartPoint.X + Module1.Span - Depth, StartPoint.Y + Module1.EaveHt + (Depth * Module1.Pitch), StartPoint.Z),
                new Point3d(StartPoint.X + Module1.Span - Depth, StartPoint.Y, StartPoint.Z)
            };
			string sizeStr = (int)Width + "x" + (int)Depth + "x" + Module1.BuyLongFeet(Module1.EaveHt);
			TimberId = Module1.DrawElement(pts, Width, "Post", BentNumber, Designation, sizeStr,
			    jointNear: NearJointType, jointFar: FarJointType);

			double tenonZ         = Module1.Make3D ? StartPoint.Z + ((Width - 2) / 2) : 0;
				string nearParamsJson = "{}";
				string farParamsJson  = "{}";
				var    ic             = System.Globalization.CultureInfo.InvariantCulture;
				double outerX         = StartPoint.X + Module1.Span;

				// Near (bottom) end -- dispatch on NearJointType.
				if (NearJointType == "Tenon")
				{
					double nearTenonWidth   = NearParams.TryGetValue("tenonWidth",   out var ntw) ? ntw : 2.0;
					double nearTopRelish    = NearParams.TryGetValue("tenonRelish",  out var ntr) ? ntr : 0.0;
					const double nearShoulder = 0.0;
					double nearHousingDepth = NearParams.TryGetValue("housingDepth", out var nhd) ? nhd : 0.0;
					// Projects -Y (into sill). Origin at outer right face. LateralDir=(-1,0,0) spans Depth inward.
					var nearRes = JointFactory.CreateWithPegs(Module1.JointType.Tenon, new JointParams(
					    Module1.JointType.Tenon,
					    new Point3d(outerX, StartPoint.Y, tenonZ),
					    new Vector3d( 0, -1, 0), new Vector3d(-1, 0, 0),
					    Width, Depth, nearTenonWidth, BentNumber, Designation,
					    nearTopRelish, nearShoulder, true, nearHousingDepth));
					TenonNearId = nearRes.JointId;
					PegCol.AddRange(nearRes.Pegs);
					if (!TenonNearId.IsNull)
					    Module1.AddJoint(TimberId, TenonNearId, Module1.Joint.Tenon);
					nearParamsJson = string.Format(ic,
					    "{{\"tenonWidth\":{0},\"tenonRelish\":{1},\"housingDepth\":{2}}}",
					    nearTenonWidth, nearTopRelish, nearHousingDepth);
				}
				// else: Butt or other types -- no geometry

				// Far (top) end -- dispatch on FarJointType.
				if (FarJointType == "Tenon")
				{
					double farTenonWidth    = FarParams.TryGetValue("tenonWidth",   out var ftw) ? ftw : 2.0;
					double farTopRelish     = FarParams.TryGetValue("tenonRelish",  out var ftr) ? ftr : 0.0;
					const double farShoulder = 0.0;
					double farHousingDepth  = FarParams.TryGetValue("housingDepth", out var fhd) ? fhd : 0.0;
					// Projects +Y (into plate). Origin at outer right face at eave. LateralDir=(-1,0,0).
					var farRes = JointFactory.CreateWithPegs(Module1.JointType.Tenon, new JointParams(
					    Module1.JointType.Tenon,
					    new Point3d(outerX, StartPoint.Y + Module1.EaveHt, tenonZ),
					    new Vector3d( 0,  1, 0), new Vector3d(-1, 0, 0),
					    Width, Depth, farTenonWidth, BentNumber, Designation,
					    farTopRelish, farShoulder, true, farHousingDepth));
					TenonFarId = farRes.JointId;
					PegCol.AddRange(farRes.Pegs);
					if (!TenonFarId.IsNull)
					    Module1.AddJoint(TimberId, TenonFarId, Module1.Joint.Tenon);
					farParamsJson = string.Format(ic,
					    "{{\"tenonWidth\":{0},\"tenonRelish\":{1},\"housingDepth\":{2}}}",
					    farTenonWidth, farTopRelish, farHousingDepth);
				}
				// else: Butt or other types -- no geometry

				var xd = Module1.GetXdata(TimberId);
				xd.JointNearParams = nearParamsJson;
				xd.JointFarParams  = farParamsJson;
				Module1.SetXdata(TimberId, xd);

			Module1.PersistPegHandles(TimberId, PegCol);
            Module1.SaveDrawContext(TimberId, BuildContextJson());
            // End markers: "N" at near (foot/bottom) face center, "F" at far (top/plate) face center.
            if (Module1.ShowEndMarkers) {
                double mz = Module1.Make3D ? StartPoint.Z + (Width / 2) : 0;
                var em = new System.Collections.Generic.List<ObjectId>();
                em.Add(Module1.DrawEndMarker(new Point3d(StartPoint.X + Depth / 2, StartPoint.Y, mz), "N"));
                em.Add(Module1.DrawEndMarker(new Point3d(StartPoint.X + Depth / 2, StartPoint.Y + Module1.EaveHt, mz), "F"));
                Module1.PersistEndMarkerHandles(TimberId, em);
            }
		}

        private string BuildContextJson()
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return string.Format(c,
                "{{\"class\":\"PostRight\"" +
                ",\"startX\":{0},\"startY\":{1},\"startZ\":{2}" +
                ",\"span\":{3},\"eaveHt\":{4},\"pitch\":{5}" +
                ",\"bog\":{6},\"tog\":{7},\"toh\":{8}" +
                ",\"make3D\":{9}" +
                ",\"hasFlrGirt\":{10},\"flrGirtHt\":{11},\"flrGirtDepth\":{12}}}",
                Module1.StartPoint.X, Module1.StartPoint.Y, Module1.StartPoint.Z,
                Module1.Span, Module1.EaveHt, Module1.Pitch,
                Module1.BOG, Module1.TOG, Module1.TOH,
                Module1.Make3D ? "true" : "false",
                HasFlrGirt     ? "true" : "false",
                FlrGirtHt, FlrGirtDepth);
        }

		public void AddMortise(ObjectId MortiseId)
		{
            Module1.AddJoint(TimberId, MortiseId, Module1.Joint.Mortise);
			Module1.DeleteJoint(MortiseId);
		}
	}
}
