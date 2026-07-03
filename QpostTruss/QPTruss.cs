using System;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{

	public class QPTruss
	{
		public bool HasGirt;
		public double GirtWidth;

		public double GirtDepth;
		public bool HasRafter;
		public double RafterWidth;

		public double RafterDepth;
		public bool HasQPost;
		public double QpostWidth;

		public double QpostDepth;
		public bool HasStrut;
		public double StrutWidth;

		public double StrutDepth;
		public bool HasQPGirt;
		public double UpperGirtWidth;

		public double UpperGirtDepth;
		public bool HasQPBrace;
		public double QPBraceWidth;
		public double QPBraceDepth;

		public double QPBraceLength;
		public QPTUpperGirt QPUGirt = new();
		public QPTStrutRight QPTSRight = new();
		public QPTStrutLeft QPTSLeft = new();
		public QPTRafterRight QPTRRight = new();
		public QPTRafterLeft QPTRLeft = new();
		public QPTPostRight QPTPRight = new();
		public QPTPostLeft QPTPLeft = new();
		public BentBrace QPTBLeft = new();
		public BentBrace QPTBRight = new();

		public TrussGirt TGirt = new();
		public bool DataReady()
		{
			bool flag = true;
			if (HasGirt)
				if (GirtWidth <= 0 | GirtDepth <= 0)
					flag = false;
			if (HasRafter)
				if (RafterWidth <= 0 | RafterDepth <= 0)
					flag = false;
			if (HasQPost)
				if (QpostWidth <= 0 | QpostDepth <= 0)
					flag = false;
			if (HasStrut)
				if (StrutWidth <= 0 | StrutDepth <= 0)
					flag = false;
			if (HasQPGirt)
				if (UpperGirtWidth <= 0 | UpperGirtDepth <= 0)
					flag = false;
			if (HasQPBrace)
				if (QPBraceWidth <= 0 | QPBraceDepth <= 0 | QPBraceLength <= 0)
					flag = false;
			return flag;
		}

		public void Draw()
		{
			if (HasGirt) {
				TGirt.Width = GirtWidth;
				TGirt.Depth = GirtDepth;
				TGirt.RafterDepth = RafterDepth;
				TGirt.Type = "Girt";
				TGirt.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
				TGirt.Designation =((char)(Module1.BentWallNumber)).ToString() + (char)(Module1.BentWallNumber + 3);
				TGirt.Draw();
			}

			if (HasQPBrace) {
				QPTBLeft.StartPoint = new Point3d((Module1.Span / 3) + QpostDepth, Module1.EaveHt + ((((Module1.Span / 3) + QpostDepth) * Module1.Pitch) - (Module1.PlumbLength + 6 + UpperGirtDepth)), 0);
				QPTBLeft.Width = QPBraceWidth;
				QPTBLeft.Depth = QPBraceDepth;
				QPTBLeft.Length = QPBraceLength;
				QPTBLeft.postWidth = QpostWidth;
				QPTBLeft.YAngle = 0;
				QPTBLeft.ZAngle = 0;
				QPTBLeft.XAngle = 0;
				QPTBLeft.Draw();
				QPTBRight.StartPoint = new Point3d(((Module1.Span / 3) * 2) - QpostDepth, Module1.EaveHt + ((((Module1.Span / 3) + QpostDepth) * Module1.Pitch) - (Module1.PlumbLength + 6 + UpperGirtDepth)), 0);
				QPTBRight.Width = QPBraceWidth;
				QPTBRight.Depth = QPBraceDepth;
				QPTBRight.Length = QPBraceLength;
				QPTBRight.postWidth = QpostWidth;
				QPTBRight.YAngle = 0;
				QPTBRight.ZAngle = 270;
				QPTBRight.XAngle = 0;
				QPTBRight.Draw();
			}

			if (HasQPost) {
				QPTPLeft.Width = QpostWidth;
				QPTPLeft.Depth = QpostDepth;
				QPTPLeft.RafterDepth = RafterDepth;
				QPTPLeft.RafterWidth = RafterWidth;
				QPTPLeft.Type = "QPost";
				QPTPLeft.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
				QPTPLeft.Designation = ((char)(Module1.BentWallNumber + 1)).ToString();
				QPTPLeft.StartPoint = new Point3d(Module1.Span / 3, Module1.EaveHt, 0);
				QPTPLeft.Draw();

				QPTPRight.Width = QpostWidth;
				QPTPRight.Depth = QpostDepth;
				QPTPRight.RafterDepth = RafterDepth;
				QPTPRight.RafterWidth = RafterWidth;
				QPTPRight.QpostDepth = QpostDepth;
				QPTPRight.Type = "QPost";
				QPTPRight.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
				QPTPRight.Designation = ((char)(Module1.BentWallNumber + 2)).ToString();
				QPTPRight.StartPoint = new Point3d(((Module1.Span / 3) * 2) - QpostDepth, Module1.EaveHt, 0);
				QPTPRight.Draw();
			}

			if (HasRafter) {
				QPTRLeft.Width = RafterWidth;
				QPTRLeft.Depth = RafterDepth;
				QPTRLeft.Type = "Rafter";
				QPTRLeft.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
				QPTRLeft.Designation = ((char)(Module1.BentWallNumber)).ToString();
				QPTRLeft.StartPoint = new Point3d(0, Module1.EaveHt, 0);
				QPTRLeft.Draw();

				QPTRRight.Width = RafterWidth;
				QPTRRight.Depth = RafterDepth;
				QPTRRight.Type = "Rafter";
				QPTRRight.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
				QPTRRight.Designation = ((char)(Module1.BentWallNumber + 3)).ToString();
				QPTRRight.StartPoint = new Point3d(Module1.Span, Module1.EaveHt, 0);
				QPTRRight.Draw();
			}

			if (HasStrut) {
				QPTSLeft.Width = StrutWidth;
				QPTSLeft.Depth = StrutDepth;
				QPTSLeft.RafterWidth = RafterWidth;
				QPTSLeft.RafterDepth = RafterDepth;
				QPTSLeft.Type = "Strut";
				QPTSLeft.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
                QPTSLeft.Designation = ((char)(Module1.BentWallNumber + 1)).ToString();
				QPTSLeft.StartPoint = new Point3d((((Module1.Span / 3) - ((RafterDepth / (Math.Sin(Module1.Beta))) + (StrutDepth / Module1.Pitch) + 0)) / 2) + ((RafterDepth / (Math.Sin(Module1.Beta))) + (StrutDepth / Module1.Pitch)), ((((Module1.Span / 3) - ((RafterDepth / (Math.Sin(Module1.Beta))) + (StrutDepth / Module1.Pitch) + 0)) / 2) * Module1.Pitch) + Module1.EaveHt + StrutDepth, 0);
				QPTSLeft.Draw();

				QPTSRight.Width = StrutWidth;
				QPTSRight.Depth = StrutDepth;
				QPTSRight.RafterDepth = RafterDepth;
				QPTSRight.RafterWidth = RafterWidth;
				QPTSRight.Type = "Strut";
				QPTSRight.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
                QPTSRight.Designation = ((char)(Module1.BentWallNumber + 2)).ToString();
				QPTSRight.StartPoint = new Point3d(Module1.Span - ((((Module1.Span / 3) - ((RafterDepth / (Math.Sin(Module1.Beta))) + (StrutDepth / Module1.Pitch) + 0)) / 2) + ((RafterDepth / (Math.Sin(Module1.Beta))) + (StrutDepth / Module1.Pitch))), ((((Module1.Span / 3) - ((RafterDepth / (Math.Sin(Module1.Beta))) + (StrutDepth / Module1.Pitch) + 0)) / 2) * Module1.Pitch) + Module1.EaveHt + StrutDepth, 0);
				QPTSRight.Draw();
			}

			if (HasQPGirt) {
				QPUGirt.Width = UpperGirtWidth;
				QPUGirt.Depth = UpperGirtDepth;
				QPUGirt.QpostDepth = QpostDepth;
				QPUGirt.Type = "Girt";
				QPUGirt.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
                QPUGirt.Designation = ((char)(Module1.BentWallNumber + 1)).ToString() + ((char)(Module1.BentWallNumber + 2)).ToString();
				QPUGirt.StartPoint = new Point3d((Module1.Span / 3) + QpostDepth, Module1.EaveHt + ((((Module1.Span / 3) + QpostDepth) * Module1.Pitch) - (Module1.PlumbLength + 6 + UpperGirtDepth)), 0);
				QPUGirt.Draw();
			}

			if (Module1.HasJoinery) {
				if (HasGirt & HasQPost){TGirt.AddMortise(QPTPLeft.TenonDownId);TGirt.AddMortise(QPTPRight.TenonDownId);}
				if (HasGirt & HasRafter){TGirt.AddMortise(QPTRLeft.TenonId);TGirt.AddMortise(QPTRRight.TenonDownId);}

				if (HasRafter & HasStrut){QPTRLeft.AddMortise(QPTSLeft.TenonUpId);QPTRRight.AddMortise(QPTSRight.TenonUpId);}
				if (HasRafter & HasQPost){QPTRLeft.AddMortise(QPTPLeft.TenonUpId);QPTRRight.AddMortise(QPTPRight.TenonUpId);}
				if (HasRafter)
					QPTRLeft.AddMortise(QPTRRight.TenonUpId);

				if (HasQPost & HasStrut){QPTPLeft.AddMortise(QPTSLeft.TenonDownId);QPTPRight.AddMortise(QPTSRight.TenonDownId);}
				if (HasQPost & HasQPBrace) {
					if (Module1.OffsetType == 2){QPTPLeft.AddMortise(QPTBLeft.TenonUp);QPTPRight.AddMortise(QPTBRight.TenonDown);}
					else{QPTPLeft.AddMortise(QPTBLeft.TenonDown);QPTPRight.AddMortise(QPTBRight.TenonUp);}
				}
				if (HasQPost & HasQPGirt){QPTPLeft.AddMortise(QPUGirt.TenonLeftId);QPTPRight.AddMortise(QPUGirt.TenonRightId);}
				if (HasQPGirt & HasQPBrace){QPUGirt.AddMortise(QPTBLeft.TenonUp);QPUGirt.AddMortise(QPTBRight.TenonDown);}

				if (HasGirt) {
					if (HasQPost) {
						Module1.AddConnection(TGirt.Timber, QPTPLeft.TimberId, Module1.JointType.Mortise);
						Module1.AddConnection(TGirt.Timber, QPTPRight.TimberId, Module1.JointType.Mortise);
					}
					if (HasRafter) {
						Module1.AddConnection(TGirt.Timber, QPTRLeft.TimberId, Module1.JointType.Mortise);
						Module1.AddConnection(TGirt.Timber, QPTRRight.TimberId, Module1.JointType.Mortise);
					}
				}
				if (HasRafter) {
					if (HasGirt) {
						Module1.AddConnection(QPTRLeft.TimberId, TGirt.Timber, Module1.JointType.Tenon);
						Module1.AddConnection(QPTRRight.TimberId, TGirt.Timber, Module1.JointType.Tenon);
					}
					if (HasStrut) {
						Module1.AddConnection(QPTRLeft.TimberId, QPTSLeft.TimberId, Module1.JointType.Mortise);
						Module1.AddConnection(QPTRRight.TimberId, QPTSRight.TimberId, Module1.JointType.Mortise);
					}
					if (HasQPost) {
						Module1.AddConnection(QPTRLeft.TimberId, QPTPLeft.TimberId, Module1.JointType.Mortise);
						Module1.AddConnection(QPTRRight.TimberId, QPTPRight.TimberId, Module1.JointType.Mortise);
					}
					// ridge joint: left rafter receives right rafter's tenon
					Module1.AddConnection(QPTRLeft.TimberId, QPTRRight.TimberId, Module1.JointType.Mortise);
					Module1.AddConnection(QPTRRight.TimberId, QPTRLeft.TimberId, Module1.JointType.Tenon);
				}
				if (HasStrut) {
					if (HasRafter) {
						Module1.AddConnection(QPTSLeft.TimberId, QPTRLeft.TimberId, Module1.JointType.Tenon);
						Module1.AddConnection(QPTSRight.TimberId, QPTRRight.TimberId, Module1.JointType.Tenon);
					}
					if (HasQPost) {
						Module1.AddConnection(QPTSLeft.TimberId, QPTPLeft.TimberId, Module1.JointType.Tenon);
						Module1.AddConnection(QPTSRight.TimberId, QPTPRight.TimberId, Module1.JointType.Tenon);
					}
				}
				if (HasQPost) {
					if (HasGirt) {
						Module1.AddConnection(QPTPLeft.TimberId, TGirt.Timber, Module1.JointType.Tenon);
						Module1.AddConnection(QPTPRight.TimberId, TGirt.Timber, Module1.JointType.Tenon);
					}
					if (HasRafter) {
						Module1.AddConnection(QPTPLeft.TimberId, QPTRLeft.TimberId, Module1.JointType.Tenon);
						Module1.AddConnection(QPTPRight.TimberId, QPTRRight.TimberId, Module1.JointType.Tenon);
					}
					if (HasStrut) {
						Module1.AddConnection(QPTPLeft.TimberId, QPTSLeft.TimberId, Module1.JointType.Mortise);
						Module1.AddConnection(QPTPRight.TimberId, QPTSRight.TimberId, Module1.JointType.Mortise);
					}
					if (HasQPBrace) {
						Module1.AddConnection(QPTPLeft.TimberId, QPTBLeft.TimberId, Module1.JointType.Mortise);
						Module1.AddConnection(QPTPRight.TimberId, QPTBRight.TimberId, Module1.JointType.Mortise);
					}
					if (HasQPGirt) {
						Module1.AddConnection(QPTPLeft.TimberId, QPUGirt.TimberId, Module1.JointType.Mortise);
						Module1.AddConnection(QPTPRight.TimberId, QPUGirt.TimberId, Module1.JointType.Mortise);
					}
				}
				if (HasQPGirt) {
					if (HasQPost) {
						Module1.AddConnection(QPUGirt.TimberId, QPTPLeft.TimberId, Module1.JointType.Tenon);
						Module1.AddConnection(QPUGirt.TimberId, QPTPRight.TimberId, Module1.JointType.Tenon);
					}
					if (HasQPBrace) {
						Module1.AddConnection(QPUGirt.TimberId, QPTBLeft.TimberId, Module1.JointType.Mortise);
						Module1.AddConnection(QPUGirt.TimberId, QPTBRight.TimberId, Module1.JointType.Mortise);
					}
				}
				if (HasQPBrace) {
					if (HasQPost) {
						Module1.AddConnection(QPTBLeft.TimberId, QPTPLeft.TimberId, Module1.JointType.Tenon);
						Module1.AddConnection(QPTBRight.TimberId, QPTPRight.TimberId, Module1.JointType.Tenon);
					}
					if (HasQPGirt) {
						Module1.AddConnection(QPTBLeft.TimberId, QPUGirt.TimberId, Module1.JointType.Tenon);
						Module1.AddConnection(QPTBRight.TimberId, QPUGirt.TimberId, Module1.JointType.Tenon);
					}
				}
			}
		}
	}
}
