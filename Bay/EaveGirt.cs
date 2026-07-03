using System.Collections.Generic;
using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using TimberFrameSuite.Standards;

namespace TimberDraw
{

	public class EaveGirt
	{
		public double TenonWidth = 2;
		public double Depth;
		public double Width;
		public double postWidth;
		public double postDepth;
		public double Length;
		public bool Make3d;
		public bool HasBayFlrGirt;
		public ObjectId TimberLeftId;
		public ObjectId TimberRightId;
		public ObjectId TenonLeftId;
		public ObjectId TenonRightId;
        public ObjectId TenonLeftFarId;
        public ObjectId TenonRightFarId;
		public List<ObjectId> PegCol = new();

        public void Draw()
		{
			double z = 0;
			string designation = null;
			double tenonZ = 0;
			if (Make3d) {
				z = postWidth;
			} else {
				z = 0;
			}
			Point3dCollection pts = new()
            {
                new Point3d(0, Module1.EaveHt, z),
                new Point3d(0, (Module1.EaveHt + 1) - Depth, z),
                new Point3d(Width, (Module1.EaveHt + 1) - Depth, z),
                new Point3d(Width, Module1.EaveHt + 1, z),
                new Point3d(1 / Module1.Pitch, Module1.EaveHt + 1, z)
            };
			designation = Module1.Arabic2roman(Properties.Settings.Default.BentNumber) + "-" + Module1.Arabic2roman(Properties.Settings.Default.BentNumber + 1);
            if (HasBayFlrGirt) designation += " UP";
			string sizeStr = (int)Width + "x" + (int)Depth + "x" + Module1.BuyLongFeet(Length);
			TimberLeftId = Module1.DrawElement(pts, Length, "Girt", ((char)(Module1.BentWallNumber)).ToString(), designation, sizeStr);
			if (Module1.HasJoinery) {
				pts.Clear();
				if (Make3d)
					tenonZ = z - 4;
				else
					tenonZ = 0;
				pts.Add(new Point3d((Width - TenonWidth) / 2, Module1.EaveHt + 1, tenonZ));
				pts.Add(Module1.AtPoint(pts[0], 0, -Depth, 0));
				pts.Add(Module1.AtPoint(pts[1], TenonWidth, 0, 0));
				pts.Add(Module1.AtPoint(pts[2], 0, Depth, 0));
				TenonLeftId = Module1.DrawElement(pts, 4, "Tenon", "1", "");
				Module1.AddJoint(TimberLeftId, TenonLeftId, Module1.Joint.Tenon);

				var peg = TFGPegStandards.GetPresetForTenonThickness(TenonWidth);
				double r = peg.DiameterInches / 2;
				double ps = peg.CalculatedSpacingInches;
				double maxPegY = Depth - peg.FirstPegSetbackInches;
				double minPegY = peg.FirstPegSetbackInches;
				double center = Depth / 2;
				Point3d pegStartPt = Module1.AtPoint(pts[2], -2.25, center, 0);
				pegStartPt = new Point3d(pegStartPt.X, pegStartPt.Y, -(((Width / 2 + 1) - 4) + 0.75));
				PegCol.Add(Module1.DrawPeg(pegStartPt, r, Width / 2 + 3.75, "Peg", "1", "", "", 90, 0, pts[2].X,pts[2].Y, pts[2].Z));
				if (center + ps <= maxPegY) {
					Point3d peg2Pt = Module1.AtPoint(pegStartPt, 0, ps, 0);
					PegCol.Add(Module1.DrawPeg(peg2Pt, r, Width / 2 + 3.75, "Peg", "2", "", "", 90, 0, pts[2].X,pts[2].Y, pts[2].Z));
				}
				if (center - ps >= minPegY) {
					Point3d peg3Pt = Module1.AtPoint(pegStartPt, 0, -ps, 0);
					PegCol.Add(Module1.DrawPeg(peg3Pt, r, Width / 2 + 3.75, "Peg", "3", "", "", 90, 0, pts[2].X,pts[2].Y, pts[2].Z));
				}

				pts.Clear();
				pts.Add(new Point3d((Width - TenonWidth) / 2, Module1.EaveHt + 1, postWidth + Length));
				pts.Add(Module1.AtPoint(pts[0], 0, -Depth, 0));
				pts.Add(Module1.AtPoint(pts[1], TenonWidth, 0, 0));
				pts.Add(Module1.AtPoint(pts[2], 0, Depth, 0));
				TenonLeftFarId = Module1.DrawElement(pts, 4, "Tenon", "2", "");

				Point3d pegFarStartPt = Module1.AtPoint(pts[2], -1.75, center, 0);
				pegFarStartPt = new Point3d(pegFarStartPt.X, pegFarStartPt.Y, (Length + postWidth) - (((Width / 2 + 1)) + 0.75));
				PegCol.Add(Module1.DrawPeg(pegFarStartPt, r, Width / 2 + 3.75, "Peg", "4", "", "", 90, 0, pts[2].X,
				pts[2].Y, pts[2].Z));
				if (center + ps <= maxPegY) {
					Point3d peg5Pt = Module1.AtPoint(pegFarStartPt, 0, ps, 0);
					PegCol.Add(Module1.DrawPeg(peg5Pt, r, Width / 2 + 3.75, "Peg", "5", "", "", 90, 0, pts[2].X,
					pts[2].Y, pts[2].Z));
				}
				if (center - ps >= minPegY) {
					Point3d peg6Pt = Module1.AtPoint(pegFarStartPt, 0, -ps, 0);
					PegCol.Add(Module1.DrawPeg(peg6Pt, r, Width / 2 + 3.75, "Peg", "6", "", "", 90, 0, pts[2].X,
					pts[2].Y, pts[2].Z));
				}

			}
			pts.Clear();
			pts.Add(new Point3d(Module1.Span, Module1.EaveHt, z));
			pts.Add(new Point3d(Module1.Span - (1 / Module1.Pitch), Module1.EaveHt + 1, z));
			pts.Add(new Point3d(Module1.Span - Width, Module1.EaveHt + 1, z));
			pts.Add(new Point3d(Module1.Span - Width, (Module1.EaveHt + 1) - Depth, z));
			pts.Add(new Point3d(Module1.Span, (Module1.EaveHt + 1) - Depth, z));
            designation = Module1.Arabic2roman(Properties.Settings.Default.BentNumber) + "-" + Module1.Arabic2roman(Properties.Settings.Default.BentNumber + 1);
            if (HasBayFlrGirt) designation += " UP";
			TimberRightId = Module1.DrawElement(pts, Length, "Girt", ((char)(Module1.BentWallNumber + 4)).ToString(), designation, sizeStr);
			if (Module1.HasJoinery) {
				pts.Clear();
				if (Make3d)
					tenonZ = z - 4;
				else
					tenonZ = 0;
				pts.Add(new Point3d(Module1.Span - ((Width - 2) / 2), Module1.EaveHt + 1, tenonZ));
				pts.Add(Module1.AtPoint(pts[0], -2, 0, 0));
				pts.Add(Module1.AtPoint(pts[1], 0, -Depth, 0));
				pts.Add(Module1.AtPoint(pts[2], 2, 0, 0));
				TenonRightId = Module1.DrawElement(pts, 4, "Tenon", "3", "");
				Module1.AddJoint(TimberRightId, TenonRightId, Module1.Joint.Tenon);

				var pegR = TFGPegStandards.GetPresetForTenonThickness(TenonWidth);
				double r = pegR.DiameterInches / 2;
				double ps = pegR.CalculatedSpacingInches;
				double maxPegY = Depth - pegR.FirstPegSetbackInches;
				double minPegY = pegR.FirstPegSetbackInches;
				double center = Depth / 2;
				Point3d pegStartPt = Module1.AtPoint(pts[2], 2.25, center, 0);
				pegStartPt = new Point3d(pegStartPt.X, pegStartPt.Y, -(((Width / 2 + 1) - 4) + 0.75));
				PegCol.Add(Module1.DrawPeg(pegStartPt, r, Width / 2 + 3.75, "Peg", "7", "", "", 270, 0, pts[2].X,pts[2].Y, pts[2].Z));
				if (center + ps <= maxPegY) {
					Point3d peg8Pt = Module1.AtPoint(pegStartPt, 0, ps, 0);
					PegCol.Add(Module1.DrawPeg(peg8Pt, r, Width / 2 + 3.75, "Peg", "8", "", "", 270, 0, pts[2].X,pts[2].Y, pts[2].Z));
				}
				if (center - ps >= minPegY) {
					Point3d peg9Pt = Module1.AtPoint(pegStartPt, 0, -ps, 0);
					PegCol.Add(Module1.DrawPeg(peg9Pt, r, Width / 2 + 3.75, "Peg", "9", "", "", 270, 0, pts[2].X,pts[2].Y, pts[2].Z));
				}

				pts.Clear();
				pts.Add(new Point3d(Module1.Span - ((Width - 2) / 2), Module1.EaveHt + 1, postWidth + Length));
				pts.Add(Module1.AtPoint(pts[0], -2, 0, 0));
				pts.Add(Module1.AtPoint(pts[1], 0, -Depth, 0));
				pts.Add(Module1.AtPoint(pts[2], 2, 0, 0));
				TenonRightFarId = Module1.DrawElement(pts, 4, "Tenon", "4", "");

				Point3d pegFar2StartPt = Module1.AtPoint(pts[2], 1.75, center, 0);
				pegFar2StartPt = new Point3d(pegFar2StartPt.X, pegFar2StartPt.Y, (Length + postWidth) - (((Width / 2 + 1)) + 0.75));
				PegCol.Add(Module1.DrawPeg(pegFar2StartPt, r, Width / 2 + 3.75, "Peg", "10", "", "", 270, 0, pts[2].X,pts[2].Y, pts[2].Z));
				if (center + ps <= maxPegY) {
					Point3d peg11Pt = Module1.AtPoint(pegFar2StartPt, 0, ps, 0);
					PegCol.Add(Module1.DrawPeg(peg11Pt, r, Width / 2 + 3.75, "Peg", "11", "", "", 270, 0, pts[2].X,pts[2].Y, pts[2].Z));
				}
				if (center - ps >= minPegY) {
					Point3d peg12Pt = Module1.AtPoint(pegFar2StartPt, 0, -ps, 0);
					PegCol.Add(Module1.DrawPeg(peg12Pt, r, Width / 2 + 3.75, "Peg", "12", "", "", 270, 0, pts[2].X,pts[2].Y, pts[2].Z));
				}

			}
			// Phase 2: store class key on both timbers to prevent wrong dispatch
			Module1.PersistPegHandles(TimberLeftId, PegCol);
			Module1.SaveDrawContext(TimberLeftId, BuildContextJson("left"));
			Module1.SaveDrawContext(TimberRightId, BuildContextJson("right"));
		}

        private string BuildContextJson(string side)
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return string.Format(c,
                "{{\"class\":\"EaveGirt\",\"side\":\"{0}\",\"width\":{1},\"depth\":{2},\"length\":{3},\"postWidth\":{4},\"postDepth\":{5},\"span\":{6},\"eaveHt\":{7},\"pitch\":{8},\"make3D\":{9}}}",
                side, Width, Depth, Length, postWidth, postDepth,
                Module1.Span, Module1.EaveHt, Module1.Pitch, Make3d ? "true" : "false");
        }
		public enum Side
		{
			Left = 0,
			Right = 1
		}
		public void AddMortise(ObjectId MortiseId, Side WhichSide)
		{
			switch (WhichSide) {
				case Side.Left:
                    Module1.AddJoint(TimberLeftId, MortiseId, Module1.Joint.Mortise);
					break;
				case Side.Right:
                    Module1.AddJoint(TimberRightId, MortiseId, Module1.Joint.Mortise);
					break;
			}
			Module1.DeleteJoint(MortiseId);
		}
	}
}
