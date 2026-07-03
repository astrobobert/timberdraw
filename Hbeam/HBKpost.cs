using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{
    public class HBKpost
    {
        public Point3d StartPoint;
        public double Width;
        public double Depth;
        public double postDepth;
        public double KpostRafterSeatDepth;
        public int HBDivisor;
        public string BentNumber;
        public string Designation;
        public string Type;
        public ObjectId TimberId;
        public ObjectId Tenon;
        public JointParams NearJointParamsDrawn;   // params used to draw Tenon
        public Dictionary<string, double> NearParams = new();
        public Dictionary<string, double> FarParams  = new();

        public List<ObjectId> PegCol = new();

        public void Draw()
        {
            Point3dCollection pts = new();
            double halfSpan = Module1.Span / 2;
            pts.Add(StartPoint);
            pts.Add(Module1.AtPoint(StartPoint, Depth, 0, 0));
            // pts.Add(new Point3d(halfSpan + (Depth / 2), ((halfSpan - (postDepth + (Depth / 2))) * Module1.Pitch) + Module1.TOH, 0));
            // pts.Add(new Point3d(((halfSpan + (Depth / 2)) - (Math.Cos(Module1.Beta) * KpostRafterSeatDepth)), ((halfSpan - (postDepth + (Depth / 2))) * Module1.Pitch) + Module1.TOH + (((Math.Cos(Math.Atan(Module1.Pitch)) * KpostRafterSeatDepth) * Module1.Pitch)), 0));
            // pts.Add(new Point3d(halfSpan + (Depth / 2), (((halfSpan - (postDepth + (Depth / 2))) * Module1.Pitch) + Module1.TOH) + (KpostRafterSeatDepth / Math.Sin(Module1.Beta)), 0));
            pts.Add(new Point3d(halfSpan + (Depth / 2), Module1.EaveHt + ((halfSpan - (Depth / 2)) * Module1.Pitch), 0));
            pts.Add(new Point3d(halfSpan, Module1.EaveHt + (halfSpan * Module1.Pitch), 0));
            pts.Add(new Point3d(halfSpan - (Depth / 2), Module1.EaveHt + ((halfSpan - (Depth / 2)) * Module1.Pitch), 0));
            // pts.Add(new Point3d(halfSpan - (Depth / 2), (((halfSpan - (postDepth + (Depth / 2))) * Module1.Pitch) + Module1.TOH) + (KpostRafterSeatDepth / Math.Sin(Module1.Beta)), 0));
            // pts.Add(new Point3d((halfSpan - (Depth / 2)) + (Math.Cos(Module1.Beta) * KpostRafterSeatDepth), ((halfSpan - (postDepth + (Depth / 2))) * Module1.Pitch) + Module1.TOH + (((Math.Cos(Math.Atan(Module1.Pitch)) * KpostRafterSeatDepth) * Module1.Pitch)), 0));
            // pts.Add(new Point3d(halfSpan - (Depth / 2), ((halfSpan - (postDepth + (Depth / 2))) * Module1.Pitch) + Module1.TOH, 0));
            
            double kpostHeight = Module1.EaveHt + ((Module1.Span / 2) * Module1.Pitch) - Module1.TOG;
            string sizeStr = (int)Width + "x" + (int)Depth + "x" + Module1.BuyLongFeet(kpostHeight);
            TimberId = Module1.DrawElement(pts, Width, Type, BentNumber, Designation, sizeStr,
                jointNear: "Tenon", jointFar: "Butt");

            // Near-end joint params (defaults: tenonWidth=2, relish=0)
            double nearTenonWidth = NearParams.TryGetValue("tenonWidth",  out var ntw) ? ntw : 2.0;
            double nearTopRelish  = NearParams.TryGetValue("tenonRelish", out var ntr) ? ntr : 0.0;

            // Near-end tenon: at base of HBKpost, projects -Y into hammer beam girt.
            double tenonZ = Module1.Make3D ? StartPoint.Z + ((Width - 2) / 2) : 0;
            var nearP = new JointParams(Module1.JointType.Tenon,
                new Point3d(StartPoint.X, StartPoint.Y, tenonZ),
                new Vector3d(0, -1, 0),   // FaceNormal: into HBGirt
                new Vector3d(1,  0, 0),   // LateralDir: along Depth (+X)
                Width, Depth, nearTenonWidth, BentNumber, Designation,
                nearTopRelish, 0.0, true, 0.0);
            var nearRes = JointFactory.CreateWithPegs(Module1.JointType.Tenon, nearP);
            Tenon = nearRes.JointId;
            NearJointParamsDrawn = nearP;
            PegCol.AddRange(nearRes.Pegs);
            Module1.AddJoint(TimberId, Tenon, Module1.Joint.Tenon);

            // Persist
            Module1.PersistPegHandles(TimberId, PegCol);
            Module1.SaveDrawContext(TimberId, BuildContextJson());
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            var xd = Module1.GetXdata(TimberId);
            xd.JointNearParams = string.Format(ic,
                "{{\"tenonWidth\":{0},\"tenonRelish\":{1}}}",
                nearTenonWidth, nearTopRelish);
            xd.JointFarParams = "";
            Module1.SetXdata(TimberId, xd);
            // End markers: "N" at near (HBGirt/bottom) face, "F" at ridge apex.
            if (Module1.ShowEndMarkers) {
                double _mz = Module1.Make3D ? StartPoint.Z + (Width / 2) : 0;
                double _halfSpan = Module1.Span / 2;
                var em = new System.Collections.Generic.List<ObjectId>();
                em.Add(Module1.DrawEndMarker(new Point3d(StartPoint.X + Depth / 2, StartPoint.Y, _mz), "N"));
                em.Add(Module1.DrawEndMarker(new Point3d(_halfSpan, Module1.EaveHt + (_halfSpan * Module1.Pitch), _mz), "F"));
                Module1.PersistEndMarkerHandles(TimberId, em);
            }
        }

        private string BuildContextJson()
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return string.Format(c,
                "{{\"class\":\"HBKpost\",\"startX\":{0},\"startY\":{1},\"startZ\":{2}" +
                ",\"postDepth\":{3},\"kpostRafterSeatDepth\":{4},\"hbDivisor\":{5}" +
                ",\"span\":{6},\"eaveHt\":{7},\"pitch\":{8},\"beta\":{9}" +
                ",\"toh\":{10},\"tog\":{11},\"make3D\":{12}}}",
                StartPoint.X, StartPoint.Y, StartPoint.Z,
                postDepth, KpostRafterSeatDepth, HBDivisor,
                Module1.Span, Module1.EaveHt, Module1.Pitch, Module1.Beta,
                Module1.TOH, Module1.TOG,
                Module1.Make3D ? "true" : "false");
        }

        public void AddMortise(ObjectId MortiseId)
        {
            Module1.AddJoint(TimberId, MortiseId, Module1.Joint.Mortise);
            Module1.DeleteJoint(MortiseId);
        }
    }
}
