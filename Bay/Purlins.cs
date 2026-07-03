using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{

	public class Purlins : IDisposable
	{
        public void Dispose()
        {
            ExtensionLeftCol.Dispose();
            ExtensionRightCol.Dispose();
            GC.SuppressFinalize(this);
        }

		public ObjectIdCollection ExtensionLeftCol = new();
		public ObjectIdCollection ExtensionRightCol = new();
        public List<(ObjectId ExtId, ObjectId PurlinId)> ExtensionLeftFarList  = new();
        public List<(ObjectId ExtId, ObjectId PurlinId)> ExtensionRightFarList = new();
		public ObjectId PurlinId;

		public ObjectId ExtensionId;
		public Purlins(double Depth, double Width, double rafterWidth, double rafterDepth, double Length, bool Make3d)
		{
			string sizeStr = (int)Depth + "x" + (int)Width + "x" + Module1.BuyLongFeet(Length);
			double z = 0;
			if (Make3d) {
				z = rafterWidth;
			} else {
				z = 0;
			}
			Point3d spt = new(Module1.Span / 2, Module1.EaveHt + ((Module1.Span / 2) * Module1.Pitch), 0);
			int purlinCnt = 1;
			double rafterlength = (Module1.Span / 2) / Math.Cos(Module1.Beta);
			
			//Left side purlins
			double movedist = 48;
			Point3d spttmp = Module1.PolarPoint(spt, Module1.rad(180) + Module1.Beta, 48);
			while (movedist < rafterlength) {
				Point3d pt1 = Module1.PolarPoint(spttmp, Module1.Beta, (rafterDepth * Math.Tan(Module1.Beta)) - (Width / 2));
				Point3d pt2 = Module1.PolarPoint(pt1, Module1.Beta, Width);
				Point3d pt3 = Module1.PolarPoint(pt2, Module1.rad(270) + Module1.Beta, Depth);
				Point3d pt4 = Module1.PolarPoint(pt1, Module1.rad(270) + Module1.Beta, Depth);
				Point3dCollection pts = new()
                {
                    new Point3d(pt1.X, pt1.Y, z),
                    new Point3d(pt4.X, pt4.Y, z),
                    new Point3d(pt3.X, pt3.Y, z),
                    new Point3d(pt2.X, pt2.Y, z)
                };
				PurlinId = Module1.DrawElement(pts, Length, "Purlin", Convert.ToString(Module1.Arabic2roman(Properties.Settings.Default.BentNumber)), "#" + Convert.ToString(purlinCnt), sizeStr);
				if (Module1.HasJoinery) {
					//Near Bent
					pts.Clear();
					pts.Add(new Point3d(pt1.X, pt1.Y, z - 1));
					pts.Add(new Point3d(pt4.X, pt4.Y, z - 1));
					pts.Add(new Point3d(pt3.X, pt3.Y, z - 1));
					pts.Add(new Point3d(pt2.X, pt2.Y, z - 1));
					ExtensionId = Module1.DrawElement(pts, 1, "DoveTail", "1", "");
					ExtensionLeftCol.Add(ExtensionId);
                    Module1.AddJoint(PurlinId, ExtensionId, Module1.Joint.Tenon);
					//Far Bent
					pts.Clear();
					pts.Add(new Point3d(pt1.X, pt1.Y, z + Length));
					pts.Add(new Point3d(pt4.X, pt4.Y, z + Length));
					pts.Add(new Point3d(pt3.X, pt3.Y, z + Length));
					pts.Add(new Point3d(pt2.X, pt2.Y, z + Length));
					ExtensionId = Module1.DrawElement(pts, 1, "DoveTail", "2", "");
                    ExtensionLeftFarList.Add((ExtensionId, PurlinId));
                    Module1.AddJoint(PurlinId, ExtensionId, Module1.Joint.Tenon);
				}
				pts.Clear();
				spttmp = Module1.PolarPoint(pt1, Module1.rad(180) + Module1.Beta, 48);
				movedist += 48;
				purlinCnt++;
			}
			
			//Right side purlins
			movedist = 48;
			spttmp = Module1.PolarPoint(spt, Module1.rad(360) - Module1.Beta, 48);
			while (movedist < rafterlength) {
				Point3d pt1 = Module1.PolarPoint(spttmp, Module1.rad(180) - Module1.Beta, (rafterDepth * Math.Tan(Module1.Beta)) - (Width / 2));
				Point3d pt2 = Module1.PolarPoint(pt1, Module1.rad(180) - Module1.Beta, Width);
				Point3d pt3 = Module1.PolarPoint(pt2, Module1.rad(270) - Module1.Beta, Depth);
				Point3d pt4 = Module1.PolarPoint(pt1, Module1.rad(270) - Module1.Beta, Depth);
				Point3dCollection pts = new()
                {
                    new Point3d(pt1.X, pt1.Y, z),
                    new Point3d(pt2.X, pt2.Y, z),
                    new Point3d(pt3.X, pt3.Y, z),
                    new Point3d(pt4.X, pt4.Y, z)
                };
				PurlinId = Module1.DrawElement(pts, Length, "Purlin", Convert.ToString(Module1.Arabic2roman(Properties.Settings.Default.BentNumber)), "#" + Convert.ToString(purlinCnt), sizeStr);
				if (Module1.HasJoinery) {
					//Near Bent
					pts.Clear();
					pts.Add(new Point3d(pt1.X, pt1.Y, z - 1));
					pts.Add(new Point3d(pt2.X, pt2.Y, z - 1));
					pts.Add(new Point3d(pt3.X, pt3.Y, z - 1));
					pts.Add(new Point3d(pt4.X, pt4.Y, z - 1));
					ExtensionId = Module1.DrawElement(pts, 1, "DoveTail", "3", "");
					ExtensionRightCol.Add(ExtensionId);
                    Module1.AddJoint(PurlinId, ExtensionId, Module1.Joint.Tenon);
					//Far Bent
					pts.Clear();
					pts.Add(new Point3d(pt1.X, pt1.Y, z + Length));
					pts.Add(new Point3d(pt2.X, pt2.Y, z + Length));
					pts.Add(new Point3d(pt3.X, pt3.Y, z + Length));
					pts.Add(new Point3d(pt4.X, pt4.Y, z + Length));
					ExtensionId = Module1.DrawElement(pts, 1, "DoveTail", "4", "");
                    ExtensionRightFarList.Add((ExtensionId, PurlinId));
                    Module1.AddJoint(PurlinId, ExtensionId, Module1.Joint.Tenon);
				}
				pts.Clear();
				spttmp = Module1.PolarPoint(pt1, Module1.rad(360) - Module1.Beta, 48);
				movedist += 48;
				purlinCnt++;
			}
		}
	}
}
