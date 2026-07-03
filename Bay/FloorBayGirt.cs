using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using TimberFrameSuite.Standards;

namespace TimberDraw
{
    public class FloorBayGirt
    {
        public double TenonWidth = 2;
        public Point3d Startpoint;
        public double Width;
        public double Depth;
        public double Length;
        public double Height;
        public double postWidth;
        public string BentNumber;
        public string Designation;
        public string Type;
        public ObjectId TimberLeftId;
        public ObjectId TimberRightId;
        public ObjectId TenonLeft1Id;
        public ObjectId TenonLeft2Id;
        public ObjectId TenonRight1Id;
        public ObjectId TenonRight2Id;
        public int HammerBeamType;
        public List<ObjectId> PegCol = new();

        const int HammerBeamBent = 0;
        const int KingPostBent   = 1;
        const int QueenPostBent  = 2;
        const int KingPostTruss  = 3;
        const int QueenPostTruss = 4;

        public void Draw()
        {
            double tenonZ = Module1.Make3D ? Startpoint.Z - 4 : 0;
            string sizeStr = (int)Width + "x" + (int)Depth + "x" + Module1.BuyLongFeet(Length);

            TimberLeftId = DrawGirt(Startpoint, BentNumber, Designation, sizeStr);
            if (Module1.HasJoinery)
                DrawTenonPair(Startpoint.X, tenonZ, TimberLeftId,
                              ref TenonLeft1Id, ref TenonLeft2Id);

            var rightOrigin = new Point3d(Module1.Span - Width, Height, postWidth);
            TimberRightId = DrawGirt(rightOrigin, RightBentNumber(), Designation, sizeStr);
            if (Module1.HasJoinery)
                DrawTenonPair(Module1.Span - Width, tenonZ, TimberRightId,
                              ref TenonRight1Id, ref TenonRight2Id);

            if (Module1.HasJoinery)
            {
                var peg      = TFGPegStandards.GetPresetForTenonThickness(TenonWidth);
                double r         = peg.DiameterInches / 2;
                double y1        = peg.FirstPegSetbackInches;
                double y2        = y1 + peg.CalculatedSpacingInches;
                double y3        = y1 + 2 * peg.CalculatedSpacingInches;
                double maxPegPos = Depth - peg.FirstPegSetbackInches;
                double nearZ     = tenonZ + 2;
                double farZ      = Startpoint.Z + Length + 2;
                double leftX     = Startpoint.X - 0.75;
                double rightX    = Module1.Span - Width - 0.75;

                // Left girt -- near bent
                PegCol.Add(Module1.DrawPeg(new Point3d(leftX, Startpoint.Y - y1, nearZ), r, Width + 1.5, "Peg", BentNumber, Designation, "", 90, 0, leftX, Startpoint.Y - y1, nearZ));
                if (y2 <= maxPegPos) PegCol.Add(Module1.DrawPeg(new Point3d(leftX, Startpoint.Y - y2, nearZ), r, Width + 1.5, "Peg", BentNumber, Designation, "", 90, 0, leftX, Startpoint.Y - y2, nearZ));
                if (y3 <= maxPegPos) PegCol.Add(Module1.DrawPeg(new Point3d(leftX, Startpoint.Y - y3, nearZ), r, Width + 1.5, "Peg", BentNumber, Designation, "", 90, 0, leftX, Startpoint.Y - y3, nearZ));
                // Left girt -- far bent
                PegCol.Add(Module1.DrawPeg(new Point3d(leftX, Startpoint.Y - y1, farZ), r, Width + 1.5, "Peg", BentNumber, Designation, "", 90, 0, leftX, Startpoint.Y - y1, farZ));
                if (y2 <= maxPegPos) PegCol.Add(Module1.DrawPeg(new Point3d(leftX, Startpoint.Y - y2, farZ), r, Width + 1.5, "Peg", BentNumber, Designation, "", 90, 0, leftX, Startpoint.Y - y2, farZ));
                if (y3 <= maxPegPos) PegCol.Add(Module1.DrawPeg(new Point3d(leftX, Startpoint.Y - y3, farZ), r, Width + 1.5, "Peg", BentNumber, Designation, "", 90, 0, leftX, Startpoint.Y - y3, farZ));

                // Right girt -- near bent
                PegCol.Add(Module1.DrawPeg(new Point3d(rightX, Startpoint.Y - y1, nearZ), r, Width + 1.5, "Peg", RightBentNumber(), Designation, "", 90, 0, rightX, Startpoint.Y - y1, nearZ));
                if (y2 <= maxPegPos) PegCol.Add(Module1.DrawPeg(new Point3d(rightX, Startpoint.Y - y2, nearZ), r, Width + 1.5, "Peg", RightBentNumber(), Designation, "", 90, 0, rightX, Startpoint.Y - y2, nearZ));
                if (y3 <= maxPegPos) PegCol.Add(Module1.DrawPeg(new Point3d(rightX, Startpoint.Y - y3, nearZ), r, Width + 1.5, "Peg", RightBentNumber(), Designation, "", 90, 0, rightX, Startpoint.Y - y3, nearZ));
                // Right girt -- far bent
                PegCol.Add(Module1.DrawPeg(new Point3d(rightX, Startpoint.Y - y1, farZ), r, Width + 1.5, "Peg", RightBentNumber(), Designation, "", 90, 0, rightX, Startpoint.Y - y1, farZ));
                if (y2 <= maxPegPos) PegCol.Add(Module1.DrawPeg(new Point3d(rightX, Startpoint.Y - y2, farZ), r, Width + 1.5, "Peg", RightBentNumber(), Designation, "", 90, 0, rightX, Startpoint.Y - y2, farZ));
                if (y3 <= maxPegPos) PegCol.Add(Module1.DrawPeg(new Point3d(rightX, Startpoint.Y - y3, farZ), r, Width + 1.5, "Peg", RightBentNumber(), Designation, "", 90, 0, rightX, Startpoint.Y - y3, farZ));
            }
            // Phase 2: store class key on both timbers (prevents wrong dispatch to BentGirt "Girt" case)
            Module1.PersistPegHandles(TimberLeftId, PegCol);
            Module1.SaveDrawContext(TimberLeftId, BuildContextJson("left"));
            Module1.SaveDrawContext(TimberRightId, BuildContextJson("right"));
        }

