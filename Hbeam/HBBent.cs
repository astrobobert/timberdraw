using System;
using System.Windows.Forms;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.DatabaseServices;

namespace TimberDraw
{

	public class HBBent
	{
		public double HBeamExtension = 4;
		public int HBDivisor;

		public double BayWidth;
		public PostLeft PLeft = new();
		public PostRight PRight = new();
		public bool HasPost;
		public double postWidth;

		public double postDepth;
		public bool HasHBeam;
		public double HBeamWidth;

		public double HBeamDepth;
		public bool HasHPost;
		public double HPostWidth;

		public double HPostDepth;
		public HBKpost HBKP = new();
		public bool HasKPost;
		public double KpostWidth;

		public double KpostDepth;
		public KPRafterLeft HBRLeft = new();
		public KPRafterRight HBRRight = new();
		public bool HasRafter;
		public double RafterWidth;

		public double RafterDepth;
		public bool HasHBGirt;
		public double HBGirtWidth;

		public double HBGirtDepth;
		public bool HasBrace;
		public double BraceWidth;
		public double BraceDepth;

		public double BraceLength;
		public bool HasBayGirt;
		public double BayGirtWidth;

		public double BayGirtDepth;
		public bool HasBGBrace;
		public double BGBraceWidth;
		public double BGBraceDepth;

		public double BGBraceLength;
		public bool HasFlrGirt;
		public double FlrGirtWidth;
		public double FlrGirtDepth;

		public double FlrGirtHt;
		public bool HasFlrBrace;
		public double FlrBraceWidth;
		public double FlrBraceDepth;

		public double FlrBraceLength;
		public double Var1;
		public double Var2;

		public double Var3;
		public bool DataReady()
		{
			bool ready = true;
			if (HBDivisor != 4 & HBDivisor != 6)
				ready = false;
			if (HasPost)
				if (postWidth <= 0 | postDepth <= 0)
					ready = false;
			if (HasHBeam)
				if (HBeamWidth <= 0 | HBeamDepth <= 0)
					ready = false;
			if (HasHPost)
				if (HPostWidth <= 0 | HPostDepth <= 0)
					ready = false;
			if (HasKPost)
				if (KpostWidth <= 0 | KpostDepth <= 0)
					ready = false;
			if (HasRafter)
				if (RafterWidth <= 0 | RafterDepth <= 0)
					ready = false;
			if (HasHBGirt)
				if (HBGirtWidth <= 0 | HBGirtDepth <= 0)
					ready = false;
			if (HasBrace)
				if (BraceWidth <= 0 | BraceDepth <= 0 | BraceLength <= 0)
					ready = false;
			if (HasBayGirt)
				if (BayWidth <= 0 | BayGirtWidth <= 0 | BayGirtDepth <= 0)
					ready = false;
			if (HasBGBrace)
				if (BGBraceWidth <= 0 | BGBraceDepth <= 0 | BGBraceLength <= 0)
					ready = false;
			if (HasFlrGirt)
				if (FlrGirtWidth <= 0 | FlrGirtDepth <= 0 | FlrGirtHt <= 0)
					ready = false;
			if (HasFlrBrace)
				if (FlrBraceWidth <= 0 | FlrBraceDepth <= 0 | FlrBraceLength <= 0)
					ready = false;
			if (!ready)
				MessageBox.Show("Error Hammer Beam Data Not ready", "TimberDraw", MessageBoxButtons.OK, MessageBoxIcon.Error);
			return ready;
		}


