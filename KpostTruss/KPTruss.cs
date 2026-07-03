using System;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{

	public class KPTruss
	{
		public double GirtWidth;
		public double GirtDepth;
		public double RafterWidth;
		public double RafterDepth;
		public double KpostWidth;
		public double KpostDepth;
		public double StrutWidth;
		public double StrutDepth;
		public double VStrutWidth;
		public double VStrutDepth;
		public bool HasGirt;
		public bool HasRafter;
		public bool HasKPost;
		public bool HasStrut;
		public bool HasVStrut;
		public TrussGirt Tgirt = new();
		public KPTRafterLeft KPTRLeft = new();
		public KPTRafterRight KPTRRight = new();
		public KPTStrutLeft KPTSLeft = new();
		public KPTStrutRight KPTSRight = new();
		public KPTVertStrutLeft KPTVSLeft = new();
		public KPTVertStrutRight KPTVSRight = new();

		public KPTPost KPTP = new();
		public bool DataReady()
		{
			bool flag = true;
			if (HasGirt)
				if (GirtWidth <= 0 | GirtDepth <= 0)
					flag = false;
			if (HasRafter)
				if (RafterWidth <= 0 | RafterDepth <= 0)
					flag = false;
			if (HasKPost)
				if (KpostWidth <= 0 | KpostDepth <= 0)
					flag = false;
			if (HasStrut)
				if (StrutWidth <= 0 | StrutDepth <= 0)
					flag = false;
			if (HasVStrut)
				if (VStrutWidth <= 0 | VStrutDepth <= 0)
					flag = false;
			return flag;
		}

		public void Draw()
		{
			if (HasGirt) {
				Tgirt.Width = GirtWidth;
				Tgirt.Depth = GirtDepth;
				Tgirt.RafterDepth = RafterDepth;
				Tgirt.Type = "Girt";
				Tgirt.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
				Tgirt.Designation = ((char)(Module1.BentWallNumber)).ToString() + ((char)(Module1.BentWallNumber + 4)).ToString();
				Tgirt.Draw();
			}

			if (HasRafter) {
				KPTRLeft.Width = RafterWidth;
				KPTRLeft.Depth = RafterDepth;
				KPTRLeft.KPostDepth = KpostDepth;
				KPTRLeft.Type = "Rafter";
				KPTRLeft.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
				KPTRLeft.Designation = ((char)(Module1.BentWallNumber)).ToString();
				KPTRLeft.StartPoint = new Point3d(0, Module1.EaveHt, 0);
				KPTRLeft.Draw();

				KPTRRight.Width = RafterWidth;
				KPTRRight.Depth = RafterDepth;
				KPTRRight.KPostDepth = KpostDepth;
				KPTRRight.Type = "Rafter";
				KPTRRight.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
				KPTRRight.Designation = ((char)(Module1.BentWallNumber + 4)).ToString();
				KPTRRight.StartPoint = new Point3d(Module1.Span, Module1.EaveHt, 0);
				KPTRRight.Draw();
			}

			if (HasStrut) {
				KPTSLeft.Width = StrutWidth;
				KPTSLeft.Depth = StrutDepth;
				KPTSLeft.KPostWidth = KpostWidth;
				KPTSLeft.KPostDepth = KpostDepth;
				KPTSLeft.KPRafterDepth = RafterDepth;
				KPTSLeft.Type = "Strut";
				KPTSLeft.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
				KPTSLeft.Designation = ((char)(Module1.BentWallNumber + 1)).ToString();
				KPTSLeft.StartPoint = new Point3d((((Module1.Span / 2) - ((RafterDepth / (Math.Sin(Module1.Beta))) + (6 / Module1.Pitch) + (KpostDepth / 2))) / 2) + ((RafterDepth / (Math.Sin(Module1.Beta))) + (6 / Module1.Pitch)), ((((Module1.Span / 2) - ((RafterDepth / (Math.Sin(Module1.Beta))) + (6 / Module1.Pitch) + (KpostDepth / 2))) / 2) * Module1.Pitch) + Module1.EaveHt + 6, 0);
				KPTSLeft.Draw();

				KPTSRight.Width = StrutWidth;
				KPTSRight.Depth = StrutDepth;
				KPTSRight.KPostWidth = KpostWidth;
				KPTSRight.KPostDepth = KpostDepth;
				KPTSRight.KPRafterDepth = RafterDepth;
				KPTSRight.Type = "Strut";
				KPTSRight.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
				KPTSRight.Designation = ((char)(Module1.BentWallNumber + 3)).ToString();
				KPTSRight.StartPoint = new Point3d(Module1.Span - ((((Module1.Span / 2) - ((RafterDepth / (Math.Sin(Module1.Beta))) + (6 / Module1.Pitch) + (KpostDepth / 2))) / 2) + ((RafterDepth / (Math.Sin(Module1.Beta))) + (6 / Module1.Pitch))), ((((Module1.Span / 2) - ((RafterDepth / (Math.Sin(Module1.Beta))) + (6 / Module1.Pitch) + (KpostDepth / 2))) / 2) * Module1.Pitch) + Module1.EaveHt + 6, 0);
				KPTSRight.Draw();
			}

			if (HasVStrut) {
				KPTVSLeft.Width = VStrutWidth;
				KPTVSLeft.Depth = VStrutDepth;
				KPTVSLeft.KPostWidth = KpostWidth;
				KPTVSLeft.KPostDepth = KpostDepth;
				KPTVSLeft.KPRafterDepth = RafterDepth;
				KPTVSLeft.Type = "VStrut";
				KPTVSLeft.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
				KPTVSLeft.Designation = ((char)(Module1.BentWallNumber + 1)).ToString();
				KPTVSLeft.StartPoint = new Point3d((((Module1.Span / 2) - ((RafterDepth / (Math.Sin(Module1.Beta))) + (6 / Module1.Pitch) + (KpostDepth / 2))) / 2) + ((RafterDepth / (Math.Sin(Module1.Beta))) + (6 / Module1.Pitch)) - VStrutDepth, Module1.EaveHt, 0);
				KPTVSLeft.Draw();

				KPTVSRight.Width = VStrutWidth;
				KPTVSRight.Depth = VStrutDepth;
				KPTVSRight.KPostWidth = KpostWidth;
				KPTVSRight.KPostDepth = KpostDepth;
				KPTVSRight.KPRafterDepth = RafterDepth;
				KPTVSRight.Type = "VStrut";
				KPTVSRight.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
				KPTVSRight.Designation = ((char)(Module1.BentWallNumber + 3)).ToString();
				KPTVSRight.StartPoint = new Point3d(Module1.Span - ((((Module1.Span / 2) - ((RafterDepth / (Math.Sin(Module1.Beta))) + (6 / Module1.Pitch) + (KpostDepth / 2))) / 2) + ((RafterDepth / (Math.Sin(Module1.Beta))) + (6 / Module1.Pitch))), Module1.EaveHt, 0);
				KPTVSRight.Draw();
			}

			if (HasKPost) {
				KPTP.Width = KpostWidth;
				KPTP.Depth = KpostDepth;
				KPTP.KPRafterDepth = RafterDepth;
				KPTP.Type = "KPost";
				KPTP.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
				KPTP.Designation = ((char)(Module1.BentWallNumber + 2)).ToString();
				KPTP.StartPoint = new Point3d((Module1.Span / 2) - (KpostDepth / 2), Module1.EaveHt, 0);
				KPTP.Draw();
			}

			if (Module1.HasJoinery) {
				if (HasGirt & HasRafter){Tgirt.AddMortise(KPTRLeft.Tenon);Tgirt.AddMortise(KPTRRight.Tenon);}
				if (HasGirt & HasVStrut){Tgirt.AddMortise(KPTVSLeft.TenonDown);Tgirt.AddMortise(KPTVSRight.TenonDown);}
				if (HasGirt & HasKPost)
					Tgirt.AddMortise(KPTP.Tenon);

				if (HasRafter & HasStrut){KPTRLeft.AddMortise(KPTSLeft.TenonUp);KPTRRight.AddMortise(KPTSRight.TenonUp);}
				if (HasRafter & HasVStrut){KPTRLeft.AddMortise(KPTVSLeft.TenonUp);KPTRRight.AddMortise(KPTVSRight.TenonUp);}

				if (HasKPost & HasStrut){KPTP.AddMortise(KPTSLeft.TenonDown);KPTP.AddMortise(KPTSRight.TenonDown);}

				if (HasGirt) {
					if (HasRafter) {
						Module1.AddConnection(Tgirt.Timber, KPTRLeft.Timber, Module1.JointType.Mortise);
						Module1.AddConnection(Tgirt.Timber, KPTRRight.Timber, Module1.JointType.Mortise);
					}
					if (HasVStrut) {
						Module1.AddConnection(Tgirt.Timber, KPTVSLeft.Timber, Module1.JointType.Mortise);
						Module1.AddConnection(Tgirt.Timber, KPTVSRight.Timber, Module1.JointType.Mortise);
					}
					if (HasKPost) {
						Module1.AddConnection(Tgirt.Timber, KPTP.Timber, Module1.JointType.Mortise);
					}
				}
				if (HasRafter) {
					if (HasGirt) {
						Module1.AddConnection(KPTRLeft.Timber, Tgirt.Timber, Module1.JointType.Tenon);
						Module1.AddConnection(KPTRRight.Timber, Tgirt.Timber, Module1.JointType.Tenon);
					}
					if (HasStrut) {
						Module1.AddConnection(KPTRLeft.Timber, KPTSLeft.Timber, Module1.JointType.Mortise);
						Module1.AddConnection(KPTRRight.Timber, KPTSRight.Timber, Module1.JointType.Mortise);
					}
					if (HasVStrut) {
						Module1.AddConnection(KPTRLeft.Timber, KPTVSLeft.Timber, Module1.JointType.Mortise);
						Module1.AddConnection(KPTRRight.Timber, KPTVSRight.Timber, Module1.JointType.Mortise);
					}
				}
				if (HasStrut) {
					if (HasRafter) {
						Module1.AddConnection(KPTSLeft.Timber, KPTRLeft.Timber, Module1.JointType.Tenon);
						Module1.AddConnection(KPTSRight.Timber, KPTRRight.Timber, Module1.JointType.Tenon);
					}
					if (HasKPost) {
						Module1.AddConnection(KPTSLeft.Timber, KPTP.Timber, Module1.JointType.Tenon);
						Module1.AddConnection(KPTSRight.Timber, KPTP.Timber, Module1.JointType.Tenon);
					}
				}
				if (HasVStrut) {
					if (HasGirt) {
						Module1.AddConnection(KPTVSLeft.Timber, Tgirt.Timber, Module1.JointType.Tenon);
						Module1.AddConnection(KPTVSRight.Timber, Tgirt.Timber, Module1.JointType.Tenon);
					}
					if (HasRafter) {
						Module1.AddConnection(KPTVSLeft.Timber, KPTRLeft.Timber, Module1.JointType.Tenon);
						Module1.AddConnection(KPTVSRight.Timber, KPTRRight.Timber, Module1.JointType.Tenon);
					}
				}
				if (HasKPost) {
					if (HasGirt) {
						Module1.AddConnection(KPTP.Timber, Tgirt.Timber, Module1.JointType.Tenon);
					}
					if (HasStrut) {
						Module1.AddConnection(KPTP.Timber, KPTSLeft.Timber, Module1.JointType.Mortise);
						Module1.AddConnection(KPTP.Timber, KPTSRight.Timber, Module1.JointType.Mortise);
					}
				}
			}

		}

	}
}
