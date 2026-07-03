using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{

	public class QPBent
	{
		public double QPPostWidth;
		public double QPPostDepth;
		public double QPGirtWidth;
		public double QPGirtDepth;
		public double QPRafterWidth;
		public double QPRafterDepth;
		public double QPQpostWidth;
		public double QPQpostDepth;
		public double QPStrutWidth;
		public double QPStrutDepth;
		public double QPQPGirtWidth;
		public double QPQPGirtDepth;
		public double QPBraceWidth;
		public double QPBraceDepth;
		public double QPBraceLength;
		public double QPQPBraceWidth;
		public double QPQPBraceDepth;
		public double QPQPBraceLength;
		public double QPFlrGirtWidth;
		public double QPFlrGirtDepth;
		public double QPFlrGirtHt;
		public double QPFlrBraceWidth;
		public double QPFlrBraceDepth;
		public double QPFlrBraceLength;
		public bool HasPost;
		public bool HasGirt;
		public bool HasRafter;
		public bool HasQPost;
		public bool HasStrut;
		public bool HasQPGirt;
		public bool HasBrace;
		public bool HasQPBrace;
		public bool HasFlrGirt;
		public bool HasFlrBrace;
		public QPRafterLeft QPRLeft = new();
		public QPRafterRight QPRRight = new();
		public PostLeft PLeft = new();

		public PostRight PRight = new();
		public bool DataReady()
		{
			bool ready = true;
			if (HasPost)
				if (QPPostWidth <= 0 | QPPostDepth <= 0)
					ready = false;
			if (HasGirt)
				if (QPGirtWidth <= 0 | QPGirtDepth <= 0)
					ready = false;
			if (HasRafter)
				if (QPRafterWidth <= 0 | QPRafterDepth <= 0)
					ready = false;
			if (HasQPost)
				if (QPQpostWidth <= 0 | QPQpostDepth <= 0)
					ready = false;
			if (HasStrut)
				if (QPStrutWidth <= 0 | QPStrutDepth <= 0)
					ready = false;
			if (HasQPGirt)
				if (QPQPGirtWidth <= 0 | QPQPGirtDepth <= 0)
					ready = false;
			if (HasBrace)
				if (QPBraceWidth <= 0 | QPBraceDepth <= 0 | QPBraceLength <= 0)
					ready = false;
			if (HasQPBrace)
				if (QPQPBraceWidth <= 0 | QPQPBraceDepth <= 0 | QPQPBraceLength <= 0)
					ready = false;
			if (HasFlrGirt)
				if (QPFlrGirtWidth <= 0 | QPFlrGirtDepth <= 0 | QPFlrGirtHt <= 0)
					ready = false;
			if (HasFlrBrace)
				if (QPFlrBraceWidth <= 0 | QPFlrBraceDepth <= 0 | QPFlrBraceLength <= 0)
					ready = false;
			return ready;
		}

		public void Draw()
		{
			string bentNum = Convert.ToString(Properties.Settings.Default.BentNumber);
			BentNetwork.BeginBent(bentNum);

			Module1.PlumbLength = QPPostDepth / Math.Cos(Module1.Beta);
			Module1.TOH = (Module1.EaveHt + (QPPostDepth * Module1.Pitch)) - Module1.PlumbLength;
			Module1.TOG = Module1.TOH - 6;
			Module1.BOG = Module1.TOG - QPGirtDepth;

			BentGirt BGirt = new();
			if (HasGirt) {
				BGirt.Width = QPGirtWidth;
				BGirt.Depth = QPGirtDepth;
				BGirt.postDepth = QPPostDepth;
				BGirt.Type = "Girt";
				BGirt.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
				BGirt.Designation = ((char)(Module1.BentWallNumber)).ToString() + ((char)(Module1.BentWallNumber + 3)).ToString();
				BGirt.StartPoint = new Point3d(QPPostDepth, Module1.BOG, 0);
				BGirt.Draw();
			}

			BentBrace BBLeft = new();
			BentBrace BBRight = new();
			if (HasBrace) {
				BBLeft.StartPoint = new Point3d(QPPostDepth, Module1.BOG, 0);
				BBLeft.Width = QPBraceWidth;
				BBLeft.Depth = QPBraceDepth;
				BBLeft.Length = QPBraceLength;
				BBLeft.postWidth = QPPostWidth;
				BBLeft.YAngle = 0;
				BBLeft.ZAngle = 0;
				BBLeft.XAngle = 0;
				BBLeft.Draw();
				BBRight.StartPoint = new Point3d(Module1.Span - QPPostDepth, Module1.BOG, 0);
				BBRight.Width = QPBraceWidth;
				BBRight.Depth = QPBraceDepth;
				BBRight.Length = QPBraceLength;
				BBRight.postWidth = QPPostWidth;
				BBRight.YAngle = 0;
				BBRight.ZAngle = 270;
				BBRight.XAngle = 0;
				BBRight.Draw();
			}

			BentBrace QPBLeft = new();
			BentBrace QPBRight = new();
			if (HasQPBrace) {
				BentBrace B = new();
				QPBLeft.StartPoint = new Point3d((Module1.Span / 3) + QPQpostDepth, Module1.TOH + (((Module1.Span / 3) - QPPostDepth) * Module1.Pitch) + (QPQpostDepth * Module1.Pitch) - (6 + QPQPGirtDepth), 0);
				QPBLeft.Width = QPQPBraceWidth;
				QPBLeft.Depth = QPQPBraceDepth;
				QPBLeft.Length = QPQPBraceLength;
				QPBLeft.postWidth = QPQpostWidth;
				QPBLeft.YAngle = 0;
				QPBLeft.ZAngle = 0;
				QPBLeft.XAngle = 0;
				QPBLeft.Draw();
				QPBRight.StartPoint = new Point3d(((Module1.Span / 3) * 2) - QPQpostDepth, Module1.TOH + (((Module1.Span / 3) - QPPostDepth) * Module1.Pitch) + (QPQpostDepth * Module1.Pitch) - (6 + QPQPGirtDepth), 0);
				QPBRight.Width = QPQPBraceWidth;
				QPBRight.Depth = QPQPBraceDepth;
				QPBRight.Length = QPQPBraceLength;
				QPBRight.postWidth = QPPostWidth;
				QPBRight.YAngle = 0;
				QPBRight.ZAngle = 270;
				QPBRight.XAngle = 0;
				QPBRight.Draw();
			}

			if (HasPost) {
				PLeft.Width = QPPostWidth;
				PLeft.Depth = QPPostDepth;
				PLeft.Type = "Post";
				PLeft.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
				PLeft.Designation = ((char)(Module1.BentWallNumber)).ToString();
				PLeft.Draw();

				PRight.Width = QPPostWidth;
				PRight.Depth = QPPostDepth;
				PRight.Type = "Post";
				PRight.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
				PRight.Designation = ((char)(Module1.BentWallNumber + 3)).ToString();
				PRight.Draw();
			}

			QPPostLeft QPPLeft = new();
			QPPostRight QPPRight = new();
			if (HasQPost) {
				QPPLeft.Width = QPQpostWidth;
				QPPLeft.Depth = QPQpostDepth;
				QPPLeft.GirtWidth = QPGirtWidth;
				QPPLeft.postDepth = QPPostDepth;
				QPPLeft.Type = "QPost";
				QPPLeft.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
				QPPLeft.Designation = ((char)(Module1.BentWallNumber + 1)).ToString();
				QPPLeft.StartPoint = new Point3d(Module1.Span / 3, Module1.TOG, 0);
				QPPLeft.Draw();

				QPPRight.Width = QPQpostWidth;
				QPPRight.Depth = QPQpostDepth;
				QPPRight.GirtWidth = QPGirtWidth;
				QPPRight.postDepth = QPPostDepth;
				QPPRight.Type = "QPost";
				QPPRight.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
				QPPRight.Designation = ((char)(Module1.BentWallNumber + 2)).ToString();
				QPPRight.StartPoint = new Point3d(((Module1.Span / 3) * 2) - QPQpostDepth, Module1.TOG, 0);
				QPPRight.Draw();
			}

			if (HasRafter) {
				QPRLeft.Width = QPRafterWidth;
				QPRLeft.Depth = QPRafterDepth;
				QPRLeft.postDepth = QPPostDepth;
				QPRLeft.Type = "Rafter";
				QPRLeft.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
				QPRLeft.Designation = ((char)(Module1.BentWallNumber)).ToString();
				QPRLeft.StartPoint = new Point3d(QPPostDepth, Module1.TOH, 0);  // foot-bottom
				QPRLeft.Draw();

				QPRRight.Width = QPRafterWidth;
				QPRRight.Depth = QPRafterDepth;
				QPRRight.postDepth = QPPostDepth;
				QPRRight.Type = "Rafter";
				QPRRight.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
				QPRRight.Designation = ((char)(Module1.BentWallNumber + 3)).ToString();
				QPRRight.StartPoint = new Point3d(Module1.Span - QPPostDepth, Module1.TOH, 0);  // foot-bottom
				QPRRight.Draw();
			}

			QPStrutLeft QPSLeft = new();
			QPStrutRight QPSRight = new();
			if (HasStrut) {
				QPSLeft.Width = QPStrutWidth;
				QPSLeft.Depth = QPStrutDepth;
				QPSLeft.QPRafterWidth = QPRafterWidth;
				QPSLeft.Type = "Strut";
				QPSLeft.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
				QPSLeft.Designation = ((char)(Module1.BentWallNumber + 1)).ToString();
				QPSLeft.StartPoint = new Point3d((Module1.Span / 3) - Module1.B, Module1.TOH + Module1.B, 0);
				QPSLeft.Draw();

				QPSRight.Width = QPStrutWidth;
				QPSRight.Depth = QPStrutDepth;
				QPSRight.QPRafterWidth = QPRafterWidth;
				QPSRight.Type = "Strut";
				QPSRight.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
				QPSRight.Designation = ((char)(Module1.BentWallNumber + 2)).ToString();
				QPSRight.StartPoint = new Point3d((Module1.Span / 3) * 2, Module1.TOH, 0);
				QPSRight.Draw();
			}

			QPUpperGirt QPUGirt = new();
			if (HasQPGirt) {
				QPUGirt.Width = QPQPGirtWidth;
				QPUGirt.Depth = QPQPGirtDepth;
				QPUGirt.postDepth = QPPostDepth;
				QPUGirt.QPRafterWidth = QPRafterWidth;
				QPUGirt.QPQpostDepth = QPQpostDepth;
				QPUGirt.Type = "Girt";
				QPUGirt.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
				QPUGirt.Designation = ((char)(Module1.BentWallNumber + 1)).ToString() + ((char)(Module1.BentWallNumber + 2)).ToString();
				QPUGirt.StartPoint = new Point3d((Module1.Span / 3) + QPQpostDepth, Module1.TOH + (((Module1.Span / 3) - QPPostDepth) * Module1.Pitch) + (QPQpostDepth * Module1.Pitch) - (6 + QPQPGirtDepth), 0);
				QPUGirt.Draw();
			}

			FloorBentGirt FlrBentGirt = new();
			if (HasFlrGirt) {
				FlrBentGirt.Width = QPFlrGirtWidth;
				FlrBentGirt.Depth = QPFlrGirtDepth;
				FlrBentGirt.Height = QPFlrGirtHt;
				FlrBentGirt.Type = "Girt";
				FlrBentGirt.BentNumber = Convert.ToString(Properties.Settings.Default.BentNumber);
				FlrBentGirt.Designation = Properties.Settings.Default.WallAlpha + ((char)(Properties.Settings.Default.WallAlpha[0] + 3)).ToString();
				FlrBentGirt.StartPoint = new Point3d(PLeft.Depth, QPFlrGirtHt - QPFlrGirtDepth, 0);
				FlrBentGirt.Draw(QPPostDepth);
			}

			BentBrace FBLeft = new();
			BentBrace FBRight = new();
			if (HasFlrBrace) {
				FBLeft.StartPoint = new Point3d(QPPostDepth, QPFlrGirtHt - QPFlrGirtDepth, 0);
				FBLeft.Width = QPFlrBraceWidth;
				FBLeft.Depth = QPFlrBraceDepth;
				FBLeft.Length = QPFlrBraceLength;
				FBLeft.postWidth = QPPostWidth;
				FBLeft.YAngle = 0;
				FBLeft.Draw();
				FBRight.StartPoint = new Point3d(Module1.Span - QPPostDepth, QPFlrGirtHt - QPFlrGirtDepth, 0);
				FBRight.Width = QPFlrBraceWidth;
				FBRight.Depth = QPFlrBraceDepth;
				FBRight.Length = QPFlrBraceLength;
				FBRight.postWidth = QPPostWidth;
				FBRight.YAngle = 0;
				FBRight.ZAngle = 270;
				FBRight.XAngle = 0;
				FBRight.Draw();
			}

			if (Module1.HasJoinery) {
				if (HasGirt & HasBrace) {
					if (Module1.OffsetType == 2){BGirt.AddMortise(BBLeft.TenonDown);BGirt.AddMortise(BBRight.TenonUp);}
					else{BGirt.AddMortise(BBLeft.TenonUp);BGirt.AddMortise(BBRight.TenonDown);}
				}
				if (HasGirt & HasQPost) {
					// JF foot tenons from QP posts into girt.
					Module1.PrepareIncomingJointRecord(BGirt.TimberId, QPPLeft.TimberId.Handle, QPPLeft.NearJointParamsDrawn);
					BentNetwork.RegisterEdge(QPPLeft.TimberId.Handle, Module1.End.Near,
						BGirt.TimberId.Handle, Module1.End.Body,
						QPPLeft.NearJointParamsDrawn.JointType.ToString(), QPPLeft.NearJointParamsDrawn);
					BGirt.AddMortise(QPPLeft.TenonDownId);
					Module1.PrepareIncomingJointRecord(BGirt.TimberId, QPPRight.TimberId.Handle, QPPRight.NearJointParamsDrawn);
					BentNetwork.RegisterEdge(QPPRight.TimberId.Handle, Module1.End.Near,
						BGirt.TimberId.Handle, Module1.End.Body,
						QPPRight.NearJointParamsDrawn.JointType.ToString(), QPPRight.NearJointParamsDrawn);
					BGirt.AddMortise(QPPRight.TenonDownId);
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
				if (HasPost & HasRafter)
				{
					// Tenon solids are deleted after BoolUnite in Draw(); recreate mortise shape from params.
					// This pattern avoids floating solids when no receiver is drawn.
					var mL = JointFactory.Create(Module1.JointType.Mortise, QPRLeft.FarJointParamsDrawn);
					if (!mL.IsNull) PLeft.AddMortise(mL);
					var mR = JointFactory.Create(Module1.JointType.Mortise, QPRRight.NearJointParamsDrawn);
					if (!mR.IsNull) PRight.AddMortise(mR);
				}
				if (HasPost & HasBrace) {
					if (Module1.OffsetType == 2){PLeft.AddMortise(BBLeft.TenonUp);PRight.AddMortise(BBRight.TenonDown);}
					else{PLeft.AddMortise(BBLeft.TenonDown);PRight.AddMortise(BBRight.TenonUp);}
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
				if (HasPost & HasFlrBrace){PLeft.AddMortise(FBLeft.TenonDown);PRight.AddMortise(FBRight.TenonUp);}

				if (HasRafter & HasStrut)
				{
					Module1.SuppressNextMortiseBbox();
					QPRLeft.AddMortise(QPSLeft.TenonUp);
					BentNetwork.RegisterEdge(QPSLeft.TimberId.Handle, Module1.End.Far,
						QPRLeft.TimberId.Handle, Module1.End.Body, "Polygon",
						JointParams.ForPolygon(QPSLeft.UpTenonPts, QPSLeft.UpTenonWidth, QPSLeft.BentNumber, QPSLeft.Designation));
					Module1.SuppressNextMortiseBbox();
					QPRRight.AddMortise(QPSRight.TenonUp);
					BentNetwork.RegisterEdge(QPSRight.TimberId.Handle, Module1.End.Far,
						QPRRight.TimberId.Handle, Module1.End.Body, "Polygon",
						JointParams.ForPolygon(QPSRight.UpTenonPts, QPSRight.UpTenonWidth, QPSRight.BentNumber, QPSRight.Designation));
				}
				if (HasRafter & HasQPost)
				{
					Module1.SuppressNextMortiseBbox();
					QPRLeft.AddMortise(QPPLeft.TenonUpId);
					BentNetwork.RegisterEdge(QPPLeft.TimberId.Handle, Module1.End.Far,
						QPRLeft.TimberId.Handle, Module1.End.Body, "Polygon",
						JointParams.ForPolygon(QPPLeft.FarTenonPts, QPPLeft.FarTenonWidth, QPPLeft.BentNumber, QPPLeft.Designation));
					Module1.SuppressNextMortiseBbox();
					QPRRight.AddMortise(QPPRight.TenonUpId);
					BentNetwork.RegisterEdge(QPPRight.TimberId.Handle, Module1.End.Far,
						QPRRight.TimberId.Handle, Module1.End.Body, "Polygon",
						JointParams.ForPolygon(QPPRight.FarTenonPts, QPPRight.FarTenonWidth, QPPRight.BentNumber, QPPRight.Designation));
				}
				if (HasRafter && !QPRRight.SeatPeakId.IsNull)
				{
					// Housing polygon: QPRRight gives 1" full-width housing into QPRLeft body
					Module1.SuppressNextMortiseBbox();
					QPRLeft.AddMortise(QPRRight.SeatPeakId);
					BentNetwork.RegisterEdge(
						QPRRight.TimberId.Handle, Module1.End.Far,
						QPRLeft.TimberId.Handle,  Module1.End.Near,
						"Polygon", QPRRight.FarJointParamsDrawn);
				}

				if (HasQPost & HasStrut)
				{
					Module1.SuppressNextMortiseBbox();
					QPPLeft.AddMortise(QPSLeft.TenonDown);
					BentNetwork.RegisterEdge(QPSLeft.TimberId.Handle, Module1.End.Near,
						QPPLeft.TimberId.Handle, Module1.End.Body, "Polygon",
						JointParams.ForPolygon(QPSLeft.DownTenonPts, QPSLeft.DownTenonWidth, QPSLeft.BentNumber, QPSLeft.Designation));
					Module1.SuppressNextMortiseBbox();
					QPPRight.AddMortise(QPSRight.TenonDown);
					BentNetwork.RegisterEdge(QPSRight.TimberId.Handle, Module1.End.Near,
						QPPRight.TimberId.Handle, Module1.End.Body, "Polygon",
						JointParams.ForPolygon(QPSRight.DownTenonPts, QPSRight.DownTenonWidth, QPSRight.BentNumber, QPSRight.Designation));
				}
				if (HasQPost & HasQPBrace) {
					if (Module1.OffsetType == 2){QPPLeft.AddMortise(QPBLeft.TenonUp);QPPRight.AddMortise(QPBRight.TenonDown);}
					else{QPPLeft.AddMortise(QPBLeft.TenonDown);QPPRight.AddMortise(QPBRight.TenonUp);}
				}
				if (HasQPost & HasQPGirt)
				{
					// Tenon solids deleted after BoolUnite in QPUpperGirt.Draw(); create mortises from params
					var mUL = JointFactory.Create(Module1.JointType.Mortise, QPUGirt.NearJointParamsDrawn);
					if (!mUL.IsNull) QPPLeft.AddMortise(mUL);
					var mUR = JointFactory.Create(Module1.JointType.Mortise, QPUGirt.FarJointParamsDrawn);
					if (!mUR.IsNull) QPPRight.AddMortise(mUR);
				}

				if (HasFlrGirt & HasFlrBrace){FlrBentGirt.AddMortise(FBLeft.TenonUp);FlrBentGirt.AddMortise(FBRight.TenonDown);}

				if (HasQPGirt & HasQPBrace) {
					if (Module1.OffsetType == 2){QPUGirt.AddMortise(QPBLeft.TenonDown);QPUGirt.AddMortise(QPBRight.TenonUp);}
					else{QPUGirt.AddMortise(QPBLeft.TenonUp);QPUGirt.AddMortise(QPBRight.TenonDown);}
				}
			}

			if (HasGirt) {
				if (HasPost) {
					Module1.AddConnectionFull(BGirt.TimberId, new Module1.Connection { ConnHandle=PLeft.TimberId.Handle,  ThisEnd=(short)Module1.End.Near, OtherEnd=(short)Module1.End.Body, ThisJoint=Module1.JointType.Tenon, TenonHandles=new Handle[0] });
					Module1.AddConnectionFull(BGirt.TimberId, new Module1.Connection { ConnHandle=PRight.TimberId.Handle, ThisEnd=(short)Module1.End.Far,  OtherEnd=(short)Module1.End.Body, ThisJoint=Module1.JointType.Tenon, TenonHandles=new Handle[0] });
					Module1.AddConnection(PLeft.TimberId,  BGirt.TimberId, Module1.JointType.Mortise);
					Module1.AddConnection(PRight.TimberId, BGirt.TimberId, Module1.JointType.Mortise);
				}
				if (HasQPost) {
					// Receiver side -- QPPosts give tenons Down into BGirt (handled in HasQPost block)
					Module1.AddConnection(BGirt.TimberId, QPPLeft.TimberId,  Module1.JointType.Mortise);
					Module1.AddConnection(BGirt.TimberId, QPPRight.TimberId, Module1.JointType.Mortise);
				}
				// Brace receiver-side handled inside HasBrace block
			}
			if (HasBrace) {
				if (HasPost) {
					Module1.AddConnectionFull(BBLeft.TimberId,  new Module1.Connection { ConnHandle=PLeft.TimberId.Handle,  ThisEnd=(short)Module1.End.Near, OtherEnd=(short)Module1.End.Body, ThisJoint=Module1.JointType.Tenon, TenonHandles=new Handle[0] });
					Module1.AddConnectionFull(BBRight.TimberId, new Module1.Connection { ConnHandle=PRight.TimberId.Handle, ThisEnd=(short)Module1.End.Near, OtherEnd=(short)Module1.End.Body, ThisJoint=Module1.JointType.Tenon, TenonHandles=new Handle[0] });
					Module1.AddConnection(PLeft.TimberId,  BBLeft.TimberId,  Module1.JointType.Mortise);
					Module1.AddConnection(PRight.TimberId, BBRight.TimberId, Module1.JointType.Mortise);
				}
				if (HasGirt) {
					Module1.AddConnectionFull(BBLeft.TimberId,  new Module1.Connection { ConnHandle=BGirt.TimberId.Handle, ThisEnd=(short)Module1.End.Far, OtherEnd=(short)Module1.End.Body, ThisJoint=Module1.JointType.Tenon, TenonHandles=new Handle[0] });
					Module1.AddConnectionFull(BBRight.TimberId, new Module1.Connection { ConnHandle=BGirt.TimberId.Handle, ThisEnd=(short)Module1.End.Far, OtherEnd=(short)Module1.End.Body, ThisJoint=Module1.JointType.Tenon, TenonHandles=new Handle[0] });
					Module1.AddConnection(BGirt.TimberId, BBLeft.TimberId,  Module1.JointType.Mortise);
					Module1.AddConnection(BGirt.TimberId, BBRight.TimberId, Module1.JointType.Mortise);
				}
			}
			if (HasQPBrace) {
				if (HasQPost) {
					Module1.AddConnection(QPBLeft.TimberId, QPPLeft.TimberId, Module1.JointType.Tenon);
					Module1.AddConnection(QPBRight.TimberId, QPPRight.TimberId, Module1.JointType.Tenon);
				}
				if (HasQPGirt) {
					Module1.AddConnection(QPBLeft.TimberId, QPUGirt.TimberId, Module1.JointType.Tenon);
					Module1.AddConnection(QPBRight.TimberId, QPUGirt.TimberId, Module1.JointType.Tenon);
				}
			}
			if (HasPost) {
				if (HasBrace) {
					Module1.AddConnection(PLeft.TimberId, BBLeft.TimberId, Module1.JointType.Mortise);
					Module1.AddConnection(PRight.TimberId, BBRight.TimberId, Module1.JointType.Mortise);
				}
				if (HasGirt) {
					Module1.AddConnection(PLeft.TimberId, BGirt.TimberId, Module1.JointType.Mortise);
					Module1.AddConnection(PRight.TimberId, BGirt.TimberId, Module1.JointType.Mortise);
				}
				if (HasRafter) {
					Module1.AddConnection(PLeft.TimberId, QPRLeft.TimberId, Module1.JointType.Mortise);
					Module1.AddConnection(PRight.TimberId, QPRRight.TimberId, Module1.JointType.Mortise);
				}
				if (HasFlrBrace) {
					Module1.AddConnection(PLeft.TimberId, FBLeft.TimberId, Module1.JointType.Mortise);
					Module1.AddConnection(PRight.TimberId, FBRight.TimberId, Module1.JointType.Mortise);
				}
				if (HasFlrGirt) {
					Module1.AddConnection(PLeft.TimberId, FlrBentGirt.TimberId, Module1.JointType.Mortise);
					Module1.AddConnection(PRight.TimberId, FlrBentGirt.TimberId, Module1.JointType.Mortise);
				}
			}
			if (HasQPost) {
				if (HasQPGirt) {
					// Receiver side only -- QPUGirt is giver (handled in HasQPGirt block)
					Module1.AddConnection(QPPLeft.TimberId,  QPUGirt.TimberId, Module1.JointType.Mortise);
					Module1.AddConnection(QPPRight.TimberId, QPUGirt.TimberId, Module1.JointType.Mortise);
				}
				if (HasGirt) {
					// QPPLeft/Right give foot tenon DOWN into BGirt
					Module1.AddConnectionFull(QPPLeft.TimberId,  new Module1.Connection { ConnHandle=BGirt.TimberId.Handle, ThisEnd=(short)Module1.End.Near, OtherEnd=(short)Module1.End.Body, ThisJoint=Module1.JointType.Tenon, TenonHandles=new Handle[0] });
					Module1.AddConnectionFull(QPPRight.TimberId, new Module1.Connection { ConnHandle=BGirt.TimberId.Handle, ThisEnd=(short)Module1.End.Near, OtherEnd=(short)Module1.End.Body, ThisJoint=Module1.JointType.Tenon, TenonHandles=new Handle[0] });
					Module1.AddConnection(BGirt.TimberId, QPPLeft.TimberId,  Module1.JointType.Mortise);
					Module1.AddConnection(BGirt.TimberId, QPPRight.TimberId, Module1.JointType.Mortise);
				}
				if (HasRafter) {
					// QPPLeft/Right give oblique UP tenon into rafters
					Module1.AddConnectionFull(QPPLeft.TimberId,  new Module1.Connection { ConnHandle=QPRLeft.TimberId.Handle,  ThisEnd=(short)Module1.End.Far, OtherEnd=(short)Module1.End.Body, ThisJoint=Module1.JointType.Tenon, TenonHandles=new Handle[0] });
					Module1.AddConnectionFull(QPPRight.TimberId, new Module1.Connection { ConnHandle=QPRRight.TimberId.Handle, ThisEnd=(short)Module1.End.Far, OtherEnd=(short)Module1.End.Body, ThisJoint=Module1.JointType.Tenon, TenonHandles=new Handle[0] });
					Module1.AddConnection(QPRLeft.TimberId,  QPPLeft.TimberId,  Module1.JointType.Mortise);
					Module1.AddConnection(QPRRight.TimberId, QPPRight.TimberId, Module1.JointType.Mortise);
				}
				if (HasStrut) {
					// Receiver side -- QPStruts are givers (handled in HasStrut block)
					Module1.AddConnection(QPPLeft.TimberId,  QPSLeft.TimberId,  Module1.JointType.Mortise);
					Module1.AddConnection(QPPRight.TimberId, QPSRight.TimberId, Module1.JointType.Mortise);
				}
				if (HasQPBrace) {
					Module1.AddConnection(QPPLeft.TimberId,  QPBLeft.TimberId,  Module1.JointType.Mortise);
					Module1.AddConnection(QPPRight.TimberId, QPBRight.TimberId, Module1.JointType.Mortise);
				}
			}
			if (HasRafter) {
				// QPRLeft receives housing from QPRRight (near/peak end of QPRLeft) -- receiver side only
				Module1.AddConnection(QPRLeft.TimberId, QPRRight.TimberId, Module1.JointType.Mortise,
					thisEnd: Module1.End.Near, otherEnd: Module1.End.Far);
				// QPRRight gives housing polygon at far/peak end into QPRLeft -- full giver wiring
				Module1.AddConnectionFull(QPRRight.TimberId, new Module1.Connection {
					ConnHandle    = QPRLeft.TimberId.Handle,
					ThisEnd       = (short)Module1.End.Far,
					OtherEnd      = (short)Module1.End.Near,
					ThisJoint     = Module1.JointType.Tenon,
					TenonHandles  = new Handle[0]  // housing solid consumed by AddMortise
				});
				if (HasPost) {
					// QPRLeft far (foot) tenon into left post -- full giver wiring
					Module1.PrepareIncomingJointRecord(PLeft.TimberId, QPRLeft.TimberId.Handle, QPRLeft.FarJointParamsDrawn);
					BentNetwork.RegisterEdge(QPRLeft.TimberId.Handle, Module1.End.Far,
						PLeft.TimberId.Handle, Module1.End.Body,
						QPRLeft.FarJointParamsDrawn.JointType.ToString(), QPRLeft.FarJointParamsDrawn);
					Module1.AddConnectionFull(QPRLeft.TimberId, new Module1.Connection {
						ConnHandle   = PLeft.TimberId.Handle,
						ThisEnd      = (short)Module1.End.Far,
						OtherEnd     = (short)Module1.End.Body,
						ThisJoint    = Module1.JointType.Tenon,
						TenonHandles = new Handle[0]  // tenon solid deleted after BoolUnite
					});
					// QPRRight near (foot) tenon into right post -- full giver wiring
					Module1.PrepareIncomingJointRecord(PRight.TimberId, QPRRight.TimberId.Handle, QPRRight.NearJointParamsDrawn);
					BentNetwork.RegisterEdge(QPRRight.TimberId.Handle, Module1.End.Near,
						PRight.TimberId.Handle, Module1.End.Body,
						QPRRight.NearJointParamsDrawn.JointType.ToString(), QPRRight.NearJointParamsDrawn);
					Module1.AddConnectionFull(QPRRight.TimberId, new Module1.Connection {
						ConnHandle   = PRight.TimberId.Handle,
						ThisEnd      = (short)Module1.End.Near,
						OtherEnd     = (short)Module1.End.Body,
						ThisJoint    = Module1.JointType.Tenon,
						TenonHandles = new Handle[0]  // tenon solid deleted after BoolUnite
					});
					// Receiver-side records on posts (stale marking)
					Module1.AddConnection(PLeft.TimberId,  QPRLeft.TimberId,  Module1.JointType.Mortise,
						thisEnd: Module1.End.Body, otherEnd: Module1.End.Far);
					Module1.AddConnection(PRight.TimberId, QPRRight.TimberId, Module1.JointType.Mortise,
						thisEnd: Module1.End.Body, otherEnd: Module1.End.Near);
				}
				if (HasStrut) {
					Module1.AddConnection(QPRLeft.TimberId, QPSLeft.TimberId, Module1.JointType.Mortise);
					Module1.AddConnection(QPRRight.TimberId, QPSRight.TimberId, Module1.JointType.Mortise);
				}
				if (HasQPost) {
					Module1.AddConnection(QPRLeft.TimberId, QPPLeft.TimberId, Module1.JointType.Mortise);
					Module1.AddConnection(QPRRight.TimberId, QPPRight.TimberId, Module1.JointType.Mortise);
				}
			}
			if (HasStrut) {
				if (HasQPost) {
					Module1.AddConnectionFull(QPSLeft.TimberId,  new Module1.Connection { ConnHandle=QPPLeft.TimberId.Handle,  ThisEnd=(short)Module1.End.Near, OtherEnd=(short)Module1.End.Body, ThisJoint=Module1.JointType.Tenon, TenonHandles=new Handle[0] });
					Module1.AddConnectionFull(QPSRight.TimberId, new Module1.Connection { ConnHandle=QPPRight.TimberId.Handle, ThisEnd=(short)Module1.End.Near, OtherEnd=(short)Module1.End.Body, ThisJoint=Module1.JointType.Tenon, TenonHandles=new Handle[0] });
				}
				if (HasRafter) {
					Module1.AddConnectionFull(QPSLeft.TimberId,  new Module1.Connection { ConnHandle=QPRLeft.TimberId.Handle,  ThisEnd=(short)Module1.End.Far, OtherEnd=(short)Module1.End.Body, ThisJoint=Module1.JointType.Tenon, TenonHandles=new Handle[0] });
					Module1.AddConnectionFull(QPSRight.TimberId, new Module1.Connection { ConnHandle=QPRRight.TimberId.Handle, ThisEnd=(short)Module1.End.Far, OtherEnd=(short)Module1.End.Body, ThisJoint=Module1.JointType.Tenon, TenonHandles=new Handle[0] });
				}
			}
			if (HasQPGirt) {
				if (HasQPost) {
					Module1.AddConnectionFull(QPUGirt.TimberId, new Module1.Connection { ConnHandle=QPPLeft.TimberId.Handle,  ThisEnd=(short)Module1.End.Near, OtherEnd=(short)Module1.End.Body, ThisJoint=Module1.JointType.Tenon, TenonHandles=new Handle[0] });
					Module1.AddConnectionFull(QPUGirt.TimberId, new Module1.Connection { ConnHandle=QPPRight.TimberId.Handle, ThisEnd=(short)Module1.End.Far,  OtherEnd=(short)Module1.End.Body, ThisJoint=Module1.JointType.Tenon, TenonHandles=new Handle[0] });
				}
				if (HasQPBrace) {
					Module1.AddConnection(QPUGirt.TimberId, QPBLeft.TimberId, Module1.JointType.Mortise);
					Module1.AddConnection(QPUGirt.TimberId, QPBRight.TimberId, Module1.JointType.Mortise);
				}
			}
			if (HasFlrGirt) {
				if (HasPost) {
					Module1.AddConnection(FlrBentGirt.TimberId, PLeft.TimberId, Module1.JointType.Tenon);
					Module1.AddConnection(FlrBentGirt.TimberId, PRight.TimberId, Module1.JointType.Tenon);
				}
				if (HasFlrBrace) {
					Module1.AddConnection(FlrBentGirt.TimberId, FBLeft.TimberId, Module1.JointType.Mortise);
					Module1.AddConnection(FlrBentGirt.TimberId, FBRight.TimberId, Module1.JointType.Mortise);
				}
			}
			if (HasFlrBrace) {
				if (HasPost) {
					Module1.AddConnectionFull(FBLeft.TimberId,  new Module1.Connection { ConnHandle=PLeft.TimberId.Handle,  ThisEnd=(short)Module1.End.Near, OtherEnd=(short)Module1.End.Body, ThisJoint=Module1.JointType.Tenon, TenonHandles=new Handle[0] });
					Module1.AddConnectionFull(FBRight.TimberId, new Module1.Connection { ConnHandle=PRight.TimberId.Handle, ThisEnd=(short)Module1.End.Near, OtherEnd=(short)Module1.End.Body, ThisJoint=Module1.JointType.Tenon, TenonHandles=new Handle[0] });
					Module1.AddConnection(PLeft.TimberId,  FBLeft.TimberId,  Module1.JointType.Mortise);
					Module1.AddConnection(PRight.TimberId, FBRight.TimberId, Module1.JointType.Mortise);
				}
				if (HasFlrGirt) {
					Module1.AddConnectionFull(FBLeft.TimberId,  new Module1.Connection { ConnHandle=FlrBentGirt.TimberId.Handle, ThisEnd=(short)Module1.End.Far, OtherEnd=(short)Module1.End.Body, ThisJoint=Module1.JointType.Tenon, TenonHandles=new Handle[0] });
					Module1.AddConnectionFull(FBRight.TimberId, new Module1.Connection { ConnHandle=FlrBentGirt.TimberId.Handle, ThisEnd=(short)Module1.End.Far, OtherEnd=(short)Module1.End.Body, ThisJoint=Module1.JointType.Tenon, TenonHandles=new Handle[0] });
					Module1.AddConnection(FlrBentGirt.TimberId, FBLeft.TimberId,  Module1.JointType.Mortise);
					Module1.AddConnection(FlrBentGirt.TimberId, FBRight.TimberId, Module1.JointType.Mortise);
				}
			}
			BentNetwork.EndBent();
		}
	}
}