		public void Draw()
		{
			string bentNum = Convert.ToString(Properties.Settings.Default.BentNumber);
			BentNetwork.BeginBent(bentNum);

			if (HasPost) {
				//Draw Left Post
				PLeft.Width = postWidth;
				PLeft.Depth = postDepth;
				PLeft.Type = "Post";
				PLeft.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
				PLeft.Designation = ((char)(Module1.BentWallNumber)).ToString();
				PLeft.Draw();

				//Draw Right Post
				PRight.Width = postWidth;
				PRight.Depth = postDepth;
				PRight.Type = "Post";
				PRight.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
				PRight.Designation = ((char)(Module1.BentWallNumber + HBDivisor)).ToString();
				PRight.Draw();
			}

			if (HasRafter) {
				//Draw Left Rafter
				if (Module1.Prise < 4)
					HBRLeft.KpostRafterSitDepth = 2;
				else
					HBRLeft.KpostRafterSitDepth = 3;
				HBRLeft.Width = RafterWidth;
				HBRLeft.Depth = RafterDepth;
				HBRLeft.postDepth = postDepth;
				HBRLeft.KPostDepth = KpostDepth;
				HBRLeft.Type = "Rafter";
				HBRLeft.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
				HBRLeft.Designation = ((char)(Module1.BentWallNumber)).ToString();
				HBRLeft.StartPoint = new Point3d(postDepth, Module1.TOH, 0);
				HBRLeft.Draw();

				//Draw Right Rafter
				if (Module1.Prise < 4)
					HBRRight.KpostRafterSitDepth = 2;
				else
					HBRRight.KpostRafterSitDepth = 3;
				HBRRight.Width = RafterWidth;
				HBRRight.Depth = RafterDepth;
				HBRRight.postDepth = postDepth;
				HBRRight.KPostDepth = KpostDepth;
				HBRRight.Type = "Rafter";
				HBRRight.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
				HBRRight.Designation = ((char)(Module1.BentWallNumber + HBDivisor)).ToString();
				HBRRight.StartPoint = new Point3d(Module1.Span - postDepth, Module1.TOH, 0);
				HBRRight.Draw();

				if (HasPost & Module1.HasJoinery) {
				Module1.PrepareIncomingJointRecord(PLeft.TimberId, HBRLeft.TimberId.Handle, HBRLeft.FarJointParamsDrawn);
				BentNetwork.RegisterEdge(HBRLeft.TimberId.Handle, Module1.End.Far,
					PLeft.TimberId.Handle, Module1.End.Body,
					HBRLeft.FarJointParamsDrawn.JointType.ToString(), HBRLeft.FarJointParamsDrawn);
				PLeft.AddMortise(HBRLeft.Tenon);
				Module1.PrepareIncomingJointRecord(PRight.TimberId, HBRRight.TimberId.Handle, HBRRight.FarJointParamsDrawn);
				BentNetwork.RegisterEdge(HBRRight.TimberId.Handle, Module1.End.Far,
					PRight.TimberId.Handle, Module1.End.Body,
					HBRRight.FarJointParamsDrawn.JointType.ToString(), HBRRight.FarJointParamsDrawn);
				PRight.AddMortise(HBRRight.Tenon);
			}

			}

			//Setup Left Hammer Beam
			HBeamLeft HBLeft = new();
			HBLeft.HBDivisor = HBDivisor;
			HBLeft.Width = HBeamWidth;
			HBLeft.Depth = HBeamDepth;
			HBLeft.postWidth = postWidth;
			HBLeft.Type = "HBeam";
			HBLeft.hbLength = ((Module1.Span - (postDepth * 2) - KpostDepth) / HBDivisor);

			//Setup Left Hammer Post
			HPostLeft HPLeft = new();
			HPLeft.HBDivisor = HBDivisor;
			HPLeft.Width = HPostWidth;
			HPLeft.Depth = HPostDepth;
			HPLeft.RafterWidth = RafterWidth;
			HPLeft.Type = "HPost";
			HPLeft.postDepth = postDepth;
			HPLeft.postWidth = postWidth;
			HPLeft.KpostDepth = KpostDepth;

			//Setup Right Hammer Beam
			HBeamRight HBRight = new();
			HBRight.HBDivisor = HBDivisor;
			HBRight.Width = HBeamWidth;
			HBRight.Depth = HBeamDepth;
			HBRight.postWidth = postWidth;
			HBRight.Type = "HBeam";
			HBRight.hbLength = ((Module1.Span - (postDepth * 2) - KpostDepth) / HBDivisor);

			//Setup Right Hammer Post
			HPostRight HPRight = new();
			HPRight.HBDivisor = HBDivisor;
			HPRight.Width = HPostWidth;
			HPRight.Depth = HPostDepth;
			HPRight.RafterWidth = RafterWidth;
			HPRight.Type = "HPost";
			HPRight.postDepth = postDepth;
			HPRight.postWidth = postWidth;
			HPRight.KpostDepth = KpostDepth;

			BentBrace B = new();
			ObjectId BLeftId1 = default(ObjectId);
			ObjectId BLeftId2 = default(ObjectId);
			ObjectId BLeftId3 = default(ObjectId);
			ObjectId BRightId1 = default(ObjectId);
			ObjectId BRightId2 = default(ObjectId);
			ObjectId BRightId3 = default(ObjectId);
			ObjectId HBLeftId1 = default(ObjectId);
			ObjectId HBLeftId2 = default(ObjectId);
			ObjectId HBRightId1 = default(ObjectId);
			ObjectId HBRightId2 = default(ObjectId);
			ObjectId HPLeftId1 = default(ObjectId);
			ObjectId HPLeftId2 = default(ObjectId);
			ObjectId HPRightId1 = default(ObjectId);
			ObjectId HPRightId2 = default(ObjectId);
			switch (HBDivisor) {
				case 4:
					//Draw Left Hammer Beam
					HBLeft.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
					HBLeft.Designation = ((char)(Module1.BentWallNumber)).ToString();
					if (HasFlrGirt)
						HBLeft.Designation += " UP";
					HBLeft.StartPoint = new Point3d(postDepth, Module1.BOG, 0);
					if (HasHBeam) {
						HBLeft.Draw();
						HBLeftId1 = HBLeft.TimberId;
						if (HasPost & Module1.HasJoinery)
							PLeft.AddMortise(HBLeft.Tenon);
					}

					//Draw Left Brace Between Post and Hammer Beam
					B.Width = BraceWidth;
					B.Depth = BraceDepth;
					B.Length = BraceLength;
					B.postWidth = postWidth;
					B.XAngle = 0;
					B.YAngle = 0;
					B.ZAngle = 0;
					B.StartPoint = new Point3d(postDepth, Module1.BOG, 0);
					if (HasBrace) {
						B.Draw();
						BLeftId2 = B.TimberId;
						if (HasPost & Module1.HasJoinery)
							PLeft.AddMortise(B.TenonUp);
					}

					//Draw First Left Hammer Post
					HPLeft.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
                    HPLeft.Designation = ((char)(Module1.BentWallNumber + 1)).ToString();
					HPLeft.StartPoint = Module1.AtPoint(HBLeft.StartPoint, HBLeft.hbLength, HBLeft.Depth, 0);
					if (HasHPost) {
						HPLeft.Draw();
						HPLeftId1 = HPLeft.TimberId;
						if (HasRafter & Module1.HasJoinery)
							HBRLeft.AddMortise(HPLeft.TenonUp);
						if (HasHBeam & Module1.HasJoinery)
							HBLeft.AddMortise(HPLeft.TenonDown);
						if (HasBrace & Module1.HasJoinery)
							HBLeft.AddMortise(B.TenonDown);
					}

					//Draw Left Hammer Beam Bay Girt and Both Braces
					if (HasBayGirt & Module1.GenerateBayMembers) {
						HBBayGirt HBBayGirt = new();
						double offset = 0;
						if (HPLeft.Depth - BayGirtWidth != 0)
							offset = (HPLeft.Depth - BayGirtWidth) / 2;
						HBBayGirt.Startpoint = new Point3d(HPLeft.StartPoint.X - offset, HPLeft.StartPoint.Y + BGBraceLength + 6, 0);
						HBBayGirt.Width = BayGirtWidth;
						HBBayGirt.Depth = BayGirtDepth;
						HBBayGirt.Type = "Girt";
						HBBayGirt.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
                        HBBayGirt.Designation = ((char)(Module1.BentWallNumber + 1)).ToString();
						HBBayGirt.Baywidth = BayWidth;
						HBBayGirt.HPostWidth = HPLeft.Width;
						HBBayGirt.HPostDepth = HPLeft.Depth;
						HBBayGirt.Draw(HBBayGirt.Sides.Left);
						if (HasHPost & Module1.HasJoinery)
							HPLeft.AddMortise(HBBayGirt.TenonLeft);
						if (Module1.Make3D) {
							BayBrace HBBrace = new();
							HBBrace.Width = BGBraceWidth;
							HBBrace.Depth = BGBraceDepth;
							HBBrace.Length = BGBraceLength;
							HBBrace.XAngle = 0;
							HBBrace.YAngle = 270;
							HBBrace.ZAngle = 0;
							HBBrace.StartPoint = Module1.AtPoint(HBBayGirt.Startpoint, -((BayGirtWidth - 3) / 2), 0, HPLeft.Width);
							if (HasBGBrace) {
								//Case 4 Lefttside Bent Lefttside Bay
								HBBrace.Peg1Length = HPLeft.Depth + 1.5;
								HBBrace.Peg2Length = BayGirtWidth + 1.5;
								HBBrace.Peg1Z = postWidth - ((HPLeft.Depth / 2 - HBBrace.Width / 2) + 0.75);
								HBBrace.Peg2Z = postWidth - ((BayGirtWidth / 2 - HBBrace.Width / 2) + 0.75);
								HBBrace.Draw();
								if (HasHPost & Module1.HasJoinery)
									HPLeft.AddMortise(HBBrace.TenonUp);
							}
							HBBrace.StartPoint = Module1.AtPoint(HBBayGirt.Startpoint, -((BayGirtWidth - 3) / 2), 0, BayWidth + HPLeft.Width);
							HBBrace.XAngle = 90;
							HBBrace.YAngle = 270;
							HBBrace.ZAngle = 0;
							if (HasBGBrace) {
								//Case 4 Rightside Bent Rightside Bay
								HBBrace.Peg1Length = BayGirtWidth + 1.5;
								HBBrace.Peg2Length = HPLeft.Depth + 1.5;
								HBBrace.Peg1Z = (BayWidth + HPLeft.Width) - ((BayGirtWidth / 2 - HBBrace.Width / 2) + 0.75);
								HBBrace.Peg2Z = (BayWidth + HPLeft.Width) - ((HPLeft.Depth / 2 - HBBrace.Width / 2) + 0.75);
								HBBrace.Draw();
							}

						}
					}

					//Draw Right Hammer Beam
					HBRight.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
                    HBRight.Designation = ((char)(Module1.BentWallNumber + 4)).ToString();
					if (HasFlrGirt)
						HBRight.Designation += " UP";
					HBRight.StartPoint = new Point3d(Module1.Span - postDepth, Module1.BOG, 0);
					if (HasHBeam) {
						HBRight.Draw();
						HBRightId1 = HBRight.TimberID;
						if (HasPost & Module1.HasJoinery)
							PRight.AddMortise(HBRight.Tenon);
					}

					//Draw Right Brace Between Post and Hammer Beam
					B.Width = BraceWidth;
					B.Depth = BraceDepth;
					B.Length = BraceLength;
					B.XAngle = 0;
					B.YAngle = 0;
					B.ZAngle = 270;
					B.StartPoint = new Point3d(Module1.Span - postDepth, Module1.BOG, 0);
					if (HasBrace) {
						B.Draw();
						BRightId2 = B.TimberId;
						if (HasPost & Module1.HasJoinery)
							PRight.AddMortise(B.TenonDown);
					}

					//Draw Right Hammer Post
					HPRight.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
                    HPRight.Designation = ((char)(Module1.BentWallNumber + 3)).ToString();
					HPRight.StartPoint = Module1.AtPoint(HBRight.StartPoint, -HBRight.hbLength, HBRight.Depth, 0);
					if (HasHPost) {
						HPRight.Draw();
						if (HasRafter & Module1.HasJoinery)
							HBRRight.AddMortise(HPRight.TenonUp);
						if (HasHBeam & Module1.HasJoinery)
							HBRight.AddMortise(HPRight.TenonDown);
						if (HasBrace & Module1.HasJoinery)
							HBRight.AddMortise(B.TenonUp);
					}

					//Draw Right Hammer Beam Bay Girt and Both Braces
					if (HasBayGirt & Module1.GenerateBayMembers) {
						HBBayGirt HBBayGirt = new();
						double offset = 0;
						if (HPRight.Depth - BayGirtWidth != 0)
							offset = (HPRight.Depth - BayGirtWidth) / 2;
						HBBayGirt.Startpoint = new Point3d(HPRight.StartPoint.X + offset, HPRight.StartPoint.Y + BGBraceLength + 6, 0);
						HBBayGirt.Width = BayGirtWidth;
						HBBayGirt.Depth = BayGirtDepth;
						HBBayGirt.Type = "Girt";
						HBBayGirt.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
                        HBBayGirt.Designation = ((char)(Module1.BentWallNumber + 3)).ToString();
						HBBayGirt.Baywidth = BayWidth;
						HBBayGirt.HPostWidth = HPRight.Width;
						HBBayGirt.HPostDepth = HPRight.Depth;
						HBBayGirt.Draw(HBBayGirt.Sides.Right);
						if (HasHPost & Module1.HasJoinery)
							HPRight.AddMortise(HBBayGirt.TenonLeft);
						if (Module1.Make3D & BGBraceLength > 0) {
							BayBrace HBBrace = new();
							HBBrace.Width = BGBraceWidth;
							HBBrace.Depth = BGBraceDepth;
							HBBrace.Length = BGBraceLength;
							HBBrace.XAngle = 270;
							HBBrace.YAngle = 90;
							HBBrace.ZAngle = 0;
							HBBrace.StartPoint = Module1.AtPoint(HBBayGirt.Startpoint, ((BayGirtWidth - 3) / 2), 0, HPRight.Width);
							if (HasBGBrace) {
								//Case 4 Rightside Bent Leftside Bay
								HBBrace.Peg1Length = BayGirtWidth + 1.5;
								HBBrace.Peg2Length = HPLeft.Depth + 1.5;
								HBBrace.Peg1Z = postWidth - ((BayGirtWidth / 2 - HBBrace.Width / 2) + 0.75);
								HBBrace.Peg2Z = postWidth - ((HPLeft.Depth / 2 - HBBrace.Width / 2) + 0.75);
								HBBrace.Draw();
								if (HasHPost & Module1.HasJoinery)
									HPRight.AddMortise(HBBrace.TenonUp);
							}
							HBBrace.XAngle = 0;
							HBBrace.YAngle = 90;
							HBBrace.ZAngle = 0;
							HBBrace.StartPoint = Module1.AtPoint(HBBayGirt.Startpoint, ((BayGirtWidth - 3) / 2), 0, BayWidth + HPRight.Width);
							if (HasBGBrace) {
								//Case 4 Lefttside Bent Rightside Bay
								HBBrace.Peg1Length = HPLeft.Depth + 1.5;
								HBBrace.Peg2Length = BayGirtWidth + 1.5;
								HBBrace.Peg1Z = (BayWidth + HPRight.Width) - ((HPLeft.Depth / 2 - HBBrace.Width / 2) + 0.75);
								HBBrace.Peg2Z = (BayWidth + HPRight.Width) - ((BayGirtWidth / 2 - HBBrace.Width / 2) + 0.75);
								HBBrace.Draw();
							}
						}
					}

					break;
				case 6:
					//Draw First Left Hammer Beam
					HBLeft.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
                    HBLeft.Designation = ((char)(Module1.BentWallNumber)).ToString();
					HBLeft.StartPoint = new Point3d(postDepth, Module1.BOG, 0);
					if (HasHBeam) {
						HBLeft.Draw();
						HBLeftId1 = HBLeft.TimberId;
						if (HasPost & Module1.HasJoinery)
							PLeft.AddMortise(HBLeft.Tenon);
					}

					//Draw First Left Brace Between Post and Hammer Beam
					B.Width = BraceWidth;
					B.Depth = BraceDepth;
					B.Length = BraceLength;
					B.postWidth = postWidth;
					B.ZAngle = 0;
					B.YAngle = 0;
					B.StartPoint = new Point3d(postDepth, Module1.BOG, 0);
					if (HasBrace) {
						B.Draw();
						BLeftId2 = B.TimberId;
						if (HasPost & Module1.HasJoinery)
							PLeft.AddMortise(B.TenonUp);
						if (HasHBeam & Module1.HasJoinery)
							HBLeft.AddMortise(B.TenonDown);
					}

					//Draw First Left Hammer Post
					HPLeft.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
                    HPLeft.Designation = ((char)(Module1.BentWallNumber + 1)).ToString();
					HPLeft.StartPoint = Module1.AtPoint(HBLeft.StartPoint, HBLeft.hbLength, HBLeft.Depth, 0);
					if (HasHPost) {
						HPLeft.Draw();
						HPLeftId1 = HPLeft.TimberId;
						if (HasRafter & Module1.HasJoinery)
							HBRLeft.AddMortise(HPLeft.TenonUp);
						if (HasHBeam & Module1.HasJoinery)
							HBLeft.AddMortise(HPLeft.TenonDown);
					}

					//Draw First Left Hammer Beam Bay Girt and Both Braces
					if (HasBayGirt & Module1.GenerateBayMembers) {
						HBBayGirt HBBayGirt = new();
						double offset = 0;
						if (HPLeft.Depth - BayGirtWidth != 0)
							offset = (HPLeft.Depth - BayGirtWidth) / 2;
						HBBayGirt.Startpoint = new Point3d(HPLeft.StartPoint.X - ((HPLeft.Width - BayGirtWidth) / 2), HPLeft.StartPoint.Y + BGBraceLength + 6, 0);
						HBBayGirt.Width = BayGirtWidth;
						HBBayGirt.Depth = BayGirtDepth;
						HBBayGirt.Type = "Girt";
						HBBayGirt.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
                        HBBayGirt.Designation = ((char)(Module1.BentWallNumber + 1)).ToString();
						HBBayGirt.Baywidth = BayWidth;
						HBBayGirt.HPostWidth = HPLeft.Width;
						HBBayGirt.HPostDepth = HPLeft.Depth;
						HBBayGirt.Draw(HBBayGirt.Sides.Left);
						if (HasHPost & Module1.HasJoinery)
							HPLeft.AddMortise(HBBayGirt.TenonLeft);
						if (Module1.Make3D & BGBraceLength > 0) {
							BayBrace HBBrace = new();
							HBBrace.Width = BGBraceWidth;
							HBBrace.Depth = BGBraceDepth;
							HBBrace.Length = BGBraceLength;
							HBBrace.XAngle = 0;
							HBBrace.YAngle = 270;
							HBBrace.ZAngle = 0;
							HBBrace.StartPoint = Module1.AtPoint(HBBayGirt.Startpoint, -((BayGirtWidth - 3) / 2), 0, HPLeft.Width);

							if (HasBGBrace) {
								// Case 6 Lefttside Bent Lefttside Bay Down
								HBBrace.Peg1Length = HPLeft.Depth + 1.5;
								HBBrace.Peg2Length = BayGirtWidth + 1.5;
								HBBrace.Peg1Z = postWidth - ((HPLeft.Depth / 2 - HBBrace.Width / 2) + 0.75);
								HBBrace.Peg2Z = postWidth - ((BayGirtWidth / 2 - HBBrace.Width / 2) + 0.75);
								HBBrace.Draw();
								if (HasHPost & Module1.HasJoinery)
									HPLeft.AddMortise(HBBrace.TenonUp);
							}
							HBBrace.XAngle = 90;
							HBBrace.YAngle = 270;
							HBBrace.ZAngle = 0;
							HBBrace.StartPoint = Module1.AtPoint(HBBayGirt.Startpoint, -((BayGirtWidth - 3) / 2), 0, BayWidth + HPLeft.Width);
							if (HasBGBrace) {
								//Case 6 Rightside Bent Rightside Bay Down
								HBBrace.Peg1Length = BayGirtWidth + 1.5;
								HBBrace.Peg2Length = HPLeft.Depth + 1.5;
								HBBrace.Peg1Z = (BayWidth + HPLeft.Width) - ((BayGirtWidth / 2 - HBBrace.Width / 2) + 0.75);
								HBBrace.Peg2Z = (BayWidth + HPLeft.Width) - ((HPLeft.Depth / 2 - HBBrace.Width / 2) + 0.75);
								HBBrace.Draw();
							}
						}
					}

					//Draw Second Left Hammer Beam
					HBLeft.StartPoint = Module1.AtPoint(HPLeft.StartPoint, 0, HPLeft.hpLengthLong - (HBeamDepth + 6), 0);
					HBLeft.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
                    HBLeft.Designation = ((char)(Module1.BentWallNumber + 1)).ToString();
					if (HasHBeam) {
						HBLeft.Draw();
						HBLeftId2 = HBLeft.TimberId;
						if (HasHPost & Module1.HasJoinery)
							HPLeft.AddMortise(HBLeft.Tenon);
					}

					//Draw Second Left Brace Between HPost1 and HBeam2
					B.Width = BraceWidth;
					B.Depth = BraceDepth;
					B.Length = BraceLength;
					B.ZAngle = 0;
					B.YAngle = 0;
					B.StartPoint = HBLeft.StartPoint;
					if (HasBrace) {
						B.Draw();
						BLeftId3 = B.TimberId;
						if (HasHBeam & Module1.HasJoinery)
							HBLeft.AddMortise(B.TenonDown);
						if (HasHPost & Module1.HasJoinery)
							HPLeft.AddMortise(B.TenonUp);
					}

					//Draw Second Left Hammer Post
					HPLeft.StartPoint = Module1.AtPoint(HBLeft.StartPoint, HBLeft.hbLength, HBLeft.Depth, 0);
					HPLeft.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
                    HPLeft.Designation = ((char)(Module1.BentWallNumber + 2)).ToString();
					if (HasHPost) {
						HPLeft.Draw();
						HPLeftId2 = HPLeft.TimberId;
						if (HasRafter & Module1.HasJoinery)
							HBRLeft.AddMortise(HPLeft.TenonUp);
						if (HasHBeam & Module1.HasJoinery)
							HBLeft.AddMortise(HPLeft.TenonDown);
					}

					//Draw Second Left Hammer Beam Bay Girt and Both Braces
					if (HasBayGirt & Module1.GenerateBayMembers) {
						HBBayGirt HBBayGirt = new();
						double offset = 0;
						if (HPLeft.Depth - BayGirtWidth != 0)
							offset = (HPLeft.Depth - BayGirtWidth) / 2;
						HBBayGirt.Startpoint = new Point3d(HPLeft.StartPoint.X - ((HPLeft.Width - BayGirtWidth) / 2), HPLeft.StartPoint.Y + BGBraceLength + 6, 0);
						HBBayGirt.Width = BayGirtWidth;
						HBBayGirt.Depth = BayGirtDepth;
						HBBayGirt.Type = "Girt";
						HBBayGirt.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
                        HBBayGirt.Designation = ((char)(Module1.BentWallNumber + 2)).ToString();
						HBBayGirt.Baywidth = BayWidth;
						HBBayGirt.HPostWidth = HPLeft.Width;
						HBBayGirt.HPostDepth = HPLeft.Depth;
						HBBayGirt.Draw(HBBayGirt.Sides.Left);
						if (HasHPost & Module1.HasJoinery)
							HPLeft.AddMortise(HBBayGirt.TenonLeft);
						if (Module1.Make3D & BGBraceLength > 0) {
							BayBrace HBBrace = new();
							HBBrace.Width = BGBraceWidth;
							HBBrace.Depth = BGBraceDepth;
							HBBrace.Length = BGBraceLength;
							HBBrace.XAngle = 0;
							HBBrace.YAngle = 270;
							HBBrace.ZAngle = 0;
							HBBrace.StartPoint = Module1.AtPoint(HBBayGirt.Startpoint, -((BayGirtWidth - 3) / 2), 0, HPRight.Width);
							if (HasBGBrace) {
								//Case 6 Rightside Bent Leftside Bay Up
								HBBrace.Peg1Length = HPLeft.Depth + 1.5;
								HBBrace.Peg2Length = BayGirtWidth + 1.5;
								HBBrace.Peg1Z = postWidth - ((HPLeft.Depth / 2 - HBBrace.Width / 2) + 0.75);
								HBBrace.Peg2Z = postWidth - ((BayGirtWidth / 2 - HBBrace.Width / 2) + 0.75);
								HBBrace.Draw();
								if (HasHPost & Module1.HasJoinery)
									HPLeft.AddMortise(HBBrace.TenonUp);
							}
							HBBrace.XAngle = 90;
							HBBrace.YAngle = 270;
							HBBrace.ZAngle = 0;
							HBBrace.StartPoint = Module1.AtPoint(HBBayGirt.Startpoint, -((BayGirtWidth - 3) / 2), 0, BayWidth + HPRight.Width);
							if (HasBGBrace) {
								//Case 6 Lefttside Bent Rightside Bay Up
								HBBrace.Peg1Length = HPLeft.Depth + 1.5;
								HBBrace.Peg2Length = BayGirtWidth + 1.5;
								HBBrace.Peg1Z = (BayWidth + HPRight.Width) - ((HPLeft.Depth / 2 - HBBrace.Width / 2) + 0.75);
								HBBrace.Peg2Z = (BayWidth + HPRight.Width) - ((BayGirtWidth / 2 - HBBrace.Width / 2) + 0.75);
								HBBrace.Draw();
							}

						}
					}

					//Draw First Right Hammer Beam
					HBRight.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
                    HBRight.Designation = ((char)(Module1.BentWallNumber + HBDivisor)).ToString();
					HBRight.StartPoint = new Point3d(Module1.Span - postDepth, Module1.BOG, 0);
					if (HasHBeam) {
						HBRight.Draw();
						HBRightId2 = HBRight.TimberID;
						if (HasPost & Module1.HasJoinery)
							PRight.AddMortise(HBRight.Tenon);
					}

					//Draw First Right Brace Between Post and Hammer Beam
					B.Width = BraceWidth;
					B.Depth = BraceDepth;
					B.Length = BraceLength;
					B.XAngle = 0;
					B.YAngle = 0;
					B.ZAngle = 270;
					B.StartPoint = HBRight.StartPoint;
					if (HasBrace) {
						B.Draw();
						BRightId2 = B.TimberId;
						if (HasPost & Module1.HasJoinery)
							PRight.AddMortise(B.TenonDown);
						if (HasHBeam & Module1.HasJoinery)
							HBRight.AddMortise(B.TenonUp);
					}

					//Draw First Right Hammer Post
					HPRight.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
                    HPRight.Designation = ((char)(Module1.BentWallNumber + HBDivisor - 1)).ToString();
					HPRight.StartPoint = Module1.AtPoint(HBRight.StartPoint, -HBRight.hbLength, HBRight.Depth, 0);
					if (HasHPost) {
						HPRight.Draw();
						HPRightId1 = HPRight.TimberId;
						if (HasRafter & Module1.HasJoinery)
							HBRRight.AddMortise(HPRight.TenonUp);
						if (HasHBeam & Module1.HasJoinery)
							HBRight.AddMortise(HPRight.TenonDown);
					}

					//Draw First Right Hammer Beam Bay Girt and Both Braces
					if (HasBayGirt & Module1.GenerateBayMembers) {
						HBBayGirt HBBayGirt = new();
						double offset = 0;
						if (HPRight.Depth - BayGirtWidth != 0)
							offset = (HPRight.Depth - BayGirtWidth) / 2;
						HBBayGirt.Startpoint = new Point3d(HPRight.StartPoint.X + ((HPRight.Width - BayGirtWidth) / 2), HPRight.StartPoint.Y + BGBraceLength + 6, 0);
						HBBayGirt.Width = BayGirtWidth;
						HBBayGirt.Depth = BayGirtDepth;
						HBBayGirt.Type = "Girt";
						HBBayGirt.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
                        HBBayGirt.Designation = ((char)(Module1.BentWallNumber + HBDivisor - 1)).ToString();
						HBBayGirt.Baywidth = BayWidth;
						HBBayGirt.HPostWidth = HPRight.Width;
						HBBayGirt.HPostDepth = HPRight.Depth;
						HBBayGirt.Draw(HBBayGirt.Sides.Right);
						if (HasHPost & Module1.HasJoinery)
							HPRight.AddMortise(HBBayGirt.TenonLeft);
						if (Module1.Make3D & BGBraceLength > 0) {
							BayBrace HBBrace = new();
							HBBrace.Width = BGBraceWidth;
							HBBrace.Depth = BGBraceDepth;
							HBBrace.Length = BGBraceLength;
							HBBrace.XAngle = 270;
							HBBrace.YAngle = 90;
							HBBrace.ZAngle = 0;
							HBBrace.StartPoint = Module1.AtPoint(HBBayGirt.Startpoint, ((BayGirtWidth - 3) / 2), 0, HPRight.Width);
							if (HasBGBrace) {
								//Case 6 Lefttside Bent Rightside Bay
								HBBrace.Peg1Length = BayGirtWidth + 1.5;
								HBBrace.Peg2Length = HPLeft.Depth + 1.5;
								HBBrace.Peg1Z = postWidth - ((BayGirtWidth / 2 - HBBrace.Width / 2) + 0.75);
								HBBrace.Peg2Z = postWidth - ((HPLeft.Depth / 2 - HBBrace.Width / 2) + 0.75);
								HBBrace.Draw();
								if (HasHPost & Module1.HasJoinery)
									HPRight.AddMortise(HBBrace.TenonUp);
							}
							HBBrace.XAngle = 0;
							HBBrace.YAngle = 90;
							HBBrace.ZAngle = 0;
							HBBrace.StartPoint = Module1.AtPoint(HBBayGirt.Startpoint, ((BayGirtWidth - 3) / 2), 0, BayWidth + HPRight.Width);
							if (HasBGBrace) {
								//Case 6 Lefttside Bent Rightside Bay
								HBBrace.Peg1Length = HPLeft.Depth + 1.5;
								HBBrace.Peg2Length = BayGirtWidth + 1.5;
								HBBrace.Peg1Z = (BayWidth + HPRight.Width) - ((HPLeft.Depth / 2 - HBBrace.Width / 2) + 0.75);
								HBBrace.Peg2Z = (BayWidth + HPRight.Width) - ((BayGirtWidth / 2 - HBBrace.Width / 2) + 0.75);
								HBBrace.Draw();
							}
						}
					}

					//Draw Second Right Hammer Beam
					HBRight.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
                    HBRight.Designation = ((char)(Module1.BentWallNumber + 5)).ToString();
					HBRight.StartPoint = Module1.AtPoint(HPRight.StartPoint, 0, HPRight.hpLengthLong - (HBeamDepth + 6), 0);
					if (HasHBeam) {
						HBRight.Draw();
						HBRightId2 = HBRight.TimberID;
						if (HasPost & Module1.HasJoinery)
							PRight.AddMortise(HBRight.Tenon);
					}

					//Draw Second Right Brace Between HPost1 and HBeam2
					B.Width = BraceWidth;
					B.Depth = BraceDepth;
					B.Length = BraceLength;
					B.XAngle = 0;
					B.YAngle = 0;
					B.ZAngle = 270;
					B.StartPoint = HBRight.StartPoint;
					if (HasBrace) {
						B.Draw();
						BRightId3 = B.TimberId;
						if (HasHPost & Module1.HasJoinery)
							HPRight.AddMortise(B.TenonDown);
						if (HasHBeam & Module1.HasJoinery)
							HBRight.AddMortise(B.TenonUp);
					}

					//Draw Second Right Hammer Post
					HPRight.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
                    HPRight.Designation = ((char)(Module1.BentWallNumber + 4)).ToString();
					HPRight.StartPoint = Module1.AtPoint(HBRight.StartPoint, -HBRight.hbLength, HBRight.Depth, 0);
					if (HasHPost) {
						HPRight.Draw();
						HPRightId2 = HPRight.TimberId;
						if (HasRafter & Module1.HasJoinery)
							HBRRight.AddMortise(HPRight.TenonUp);
						if (HasHBeam & Module1.HasJoinery)
							HBRight.AddMortise(HPRight.TenonDown);
					}

					//Draw Right Hammer Beam Bay Girt and Both Braces
					if (HasBayGirt & Module1.GenerateBayMembers) {
						HBBayGirt HBBayGirt = new();
						double offset = 0;
						if (HPRight.Depth - BayGirtWidth != 0)
							offset = (HPRight.Depth - BayGirtWidth) / 2;
						HBBayGirt.Startpoint = new Point3d(HPRight.StartPoint.X + ((HPRight.Width - BayGirtWidth) / 2), HPRight.StartPoint.Y + BGBraceLength + 6, 0);
						HBBayGirt.Width = BayGirtWidth;
						HBBayGirt.Depth = BayGirtDepth;
						HBBayGirt.Type = "Girt";
						if (HBDivisor == 4)
                            HBBayGirt.Designation = ((char)(Module1.BentWallNumber + 3)).ToString();
						else
                            HBBayGirt.Designation = ((char)(Module1.BentWallNumber + 4)).ToString();
						HBBayGirt.Baywidth = BayWidth;
						HBBayGirt.HPostWidth = HPRight.Width;
						HBBayGirt.HPostDepth = HPRight.Depth;
						HBBayGirt.Draw(HBBayGirt.Sides.Right);
						if (HasHPost & Module1.HasJoinery)
							HPRight.AddMortise(HBBayGirt.TenonLeft);
						if (Module1.Make3D & BGBraceLength > 0) {
							BayBrace HBBrace = new();
							HBBrace.Width = BGBraceWidth;
							HBBrace.Depth = BGBraceDepth;
							HBBrace.Length = BGBraceLength;
							HBBrace.XAngle = 270;
							HBBrace.YAngle = 90;
							HBBrace.ZAngle = 0;
							HBBrace.StartPoint = Module1.AtPoint(HBBayGirt.Startpoint, ((BayGirtWidth - 3) / 2), 0, HPRight.Width);
							if (HasBGBrace) {
								//Case 6 Lefttside Bent Rightside Bay
								HBBrace.Peg1Length = BayGirtWidth + 1.5;
								HBBrace.Peg2Length = HPLeft.Depth + 1.5;
								HBBrace.Peg1Z = postWidth - ((BayGirtWidth / 2 - HBBrace.Width / 2) + 0.75);
								HBBrace.Peg2Z = postWidth - ((HPLeft.Depth / 2 - HBBrace.Width / 2) + 0.75);
								HBBrace.Draw();
								if (HasHPost & Module1.HasJoinery)
									HPRight.AddMortise(HBBrace.TenonUp);
							}
							HBBrace.XAngle = 0;
							HBBrace.YAngle = 90;
							HBBrace.ZAngle = 0;
							HBBrace.StartPoint = Module1.AtPoint(HBBayGirt.Startpoint, ((BayGirtWidth - 3) / 2), 0, BayWidth + HPRight.Width);
							if (HasBGBrace) {
								//Case 6 Lefttside Bent Rightside Bay
								HBBrace.Peg1Length = HPLeft.Depth + 1.5;
								HBBrace.Peg2Length = BayGirtWidth + 1.5;
								HBBrace.Peg1Z = (BayWidth + HPRight.Width) - ((HPLeft.Depth / 2 - HBBrace.Width / 2) + 0.75);
								HBBrace.Peg2Z = (BayWidth + HPRight.Width) - ((BayGirtWidth / 2 - HBBrace.Width / 2) + 0.75);
								HBBrace.Draw();
							}

						}
					}
					break;
			}

			//Draw Hammer Beam Girt
			HBGirt HBG = new();
			HBG.HBDivisor = HBDivisor;
			HBG.postDepth = postDepth;
			HBG.postWidth = postWidth;
			HBG.KpostDepth = KpostDepth;
			HBG.Width = HBGirtWidth;
			HBG.Depth = HBGirtDepth;
			HBG.Type = "HBeam";
			HBG.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
			if (HBDivisor == 4)
                HBG.Designation = ((char)(Module1.BentWallNumber + 1)).ToString() + ((char)(Module1.BentWallNumber + 3)).ToString();
			else
                HBG.Designation = ((char)(Module1.BentWallNumber + 2)).ToString() + "-" + ((char)(Module1.BentWallNumber + 4)).ToString();
			if (HasFlrGirt)
				HBG.Designation += " UP";
			HBG.StartPoint = Module1.AtPoint(HPLeft.StartPoint, 0, HPLeft.hpLengthLong - (HBGirtDepth + 6), 0);
			if (HasHBGirt) {
				HBG.Draw();
				// Apply HBGirt tenons to innermost HPosts (currently final HPLeft/HPRight after switch)
				if (HasHPost & Module1.HasJoinery) {
					HPLeft.AddMortise(HBG.TenonLeft);
					HPRight.AddMortise(HBG.TenonRight);
				}
			}


			//Draw Left Hammer Beam Girt Brace Between Last Hammer Beam Post and Girt

			B.Width = BraceWidth;
			B.Depth = BraceDepth;
			B.Length = BraceLength;
			B.XAngle = 0;
			B.YAngle = 0;
			B.ZAngle = 0;
			B.StartPoint = HBG.StartPoint;
			if (HasBrace) {
				B.Draw();
				BLeftId1 = B.TimberId;
				if (HasHBGirt & Module1.HasJoinery)
					HBG.AddMortise(B.TenonDown);
				if (HasHPost & Module1.HasJoinery)
					HPLeft.AddMortise(B.TenonUp);
			}

			B.Width = BraceWidth;
			B.Depth = BraceDepth;
			B.Length = BraceLength;
			B.StartPoint = Module1.AtPoint(HPRight.StartPoint, 0, HPRight.hpLengthLong - (HBGirtDepth + 6), 0);
			B.postWidth = postWidth;
			B.XAngle = 0;
			B.YAngle = 0;
			B.ZAngle = 270;
			if (HasBrace) {
				B.Draw();
				BRightId1 = B.TimberId;
				if (HasHBGirt & Module1.HasJoinery)
					HBG.AddMortise(B.TenonUp);
				if (HasHPost & Module1.HasJoinery)
					HPRight.AddMortise(B.TenonDown);
			}

			//Draw Hammer Beam King Post
			if (HasKPost) {
				HBKP.postDepth = postDepth;
				if (Module1.Prise < 4)
					HBKP.KpostRafterSeatDepth = 2;
				else
					HBKP.KpostRafterSeatDepth = 3;
				HBKP.Width = KpostWidth;
				HBKP.Depth = KpostDepth;
				HBKP.Type = "KPost";
				HBKP.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
                HBKP.Designation = ((char)(Module1.BentWallNumber + (HBDivisor / 2))).ToString();
				HBKP.StartPoint = Module1.AtPoint(HBG.StartPoint, HBLeft.hbLength, HBG.Depth, 0);
				if (HasKPost) {
					HBKP.Draw();
					if (HasHBGirt & Module1.HasJoinery) {
						Module1.PrepareIncomingJointRecord(HBG.Timber, HBKP.TimberId.Handle, HBKP.NearJointParamsDrawn);
						BentNetwork.RegisterEdge(HBKP.TimberId.Handle, Module1.End.Near,
							HBG.Timber.Handle, Module1.End.Body,
							HBKP.NearJointParamsDrawn.JointType.ToString(), HBKP.NearJointParamsDrawn);
						HBG.AddMortise(HBKP.Tenon);
                        // Rich cascade path: stores tenon handle so ApplyCascade re-cuts
                        // the HBGirt mortise directly when HBKpost is regenerated.
                        Module1.AddConnectionFull(HBKP.TimberId, new Module1.Connection {
                            ConnHandle   = HBG.Timber.Handle,
                            ThisEnd      = Module1.End.Near,
                            OtherEnd     = Module1.End.Body,
                            ThisJoint    = Module1.JointType.Tenon,
                            TenonHandles = new[] { HBKP.Tenon.Handle }
                        });
					}
				}
			}

			//Draw Floor Girt
			FloorBentGirt FlrBentGirt = new();

			if (HasFlrGirt) {
				FlrBentGirt.Width = FlrGirtWidth;
				FlrBentGirt.Depth = FlrGirtDepth;
				FlrBentGirt.Height = FlrGirtHt;
				FlrBentGirt.Type = "Girt";
				FlrBentGirt.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
                FlrBentGirt.Designation = ((char)(Module1.BentWallNumber)).ToString() + ((char)(Module1.BentWallNumber + HBDivisor)).ToString();
				FlrBentGirt.StartPoint = new Point3d(postDepth, FlrGirtHt - FlrBentGirt.Depth, 0);
				FlrBentGirt.Draw(postDepth);
			}

			BentBrace FGBLeft = new();
			FGBLeft.postWidth = postWidth;
			FGBLeft.Width = BraceWidth;
			FGBLeft.Depth = BraceDepth;
			FGBLeft.Length = FlrBraceLength;
			FGBLeft.XAngle = 0;
			FGBLeft.YAngle = 0;
			FGBLeft.ZAngle = 0;
			FGBLeft.StartPoint = new Point3d(postDepth, FlrGirtHt - FlrGirtDepth, 0);
			if (HasFlrBrace) {
				FGBLeft.Draw();
				if (HasPost & Module1.HasJoinery)
					PLeft.AddMortise(FGBLeft.TenonDown);
				if (HasFlrGirt & Module1.HasJoinery)
					FlrBentGirt.AddMortise(FGBLeft.TenonUp);
			}

			BentBrace FGBRight = new();
			FGBRight.Width = BraceWidth;
			FGBRight.Depth = BraceDepth;
			FGBRight.Length = FlrBraceLength;
			FGBRight.StartPoint = new Point3d(Module1.Span - postDepth, FlrGirtHt - FlrGirtDepth, 0);
			FGBRight.postWidth = postWidth;
			FGBRight.XAngle = 0;
			FGBRight.YAngle = 0;
			FGBRight.ZAngle = 270;
			if (HasFlrBrace) {
				FGBRight.Draw();
				if (HasPost & Module1.HasJoinery)
					PRight.AddMortise(FGBRight.TenonUp);
				if (HasFlrGirt & Module1.HasJoinery)
					FlrBentGirt.AddMortise(FGBRight.TenonDown);
			}

			if (HasPost) {
				if (HasFlrBrace) {
					Module1.AddConnection(PLeft.TimberId, FGBLeft.TimberId, Module1.JointType.Mortise);
					Module1.AddConnection(PRight.TimberId, FGBRight.TimberId, Module1.JointType.Mortise);
				}
				if (HasFlrGirt) {
					Module1.AddConnection(PLeft.TimberId, FlrBentGirt.TimberId, Module1.JointType.Mortise);
					Module1.AddConnection(PRight.TimberId, FlrBentGirt.TimberId, Module1.JointType.Mortise);
				}
				if (HasBrace) {
					Module1.AddConnection(PLeft.TimberId, (HBDivisor == 4 ? BLeftId2 : BLeftId3), Module1.JointType.Mortise);
					Module1.AddConnection(PRight.TimberId, (HBDivisor == 4 ? BRightId2 : BRightId3), Module1.JointType.Mortise);
				}
				if (HasHBeam) {
					Module1.AddConnection(PLeft.TimberId, HBLeft.TimberId, Module1.JointType.Mortise);
					Module1.AddConnection(PRight.TimberId, HBRight.TimberID, Module1.JointType.Mortise);
				}
				if (HasRafter) {
					Module1.AddConnection(PLeft.TimberId, HBRLeft.TimberId, Module1.JointType.Mortise);
					Module1.AddConnection(PRight.TimberId, HBRRight.TimberId, Module1.JointType.Mortise);
				}
			}
			if (HasFlrBrace) {
				if (HasPost) {
					Module1.AddConnection(FGBLeft.TimberId, PLeft.TimberId, Module1.JointType.Tenon);
					Module1.AddConnection(FGBRight.TimberId, PRight.TimberId, Module1.JointType.Tenon);
				}
				if (HasFlrGirt) {
					Module1.AddConnection(FGBLeft.TimberId, FlrBentGirt.TimberId, Module1.JointType.Tenon);
					Module1.AddConnection(FGBRight.TimberId, FlrBentGirt.TimberId, Module1.JointType.Tenon);
				}
			}
			if (HasFlrGirt) {
				if (HasPost) {
					Module1.AddConnection(FlrBentGirt.TimberId, PLeft.TimberId, Module1.JointType.Tenon);
					Module1.AddConnection(FlrBentGirt.TimberId, PRight.TimberId, Module1.JointType.Tenon);
				}
				if (HasFlrBrace) {
					Module1.AddConnection(FlrBentGirt.TimberId, FGBLeft.TimberId, Module1.JointType.Mortise);
					Module1.AddConnection(FlrBentGirt.TimberId, FGBRight.TimberId, Module1.JointType.Mortise);
				}
			}
			if (HasHBeam) {
				if (HasPost) {
					// HBeamLeft → PLeft: full giver wiring
					Module1.PrepareIncomingJointRecord(PLeft.TimberId, HBLeft.TimberId.Handle, HBLeft.FarJointParamsDrawn);
					BentNetwork.RegisterEdge(HBLeft.TimberId.Handle, Module1.End.Far,
						PLeft.TimberId.Handle, Module1.End.Body,
						HBLeft.FarJointParamsDrawn.JointType.ToString(), HBLeft.FarJointParamsDrawn);
					Module1.AddConnectionFull(HBLeft.TimberId, new Module1.Connection {
						ConnHandle=PLeft.TimberId.Handle, ThisEnd=(short)Module1.End.Far,
						OtherEnd=(short)Module1.End.Body, ThisJoint=Module1.JointType.Tenon,
						TenonHandles=new Handle[0] });
					Module1.AddConnection(PLeft.TimberId, HBLeft.TimberId, Module1.JointType.Mortise);
					// HBeamRight → PRight: full giver wiring
					Module1.PrepareIncomingJointRecord(PRight.TimberId, HBRight.TimberID.Handle, HBRight.FarJointParamsDrawn);
					BentNetwork.RegisterEdge(HBRight.TimberID.Handle, Module1.End.Far,
						PRight.TimberId.Handle, Module1.End.Body,
						HBRight.FarJointParamsDrawn.JointType.ToString(), HBRight.FarJointParamsDrawn);
					Module1.AddConnectionFull(HBRight.TimberID, new Module1.Connection {
						ConnHandle=PRight.TimberId.Handle, ThisEnd=(short)Module1.End.Far,
						OtherEnd=(short)Module1.End.Body, ThisJoint=Module1.JointType.Tenon,
						TenonHandles=new Handle[0] });
					Module1.AddConnection(PRight.TimberId, HBRight.TimberID, Module1.JointType.Mortise);
				}
				if (HasBrace) {
					Module1.AddConnection(HBLeft.TimberId, (HBDivisor == 4 ? BLeftId2 : BLeftId3), Module1.JointType.Mortise);
					Module1.AddConnection(HBRight.TimberID, (HBDivisor == 4 ? BRightId2 : BRightId3), Module1.JointType.Mortise);
				}
				if (HasHPost) {
					Module1.AddConnection(HBLeft.TimberId, HPLeft.TimberId, Module1.JointType.Mortise);
					Module1.AddConnection(HBRight.TimberID, HPRight.TimberId, Module1.JointType.Mortise);
				}
			}
			if (HasHPost) {
				if (HasHBeam) {
					// HPLeft → HBeamLeft: foot tenon (Near end)
					Module1.PrepareIncomingJointRecord(HBLeft.TimberId, HPLeft.TimberId.Handle, HPLeft.NearJointParamsDrawn);
					BentNetwork.RegisterEdge(HPLeft.TimberId.Handle, Module1.End.Near,
						HBLeft.TimberId.Handle, Module1.End.Body, "Tenon", HPLeft.NearJointParamsDrawn);
					Module1.AddConnectionFull(HPLeft.TimberId, new Module1.Connection {
						ConnHandle=HBLeft.TimberId.Handle, ThisEnd=(short)Module1.End.Near,
						OtherEnd=(short)Module1.End.Body, ThisJoint=Module1.JointType.Tenon,
						TenonHandles=new Handle[0] });
					Module1.AddConnection(HBLeft.TimberId, HPLeft.TimberId, Module1.JointType.Mortise);
					// HPRight → HBeamRight: foot tenon (Near end)
					Module1.PrepareIncomingJointRecord(HBRight.TimberID, HPRight.TimberId.Handle, HPRight.NearJointParamsDrawn);
					BentNetwork.RegisterEdge(HPRight.TimberId.Handle, Module1.End.Near,
						HBRight.TimberID.Handle, Module1.End.Body, "Tenon", HPRight.NearJointParamsDrawn);
					Module1.AddConnectionFull(HPRight.TimberId, new Module1.Connection {
						ConnHandle=HBRight.TimberID.Handle, ThisEnd=(short)Module1.End.Near,
						OtherEnd=(short)Module1.End.Body, ThisJoint=Module1.JointType.Tenon,
						TenonHandles=new Handle[0] });
					Module1.AddConnection(HBRight.TimberID, HPRight.TimberId, Module1.JointType.Mortise);
				}
				if (HasRafter) {
					// HPLeft.TenonUp → HBRLeft (oblique polygon, Far end)
					Module1.SuppressNextMortiseBbox();
					BentNetwork.RegisterEdge(HPLeft.TimberId.Handle, Module1.End.Far,
						HBRLeft.TimberId.Handle, Module1.End.Body, "Polygon",
						JointParams.ForPolygon(HPLeft.UpTenonPts, HPLeft.UpTenonWidth, HPLeft.BentNumber, HPLeft.Designation));
					Module1.AddConnectionFull(HPLeft.TimberId, new Module1.Connection {
						ConnHandle=HBRLeft.TimberId.Handle, ThisEnd=(short)Module1.End.Far,
						OtherEnd=(short)Module1.End.Body, ThisJoint=Module1.JointType.Tenon,
						TenonHandles=new Handle[0] });
					Module1.AddConnection(HBRLeft.TimberId, HPLeft.TimberId, Module1.JointType.Mortise);
					// HPRight.TenonUp → HBRRight
					Module1.SuppressNextMortiseBbox();
					BentNetwork.RegisterEdge(HPRight.TimberId.Handle, Module1.End.Far,
						HBRRight.TimberId.Handle, Module1.End.Body, "Polygon",
						JointParams.ForPolygon(HPRight.UpTenonPts, HPRight.UpTenonWidth, HPRight.BentNumber, HPRight.Designation));
					Module1.AddConnectionFull(HPRight.TimberId, new Module1.Connection {
						ConnHandle=HBRRight.TimberId.Handle, ThisEnd=(short)Module1.End.Far,
						OtherEnd=(short)Module1.End.Body, ThisJoint=Module1.JointType.Tenon,
						TenonHandles=new Handle[0] });
					Module1.AddConnection(HBRRight.TimberId, HPRight.TimberId, Module1.JointType.Mortise);
				}
			}
			if (HasHBGirt & HasHPost) {
				// HBGirt → HPLeft (Near end) and HPRight (Far end)
				Module1.PrepareIncomingJointRecord(HPLeft.TimberId, HBG.Timber.Handle, HBG.NearJointParamsDrawn);
				BentNetwork.RegisterEdge(HBG.Timber.Handle, Module1.End.Near,
					HPLeft.TimberId.Handle, Module1.End.Body,
					HBG.NearJointParamsDrawn.JointType.ToString(), HBG.NearJointParamsDrawn);
				Module1.AddConnectionFull(HBG.Timber, new Module1.Connection {
					ConnHandle=HPLeft.TimberId.Handle, ThisEnd=(short)Module1.End.Near,
					OtherEnd=(short)Module1.End.Body, ThisJoint=Module1.JointType.Tenon,
					TenonHandles=new Handle[0] });
				Module1.AddConnection(HPLeft.TimberId, HBG.Timber, Module1.JointType.Mortise);
				Module1.PrepareIncomingJointRecord(HPRight.TimberId, HBG.Timber.Handle, HBG.FarJointParamsDrawn);
				BentNetwork.RegisterEdge(HBG.Timber.Handle, Module1.End.Far,
					HPRight.TimberId.Handle, Module1.End.Body,
					HBG.FarJointParamsDrawn.JointType.ToString(), HBG.FarJointParamsDrawn);
				Module1.AddConnectionFull(HBG.Timber, new Module1.Connection {
					ConnHandle=HPRight.TimberId.Handle, ThisEnd=(short)Module1.End.Far,
					OtherEnd=(short)Module1.End.Body, ThisJoint=Module1.JointType.Tenon,
					TenonHandles=new Handle[0] });
				Module1.AddConnection(HPRight.TimberId, HBG.Timber, Module1.JointType.Mortise);
			}

			BentNetwork.EndBent();
		}
	}
}
