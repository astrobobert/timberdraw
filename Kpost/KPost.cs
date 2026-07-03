using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{
    public class KPost
    {
        public Point3d StartPoint;
        public double Width;
        public double Depth;
        public double postDepth;
        public double KpostRafterSitDepth;
        public string BentNumber;
        public string Designation;
        public string Type;
        public ObjectId TimberId;
        public ObjectId Tenon;
        public JointParams NearJointParamsDrawn;   // params used to draw Tenon
        public string NearJointType = "Tenon";     // "Butt" skips near tenon BoolUnite
        public Dictionary<string, double> NearParams = new();
        public Dictionary<string, double> FarParams  = new();

        public List<ObjectId> PegCol = new();

        public void Draw()
        {
            double halfSpan = Module1.Span / 2;
            Point3dCollection pts = new()
            {
                StartPoint,
                new Point3d(StartPoint.X + Depth, Module1.TOG, StartPoint.Z),
                new Point3d(StartPoint.X + Depth, Module1.EaveHt + ((halfSpan - (Depth / 2)) * Module1.Pitch), StartPoint.Z),
                new Point3d(halfSpan, Module1.EaveHt + (halfSpan * Module1.Pitch), StartPoint.Z),
                new Point3d(StartPoint.X, Module1.EaveHt + (halfSpan - Depth / 2) * Module1.Pitch, StartPoint.Z)
            };
            
            double kpostHeight = Module1.EaveHt + ((Module1.Span / 2) * Module1.Pitch) - Module1.TOG;
            string sizeStr = (int)Width + "x" + (int)Depth + "x" + Module1.BuyLongFeet(kpostHeight);
            TimberId = Module1.DrawElement(pts, Width, Type, BentNumber, Designation, sizeStr,
                jointNear: NearJointType, jointFar: "Butt");

            double tenonZ = Module1.Make3D ? StartPoint.Z + ((Width - 2) / 2) : 0;
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            string nearParamsJson = "{}";

            // Near tenon: only drawn when NearJointType is "Tenon".
            if (NearJointType == "Tenon")
            {
                double nearTenonWidth = NearParams.TryGetValue("tenonWidth",  out var ntw) ? ntw : 2.0;
                double nearTopRelish  = NearParams.TryGetValue("tenonRelish", out var ntr) ? ntr : 0.0;
                var nearP = new JointParams(Module1.JointType.Tenon,
                    new Point3d(StartPoint.X, StartPoint.Y, tenonZ),
                    new Vector3d(0, -1, 0),   // FaceNormal: into tie beam
                    new Vector3d(1,  0, 0),   // LateralDir: along Depth (+X)
                    Width, Depth, nearTenonWidth, BentNumber, Designation,
                    nearTopRelish, 0.0, true, 0.0);
                var nearRes = JointFactory.CreateWithPegs(Module1.JointType.Tenon, nearP);
                Tenon = nearRes.JointId;
                NearJointParamsDrawn = nearP;
                PegCol.AddRange(nearRes.Pegs);
                Module1.AddJoint(TimberId, Tenon, Module1.Joint.Tenon);
                nearParamsJson = string.Format(ic,
                    "{{\"tenonWidth\":{0},\"tenonRelish\":{1}}}",
                    nearTenonWidth, nearTopRelish);
            }

            // Persist
            Module1.PersistPegHandles(TimberId, PegCol);
            Module1.SaveDrawContext(TimberId, BuildContextJson());
            var xd = Module1.GetXdata(TimberId);
            xd.JointNearParams = nearParamsJson;
            xd.JointFarParams = "";
            Module1.SetXdata(TimberId, xd);
            // End markers: "N" at near (tie beam/bottom) face center, "F" at ridge apex.
            if (Module1.ShowEndMarkers) {
                double mz = Module1.Make3D ? StartPoint.Z + (Width / 2) : 0;
                double _halfSpan = Module1.Span / 2;
                var em = new System.Collections.Generic.List<ObjectId>();
                em.Add(Module1.DrawEndMarker(new Point3d(StartPoint.X + Depth / 2, StartPoint.Y, mz), "N"));
                em.Add(Module1.DrawEndMarker(new Point3d(_halfSpan, Module1.EaveHt + (_halfSpan * Module1.Pitch), mz), "F"));
                Module1.PersistEndMarkerHandles(TimberId, em);
            }
        }

        private string BuildContextJson()
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return string.Format(c,
                "{{\"class\":\"KPost\"" +
                ",\"startX\":{0},\"startY\":{1},\"startZ\":{2}" +
                ",\"span\":{3},\"eaveHt\":{4},\"pitch\":{5},\"beta\":{6}" +
                ",\"tog\":{7},\"toh\":{8}" +
                ",\"make3D\":{9}" +
                ",\"postDepth\":{10},\"kpostRafterSitDepth\":{11}}}",
                StartPoint.X, StartPoint.Y, StartPoint.Z,
                Module1.Span, Module1.EaveHt, Module1.Pitch, Module1.Beta,
                Module1.TOG, Module1.TOH,
                Module1.Make3D ? "true" : "false",
                postDepth, KpostRafterSitDepth);
        }

        public void AddMortise(ObjectId MortiseId)
        {
            Module1.AddJoint(TimberId, MortiseId, Module1.Joint.Mortise);
            Module1.DeleteJoint(MortiseId);
        }
    }
}