        private string BuildContextJson(string side)
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return string.Format(c,
                "{{\"class\":\"FloorBayGirt\",\"side\":\"{0}\",\"width\":{1},\"depth\":{2},\"length\":{3},\"height\":{4},\"postWidth\":{5},\"span\":{6},\"make3D\":{7}}}",
                side, Width, Depth, Length, Height, postWidth,
                Module1.Span, Module1.Make3D ? "true" : "false");
        }

        private ObjectId DrawGirt(Point3d origin, string bentNumber, string designation, string size)
        {
            var pts = new Point3dCollection
            {
                origin,
                Module1.AtPoint(origin, 0, -Depth, 0),
                Module1.AtPoint(origin, Width, -Depth, 0),
                Module1.AtPoint(origin, Width, 0, 0)
            };
            return Module1.DrawElement(pts, Length, Type, bentNumber, designation, size);
        }

        private void DrawTenonPair(double baseX, double tenonZ, ObjectId timberId,
                                   ref ObjectId tenon1Id, ref ObjectId tenon2Id)
        {
            double x = baseX + (Width - TenonWidth) / 2;
            tenon1Id = DrawTenon(x, Startpoint.Y, tenonZ, "1");
            Module1.AddJoint(timberId, tenon1Id, Module1.Joint.Tenon);
            tenon2Id = DrawTenon(x, Startpoint.Y, Startpoint.Z + Length, "2");
            Module1.AddJoint(timberId, tenon2Id, Module1.Joint.Tenon);
        }

        private ObjectId DrawTenon(double x, double y, double z, string number)
        {
            var pts = new Point3dCollection
            {
                new Point3d(x, y, z)
            };
            pts.Add(Module1.AtPoint(pts[0], 0,  -Depth, 0));
            pts.Add(Module1.AtPoint(pts[1], 2,   0,     0));
            pts.Add(Module1.AtPoint(pts[2], 0,   Depth, 0));
            return Module1.DrawElement(pts, 4, "Tenon", number, "");
        }

        private string RightBentNumber()
        {
            switch (Module1.TrussType)
            {
                case KingPostBent:  return ((char)(Module1.BentWallNumber + 4)).ToString();
                case QueenPostBent: return ((char)(Module1.BentWallNumber + 3)).ToString();
                case HammerBeamBent:
                    if (HammerBeamType == 0) return ((char)(Module1.BentWallNumber + 4)).ToString();
                    if (HammerBeamType == 1) return ((char)(Module1.BentWallNumber + 6)).ToString();
                    break;
            }
            return BentNumber;
        }

        public enum Side { Left = 0, Right = 1 }

        public void AddMortise(ObjectId mortiseId, Side side)
        {
            ObjectId timberId = side == Side.Left ? TimberLeftId : TimberRightId;
            Module1.AddJoint(timberId, mortiseId, Module1.Joint.Mortise);
            Module1.DeleteJoint(mortiseId);
        }
    }
}
