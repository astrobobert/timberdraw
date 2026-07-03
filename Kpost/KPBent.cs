using System;
using System.Windows.Forms;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{

	public class KPBent
	{
		public double FlrGirtWidth;
		public double FlrGirtDepth;
		public double FlrGirtHt;
		public double FlrGirtBraceSize;
		public double postWidth;
		public double postDepth;
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
		public double BraceWidth;
		public double BraceDepth;
		public double BraceLength;
		public double FlrBraceWidth;
		public double FlrBraceDepth;
		public double FlrBraceLength;
		public bool HasFlrGirt;
		public bool HasPost;
		public bool HasGirt;
		public bool HasRafter;
		public bool HasKPost;
		public bool HasStrut;
		public bool HasVStrut;
		public bool HasBrace;
		public bool HasFlrBrace;
		public KPost KP = new();
		public PostLeft PLeft = new();
		public PostRight PRight = new();
		public KPRafterLeft RLeft = new();
		public KPRafterRight RRight = new();
		public BentGirt BGirt = new();
		public BentBrace BBLeft = new();
		public BentBrace BBRight = new();
		public KPStrutLeft SLeft = new();
		public KPStrutRight SRight = new();
		public KPVertStrutLeft VSLeft = new();
		public KPVertStrutRight VSRight = new();
		public FloorBentGirt FlrBentGirt = new();
		public BentBrace FBLeft = new();
		public BentBrace FBRight = new();

        public bool DataReady()
		{
			bool ready = true;
			if (Module1.EaveHt <= 0)
				ready = false;
			if (Module1.Span <= 0)
				ready = false;
			if (HasPost)
				if (postWidth <= 0 | postDepth <= 0)
					ready = false;
			if (HasGirt)
				if (GirtWidth <= 0 | GirtDepth <= 0)
					ready = false;
			if (HasRafter)
				if (RafterWidth <= 0 | RafterDepth <= 0)
					ready = false;
			if (HasKPost)
				if (KpostWidth <= 0 | KpostDepth <= 0)
					ready = false;
			if (HasStrut)
				if (StrutWidth <= 0 | StrutDepth <= 0)
					ready = false;
			if (HasVStrut)
				if (VStrutWidth <= 0 | VStrutDepth <= 0)
					ready = false;
			if (HasFlrGirt)
				if (FlrGirtWidth <= 0 | FlrGirtDepth <= 0 | FlrGirtHt <= 0)
					ready = false;
			if (HasBrace)
				if (BraceWidth <= 0 | BraceDepth <= 0 | BraceLength <= 0)
					ready = false;
			if (HasFlrBrace)
				if (FlrBraceWidth <= 0 | FlrBraceDepth <= 0 | FlrBraceLength <= 0)
					ready = false;
			if (!ready)
				MessageBox.Show("King Post Bent Data Not ready", "Data Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return ready;
		}

		public void Draw()
		{
			// Register this bent in the network so TimberFactory can re-cut incoming
			// joints for any member without per-type migration.
			string bentNum = Convert.ToString(Properties.Settings.Default.BentNumber);
			BentNetwork.BeginBent(bentNum);

			Module1.PlumbLength = RafterDepth / Math.Cos(Module1.Beta);
			Module1.TOH = (Module1.EaveHt + (postDepth * Module1.Pitch)) - Module1.PlumbLength;
			Module1.TOG = Module1.TOH - 6;
			Module1.BOG = Module1.TOG - GirtDepth;

			if (HasGirt) {
				BGirt.Width = GirtWidth;
				BGirt.Depth = GirtDepth;
				BGirt.postDepth = postDepth;
				BGirt.Type = "Girt";
				BGirt.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
                BGirt.Designation = ((char)(Module1.BentWallNumber)).ToString() + ((char)(Module1.BentWallNumber + 4)).ToString();
				BGirt.StartPoint = new Point3d(postDepth, Module1.BOG, 0);
				BGirt.Draw();
			}

			if (HasBrace) {
				BBLeft.StartPoint = new Point3d(postDepth, Module1.TOH - (GirtDepth + 6), 0);
				BBLeft.Width = BraceWidth;
				BBLeft.Depth = BraceDepth;
				BBLeft.Length = BraceLength;
				BBLeft.postWidth = postWidth;
				BBLeft.ZAngle = 0;
				BBLeft.YAngle = 0;
				// SwapEnds depends on OffsetType: non-Front=Down/post, Front=Up/post
				BBLeft.SwapEnds = (Module1.OffsetType == Module1.Front);
				BBLeft.Draw();

				BBRight.StartPoint = new Point3d(Module1.Span - postDepth, Module1.TOH - (GirtDepth + 6), 0);
				BBRight.Width = BraceWidth;
				BBRight.Depth = BraceDepth;
				BBRight.Length = BraceLength;
				BBRight.postWidth = postWidth;
				BBRight.ZAngle = 270;
				BBRight.YAngle = 0;
				// SwapEnds opposite of BBLeft (mirrored brace)
				BBRight.SwapEnds = (Module1.OffsetType != Module1.Front);
				BBRight.Draw();
			}

			if (HasPost) {
				PLeft.Width = postWidth;
				PLeft.Depth = postDepth;
				PLeft.Type = "Post";
				PLeft.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
				PLeft.Designation = ((char)(Module1.BentWallNumber)).ToString();
				PLeft.HasFlrGirt = HasFlrGirt;
				PLeft.FlrGirtHt = FlrGirtHt;
				PLeft.FlrGirtDepth = FlrGirtDepth;
				PLeft.Draw();

				PRight.Width = postWidth;
				PRight.Depth = postDepth;
				PRight.Type = "Post";
				PRight.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
				PRight.Designation = ((char)(Module1.BentWallNumber + 4)).ToString();
				PRight.HasFlrGirt = HasFlrGirt;
				PRight.FlrGirtHt = FlrGirtHt;
				PRight.FlrGirtDepth = FlrGirtDepth;
				PRight.Draw();
			}

			if (HasRafter) {
				if (Module1.Prise < 4)
					RLeft.KpostRafterSitDepth = 2;
				else
					RLeft.KpostRafterSitDepth = 3;
				RLeft.Width = RafterWidth;
				RLeft.Depth = RafterDepth;
				RLeft.postDepth = postDepth;
				RLeft.KPostDepth = KpostDepth;
				RLeft.Type = "Rafter";
				RLeft.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
				RLeft.Designation = ((char)(Module1.BentWallNumber)).ToString();
				RLeft.StartPoint = new Point3d(postDepth, Module1.TOH, 0);
				RLeft.Draw();

				if (Module1.Prise < 4)
					RRight.KpostRafterSitDepth = 2;
				else
					RRight.KpostRafterSitDepth = 3;
				RRight.Width = RafterWidth;
				RRight.Depth = RafterDepth;
				RRight.postDepth = postDepth;
				RRight.KPostDepth = KpostDepth;
				RRight.Type = "Rafter";
				RRight.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
				RRight.Designation = ((char)(Module1.BentWallNumber + 4)).ToString();
				RRight.StartPoint = new Point3d(Module1.Span - postDepth, Module1.TOH, 0);
				RRight.Draw();
			}

			if (HasStrut) {
				SLeft.EaveHt = Module1.EaveHt;
				SLeft.Span = Module1.Span;
				SLeft.Width = StrutWidth;
				SLeft.Depth = StrutDepth;
				SLeft.postDepth = postDepth;
				SLeft.KPostWidth = KpostWidth;
				SLeft.KPostDepth = KpostDepth;
				SLeft.Type = "Strut";
				SLeft.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
				SLeft.Designation = ((char)(Module1.BentWallNumber + 1)).ToString();
				SLeft.StartPoint = new Point3d((((Module1.Span / 2) - (postDepth + (KpostDepth / 2))) / 2) + postDepth, ((((Module1.Span / 2) - (postDepth + (KpostDepth / 2))) / 2) * Module1.Pitch) + Module1.TOH, 0);
				SLeft.Draw();

				SRight.EaveHt = Module1.EaveHt;
				SRight.Span = Module1.Span;
				SRight.Width = StrutWidth;
				SRight.Depth = StrutDepth;
				SRight.postDepth = postDepth;
				SRight.KPostWidth = KpostWidth;
				SRight.KPostDepth = KpostDepth;
				SRight.Type = "Strut";
				SRight.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
                SRight.Designation = ((char)(Module1.BentWallNumber + 3)).ToString();
				SRight.StartPoint = new Point3d(((Module1.Span - ((postDepth * 2) + KpostDepth)) * 0.75) + (postDepth + KpostDepth), Module1.TOH + ((((Module1.Span / 2) - (postDepth + 5)) / 2) * Module1.Pitch), 0);
				SRight.Draw();
			}

			if (HasVStrut) {
				VSLeft.EaveHt = Module1.EaveHt;
				VSLeft.Span = Module1.Span;
				VSLeft.Width = VStrutWidth;
				VSLeft.Depth = VStrutDepth;
				VSLeft.postDepth = postDepth;
				VSLeft.KPostWidth = KpostWidth;
				VSLeft.KPostDepth = KpostDepth;
				VSLeft.Type = "VStrut";
				VSLeft.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
                VSLeft.Designation = ((char)(Module1.BentWallNumber + 1)).ToString();
				VSLeft.StartPoint = new Point3d((((Module1.Span / 2) - (postDepth + (KpostDepth / 2))) / 2) + postDepth - VStrutDepth, Module1.TOG, 0);
				VSLeft.Draw();

				VSRight.EaveHt = Module1.EaveHt;
				VSRight.Span = Module1.Span;
				VSRight.Width = VStrutWidth;
				VSRight.Depth = VStrutDepth;
				VSRight.postDepth = postDepth;
				VSRight.KPostWidth = KpostWidth;
				VSRight.KPostDepth = KpostDepth;
				VSRight.Type = "VStrut";
				VSRight.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
                VSRight.Designation = ((char)(Module1.BentWallNumber + 3)).ToString();
				VSRight.StartPoint = new Point3d(((Module1.Span - ((postDepth * 2) + KpostDepth)) * 0.75) + (postDepth + KpostDepth), Module1.TOG, 0);
				VSRight.Draw();
			}

			if (HasKPost) {
				if (Module1.Prise < 4)
					KP.KpostRafterSitDepth = 2;
				else
					KP.KpostRafterSitDepth = 3;
				KP.Width = KpostWidth;
				KP.Depth = KpostDepth;
				KP.postDepth = postDepth;
				KP.Type = "KPost";
				KP.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
                KP.Designation = ((char)(Module1.BentWallNumber + 2)).ToString();
				KP.StartPoint = new Point3d((Module1.Span / 2) - (KpostDepth / 2), Module1.TOG, 0);
				KP.Draw();
			}

			if (HasFlrGirt) {
				FlrBentGirt.StartPoint  = new Point3d(postDepth, FlrGirtHt - FlrGirtDepth, 0);
				FlrBentGirt.Width       = FlrGirtWidth;
				FlrBentGirt.Depth       = FlrGirtDepth;
				FlrBentGirt.Height      = FlrGirtHt;
				FlrBentGirt.Type        = "Girt";
				FlrBentGirt.BentNumber  = Convert.ToString(Properties.Settings.Default.BentNumber);
				FlrBentGirt.Designation = ((char)(Module1.BentWallNumber)).ToString() + ((char)(Module1.BentWallNumber + 4)).ToString();
				FlrBentGirt.Draw(postDepth);
			}

			if (HasFlrBrace) {
				FBLeft.StartPoint = new Point3d(postDepth, FlrGirtHt - FlrGirtDepth, 0);
				FBLeft.Width      = FlrBraceWidth;
				FBLeft.Depth      = FlrBraceDepth;
				FBLeft.Length     = FlrBraceLength;
				FBLeft.postWidth  = postWidth;
				FBLeft.ZAngle     = 0;
				FBLeft.YAngle     = 0;
				// SwapEnds=false: TenonDown=post(Near), TenonUp=girt(Far) -- same hand as BBLeft
				FBLeft.SwapEnds   = false;
				FBLeft.Draw();

				FBRight.StartPoint = new Point3d(Module1.Span - postDepth, FlrGirtHt - FlrGirtDepth, 0);
				FBRight.Width      = FlrBraceWidth;
				FBRight.Depth      = FlrBraceDepth;
				FBRight.Length     = FlrBraceLength;
				FBRight.postWidth  = postWidth;
				FBRight.ZAngle     = 270;
				FBRight.YAngle     = 0;
				// SwapEnds=true: ZAngle=270 rotation maps TenonUp→PRight, TenonDown→FlrBentGirt.
				// Same hand as BBRight (also ZAngle=270, SwapEnds=true).
				FBRight.SwapEnds   = true;
				FBRight.Draw();
			}

			if (Module1.HasJoinery) {
				if (HasGirt & HasBrace) {
					// BGirt is the girt-end receiver for both braces -> always ThisEnd=Far.
					// The tenon that contacts BGirt is determined by the brace hand + OffsetType.
					if (Module1.OffsetType == 2) {
						// Front: BBLeft.TenonDown->BGirt(girt=Far), BBRight.TenonUp->BGirt(girt=Far)
						Module1.AddConnectionFull(BBLeft.TimberId, new Module1.Connection {
							ConnHandle = BGirt.TimberId.Handle, ThisEnd = Module1.End.Far,
							OtherEnd = Module1.End.Body, ThisJoint = Module1.JointType.Tenon,
							TenonHandles = BBLeft.TenonDown.IsNull ? System.Array.Empty<Handle>() : new[] { BBLeft.TenonDown.Handle }
						});
						BGirt.AddMortise(BBLeft.TenonDown);
						Module1.AddConnectionFull(BBRight.TimberId, new Module1.Connection {
							ConnHandle = BGirt.TimberId.Handle, ThisEnd = Module1.End.Far,
							OtherEnd = Module1.End.Body, ThisJoint = Module1.JointType.Tenon,
							TenonHandles = BBRight.TenonUp.IsNull ? System.Array.Empty<Handle>() : new[] { BBRight.TenonUp.Handle }
						});
						BGirt.AddMortise(BBRight.TenonUp);
					} else {
						// Default: BBLeft.TenonUp->BGirt(girt=Far), BBRight.TenonDown->BGirt(girt=Far)
						Module1.AddConnectionFull(BBLeft.TimberId, new Module1.Connection {
							ConnHandle = BGirt.TimberId.Handle, ThisEnd = Module1.End.Far,
							OtherEnd = Module1.End.Body, ThisJoint = Module1.JointType.Tenon,
							TenonHandles = BBLeft.TenonUp.IsNull ? System.Array.Empty<Handle>() : new[] { BBLeft.TenonUp.Handle }
						});
						BGirt.AddMortise(BBLeft.TenonUp);
						Module1.AddConnectionFull(BBRight.TimberId, new Module1.Connection {
							ConnHandle = BGirt.TimberId.Handle, ThisEnd = Module1.End.Far,
							OtherEnd = Module1.End.Body, ThisJoint = Module1.JointType.Tenon,
							TenonHandles = BBRight.TenonDown.IsNull ? System.Array.Empty<Handle>() : new[] { BBRight.TenonDown.Handle }
						});
						BGirt.AddMortise(BBRight.TenonDown);
					}
				}
				if (HasGirt & HasVStrut) {
					// JF foot tenons: register in network + store for direct-regen re-cut.
					Module1.PrepareIncomingJointRecord(BGirt.TimberId, VSLeft.TimberId.Handle, VSLeft.NearJointParamsDrawn);
					BentNetwork.RegisterEdge(VSLeft.TimberId.Handle, Module1.End.Near,
						BGirt.TimberId.Handle, Module1.End.Body,
						VSLeft.NearJointParamsDrawn.JointType.ToString(), VSLeft.NearJointParamsDrawn);
					// Store live handle before AddMortise erases it so the rich cascade path works.
					Module1.AddConnectionFull(VSLeft.TimberId, new Module1.Connection {
						ConnHandle = BGirt.TimberId.Handle, ThisEnd = Module1.End.Near,
						OtherEnd = Module1.End.Body, ThisJoint = Module1.JointType.Tenon,
						TenonHandles = new[] { VSLeft.TenonDownId.Handle }
					});
					BGirt.AddMortise(VSLeft.TenonDownId);
					Module1.PrepareIncomingJointRecord(BGirt.TimberId, VSRight.TimberId.Handle, VSRight.NearJointParamsDrawn);
					BentNetwork.RegisterEdge(VSRight.TimberId.Handle, Module1.End.Near,
						BGirt.TimberId.Handle, Module1.End.Body,
						VSRight.NearJointParamsDrawn.JointType.ToString(), VSRight.NearJointParamsDrawn);
					Module1.AddConnectionFull(VSRight.TimberId, new Module1.Connection {
						ConnHandle = BGirt.TimberId.Handle, ThisEnd = Module1.End.Near,
						OtherEnd = Module1.End.Body, ThisJoint = Module1.JointType.Tenon,
						TenonHandles = new[] { VSRight.TenonDownId.Handle }
					});
					BGirt.AddMortise(VSRight.TenonDownId);
				}
				if (HasGirt & HasKPost) {
					Module1.PrepareIncomingJointRecord(BGirt.TimberId, KP.TimberId.Handle, KP.NearJointParamsDrawn);
					BentNetwork.RegisterEdge(KP.TimberId.Handle, Module1.End.Near,
						BGirt.TimberId.Handle, Module1.End.Body,
						KP.NearJointParamsDrawn.JointType.ToString(), KP.NearJointParamsDrawn);
					// Capture live handle before AddMortise erases it.
					Module1.AddConnectionFull(KP.TimberId, new Module1.Connection {
						ConnHandle = BGirt.TimberId.Handle, ThisEnd = Module1.End.Near,
						OtherEnd = Module1.End.Body, ThisJoint = Module1.JointType.Tenon,
						TenonHandles = new[] { KP.Tenon.Handle }
					});
					BGirt.AddMortise(KP.Tenon);
				}

				if (HasPost & HasGirt) {
					Module1.PrepareIncomingJointRecord(PLeft.TimberId, BGirt.TimberId.Handle, BGirt.NearJointParamsDrawn);
					BentNetwork.RegisterEdge(BGirt.TimberId.Handle, Module1.End.Near,
						PLeft.TimberId.Handle, Module1.End.Body,
						BGirt.NearJointParamsDrawn.JointType.ToString(), BGirt.NearJointParamsDrawn);
					PLeft.AddMortise(BGirt.TenonLeftId);
					Module1.PrepareIncomingJointRecord(PRight.TimberId, BGirt.TimberId.Handle, BGirt.FarJointParamsDrawn);
					BentNetwork.RegisterEdge(BGirt.TimberId.Handle, Module1.End.Far,
						PRight.TimberId.Handle, Module1.End.Body,
						BGirt.FarJointParamsDrawn.JointType.ToString(), BGirt.FarJointParamsDrawn);
					PRight.AddMortise(BGirt.TenonRightId);
				}
				if (HasPost & HasRafter) {
					Module1.PrepareIncomingJointRecord(PLeft.TimberId, RLeft.TimberId.Handle, RLeft.FarJointParamsDrawn);
					BentNetwork.RegisterEdge(RLeft.TimberId.Handle, Module1.End.Far,
						PLeft.TimberId.Handle, Module1.End.Body,
						RLeft.FarJointParamsDrawn.JointType.ToString(), RLeft.FarJointParamsDrawn);
					Module1.AddConnectionFull(RLeft.TimberId, new Module1.Connection {
						ConnHandle = PLeft.TimberId.Handle, ThisEnd = Module1.End.Far,
						OtherEnd = Module1.End.Body, ThisJoint = Module1.JointType.Tenon,
						TenonHandles = new[] { RLeft.Tenon.Handle }
					});
					PLeft.AddMortise(RLeft.Tenon);
					Module1.PrepareIncomingJointRecord(PRight.TimberId, RRight.TimberId.Handle, RRight.FarJointParamsDrawn);
					BentNetwork.RegisterEdge(RRight.TimberId.Handle, Module1.End.Far,
						PRight.TimberId.Handle, Module1.End.Body,
						RRight.FarJointParamsDrawn.JointType.ToString(), RRight.FarJointParamsDrawn);
					Module1.AddConnectionFull(RRight.TimberId, new Module1.Connection {
						ConnHandle = PRight.TimberId.Handle, ThisEnd = Module1.End.Far,
						OtherEnd = Module1.End.Body, ThisJoint = Module1.JointType.Tenon,
						TenonHandles = new[] { RRight.Tenon.Handle }
					});
					PRight.AddMortise(RRight.Tenon);
				}
				if (HasPost & HasBrace) {
					// Post is the near-end receiver for both braces -> always ThisEnd=Near.
					if (Module1.OffsetType == 2) {
						// Front: BBLeft.TenonUp->PLeft(post=Near), BBRight.TenonDown->PRight(post=Near)
						Module1.AddConnectionFull(BBLeft.TimberId, new Module1.Connection {
							ConnHandle = PLeft.TimberId.Handle, ThisEnd = Module1.End.Near,
							OtherEnd = Module1.End.Body, ThisJoint = Module1.JointType.Tenon,
							TenonHandles = BBLeft.TenonUp.IsNull ? System.Array.Empty<Handle>() : new[] { BBLeft.TenonUp.Handle }
						});
						PLeft.AddMortise(BBLeft.TenonUp);
						Module1.AddConnectionFull(BBRight.TimberId, new Module1.Connection {
							ConnHandle = PRight.TimberId.Handle, ThisEnd = Module1.End.Near,
							OtherEnd = Module1.End.Body, ThisJoint = Module1.JointType.Tenon,
							TenonHandles = BBRight.TenonDown.IsNull ? System.Array.Empty<Handle>() : new[] { BBRight.TenonDown.Handle }
						});
						PRight.AddMortise(BBRight.TenonDown);
					} else {
						// Default: BBLeft.TenonDown->PLeft(post=Near), BBRight.TenonUp->PRight(post=Near)
						Module1.AddConnectionFull(BBLeft.TimberId, new Module1.Connection {
							ConnHandle = PLeft.TimberId.Handle, ThisEnd = Module1.End.Near,
							OtherEnd = Module1.End.Body, ThisJoint = Module1.JointType.Tenon,
							TenonHandles = BBLeft.TenonDown.IsNull ? System.Array.Empty<Handle>() : new[] { BBLeft.TenonDown.Handle }
						});
						PLeft.AddMortise(BBLeft.TenonDown);
						Module1.AddConnectionFull(BBRight.TimberId, new Module1.Connection {
							ConnHandle = PRight.TimberId.Handle, ThisEnd = Module1.End.Near,
							OtherEnd = Module1.End.Body, ThisJoint = Module1.JointType.Tenon,
							TenonHandles = BBRight.TenonUp.IsNull ? System.Array.Empty<Handle>() : new[] { BBRight.TenonUp.Handle }
						});
						PRight.AddMortise(BBRight.TenonUp);
					}
				}
				if (HasPost & HasFlrGirt) {
					Module1.PrepareIncomingJointRecord(PLeft.TimberId, FlrBentGirt.TimberId.Handle, FlrBentGirt.NearJointParamsDrawn);
					BentNetwork.RegisterEdge(FlrBentGirt.TimberId.Handle, Module1.End.Near,
						PLeft.TimberId.Handle, Module1.End.Body,
						FlrBentGirt.NearJointParamsDrawn.JointType.ToString(), FlrBentGirt.NearJointParamsDrawn);
					PLeft.AddMortise(FlrBentGirt.TenonLeftId);
					Module1.PrepareIncomingJointRecord(PRight.TimberId, FlrBentGirt.TimberId.Handle, FlrBentGirt.FarJointParamsDrawn);
					BentNetwork.RegisterEdge(FlrBentGirt.TimberId.Handle, Module1.End.Far,
						PRight.TimberId.Handle, Module1.End.Body,
						FlrBentGirt.FarJointParamsDrawn.JointType.ToString(), FlrBentGirt.FarJointParamsDrawn);
					PRight.AddMortise(FlrBentGirt.TenonRightId);
				}
				if (HasPost & HasFlrBrace) {
					// Post is Near for both floor braces.
					// FBLeft ZAngle=0,  SwapEnds=false: TenonDown (pts[0] area) extends -X → PLeft.
					// FBRight ZAngle=270, SwapEnds=true: 270° rotation maps TenonUp (pts[4] area) → PRight.
					Module1.AddConnectionFull(FBLeft.TimberId, new Module1.Connection {
						ConnHandle = PLeft.TimberId.Handle, ThisEnd = Module1.End.Near,
						OtherEnd = Module1.End.Body, ThisJoint = Module1.JointType.Tenon,
						TenonHandles = FBLeft.TenonDown.IsNull ? System.Array.Empty<Handle>() : new[] { FBLeft.TenonDown.Handle }
					});
					PLeft.AddMortise(FBLeft.TenonDown);
					Module1.AddConnectionFull(FBRight.TimberId, new Module1.Connection {
						ConnHandle = PRight.TimberId.Handle, ThisEnd = Module1.End.Near,
						OtherEnd = Module1.End.Body, ThisJoint = Module1.JointType.Tenon,
						TenonHandles = FBRight.TenonUp.IsNull ? System.Array.Empty<Handle>() : new[] { FBRight.TenonUp.Handle }
					});
					PRight.AddMortise(FBRight.TenonUp);
				}

				if (HasRafter & HasStrut) {
					Module1.AddConnectionFull(SLeft.TimberId, new Module1.Connection {
						ConnHandle = RLeft.TimberId.Handle, ThisEnd = Module1.End.Near,
						OtherEnd = Module1.End.Body, ThisJoint = Module1.JointType.Tenon,
						TenonHandles = new[] { SLeft.TenonUpId.Handle }
					});
					if (SLeft.NearTenonPts != null)
						BentNetwork.RegisterEdge(SLeft.TimberId.Handle, Module1.End.Near,
							RLeft.TimberId.Handle, Module1.End.Body, "Polygon",
							JointParams.ForPolygon(SLeft.NearTenonPts, SLeft.NearTenonWidth,
								SLeft.BentNumber, SLeft.Designation));
					Module1.SuppressNextMortiseBbox();
					RLeft.AddMortise(SLeft.TenonUpId);

					Module1.AddConnectionFull(SRight.TimberId, new Module1.Connection {
						ConnHandle = RRight.TimberId.Handle, ThisEnd = Module1.End.Near,
						OtherEnd = Module1.End.Body, ThisJoint = Module1.JointType.Tenon,
						TenonHandles = new[] { SRight.TenonUpId.Handle }
					});
					if (SRight.NearTenonPts != null)
						BentNetwork.RegisterEdge(SRight.TimberId.Handle, Module1.End.Near,
							RRight.TimberId.Handle, Module1.End.Body, "Polygon",
							JointParams.ForPolygon(SRight.NearTenonPts, SRight.NearTenonWidth,
								SRight.BentNumber, SRight.Designation));
					Module1.SuppressNextMortiseBbox();
					RRight.AddMortise(SRight.TenonUpId);
				}
				if (HasRafter & HasVStrut) {
					Module1.AddConnectionFull(VSLeft.TimberId, new Module1.Connection {
						ConnHandle = RLeft.TimberId.Handle, ThisEnd = Module1.End.Far,
						OtherEnd = Module1.End.Body, ThisJoint = Module1.JointType.Tenon,
						TenonHandles = new[] { VSLeft.TenonUpId.Handle }
					});
					if (VSLeft.FarTenonPts != null)
						BentNetwork.RegisterEdge(VSLeft.TimberId.Handle, Module1.End.Far,
							RLeft.TimberId.Handle, Module1.End.Body, "Polygon",
							JointParams.ForPolygon(VSLeft.FarTenonPts, VSLeft.FarTenonWidth,
								VSLeft.BentNumber, VSLeft.Designation));
					Module1.SuppressNextMortiseBbox();
					RLeft.AddMortise(VSLeft.TenonUpId);

					Module1.AddConnectionFull(VSRight.TimberId, new Module1.Connection {
						ConnHandle = RRight.TimberId.Handle, ThisEnd = Module1.End.Far,
						OtherEnd = Module1.End.Body, ThisJoint = Module1.JointType.Tenon,
						TenonHandles = new[] { VSRight.TenonUpId.Handle }
					});
					if (VSRight.FarTenonPts != null)
						BentNetwork.RegisterEdge(VSRight.TimberId.Handle, Module1.End.Far,
							RRight.TimberId.Handle, Module1.End.Body, "Polygon",
							JointParams.ForPolygon(VSRight.FarTenonPts, VSRight.FarTenonWidth,
								VSRight.BentNumber, VSRight.Designation));
					Module1.SuppressNextMortiseBbox();
					RRight.AddMortise(VSRight.TenonUpId);
				}

				if (HasKPost & HasStrut) {
					// Compute JF params for the strut far (kpost face) tenon so that KPost
					// regen re-cuts the exact sloped mortise instead of a bbox rectangle.
					// Geometry matches GetTenonParamsGeometric "KPStrutLeft/Right" far case.
					double strutHs = Module1.Span / 2.0;
					double strutZ  = Module1.Make3D
						? (Module1.OffsetType == Module1.Centered ? (KpostWidth - StrutWidth) / 2.0
						   : Module1.OffsetType == Module1.Front  ?  KpostWidth - StrutWidth : 0.0)
						: 0.0;
					double strutTenonZ = Module1.Make3D ? strutZ + (StrutWidth - 2.0) / 2.0 : 0.0;

					// Left strut -- tenon enters KPost from the left face (+X direction).
					var sLKP = new JointParams(Module1.JointType.Tenon,
						new Point3d(strutHs - KpostDepth / 2.0, Module1.TOH, strutTenonZ),
						new Vector3d(1, 0, 0), new Vector3d(0, 1, 0),
						StrutWidth, StrutDepth / Math.Cos(Module1.Beta), 2.0,
						SLeft.BentNumber, SLeft.Designation,
						0.0, 0.0, false, 0.0, Module1.Pitch);
					Module1.PrepareIncomingJointRecord(KP.TimberId, SLeft.TimberId.Handle, sLKP);
					BentNetwork.RegisterEdge(SLeft.TimberId.Handle, Module1.End.Far,
						KP.TimberId.Handle, Module1.End.Body, "Tenon", sLKP);
					Module1.AddConnectionFull(SLeft.TimberId, new Module1.Connection {
						ConnHandle = KP.TimberId.Handle, ThisEnd = Module1.End.Far,
						OtherEnd = Module1.End.Body, ThisJoint = Module1.JointType.Tenon,
						TenonHandles = new[] { SLeft.TenonDownId.Handle }
					});
					Module1.SuppressNextMortiseBbox();
					KP.AddMortise(SLeft.TenonDownId);

					// Right strut -- tenon enters KPost from the right face (-X direction).
					var sRKP = new JointParams(Module1.JointType.Tenon,
						new Point3d(strutHs + KpostDepth / 2.0, Module1.TOH, strutTenonZ),
						new Vector3d(-1, 0, 0), new Vector3d(0, 1, 0),
						StrutWidth, StrutDepth / Math.Cos(Module1.Beta), 2.0,
						SRight.BentNumber, SRight.Designation,
						0.0, 0.0, false, 0.0, Module1.Pitch);
					Module1.PrepareIncomingJointRecord(KP.TimberId, SRight.TimberId.Handle, sRKP);
					BentNetwork.RegisterEdge(SRight.TimberId.Handle, Module1.End.Far,
						KP.TimberId.Handle, Module1.End.Body, "Tenon", sRKP);
					Module1.AddConnectionFull(SRight.TimberId, new Module1.Connection {
						ConnHandle = KP.TimberId.Handle, ThisEnd = Module1.End.Far,
						OtherEnd = Module1.End.Body, ThisJoint = Module1.JointType.Tenon,
						TenonHandles = new[] { SRight.TenonDownId.Handle }
					});
					Module1.SuppressNextMortiseBbox();
					KP.AddMortise(SRight.TenonDownId);
				}
				if (HasKPost & HasRafter) {
					Module1.PrepareIncomingJointRecord(KP.TimberId, RLeft.TimberId.Handle, RLeft.NearJointParamsDrawn);
					BentNetwork.RegisterEdge(RLeft.TimberId.Handle, Module1.End.Near,
						KP.TimberId.Handle, Module1.End.Body,
						RLeft.NearJointParamsDrawn.JointType.ToString(), RLeft.NearJointParamsDrawn);
					Module1.AddConnectionFull(RLeft.TimberId, new Module1.Connection {
						ConnHandle = KP.TimberId.Handle, ThisEnd = Module1.End.Near,
						OtherEnd = Module1.End.Body, ThisJoint = Module1.JointType.Tenon,
						TenonHandles = new[] { RLeft.SeatPeakId.Handle }
					});
					Module1.SuppressNextMortiseBbox();
					KP.AddMortise(RLeft.SeatPeakId);
					Module1.PrepareIncomingJointRecord(KP.TimberId, RRight.TimberId.Handle, RRight.NearJointParamsDrawn);
					BentNetwork.RegisterEdge(RRight.TimberId.Handle, Module1.End.Near,
						KP.TimberId.Handle, Module1.End.Body,
						RRight.NearJointParamsDrawn.JointType.ToString(), RRight.NearJointParamsDrawn);
					Module1.AddConnectionFull(RRight.TimberId, new Module1.Connection {
						ConnHandle = KP.TimberId.Handle, ThisEnd = Module1.End.Near,
						OtherEnd = Module1.End.Body, ThisJoint = Module1.JointType.Tenon,
						TenonHandles = new[] { RRight.SeatPeakId.Handle }
					});
					Module1.SuppressNextMortiseBbox();
					KP.AddMortise(RRight.SeatPeakId);
				}

				if (HasFlrGirt & HasFlrBrace) {
					// FlrBentGirt is the girt-end receiver for both -> always ThisEnd=Far.
					// FBLeft ZAngle=0,  SwapEnds=false: TenonUp (pts[4] area) extends +Y → FlrBentGirt.
					// FBRight ZAngle=270, SwapEnds=true: 270° rotation maps TenonDown (pts[0] area) → FlrBentGirt.
					Module1.AddConnectionFull(FBLeft.TimberId, new Module1.Connection {
						ConnHandle = FlrBentGirt.TimberId.Handle, ThisEnd = Module1.End.Far,
						OtherEnd = Module1.End.Body, ThisJoint = Module1.JointType.Tenon,
						TenonHandles = FBLeft.TenonUp.IsNull ? System.Array.Empty<Handle>() : new[] { FBLeft.TenonUp.Handle }
					});
					FlrBentGirt.AddMortise(FBLeft.TenonUp);
					Module1.AddConnectionFull(FBRight.TimberId, new Module1.Connection {
						ConnHandle = FlrBentGirt.TimberId.Handle, ThisEnd = Module1.End.Far,
						OtherEnd = Module1.End.Body, ThisJoint = Module1.JointType.Tenon,
						TenonHandles = FBRight.TenonDown.IsNull ? System.Array.Empty<Handle>() : new[] { FBRight.TenonDown.Handle }
					});
					FlrBentGirt.AddMortise(FBRight.TenonDown);
				}
			}

			if (HasGirt) {
				if (HasPost) {
                    // Use AddConnectionFull so the per-connection tenon solid handles are stored.
                    // ApplyCascade reads these to re-cut post mortises after a girt regen.
                    Module1.AddConnectionFull(BGirt.TimberId, new Module1.Connection {
                        ConnHandle   = PLeft.TimberId.Handle,
                        ThisEnd      = Module1.End.Near,
                        OtherEnd     = Module1.End.Body,
                        ThisJoint    = Module1.JointType.Tenon,
                        TenonHandles = new[] { BGirt.TenonLeftId.Handle }
                    });
                    Module1.AddConnectionFull(BGirt.TimberId, new Module1.Connection {
                        ConnHandle   = PRight.TimberId.Handle,
                        ThisEnd      = Module1.End.Far,
                        OtherEnd     = Module1.End.Body,
                        ThisJoint    = Module1.JointType.Tenon,
                        TenonHandles = new[] { BGirt.TenonRightId.Handle }
                    });
				}
				if (HasKPost) {
					Module1.AddConnection(BGirt.TimberId, KP.TimberId, Module1.JointType.Mortise);
				}
				if (HasVStrut) {
					Module1.AddConnection(BGirt.TimberId, VSLeft.TimberId, Module1.JointType.Mortise);
					Module1.AddConnection(BGirt.TimberId, VSRight.TimberId, Module1.JointType.Mortise);
				}
				if (HasBrace) {
					Module1.AddConnection(BGirt.TimberId, BBLeft.TimberId, Module1.JointType.Mortise);
					Module1.AddConnection(BGirt.TimberId, BBRight.TimberId, Module1.JointType.Mortise);
				}
			}
			// BentBrace Tenon connections recorded by AddConnectionFull above (before AddMortise).
			// Duplicate AddConnection(Tenon) calls removed -- they cause ApplyCascade to refill
			// voids it just cut on Butt->Tenon transitions.
			if (HasBrace) {
			}
			if (HasPost) {
				if (HasGirt) {
					Module1.AddConnection(PLeft.TimberId, BGirt.TimberId, Module1.JointType.Mortise);
					Module1.AddConnection(PRight.TimberId, BGirt.TimberId, Module1.JointType.Mortise);
				}
				if (HasRafter) {
					Module1.AddConnection(PLeft.TimberId, RLeft.TimberId, Module1.JointType.Mortise);
					Module1.AddConnection(PRight.TimberId, RRight.TimberId, Module1.JointType.Mortise);
				}
				if (HasBrace) {
					Module1.AddConnection(PLeft.TimberId, BBLeft.TimberId, Module1.JointType.Mortise);
					Module1.AddConnection(PRight.TimberId, BBRight.TimberId, Module1.JointType.Mortise);
				}
				if (HasFlrGirt) {
					Module1.AddConnection(PLeft.TimberId, FlrBentGirt.TimberId, Module1.JointType.Mortise);
					Module1.AddConnection(PRight.TimberId, FlrBentGirt.TimberId, Module1.JointType.Mortise);
				}
				if (HasFlrBrace) {
					Module1.AddConnection(PLeft.TimberId, FBLeft.TimberId, Module1.JointType.Mortise);
					Module1.AddConnection(PRight.TimberId, FBRight.TimberId, Module1.JointType.Mortise);
				}
			}
			// Mortise-side records (receiver) -- for stale marking only.
			// Tenon-side (sender) connections are already stored by AddConnectionFull above
			// (before each AddMortise call) with the live tenon solid handles.
			// Keeping duplicate Tenon AddConnection calls here would create double entries
			// in the Connections xrecord, causing ApplyCascade to fire the "no live tenon"
			// fill path a second time and undo the cut it just made on Butt->Tenon transitions.
			if (HasRafter) {
				if (HasVStrut) {
					Module1.AddConnection(RLeft.TimberId, VSLeft.TimberId, Module1.JointType.Mortise);
					Module1.AddConnection(RRight.TimberId, VSRight.TimberId, Module1.JointType.Mortise);
				}
				if (HasStrut) {
					Module1.AddConnection(RLeft.TimberId, SLeft.TimberId, Module1.JointType.Mortise);
					Module1.AddConnection(RRight.TimberId, SRight.TimberId, Module1.JointType.Mortise);
				}
			}
			if (HasKPost) {
				if (HasRafter) {
					Module1.AddConnection(KP.TimberId, RLeft.TimberId, Module1.JointType.Mortise);
					Module1.AddConnection(KP.TimberId, RRight.TimberId, Module1.JointType.Mortise);
				}
				if (HasStrut) {
					Module1.AddConnection(KP.TimberId, SLeft.TimberId, Module1.JointType.Mortise);
					Module1.AddConnection(KP.TimberId, SRight.TimberId, Module1.JointType.Mortise);
				}
			}
			if (HasFlrGirt) {
				if (HasPost) {
                    Module1.AddConnectionFull(FlrBentGirt.TimberId, new Module1.Connection {
                        ConnHandle   = PLeft.TimberId.Handle,
                        ThisEnd      = Module1.End.Near,
                        OtherEnd     = Module1.End.Body,
                        ThisJoint    = Module1.JointType.Tenon,
                        TenonHandles = new[] { FlrBentGirt.TenonLeftId.Handle }
                    });
                    Module1.AddConnectionFull(FlrBentGirt.TimberId, new Module1.Connection {
                        ConnHandle   = PRight.TimberId.Handle,
                        ThisEnd      = Module1.End.Far,
                        OtherEnd     = Module1.End.Body,
                        ThisJoint    = Module1.JointType.Tenon,
                        TenonHandles = new[] { FlrBentGirt.TenonRightId.Handle }
                    });
				}
				if (HasFlrBrace) {
					Module1.AddConnection(FlrBentGirt.TimberId, FBLeft.TimberId, Module1.JointType.Mortise);
					Module1.AddConnection(FlrBentGirt.TimberId, FBRight.TimberId, Module1.JointType.Mortise);
				}
			}
			// FloorBrace Tenon connections recorded by AddConnectionFull above (before AddMortise).
			// Duplicate AddConnection(Tenon) calls removed.

			// Commit all registered edges to the Named Object Dictionary.
			// TimberFactory.ReapplyIncoming can now re-cut incoming joints for any
			// member in this bent without per-type migration.
			BentNetwork.EndBent();
		}
	}
}
