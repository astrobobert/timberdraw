using System.Collections.Generic;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.DatabaseServices;
using TimberFrameSuite.Standards;
namespace TimberDraw
{

    public class HBBayGirt
    {
        private double TenonWidth = 2;
        public Point3d Startpoint;
        public double Width;
        public double Depth;
        public double Length;
        public double Baywidth;
        public double HPostWidth;
        public double HPostDepth;
        public string BentNumber;
        public string Designation;
        public string Type;
        public ObjectId Timber;
        public ObjectId TenonLeft;
        public ObjectId TenonRight;

        public List<ObjectId> PegCol = new();
        public enum Sides
        {
            Left = 0,
            Right = 1
        }

        public void Draw(Sides Side)
		{
			string sizeStr = (int)Width + "x" + (int)Depth + "x" + Module1.BuyLongFeet(Baywidth);
			double tenonZ = 0;
			double z = 0;
			if (Module1.Make3D) {
				z = HPostWidth;
			} else {
				z = 0;
			}
			Point3dCollection pts = new();
            // Near/far body pts captured before pts is cleared for tenon geometry.
            Point3d _nearBodyPt = new Point3d();
            Point3d _farBodyPt  = new Point3d();
			switch (Side) {
				case Sides.Left:
					pts.Add(new Point3d(Startpoint.X, Startpoint.Y, z));
					pts.Add(Module1.AtPoint(pts[0], 0, Depth, 0));
					pts.Add(Module1.AtPoint(pts[0], -Width, Depth, 0));
					pts.Add(Module1.AtPoint(pts[0], -Width, 0, 0));
					Timber = Module1.DrawElement(pts, Baywidth, Type, BentNumber, Designation, sizeStr, jointNear: "Tenon", jointFar: "Tenon");
                    _nearBodyPt = pts[0];
                    _farBodyPt  = pts[3];
					if (Module1.HasJoinery) {
						pts.Clear();
						if (Module1.Make3D)
							tenonZ = z - 4;
						else
							tenonZ = 0;
						pts.Add(new Point3d(Startpoint.X - ((Width - 2) / 2), Startpoint.Y, tenonZ));
						pts.Add(Module1.AtPoint(pts[0], 0, Depth, 0));
						pts.Add(Module1.AtPoint(pts[1], -2, 0, 0));
						pts.Add(Module1.AtPoint(pts[2], 0, -Depth, 0));
						TenonLeft = Module1.DrawElement(pts, 4, "Tenon", "1", "");
						Module1.AddJoint(Timber, TenonLeft, Module1.Joint.Tenon);

						var peg = TFGPegStandards.GetPresetForTenonThickness(TenonWidth);
						double r = peg.DiameterInches / 2;
						double ps = peg.CalculatedSpacingInches;
						Point3d pegStartPt = Module1.AtPoint(pts[0], -2.25, Depth / 2, 0);
						pegStartPt = new Point3d(pegStartPt.X, pegStartPt.Y, 2.25 - (HPostDepth / 2));
						PegCol.Add(Module1.DrawPeg(pegStartPt, r, HPostDepth + 1.5, "Peg", "1", "", "", 90, 0, pts[0].X,pts[0].Y, pts[0].Z));
						pegStartPt = Module1.AtPoint(pegStartPt, 0, ps, 0);
						PegCol.Add(Module1.DrawPeg(pegStartPt, r, HPostDepth + 1.5, "Peg", "2", "", "", 90, 0, pts[0].X,pts[0].Y, pts[0].Z));
						pegStartPt = Module1.AtPoint(pegStartPt, 0, -(2 * ps), 0);
						PegCol.Add(Module1.DrawPeg(pegStartPt, r, HPostDepth + 1.5, "Peg", "3", "", "", 90, 0, pts[0].X,pts[0].Y, pts[0].Z));

						pts.Clear();
						pts.Add(new Point3d(Startpoint.X - ((Width - 2) / 2), Startpoint.Y, HPostWidth + Baywidth));
						pts.Add(Module1.AtPoint(pts[0], 0, Depth, 0));
						pts.Add(Module1.AtPoint(pts[1], -2, 0, 0));
						pts.Add(Module1.AtPoint(pts[2], 0, -Depth, 0));
						TenonRight = Module1.DrawElement(pts, 4, "Tenon", "2", "");
						Module1.AddJoint(Timber, TenonRight, Module1.Joint.Tenon);

						pegStartPt = Module1.AtPoint(pts[0], 1.75, Depth / 2, 0);
						pegStartPt = new Point3d(pegStartPt.X, pegStartPt.Y, (Baywidth + HPostWidth) - ((HPostDepth / 2) - 0.25));
						PegCol.Add(Module1.DrawPeg(pegStartPt, r, HPostDepth + 1.5, "Peg", "4", "", "", 270, 0, pts[0].X,pts[0].Y, pts[0].Z));
						pegStartPt = Module1.AtPoint(pegStartPt, 0, ps, 0);
						PegCol.Add(Module1.DrawPeg(pegStartPt, r, HPostDepth + 1.5, "Peg", "5", "", "", 270, 0, pts[0].X,pts[0].Y, pts[0].Z));
						pegStartPt = Module1.AtPoint(pegStartPt, 0, -(2 * ps), 0);
						PegCol.Add(Module1.DrawPeg(pegStartPt, r, HPostDepth + 1.5, "Peg", "6", "", "", 270, 0, pts[0].X,pts[0].Y, pts[0].Z));

					}
					break;
				case Sides.Right:
					pts.Add(new Point3d(Startpoint.X, Startpoint.Y, z));
					pts.Add(Module1.AtPoint(pts[0], Width, 0, 0));
					pts.Add(Module1.AtPoint(pts[0], Width, Depth, 0));
					pts.Add(Module1.AtPoint(pts[0], 0, Depth, 0));
					Timber = Module1.DrawElement(pts, Baywidth, Type, BentNumber, Designation, sizeStr, jointNear: "Tenon", jointFar: "Tenon");
                    _nearBodyPt = pts[0];
                    _farBodyPt  = pts[1];
					if (Module1.HasJoinery) {
						pts.Clear();
						if (Module1.Make3D)
							tenonZ = z - 4;
						else
							tenonZ = 0;
						pts.Add(new Point3d(Startpoint.X + ((Width - 2) / 2), Startpoint.Y, tenonZ));
						pts.Add(Module1.AtPoint(pts[0], 2, 0, 0));
						pts.Add(Module1.AtPoint(pts[1], 0, Depth, 0));
						pts.Add(Module1.AtPoint(pts[2], -2, 0, 0));
						TenonLeft = Module1.DrawElement(pts, 4, "Tenon", "1", "");
						Module1.AddJoint(Timber, TenonLeft, Module1.Joint.Tenon);

						var pegR = TFGPegStandards.GetPresetForTenonThickness(TenonWidth);
						double r = pegR.DiameterInches / 2;
						double ps = pegR.CalculatedSpacingInches;
						Point3d pegStartPt = Module1.AtPoint(pts[0], -2.25, Depth / 2, 0);
						pegStartPt = new Point3d(pegStartPt.X, pegStartPt.Y, 4 - (HPostDepth / 2 - 1) - 0.75);
						PegCol.Add(Module1.DrawPeg(pegStartPt, r, HPostDepth + 1.5, "Peg", "7", "", "", 90, 0, pts[0].X,						pts[0].Y, pts[0].Z));
						pegStartPt = Module1.AtPoint(pegStartPt, 0, ps, 0);
						PegCol.Add(Module1.DrawPeg(pegStartPt, r, HPostDepth + 1.5, "Peg", "8", "", "", 90, 0, pts[0].X,						pts[0].Y, pts[0].Z));
						pegStartPt = Module1.AtPoint(pegStartPt, 0, -(2 * ps), 0);
						PegCol.Add(Module1.DrawPeg(pegStartPt, r, HPostDepth + 1.5, "Peg", "9", "", "", 90, 0, pts[0].X,						pts[0].Y, pts[0].Z));

						pts.Clear();
						pts.Add(new Point3d(Startpoint.X + ((Width - 2) / 2), Startpoint.Y, HPostWidth + Baywidth));
						pts.Add(Module1.AtPoint(pts[0], 2, 0, 0));
						pts.Add(Module1.AtPoint(pts[1], 0, Depth, 0));
						pts.Add(Module1.AtPoint(pts[2], -2, 0, 0));
						TenonRight = Module1.DrawElement(pts, 4, "Tenon", "2", "");
						Module1.AddJoint(Timber, TenonRight, Module1.Joint.Tenon);

						pegStartPt = Module1.AtPoint(pts[0], 1.75, Depth / 2, 0);
						pegStartPt = new Point3d(pegStartPt.X, pegStartPt.Y, (Baywidth + HPostWidth) - ((HPostDepth / 2) + 1.75));
						PegCol.Add(Module1.DrawPeg(pegStartPt, r, HPostDepth + 1.5, "Peg", "10", "", "", 270,0 , pts[0].X,pts[0].Y, pts[0].Z));
						pegStartPt = Module1.AtPoint(pegStartPt, 0, ps, 0);
						PegCol.Add(Module1.DrawPeg(pegStartPt, r, HPostDepth + 1.5, "Peg", "11", "", "", 270, 0 , pts[0].X,pts[0].Y, pts[0].Z));
						pegStartPt = Module1.AtPoint(pegStartPt, 0, -(2 * ps), 0);
						PegCol.Add(Module1.DrawPeg(pegStartPt, r, HPostDepth + 1.5, "Peg", "12", "", "", 270, 0 , pts[0].X,pts[0].Y, pts[0].Z));

					}
					break;
			}
			// Phase 2: persist regeneration data
			Module1.PersistPegHandles(Timber, PegCol);
			Module1.SaveDrawContext(Timber, BuildContextJson(Side));
            // End markers: "N" and "F" at the two cross-section body corners in the XY plane.
            if (Module1.ShowEndMarkers) {
                double _mz = Module1.Make3D ? z + (Baywidth / 2) : 0;
                var em = new System.Collections.Generic.List<ObjectId>();
                em.Add(Module1.DrawEndMarker(new Point3d(_nearBodyPt.X, _nearBodyPt.Y + Depth / 2, _mz), "N"));
                em.Add(Module1.DrawEndMarker(new Point3d(_farBodyPt.X,  _farBodyPt.Y  + Depth / 2, _mz), "F"));
                Module1.PersistEndMarkerHandles(Timber, em);
            }
		}

        private string BuildContextJson(Sides side)
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return string.Format(c,
                "{{\"class\":\"HBBayGirt\"" +
                ",\"side\":{0},\"startX\":{1},\"startY\":{2}" +
                ",\"width\":{3},\"depth\":{4},\"baywidth\":{5}" +
                ",\"hPostWidth\":{6},\"hPostDepth\":{7},\"make3D\":{8}}}",
                (int)side,
                Startpoint.X, Startpoint.Y,
                Width, Depth, Baywidth,
                HPostWidth, HPostDepth,
                Module1.Make3D ? "true" : "false");
        }
    }
}
