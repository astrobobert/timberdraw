using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{

    public class Commons : IDisposable
	{
        public void Dispose()
        {
            EaveHousingLeft.Dispose();
            RidgeHousingLeft.Dispose();
            EaveHousingRight.Dispose();
            RidgeHousingRight.Dispose();
            GC.SuppressFinalize(this);
        }

		private const double TenonWidth = 2;
		private const double RafterSpacingDivisor = 48;
		public double commonWidth;
		public double commonDepth;
		public ObjectIdCollection EaveHousingLeft = new();
		public ObjectIdCollection RidgeHousingLeft = new();
		public ObjectIdCollection EaveHousingRight = new();
		public ObjectIdCollection RidgeHousingRight = new();
		public ObjectId CommonLeftId;
		public ObjectId CommonRightId;

		public ObjectId HousingId;
		public Commons(double Depth, double Width, double postWidth, double postDepth, double RidgeWidth, double EaveGirtWidth, double BayWidth, bool Make3d, bool HasRidge)
		{
			commonWidth = Width;
			commonDepth = Depth;
			int spaces = 0;
			double spacing = 0;
			double halfSpan = Module1.Span / 2;
			spaces = Convert.ToInt32(Math.Round(BayWidth / RafterSpacingDivisor));
			spacing = (BayWidth - (commonWidth * (spaces - 1))) / spaces;
			double z = 0;
			if (Make3d) {
				z = postWidth + spacing;
			} else {
				z = 0;
				spaces = 2;
			}
			string sizeStr = (int)commonWidth + "x" + (int)commonDepth + "x" + Module1.BuyLongFeet((Module1.Span / 2.0) / Math.Cos(Module1.Beta));
			int commonCnt = 1;
			Point3dCollection pts = new();
			for (int i = 1; i <= spaces - 1; i++) {
				if (HasRidge) {
					//Draw Left Common
					pts.Add(new Point3d(1 / Module1.Pitch, Module1.EaveHt + 1, z));
					pts.Add(new Point3d(EaveGirtWidth, Module1.EaveHt + 1, z));
					pts.Add(new Point3d(EaveGirtWidth, Module1.EaveHt - (Depth / Math.Cos(Module1.Beta)) + (EaveGirtWidth * Module1.Pitch), z));
					pts.Add(new Point3d((Module1.Span / 2) - (RidgeWidth / 2), Module1.EaveHt + (((Module1.Span / 2) - (RidgeWidth / 2)) * Module1.Pitch) - (Depth / Math.Cos(Module1.Beta)), z));
					pts.Add(new Point3d((Module1.Span / 2) - (RidgeWidth / 2), Module1.EaveHt + (((Module1.Span / 2) - (RidgeWidth / 2)) * Module1.Pitch), z));
					CommonLeftId = Module1.DrawElement(pts, Width, "CRafter", Convert.ToString(Module1.Arabic2roman(Properties.Settings.Default.BentNumber)), "#" + Convert.ToString(commonCnt), sizeStr);
					if (Module1.HasJoinery) {
						pts.Clear();
						//Draw Left Eave Housing
						pts.Add(new Point3d(0, Module1.EaveHt, z));
						pts.Add(Module1.AtPoint(pts[0], EaveGirtWidth - 1, 0, 0));
						pts.Add(new Point3d(EaveGirtWidth - 1, Module1.EaveHt - (Depth / Math.Cos(Module1.Beta)) + (EaveGirtWidth * Module1.Pitch), z));
						pts.Add(new Point3d(EaveGirtWidth, Module1.EaveHt - (Depth / Math.Cos(Module1.Beta)) + (EaveGirtWidth * Module1.Pitch), z));
						pts.Add(new Point3d(EaveGirtWidth, Module1.EaveHt + 1, z));
						pts.Add(new Point3d(1 / Module1.Pitch, Module1.EaveHt + 1, z));
						HousingId = Module1.DrawElement(pts, Width, "Housing", "1", "A");
						EaveHousingLeft.Add(HousingId);
                        Module1.AddJoint(CommonLeftId, HousingId, Module1.Joint.Tenon);
						pts.Clear();
						//Draw Left Ridge Housing
						pts.Add(new Point3d((Module1.Span / 2) - (RidgeWidth / 2), Module1.EaveHt + (((Module1.Span / 2) - (RidgeWidth / 2)) * Module1.Pitch) - (Depth / Math.Cos(Module1.Beta)), z));
						pts.Add(Module1.AtPoint(pts[0], 1, 1 * Module1.Pitch, 0));
						pts.Add(Module1.AtPoint(pts[1], 0, Depth / Math.Cos(Module1.Beta), 0));
						pts.Add(new Point3d((Module1.Span / 2) - (RidgeWidth / 2), Module1.EaveHt + (((Module1.Span / 2) - (RidgeWidth / 2)) * Module1.Pitch), z));
						HousingId = Module1.DrawElement(pts, Width, "Housing", "2", "A");
						RidgeHousingLeft.Add(HousingId);
                        Module1.AddJoint(CommonLeftId, HousingId, Module1.Joint.Tenon);
					}
					commonCnt++;
					pts.Clear();
					//Draw Right Common
					pts.Add(new Point3d(Module1.Span - (1 / Module1.Pitch), Module1.EaveHt + 1, z));
					pts.Add(new Point3d(Module1.Span - ((Module1.Span / 2) - (RidgeWidth / 2)), Module1.EaveHt + (((Module1.Span / 2) - (RidgeWidth / 2)) * Module1.Pitch), z));
					pts.Add(new Point3d(Module1.Span - ((Module1.Span / 2) - (RidgeWidth / 2)), Module1.EaveHt + (((Module1.Span / 2) - (RidgeWidth / 2)) * Module1.Pitch) - (Depth / Math.Cos(Module1.Beta)), z));
					pts.Add(new Point3d(Module1.Span - EaveGirtWidth, Module1.EaveHt - (Depth / Math.Cos(Module1.Beta)) + (EaveGirtWidth * Module1.Pitch), z));
					pts.Add(new Point3d(Module1.Span - EaveGirtWidth, Module1.EaveHt + 1, z));
					CommonRightId = Module1.DrawElement(pts, Width, "CRafter", Convert.ToString(Module1.Arabic2roman(Properties.Settings.Default.BentNumber)), "#" + Convert.ToString(commonCnt), sizeStr);
					if (Module1.HasJoinery) {
						pts.Clear();
						//Draw Right Eave Housing
						pts.Add(new Point3d(Module1.Span - (1 / Module1.Pitch), Module1.EaveHt + 1, z));
						pts.Add(new Point3d(Module1.Span - EaveGirtWidth, Module1.EaveHt + 1, z));
						pts.Add(new Point3d(Module1.Span - EaveGirtWidth, Module1.EaveHt - (Depth / Math.Cos(Module1.Beta)) + (EaveGirtWidth * Module1.Pitch), z));
						pts.Add(Module1.AtPoint(pts[2], 1, 0, 0));
						pts.Add(new Point3d(Module1.Span - (EaveGirtWidth - 1), Module1.EaveHt, z));
						pts.Add(new Point3d(Module1.Span, Module1.EaveHt, z));
						HousingId = Module1.DrawElement(pts, Width, "Housing", "1", "");
						EaveHousingRight.Add(HousingId);
                        Module1.AddJoint(CommonRightId, HousingId, Module1.Joint.Tenon);
						pts.Clear();
						//Draw Right Ridge Housing
						pts.Add(new Point3d(Module1.Span - ((Module1.Span / 2) - (RidgeWidth / 2)), Module1.EaveHt + (((Module1.Span / 2) - (RidgeWidth / 2)) * Module1.Pitch), z));
						pts.Add(Module1.AtPoint(pts[0], -1, 1 * Module1.Pitch, 0));
						pts.Add(Module1.AtPoint(pts[1], 0, -(Depth / Math.Cos(Module1.Beta)), 0));
						pts.Add(new Point3d(Module1.Span - ((Module1.Span / 2) - (RidgeWidth / 2)), Module1.EaveHt + (((Module1.Span / 2) - (RidgeWidth / 2)) * Module1.Pitch) - (Depth / Math.Cos(Module1.Beta)), z));
						HousingId = Module1.DrawElement(pts, Width, "Housing", "2", "");
						RidgeHousingRight.Add(HousingId);
                        Module1.AddJoint(CommonRightId, HousingId, Module1.Joint.Tenon);
					}
				} else {
					//Draw Left Common
					pts.Add(new Point3d(1 / Module1.Pitch, Module1.EaveHt + 1, z));
					pts.Add(new Point3d(EaveGirtWidth, Module1.EaveHt + 1, z));
					pts.Add(new Point3d(EaveGirtWidth, Module1.EaveHt - (Depth / Math.Cos(Module1.Beta)) + (EaveGirtWidth * Module1.Pitch), z));
					pts.Add(new Point3d(halfSpan, Module1.TOH + (((Module1.Span / 2) - Depth) * Module1.Pitch), z));
					pts.Add(new Point3d(halfSpan - (1 * Math.Sin(Module1.Beta)), Module1.TOH + ((halfSpan - Depth) * Module1.Pitch) + (1 * Math.Cos(Module1.Beta)), z));
					pts.Add(new Point3d(halfSpan + Depth / Math.Sin(Module1.Beta) / 2, (Module1.EaveHt + (halfSpan * Module1.Pitch)) - ((Depth / Math.Cos(Module1.Beta)) / 2), z));
					pts.Add(new Point3d(halfSpan, Module1.EaveHt + (halfSpan * Module1.Pitch), z));
					CommonLeftId = Module1.DrawElement(pts, Width, "CRafter", Convert.ToString(Module1.Arabic2roman(Properties.Settings.Default.BentNumber)), "#" + Convert.ToString(commonCnt), sizeStr);
					if (Module1.HasJoinery) {
						pts.Clear();
						//Draw Left Tenon
						pts.Add(new Point3d(0, Module1.EaveHt, z));
						pts.Add(Module1.AtPoint(pts[0], EaveGirtWidth - 1, 0, 0));
						pts.Add(new Point3d(EaveGirtWidth - 1, Module1.EaveHt - (Depth / Math.Cos(Module1.Beta)) + (EaveGirtWidth * Module1.Pitch), z));
						pts.Add(new Point3d(EaveGirtWidth, Module1.EaveHt - (Depth / Math.Cos(Module1.Beta)) + (EaveGirtWidth * Module1.Pitch), z));
						pts.Add(new Point3d(EaveGirtWidth, Module1.EaveHt + 1, z));
						pts.Add(new Point3d(1 / Module1.Pitch, Module1.EaveHt + 1, z));
						HousingId = Module1.DrawElement(pts, Width, "Housing", "1", "");
						EaveHousingLeft.Add(HousingId);
                        Module1.AddJoint(CommonLeftId, HousingId, Module1.Joint.Tenon);
					}
					commonCnt++;
					//Draw Right Common
					pts.Clear();
					pts.Add(new Point3d(Module1.Span - (1 / Module1.Pitch), Module1.EaveHt + 1, z));
					pts.Add(new Point3d(halfSpan, Module1.EaveHt + (halfSpan * Module1.Pitch), z));
					pts.Add(new Point3d(halfSpan + Depth / Math.Sin(Module1.Beta) / 2, (Module1.EaveHt + (halfSpan * Module1.Pitch)) - ((Depth / Math.Cos(Module1.Beta)) / 2), z));
					pts.Add(new Point3d(halfSpan - (1 * Math.Sin(Module1.Beta)), Module1.TOH + ((halfSpan - Depth) * Module1.Pitch) + (1 * Math.Cos(Module1.Beta)), z));
					pts.Add(new Point3d(halfSpan, Module1.TOH + (((Module1.Span / 2) - Depth) * Module1.Pitch), z));
					pts.Add(new Point3d(Module1.Span - EaveGirtWidth, Module1.EaveHt - (Depth / Math.Cos(Module1.Beta)) + (EaveGirtWidth * Module1.Pitch), z));
					pts.Add(new Point3d(Module1.Span - EaveGirtWidth, Module1.EaveHt + 1, z));
					CommonRightId = Module1.DrawElement(pts, Width, "CRafter", Convert.ToString(Module1.Arabic2roman(Properties.Settings.Default.BentNumber)), "#" + Convert.ToString(commonCnt), sizeStr);
					if (Module1.HasJoinery) {
						pts.Clear();
						//Draw Right Eave Housing
						pts.Add(new Point3d(Module1.Span - (1 / Module1.Pitch), Module1.EaveHt + 1, z));
						pts.Add(new Point3d(Module1.Span - EaveGirtWidth, Module1.EaveHt + 1, z));
						pts.Add(new Point3d(Module1.Span - EaveGirtWidth, Module1.EaveHt - (Depth / Math.Cos(Module1.Beta)) + (EaveGirtWidth * Module1.Pitch), z));
						pts.Add(Module1.AtPoint(pts[2], 1, 0, 0));
						pts.Add(new Point3d(Module1.Span - (EaveGirtWidth - 1), Module1.EaveHt, z));
						pts.Add(new Point3d(Module1.Span, Module1.EaveHt, z));
						HousingId = Module1.DrawElement(pts, -Width, "Housing", "1", "");
						EaveHousingRight.Add(HousingId);
                        Module1.AddJoint(CommonRightId, HousingId, Module1.Joint.Tenon);
						double tenonZ = 0;
						if (Make3d)
							tenonZ = z + ((Width - 2) / 2);
						else
							tenonZ = 0;
						pts.Clear();
						//Draw Left Tenon
						pts.Add(new Point3d(halfSpan - (1 * Math.Sin(Module1.Beta)), Module1.TOH + ((halfSpan - Depth) * Module1.Pitch) + (1 * Math.Cos(Module1.Beta)), tenonZ));
						pts.Add(new Point3d(halfSpan + Depth / Math.Sin(Module1.Beta) / 2, (Module1.EaveHt + (halfSpan * Module1.Pitch)) - ((Depth / Math.Cos(Module1.Beta)) / 2), tenonZ));
						pts.Add(Module1.PolarPoint(pts[1], Math.PI - Module1.Beta, 4 / Math.Cos((Math.PI / 2) - (Module1.Beta * 2))));
						pts.Add(Module1.PolarPoint(pts[0], Module1.Beta + (Math.PI / 2), 4));
						HousingId = Module1.DrawElement(pts, TenonWidth, "Tenon", "8", "");
                        Module1.AddJoint(CommonRightId, HousingId, Module1.Joint.Tenon);
                        Module1.AddJoint(CommonLeftId, HousingId, Module1.Joint.Mortise);
					}
				}
				commonCnt++;
				pts.Clear();
				z = z + spacing + Width;
			}
		}

	}
}
