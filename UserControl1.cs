using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Geometry;
using System.Drawing;
using System.Windows.Forms;

namespace TimberDraw
{

    public partial class UserControl1
    {
        static Color hiLite = Color.Red;
        static Color loLite = Color.Black;
        const int HammerBeamBent = 0;
        const int KingPostBent = 1;
        const int QueenPostBent = 2;
        const int KingPostTruss = 3;
        const int QueenPostTruss = 4;

        #region Get Settings
        private void GeneralSettings()
        {
            //General Settings
            CheckBoxShowToolTips.Checked = TimberDraw.Properties.Settings.Default.ToolTips;
            CheckBoxHasJoinery.Checked = TimberDraw.Properties.Settings.Default.HasJoinery;
            Module1.HasJoinery = CheckBoxHasJoinery.Checked;
            CheckBoxDeletePolylines.Checked = TimberDraw.Properties.Settings.Default.DeletePolylines;
            Module1.DeletePolylines = CheckBoxDeletePolylines.Checked;
            TextBoxEaveHeight.Text = Converter.DistanceToString(TimberDraw.Properties.Settings.Default.EaveHt, DistanceUnitFormat.Current, -1);
            TextBoxSpan.Text = Converter.DistanceToString(TimberDraw.Properties.Settings.Default.Span, DistanceUnitFormat.Current, -1);
            TextBoxRoofPitchRun.Text = Converter.DistanceToString(TimberDraw.Properties.Settings.Default.RoofPitchRun, DistanceUnitFormat.Current, -1);
            TextBoxRoofPitchRise.Text = Converter.DistanceToString(TimberDraw.Properties.Settings.Default.RoofPitchRise, DistanceUnitFormat.Current, -1);
            TextBoxBayWidth.Text = Converter.DistanceToString(TimberDraw.Properties.Settings.Default.BayWidth, DistanceUnitFormat.Current, -1);
            CheckBoxMake3D.Checked = TimberDraw.Properties.Settings.Default.Make3D;
            TextBoxFlrGirtHt.Text = Converter.DistanceToString(TimberDraw.Properties.Settings.Default.FlrGirtHt, DistanceUnitFormat.Current, -1);
            switch (TimberDraw.Properties.Settings.Default.TrussType)
            {
                case 0:
                    RadioButtonHammerBeam.Checked = true;
                    Module1.TrussType = HammerBeamBent;
                    break;
                case 1:
                    RadioButtonKingPostBent.Checked = true;
                    Module1.TrussType = KingPostBent;
                    break;
                case 2:
                    RadioButtonQueenPostBent.Checked = true;
                    Module1.TrussType = QueenPostBent;
                    break;
                case 3:
                    RadioButtonKingPostTruss.Checked = true;
                    Module1.TrussType = KingPostTruss;
                    break;
                case 4:
                    RadioButtonQueenPostTruss.Checked = true;
                    Module1.TrussType = QueenPostTruss;
                    break;
            }
            //Scroll Bar Setup
            VScrollBarBentNumber.Value = 100 - TimberDraw.Properties.Settings.Default.BentNumber;
            TextBoxBentNumber.Text = TimberDraw.Properties.Settings.Default.BentNumber.ToString();
            VScrollBarWallNumber.Value = TimberDraw.Properties.Settings.Default.WallNumber;
            TextBoxWallNumber.Text = ((char)((VScrollBarWallNumber.Maximum - TimberDraw.Properties.Settings.Default.WallNumber) + 65)).ToString();
            TimberDraw.Properties.Settings.Default.WallAlpha = TextBoxWallNumber.Text;
            switch (TimberDraw.Properties.Settings.Default.BraceStrutPlacement)
            {
                case 0:
                    RadioButtonFlushToBack.Checked = true;
                    break;
                case 1:
                    RadioButtonCenteredInBent.Checked = true;
                    break;
                case 2:
                    RadioButtonFlushToFront.Checked = true;
                    break;
            }
            //ToolTip2.SetToolTip(Label2, "Horizonal Distance");
        }
        private void HBSettings()
        {
            //Hammer Beam Bent Settings
            CheckBoxHBHasPost.Checked = TimberDraw.Properties.Settings.Default.HBHasPost;
            TextBoxHBPostWidth.Text = TimberDraw.Properties.Settings.Default.HBPostWidth.ToString();
            TextBoxHBPostDepth.Text = TimberDraw.Properties.Settings.Default.HBPostDepth.ToString();

            CheckBoxHBHasHBeam.Checked = TimberDraw.Properties.Settings.Default.HBHasHbeam;
            TextBoxHBeamWidth.Text = TimberDraw.Properties.Settings.Default.HBeamWidth.ToString();
            TextBoxHBeamDepth.Text = TimberDraw.Properties.Settings.Default.HBeamDepth.ToString();

            CheckBoxHBHasHPost.Checked = TimberDraw.Properties.Settings.Default.HBHasHPost;
            TextBoxHPostWidth.Text = TimberDraw.Properties.Settings.Default.HPostWidth.ToString();
            TextBoxHPostDepth.Text = TimberDraw.Properties.Settings.Default.HPostDepth.ToString();

            CheckBoxHBHasKPost.Checked = TimberDraw.Properties.Settings.Default.HBHasKPost;
            TextBoxHBKPostWidth.Text = TimberDraw.Properties.Settings.Default.HBKPostWidth.ToString();
            TextBoxHBKPostDepth.Text = TimberDraw.Properties.Settings.Default.HBKPostDepth.ToString();

            CheckBoxHBHasRafter.Checked = TimberDraw.Properties.Settings.Default.HBHasRafter;
            TextBoxHBRafterWidth.Text = TimberDraw.Properties.Settings.Default.HBRafterWidth.ToString();
            TextBoxHBRafterDepth.Text = TimberDraw.Properties.Settings.Default.HBRafterDepth.ToString();

            CheckBoxHBHasHBGirt.Checked = TimberDraw.Properties.Settings.Default.HBHasHBGirt;
            TextBoxHBGirtWidth.Text = TimberDraw.Properties.Settings.Default.HBGirtWidth.ToString();
            TextBoxHBGirtDepth.Text = TimberDraw.Properties.Settings.Default.HBGirtDepth.ToString();

            CheckBoxHBHasBrace.Checked = TimberDraw.Properties.Settings.Default.HBHasBrace;
            TextBoxHBBraceWidth.Text = TimberDraw.Properties.Settings.Default.HBBraceWidth.ToString();
            TextBoxHBBraceDepth.Text = TimberDraw.Properties.Settings.Default.HBBraceDepth.ToString();
            TextBoxHBBraceLength.Text = TimberDraw.Properties.Settings.Default.HBBraceLength.ToString();

            CheckBoxHBHasBayGirt.Checked = TimberDraw.Properties.Settings.Default.HBHasBayGirt;
            TextBoxHBBayGirtWidth.Text = TimberDraw.Properties.Settings.Default.HBBayGirtWidth.ToString();
            TextBoxHBBayGirtDepth.Text = TimberDraw.Properties.Settings.Default.HBBayGirtDepth.ToString();

            CheckBoxHBHasBGBrace.Checked = TimberDraw.Properties.Settings.Default.HBHasBGBrace;
            TextBoxHBBGBraceWidth.Text = TimberDraw.Properties.Settings.Default.HBBGBraceWidth.ToString();
            TextBoxHBBGBraceDepth.Text = TimberDraw.Properties.Settings.Default.HBBGBraceDepth.ToString();
            TextBoxHBBGBraceLength.Text = TimberDraw.Properties.Settings.Default.HBBGBraceLength.ToString();

            CheckBoxHBHasFlrGirt.Checked = TimberDraw.Properties.Settings.Default.HBHasFlrGirt;
            TextBoxHBFlrGirtWidth.Text = TimberDraw.Properties.Settings.Default.HBFlrGirtWidth.ToString();
            TextBoxHBFlrGirtDepth.Text = TimberDraw.Properties.Settings.Default.HBFlrGirtDepth.ToString();
            TextBoxHBFlrGirtHt.Text = TimberDraw.Properties.Settings.Default.HBFlrGirtHt.ToString();

            CheckBoxHBHasFlrBrace.Checked = TimberDraw.Properties.Settings.Default.HBHasFlrBrace;
            TextBoxHBFlrBraceWidth.Text = TimberDraw.Properties.Settings.Default.HBFlrBraceWidth.ToString();
            TextBoxHBFlrBraceDepth.Text = TimberDraw.Properties.Settings.Default.HBFlrBraceDepth.ToString();
            TextBoxHBFlrBraceLength.Text = TimberDraw.Properties.Settings.Default.HBFlrBraceLength.ToString();

            switch (TimberDraw.Properties.Settings.Default.HBDivisor)
            {
                case 4:
                    RadioButton1Hbeam.Checked = true;
                    break;
                case 0:
                case 6:
                    RadioButton2Hbeam.Checked = true;
                    break;
            }
        }
        private void KBSettings()
        {
            //KingPost Bent Settings
            CheckBoxKPHasPost.Checked = TimberDraw.Properties.Settings.Default.KPHasPost;
            TextBoxKPPostWidth.Text = TimberDraw.Properties.Settings.Default.KPPostWidth.ToString();
            TextBoxKPPostDepth.Text = TimberDraw.Properties.Settings.Default.KPPostDepth.ToString();

            CheckBoxKPHasGirt.Checked = TimberDraw.Properties.Settings.Default.KPHasGirt;
            TextBoxKPGirtWidth.Text = TimberDraw.Properties.Settings.Default.KPGirtWidth.ToString();
            TextBoxKPGirtDepth.Text = TimberDraw.Properties.Settings.Default.KPGirtDepth.ToString();

            CheckBoxKPHasRafter.Checked = TimberDraw.Properties.Settings.Default.KPHasRafter;
            TextBoxKPRafterWidth.Text = TimberDraw.Properties.Settings.Default.KPRafterWidth.ToString();
            TextBoxKPRafterDepth.Text = TimberDraw.Properties.Settings.Default.KPRafterDepth.ToString();

            CheckBoxKPHasKPost.Checked = TimberDraw.Properties.Settings.Default.KPHasKPost;
            TextBoxKPKpostWidth.Text = TimberDraw.Properties.Settings.Default.KPKpostWidth.ToString();
            TextBoxKPKpostDepth.Text = TimberDraw.Properties.Settings.Default.KPKpostDepth.ToString();

            CheckBoxKPHasStrut.Checked = TimberDraw.Properties.Settings.Default.KPHasStrut;
            TextBoxKPStrutWidth.Text = TimberDraw.Properties.Settings.Default.KPStrutWidth.ToString();
            TextBoxKPStrutDepth.Text = TimberDraw.Properties.Settings.Default.KPStrutDepth.ToString();
            TextBoxKPStrutAngle.Text = TimberDraw.Properties.Settings.Default.KPStrutAngle.ToString();
            TextBoxKPBraceAngle.Text = TimberDraw.Properties.Settings.Default.KPBraceAngle.ToString();

            CheckBoxKPHasVStrut.Checked = TimberDraw.Properties.Settings.Default.KPHasVStrut;
            TextBoxKPVStrutWidth.Text = TimberDraw.Properties.Settings.Default.KPVStrutWidth.ToString();
            TextBoxKPVStrutDepth.Text = TimberDraw.Properties.Settings.Default.KPVStrutDepth.ToString();

            CheckBoxKPHasBrace.Checked = TimberDraw.Properties.Settings.Default.KPHasBrace;
            TextBoxKPBraceWidth.Text = TimberDraw.Properties.Settings.Default.KPBraceWidth.ToString();
            TextBoxKPBraceDepth.Text = TimberDraw.Properties.Settings.Default.KPBraceDepth.ToString();
            TextBoxKPBraceLength.Text = TimberDraw.Properties.Settings.Default.KPBraceLength.ToString();

            CheckBoxKPHasFlrGirt.Checked = TimberDraw.Properties.Settings.Default.KPHasFlrGirt;
            TextBoxKPFlrGirtWidth.Text = TimberDraw.Properties.Settings.Default.KPFlrGirtWidth.ToString();
            TextBoxKPFlrGirtDepth.Text = TimberDraw.Properties.Settings.Default.KPFlrGirtDepth.ToString();
            TextBoxKPFlrGirtHt.Text = TimberDraw.Properties.Settings.Default.KPFlrGirtHt.ToString();

            CheckBoxKPHasFlrBrace.Checked = TimberDraw.Properties.Settings.Default.KPHasFlrBrace;
            TextBoxKPFlrBraceWidth.Text = TimberDraw.Properties.Settings.Default.KPFlrBraceWidth.ToString();
            TextBoxKPFlrBraceDepth.Text = TimberDraw.Properties.Settings.Default.KPFlrBraceDepth.ToString();
            TextBoxKPFlrBraceLength.Text = TimberDraw.Properties.Settings.Default.KPFlrBraceLength.ToString();
        }
        private void QBSettings()
        {
            //QueenPost Bent Settings
            CheckBoxQPHasPost.Checked = TimberDraw.Properties.Settings.Default.QPHasPost;
            TextBoxQPPostWidth.Text = TimberDraw.Properties.Settings.Default.QPPostWidth.ToString();
            TextBoxQPPostDepth.Text = TimberDraw.Properties.Settings.Default.QPPostDepth.ToString();

            CheckBoxQPHasGirt.Checked = TimberDraw.Properties.Settings.Default.QPHasGirt;
            TextBoxQPGirtWidth.Text = TimberDraw.Properties.Settings.Default.QPGirtWidth.ToString();
            TextBoxQPGirtDepth.Text = TimberDraw.Properties.Settings.Default.QPGirtDepth.ToString();

            CheckBoxQPHasRafter.Checked = TimberDraw.Properties.Settings.Default.QPHasRafter;
            TextBoxQPRafterWidth.Text = TimberDraw.Properties.Settings.Default.QPRafterWidth.ToString();
            TextBoxQPRafterDepth.Text = TimberDraw.Properties.Settings.Default.QPRafterDepth.ToString();

            CheckBoxQPHasQPost.Checked = TimberDraw.Properties.Settings.Default.QPHasQPost;
            TextBoxQPQPostWidth.Text = TimberDraw.Properties.Settings.Default.QPQPostWidth.ToString();
            TextBoxQPQPostDepth.Text = TimberDraw.Properties.Settings.Default.QPQPostDepth.ToString();

            CheckBoxQPHasStrut.Checked = TimberDraw.Properties.Settings.Default.QPHasStrut;
            TextBoxQPStrutWidth.Text = TimberDraw.Properties.Settings.Default.QPStrutWidth.ToString();
            TextBoxQPStrutDepth.Text = TimberDraw.Properties.Settings.Default.QPStrutDepth.ToString();

            CheckBoxQPHasQPGirt.Checked = TimberDraw.Properties.Settings.Default.QPHasQPGirt;
            TextBoxQPQPGirtWidth.Text = TimberDraw.Properties.Settings.Default.QPQPGirtWidth.ToString();
            TextBoxQPQPGirtDepth.Text = TimberDraw.Properties.Settings.Default.QPQPGirtDepth.ToString();

            CheckBoxQPHasBrace.Checked = TimberDraw.Properties.Settings.Default.QPHasBrace;
            TextBoxQPBraceWidth.Text = TimberDraw.Properties.Settings.Default.QPBraceWidth.ToString();
            TextBoxQPBraceDepth.Text = TimberDraw.Properties.Settings.Default.QPBraceDepth.ToString();
            TextBoxQPBraceLength.Text = TimberDraw.Properties.Settings.Default.QPBraceLength.ToString();

            CheckBoxQPHasQPBrace.Checked = TimberDraw.Properties.Settings.Default.QPHasQPBrace;
            TextBoxQPQPBraceWidth.Text = TimberDraw.Properties.Settings.Default.QPQPBraceWidth.ToString();
            TextBoxQPQPBraceDepth.Text = TimberDraw.Properties.Settings.Default.QPQPBraceDepth.ToString();
            TextBoxQPQPBraceLength.Text = TimberDraw.Properties.Settings.Default.QPQPBraceLength.ToString();

            CheckBoxQPHasFlrGirt.Checked = TimberDraw.Properties.Settings.Default.QPHasFlrGirt;
            TextBoxQPFlrGirtWidth.Text = TimberDraw.Properties.Settings.Default.QPFlrGirtWidth.ToString();
            TextBoxQPFlrGirtDepth.Text = TimberDraw.Properties.Settings.Default.QPFlrGirtDepth.ToString();
            TextBoxQPFlrGirtHt.Text = TimberDraw.Properties.Settings.Default.QPFlrGirtHt.ToString();

            CheckBoxQPHasFlrBrace.Checked = TimberDraw.Properties.Settings.Default.QPHasFlrBrace;
            TextBoxQPFlrBraceWidth.Text = TimberDraw.Properties.Settings.Default.QPFlrBraceWidth.ToString();
            TextBoxQPFlrBraceDepth.Text = TimberDraw.Properties.Settings.Default.QPFlrBraceDepth.ToString();
            TextBoxQPFlrBraceLength.Text = TimberDraw.Properties.Settings.Default.QPFlrBraceLength.ToString();
        }
        private void KBTSettings()
        {
            //KingPost Truss Settings
            CheckBoxKPTHasGirt.Checked = TimberDraw.Properties.Settings.Default.KPTHasGirt;
            TextBoxKPTGirtWidth.Text = TimberDraw.Properties.Settings.Default.KPTGirtWidth.ToString();
            TextBoxKPTGirtDepth.Text = TimberDraw.Properties.Settings.Default.KPTGirtDepth.ToString();

            CheckBoxKPTHasRafter.Checked = TimberDraw.Properties.Settings.Default.KPTHasRafter;
            TextBoxKPTRafterWidth.Text = TimberDraw.Properties.Settings.Default.KPTRafterWidth.ToString();
            TextBoxKPTRafterDepth.Text = TimberDraw.Properties.Settings.Default.KPTRafterDepth.ToString();

            CheckBoxKPTHasKPost.Checked = TimberDraw.Properties.Settings.Default.KPTHasKpost;
            TextBoxKPTKpostWidth.Text = TimberDraw.Properties.Settings.Default.KPTKpostWidth.ToString();
            TextBoxKPTKpostDepth.Text = TimberDraw.Properties.Settings.Default.KPTKpostDepth.ToString();

            CheckBoxKPTHasStrut.Checked = TimberDraw.Properties.Settings.Default.KPTHasStrut;
            TextBoxKPTStrutWidth.Text = TimberDraw.Properties.Settings.Default.KPTStrutWidth.ToString();
            TextBoxKPTStrutDepth.Text = TimberDraw.Properties.Settings.Default.KPTStrutDepth.ToString();

            CheckBoxKPTHasVStrut.Checked = TimberDraw.Properties.Settings.Default.KPTHasVStrut;
            TextBoxKPTVStrutWidth.Text = TimberDraw.Properties.Settings.Default.KPTVStrutWidth.ToString();
            TextBoxKPTVStrutDepth.Text = TimberDraw.Properties.Settings.Default.KPTVStrutDepth.ToString();
        }
        private void QBTSettings()
        {
            //QueenPost Truss Settings
            CheckBoxQPTHasGirt.Checked = TimberDraw.Properties.Settings.Default.QPTHasGirt;
            TextBoxQPTGirtWidth.Text = TimberDraw.Properties.Settings.Default.QPTGirtWidth.ToString();
            TextBoxQPTGirtDepth.Text = TimberDraw.Properties.Settings.Default.QPTGirtDepth.ToString();

            CheckBoxQPTHasRafter.Checked = TimberDraw.Properties.Settings.Default.QPTHasRafter;
            TextBoxQPTRafterWidth.Text = TimberDraw.Properties.Settings.Default.QPTRafterWidth.ToString();
            TextBoxQPTRafterDepth.Text = TimberDraw.Properties.Settings.Default.QPTRafterDepth.ToString();

            CheckBoxQPTHasQPost.Checked = TimberDraw.Properties.Settings.Default.QPTHasQpost;
            TextBoxQPTQpostWidth.Text = TimberDraw.Properties.Settings.Default.QPTQpostWidth.ToString();
            TextBoxQPTQpostDepth.Text = TimberDraw.Properties.Settings.Default.QPTQpostDepth.ToString();

            CheckBoxQPTHasStrut.Checked = TimberDraw.Properties.Settings.Default.QPTHasStrut;
            TextBoxQPTStrutWidth.Text = TimberDraw.Properties.Settings.Default.QPTStrutWidth.ToString();
            TextBoxQPTStrutDepth.Text = TimberDraw.Properties.Settings.Default.QPTStrutDepth.ToString();

            CheckBoxQPTHasQPGirt.Checked = TimberDraw.Properties.Settings.Default.QPTHasQPGirt;
            TextBoxQPTUpperGirtWidth.Text = TimberDraw.Properties.Settings.Default.QPTUpperGirtWidth.ToString();
            TextBoxQPTUpperGirtDepth.Text = TimberDraw.Properties.Settings.Default.QPTUpperGirtDepth.ToString();

            CheckBoxQPTHasQPBrace.Checked = TimberDraw.Properties.Settings.Default.QPTHasQPBrace;
            TextBoxQPTQPBraceWidth.Text = TimberDraw.Properties.Settings.Default.QPTQPBraceWidth.ToString();
            TextBoxQPTQPBraceDepth.Text = TimberDraw.Properties.Settings.Default.QPTQPBraceDepth.ToString();
            TextBoxQPTQPBraceLength.Text = TimberDraw.Properties.Settings.Default.QPTQPBraceLength.ToString();
        }
        private void RidgeSettings()
        {
            //Ridge
            CheckBoxHasRidge.Checked = TimberDraw.Properties.Settings.Default.HasRidge;
            TextBoxRidgeWidth.Text = TimberDraw.Properties.Settings.Default.RidgeWidth.ToString();
            TextBoxRidgeDepth.Text = TimberDraw.Properties.Settings.Default.RidgeDepth.ToString();
            CheckBoxHasRidgeBrace.Checked = TimberDraw.Properties.Settings.Default.HasRidgeBrace;
            TextBoxRidgeBraceWidth.Text = TimberDraw.Properties.Settings.Default.RidgeBraceWidth.ToString();
            TextBoxRidgeBraceDepth.Text = TimberDraw.Properties.Settings.Default.RidgeBraceDepth.ToString();
            TextBoxRidgeBraceLength.Text = TimberDraw.Properties.Settings.Default.RidgeBraceLength.ToString();
        }
        private void EaveSettings()
        {
            //Eave Girt
            CheckBoxHasEaveGirt.Checked = TimberDraw.Properties.Settings.Default.HasEaveGirt;
            TextBoxEaveGirtWidth.Text = TimberDraw.Properties.Settings.Default.EaveGirtWidth.ToString();
            TextBoxEaveGirtDepth.Text = TimberDraw.Properties.Settings.Default.EaveGirtDepth.ToString();
            //Eave Girt Brace
            CheckBoxHasEaveGirtBrace.Checked = TimberDraw.Properties.Settings.Default.HasEaveGirtBrace;
            TextBoxEaveGirtBraceWidth.Text = TimberDraw.Properties.Settings.Default.EaveGirtBraceWidth.ToString();
            TextBoxEaveGirtBraceDepth.Text = TimberDraw.Properties.Settings.Default.EaveGirtBraceDepth.ToString();
            TextBoxEaveGirtBraceLength.Text = TimberDraw.Properties.Settings.Default.EaveGirtBraceLength.ToString();
        }
        private void PurlinSettings()
        {
            //Purlins
            CheckBoxHasPurlins.Checked = TimberDraw.Properties.Settings.Default.HasPurlins;
            TextBoxPurlinWidth.Text = TimberDraw.Properties.Settings.Default.PurlinWidth.ToString();
            TextBoxPurlinDepth.Text = TimberDraw.Properties.Settings.Default.PurlinDepth.ToString();
        }
        private void CommonSettings()
        {
            //Commons
            CheckBoxHasCommons.Checked = TimberDraw.Properties.Settings.Default.HasCommons;
            TextBoxCommonWidth.Text = TimberDraw.Properties.Settings.Default.CommonWidth.ToString();
            TextBoxCommonDepth.Text = TimberDraw.Properties.Settings.Default.CommonDepth.ToString();
        }
        private void BaySettings()
        {
            //Bay Floor Girt
            CheckBoxGenerateBayMembers.Checked = TimberDraw.Properties.Settings.Default.GenerateBayMembers;
            Module1.GenerateBayMembers = TimberDraw.Properties.Settings.Default.GenerateBayMembers;
            CheckBoxHasFlrGirt.Checked = TimberDraw.Properties.Settings.Default.HasFlrGirt;
            TextBoxFlrGirtWidth.Text = TimberDraw.Properties.Settings.Default.FlrGirtWidth.ToString();
            TextBoxFlrGirtDepth.Text = TimberDraw.Properties.Settings.Default.FlrGirtDepth.ToString();
            TextBoxFlrGirtHt.Text = TimberDraw.Properties.Settings.Default.FlrGirtHt.ToString();
        }
        private void FlrGirtBraceSettings()
        {
            //Floor Girt Brace
            CheckBoxHasFlrGirtBrace.Checked = TimberDraw.Properties.Settings.Default.HasFlrGirtBrace;
            TextBoxFlrGirtBraceWidth.Text = TimberDraw.Properties.Settings.Default.FlrGirtBraceWidth.ToString();
            TextBoxFlrGirtBraceDepth.Text = TimberDraw.Properties.Settings.Default.FlrGirtBraceDepth.ToString();
            TextBoxFlrGirtBraceLength.Text = TimberDraw.Properties.Settings.Default.FlrGirtBraceLength.ToString();
        }
        private void RoofLoadSettings()
        {
            //Roof Load Settings
            TextBoxRoofLoad.Text = TimberDraw.Properties.Settings.Default.RoofLoad.ToString();
            TextBoxEmod.Text = TimberDraw.Properties.Settings.Default.Emod.ToString();
            TextBoxAllowableDeflection.Text = TimberDraw.Properties.Settings.Default.AllowableDeflection.ToString();
        }
        # endregion

        #region Load User Control
        private void UserControl1_Load(System.Object sender, System.EventArgs e)
        {
            //Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.DocumentBecameCurrent += ChangeUnits;
            Autodesk.AutoCAD.ApplicationServices.Application.SystemVariableChanged += new Autodesk.AutoCAD.ApplicationServices.SystemVariableChangedEventHandler(ChangeUnitsEventHandler);
            TimberDraw.Properties.Settings.Default.PropertyChanged += (s, ea) => TimberDraw.Properties.Settings.Default.Save();
            //Get Settings
            GeneralSettings();
            HBSettings();
            KBSettings();
            QBSettings();
            KBTSettings();
            QBTSettings();
            RidgeSettings();
            EaveSettings();
            PurlinSettings();
            CommonSettings();
            BaySettings();
            FlrGirtBraceSettings();
            RoofLoadSettings();
        }
        private void ChangeUnitsEventHandler(object sender, Autodesk.AutoCAD.ApplicationServices.SystemVariableChangedEventArgs e)
        {
            if (e.Name == "LUNITS" | e.Name == "LUPREC")
            {
                ChangeUnits();
            }
        }
        private void ChangeUnits()
        {
            TextBoxEaveHeight.Text = Converter.DistanceToString(TimberDraw.Properties.Settings.Default.EaveHt, DistanceUnitFormat.Current, -1);
            TextBoxSpan.Text = Converter.DistanceToString(TimberDraw.Properties.Settings.Default.Span, DistanceUnitFormat.Current, -1);
            TextBoxRoofPitchRun.Text = Converter.DistanceToString(TimberDraw.Properties.Settings.Default.RoofPitchRun, DistanceUnitFormat.Current, -1);
            TextBoxRoofPitchRise.Text = Converter.DistanceToString(TimberDraw.Properties.Settings.Default.RoofPitchRise, DistanceUnitFormat.Current, -1);
            TextBoxBayWidth.Text = Converter.DistanceToString(TimberDraw.Properties.Settings.Default.BayWidth, DistanceUnitFormat.Current, -1);
            TextBoxFlrGirtHt.Text = Converter.DistanceToString(TimberDraw.Properties.Settings.Default.FlrGirtHt, DistanceUnitFormat.Current, -1);
        }
        private void ButtonPickStartPoint_Click(System.Object sender, System.EventArgs e)
        {
            // TEMP TESTING: fixed origin -- restore GetPoint block when done
            Module1.StartPoint = new Point3d(0, 0, 0);
            if (true)
            {
                // Module1.StartPoint = res.Value;  // restored from pick when reverted
                Module1.EaveHt = TimberDraw.Properties.Settings.Default.EaveHt;
                Module1.Span = TimberDraw.Properties.Settings.Default.Span;
                Module1.Prun = TimberDraw.Properties.Settings.Default.RoofPitchRun;
                Module1.Prise = TimberDraw.Properties.Settings.Default.RoofPitchRise;
                if (DataReady())
                {
                    Module1.Beta = Math.Atan(Module1.Prise / Module1.Prun);
                    Module1.Pitch = Module1.Prise / Module1.Prun;
                    Module1.R = ((Module1.Span / 3) - 10) / (2 * Math.Sin(Math.PI - Module1.Beta - Math.Atan(1)));
                    Module1.B = Math.Sin(Module1.Beta) * (2 * Module1.R);
                    Module1.B = (Math.Sqrt((Module1.B * Module1.B) / 2));
                    Module1.C = Math.Sin(Math.Atan(1)) * (2 * Module1.R);

                    HBBent HB = new();
                    if (RadioButtonHammerBeam.Checked)
                    {
                        if (RadioButton1Hbeam.Checked)
                            HB.HBDivisor = 4;
                        if (RadioButton2Hbeam.Checked)
                            HB.HBDivisor = 6;

                        HB.HasPost = CheckBoxHBHasPost.Checked;
                        HB.postWidth = TimberDraw.Properties.Settings.Default.HBPostWidth;
                        HB.postDepth = TimberDraw.Properties.Settings.Default.HBPostDepth;

                        HB.HasHBeam = TimberDraw.Properties.Settings.Default.HBHasHbeam;
                        HB.HBeamWidth = TimberDraw.Properties.Settings.Default.HBeamWidth;
                        HB.HBeamDepth = TimberDraw.Properties.Settings.Default.HBeamDepth;

                        HB.HasHPost = TimberDraw.Properties.Settings.Default.HBHasHPost;
                        HB.HPostWidth = TimberDraw.Properties.Settings.Default.HPostWidth;
                        HB.HPostDepth = TimberDraw.Properties.Settings.Default.HPostDepth;

                        HB.HasKPost = TimberDraw.Properties.Settings.Default.HBHasKPost;
                        HB.KpostWidth = TimberDraw.Properties.Settings.Default.HBKPostWidth;
                        HB.KpostDepth = TimberDraw.Properties.Settings.Default.HBKPostDepth;

                        HB.HasRafter = TimberDraw.Properties.Settings.Default.HBHasRafter;
                        HB.RafterWidth = TimberDraw.Properties.Settings.Default.HBRafterWidth;
                        HB.RafterDepth = TimberDraw.Properties.Settings.Default.HBRafterDepth;

                        HB.HasHBGirt = TimberDraw.Properties.Settings.Default.HBHasHBGirt;
                        HB.HBGirtWidth = TimberDraw.Properties.Settings.Default.HBGirtWidth;
                        HB.HBGirtDepth = TimberDraw.Properties.Settings.Default.HBGirtDepth;

                        HB.HasBrace = TimberDraw.Properties.Settings.Default.HBHasBrace;
                        HB.BraceWidth = TimberDraw.Properties.Settings.Default.HBBraceWidth;
                        HB.BraceDepth = TimberDraw.Properties.Settings.Default.HBBraceDepth;
                        HB.BraceLength = TimberDraw.Properties.Settings.Default.HBBraceLength;

                        HB.HasBayGirt = TimberDraw.Properties.Settings.Default.HBHasBayGirt;
                        HB.BayGirtWidth = TimberDraw.Properties.Settings.Default.HBBayGirtWidth;
                        HB.BayGirtDepth = TimberDraw.Properties.Settings.Default.HBBayGirtDepth;

                        HB.HasBGBrace = TimberDraw.Properties.Settings.Default.HBHasBGBrace;
                        HB.BGBraceWidth = TimberDraw.Properties.Settings.Default.HBBGBraceWidth;
                        HB.BGBraceDepth = TimberDraw.Properties.Settings.Default.HBBGBraceDepth;
                        HB.BGBraceLength = TimberDraw.Properties.Settings.Default.HBBGBraceLength;

                        HB.HasFlrGirt = TimberDraw.Properties.Settings.Default.HBHasFlrGirt;
                        HB.FlrGirtWidth = TimberDraw.Properties.Settings.Default.HBFlrGirtWidth;
                        HB.FlrGirtDepth = TimberDraw.Properties.Settings.Default.HBFlrGirtDepth;
                        HB.FlrGirtHt = TimberDraw.Properties.Settings.Default.HBFlrGirtHt;

                        HB.HasFlrBrace = TimberDraw.Properties.Settings.Default.HBHasFlrBrace;
                        HB.FlrBraceWidth = TimberDraw.Properties.Settings.Default.HBFlrBraceWidth;
                        HB.FlrBraceDepth = TimberDraw.Properties.Settings.Default.HBFlrBraceDepth;
                        HB.FlrBraceLength = TimberDraw.Properties.Settings.Default.HBFlrBraceLength;

                        HB.BayWidth = TimberDraw.Properties.Settings.Default.BayWidth;
                        if (HB.DataReady())
                        {
                            Module1.PlumbLength = HB.RafterDepth / Math.Cos(Module1.Beta);
                            Module1.TOH = (Module1.EaveHt + (HB.postDepth * Module1.Pitch)) - Module1.PlumbLength;
                            Module1.TOG = Module1.TOH - 6;
                            Module1.BOG = Module1.TOG - HB.HBeamDepth;
                            HB.Draw();
                            if (Module1.HasJoinery && HB.HasPost)
                            {
                                foreach (var pending in Module1.PendingLeftPostMortises)
                                {
                                    HB.PLeft.AddMortise(pending.MortiseId);
                                    Module1.AddConnection(HB.PLeft.TimberId, pending.SourceTimberId, Module1.JointType.Mortise);
                                    Module1.AddConnection(pending.SourceTimberId, HB.PLeft.TimberId, Module1.JointType.Tenon);
                                }
                                foreach (var pending in Module1.PendingRightPostMortises)
                                {
                                    HB.PRight.AddMortise(pending.MortiseId);
                                    Module1.AddConnection(HB.PRight.TimberId, pending.SourceTimberId, Module1.JointType.Mortise);
                                    Module1.AddConnection(pending.SourceTimberId, HB.PRight.TimberId, Module1.JointType.Tenon);
                                }
                            }
                            Module1.PendingLeftPostMortises.Clear();
                            Module1.PendingRightPostMortises.Clear();
                            if (Module1.HasJoinery && HB.HasRafter)
                            {
                                foreach (var pending in Module1.PendingLeftRafterMortises)
                                {
                                    HB.HBRLeft.AddMortise(pending.MortiseId);
                                    Module1.AddConnection(HB.HBRLeft.TimberId, pending.SourceTimberId, Module1.JointType.Mortise);
                                    Module1.AddConnection(pending.SourceTimberId, HB.HBRLeft.TimberId, Module1.JointType.Tenon);
                                }
                                foreach (var pending in Module1.PendingRightRafterMortises)
                                {
                                    HB.HBRRight.AddMortise(pending.MortiseId);
                                    Module1.AddConnection(HB.HBRRight.TimberId, pending.SourceTimberId, Module1.JointType.Mortise);
                                    Module1.AddConnection(pending.SourceTimberId, HB.HBRRight.TimberId, Module1.JointType.Tenon);
                                }
                            }
                            if (Module1.HasJoinery && HB.HasKPost)
                            {
                                foreach (var pending in Module1.PendingKPostMortises)
                                {
                                    HB.HBKP.AddMortise(pending.MortiseId);
                                    Module1.AddConnection(HB.HBKP.TimberId, pending.SourceTimberId, Module1.JointType.Mortise);
                                    Module1.AddConnection(pending.SourceTimberId, HB.HBKP.TimberId, Module1.JointType.Tenon);
                                }
                            }
                            Module1.PendingLeftRafterMortises.Clear();
                            Module1.PendingRightRafterMortises.Clear();
                            Module1.PendingKPostMortises.Clear();
                        }
                        else
                        {
                            MessageBox.Show("Hammer Beam Bent Data Not ready", "TimberDraw", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }

                    KPBent KPB = new();
                    if (RadioButtonKingPostBent.Checked)
                    {
                        KPB.HasPost = TimberDraw.Properties.Settings.Default.KPHasPost;
                        KPB.postWidth = TimberDraw.Properties.Settings.Default.KPPostWidth;
                        KPB.postDepth = TimberDraw.Properties.Settings.Default.KPPostDepth;

                        KPB.HasGirt = TimberDraw.Properties.Settings.Default.KPHasGirt;
                        KPB.GirtWidth = TimberDraw.Properties.Settings.Default.KPGirtWidth;
                        KPB.GirtDepth = TimberDraw.Properties.Settings.Default.KPGirtDepth;

                        KPB.HasRafter = TimberDraw.Properties.Settings.Default.KPHasRafter;
                        KPB.RafterWidth = TimberDraw.Properties.Settings.Default.KPRafterWidth;
                        KPB.RafterDepth = TimberDraw.Properties.Settings.Default.KPRafterDepth;

                        KPB.HasKPost = TimberDraw.Properties.Settings.Default.KPHasKPost;
                        KPB.KpostWidth = TimberDraw.Properties.Settings.Default.KPKpostWidth;
                        KPB.KpostDepth = TimberDraw.Properties.Settings.Default.KPKpostDepth;

                        KPB.HasStrut = TimberDraw.Properties.Settings.Default.KPHasStrut;
                        KPB.StrutWidth = TimberDraw.Properties.Settings.Default.KPStrutWidth;
                        KPB.StrutDepth = TimberDraw.Properties.Settings.Default.KPStrutDepth;

                        KPB.HasVStrut = TimberDraw.Properties.Settings.Default.KPHasVStrut;
                        KPB.VStrutWidth = TimberDraw.Properties.Settings.Default.KPVStrutWidth;
                        KPB.VStrutDepth = TimberDraw.Properties.Settings.Default.KPVStrutDepth;

                        KPB.HasFlrGirt = TimberDraw.Properties.Settings.Default.KPHasFlrGirt;
                        KPB.FlrGirtWidth = TimberDraw.Properties.Settings.Default.KPFlrGirtWidth;
                        KPB.FlrGirtDepth = TimberDraw.Properties.Settings.Default.KPFlrGirtDepth;
                        KPB.FlrGirtHt = TimberDraw.Properties.Settings.Default.KPFlrGirtHt;

                        KPB.HasBrace = TimberDraw.Properties.Settings.Default.KPHasBrace;
                        KPB.BraceWidth = TimberDraw.Properties.Settings.Default.KPBraceWidth;
                        KPB.BraceDepth = TimberDraw.Properties.Settings.Default.KPBraceDepth;
                        KPB.BraceLength = TimberDraw.Properties.Settings.Default.KPBraceLength;

                        KPB.HasFlrBrace = TimberDraw.Properties.Settings.Default.KPHasFlrBrace;
                        KPB.FlrBraceWidth = TimberDraw.Properties.Settings.Default.KPFlrBraceWidth;
                        KPB.FlrBraceDepth = TimberDraw.Properties.Settings.Default.KPFlrBraceDepth;
                        KPB.FlrBraceLength = TimberDraw.Properties.Settings.Default.KPFlrBraceLength;

                        if (KPB.DataReady())
                        {
                            KPB.Draw();
                            if (Module1.HasJoinery && KPB.HasPost)
                            {
                                foreach (var pending in Module1.PendingLeftPostMortises)
                                {
                                    KPB.PLeft.AddMortise(pending.MortiseId);
                                    Module1.AddConnection(KPB.PLeft.TimberId, pending.SourceTimberId, Module1.JointType.Mortise);
                                    Module1.AddConnection(pending.SourceTimberId, KPB.PLeft.TimberId, Module1.JointType.Tenon);
                                }
                                foreach (var pending in Module1.PendingRightPostMortises)
                                {
                                    KPB.PRight.AddMortise(pending.MortiseId);
                                    Module1.AddConnection(KPB.PRight.TimberId, pending.SourceTimberId, Module1.JointType.Mortise);
                                    Module1.AddConnection(pending.SourceTimberId, KPB.PRight.TimberId, Module1.JointType.Tenon);
                                }
                            }
                            Module1.PendingLeftPostMortises.Clear();
                            Module1.PendingRightPostMortises.Clear();
                            if (Module1.HasJoinery && KPB.HasRafter)
                            {
                                foreach (var pending in Module1.PendingLeftRafterMortises)
                                {
                                    KPB.RLeft.AddMortise(pending.MortiseId);
                                    Module1.AddConnection(KPB.RLeft.TimberId, pending.SourceTimberId, Module1.JointType.Mortise);
                                    Module1.AddConnection(pending.SourceTimberId, KPB.RLeft.TimberId, Module1.JointType.Tenon);
                                }
                                foreach (var pending in Module1.PendingRightRafterMortises)
                                {
                                    KPB.RRight.AddMortise(pending.MortiseId);
                                    Module1.AddConnection(KPB.RRight.TimberId, pending.SourceTimberId, Module1.JointType.Mortise);
                                    Module1.AddConnection(pending.SourceTimberId, KPB.RRight.TimberId, Module1.JointType.Tenon);
                                }
                            }
                            if (Module1.HasJoinery && KPB.HasKPost)
                            {
                                foreach (var pending in Module1.PendingKPostMortises)
                                {
                                    KPB.KP.AddMortise(pending.MortiseId);
                                    Module1.AddConnection(KPB.KP.TimberId, pending.SourceTimberId, Module1.JointType.Mortise);
                                    Module1.AddConnection(pending.SourceTimberId, KPB.KP.TimberId, Module1.JointType.Tenon);
                                }
                            }
                            Module1.PendingLeftRafterMortises.Clear();
                            Module1.PendingRightRafterMortises.Clear();
                            Module1.PendingKPostMortises.Clear();
                        }
                        else
                        {
                            MessageBox.Show("King Post Bent Data Not ready", "TimberDraw", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }

                    QPBent QPB = new();
                    if (RadioButtonQueenPostBent.Checked)
                    {
                        QPB.HasPost = TimberDraw.Properties.Settings.Default.QPHasPost;
                        QPB.QPPostWidth = TimberDraw.Properties.Settings.Default.QPPostWidth;
                        QPB.QPPostDepth = TimberDraw.Properties.Settings.Default.QPPostDepth;

                        QPB.HasGirt = TimberDraw.Properties.Settings.Default.QPHasGirt;
                        QPB.QPGirtWidth = TimberDraw.Properties.Settings.Default.QPGirtWidth;
                        QPB.QPGirtDepth = TimberDraw.Properties.Settings.Default.QPGirtDepth;

                        QPB.HasRafter = TimberDraw.Properties.Settings.Default.QPHasRafter;
                        QPB.QPRafterWidth = TimberDraw.Properties.Settings.Default.QPRafterWidth;
                        QPB.QPRafterDepth = TimberDraw.Properties.Settings.Default.QPRafterDepth;

                        QPB.HasQPost = TimberDraw.Properties.Settings.Default.QPHasQPost;
                        QPB.QPQpostWidth = TimberDraw.Properties.Settings.Default.QPQPostWidth;
                        QPB.QPQpostDepth = TimberDraw.Properties.Settings.Default.QPQPostDepth;

                        QPB.HasStrut = TimberDraw.Properties.Settings.Default.QPHasStrut;
                        QPB.QPStrutWidth = TimberDraw.Properties.Settings.Default.QPStrutWidth;
                        QPB.QPStrutDepth = TimberDraw.Properties.Settings.Default.QPStrutDepth;

                        QPB.HasQPGirt = TimberDraw.Properties.Settings.Default.QPHasQPGirt;
                        QPB.QPQPGirtWidth = TimberDraw.Properties.Settings.Default.QPQPGirtWidth;
                        QPB.QPQPGirtDepth = TimberDraw.Properties.Settings.Default.QPQPGirtDepth;

                        QPB.HasBrace = TimberDraw.Properties.Settings.Default.QPHasBrace;
                        QPB.QPBraceWidth = TimberDraw.Properties.Settings.Default.QPBraceWidth;
                        QPB.QPBraceDepth = TimberDraw.Properties.Settings.Default.QPBraceDepth;
                        QPB.QPBraceLength = TimberDraw.Properties.Settings.Default.QPBraceLength;

                        QPB.HasQPBrace = TimberDraw.Properties.Settings.Default.QPHasQPBrace;
                        QPB.QPQPBraceWidth = TimberDraw.Properties.Settings.Default.QPQPBraceWidth;
                        QPB.QPQPBraceDepth = TimberDraw.Properties.Settings.Default.QPQPBraceDepth;
                        QPB.QPQPBraceLength = TimberDraw.Properties.Settings.Default.QPQPBraceLength;

                        QPB.HasFlrGirt = TimberDraw.Properties.Settings.Default.QPHasFlrGirt;
                        QPB.QPFlrGirtWidth = TimberDraw.Properties.Settings.Default.QPFlrGirtWidth;
                        QPB.QPFlrGirtDepth = TimberDraw.Properties.Settings.Default.QPFlrGirtDepth;
                        QPB.QPFlrGirtHt = TimberDraw.Properties.Settings.Default.QPFlrGirtHt;

                        QPB.HasFlrBrace = TimberDraw.Properties.Settings.Default.QPHasFlrBrace;
                        QPB.QPFlrBraceWidth = TimberDraw.Properties.Settings.Default.QPFlrBraceWidth;
                        QPB.QPFlrBraceDepth = TimberDraw.Properties.Settings.Default.QPFlrBraceDepth;
                        QPB.QPFlrBraceLength = TimberDraw.Properties.Settings.Default.QPFlrBraceLength;

                        if (QPB.DataReady())
                        {
                            QPB.Draw();
                            if (Module1.HasJoinery && QPB.HasPost)
                            {
                                foreach (var pending in Module1.PendingLeftPostMortises)
                                {
                                    QPB.PLeft.AddMortise(pending.MortiseId);
                                    Module1.AddConnection(QPB.PLeft.TimberId, pending.SourceTimberId, Module1.JointType.Mortise);
                                    Module1.AddConnection(pending.SourceTimberId, QPB.PLeft.TimberId, Module1.JointType.Tenon);
                                }
                                foreach (var pending in Module1.PendingRightPostMortises)
                                {
                                    QPB.PRight.AddMortise(pending.MortiseId);
                                    Module1.AddConnection(QPB.PRight.TimberId, pending.SourceTimberId, Module1.JointType.Mortise);
                                    Module1.AddConnection(pending.SourceTimberId, QPB.PRight.TimberId, Module1.JointType.Tenon);
                                }
                            }
                            Module1.PendingLeftPostMortises.Clear();
                            Module1.PendingRightPostMortises.Clear();
                            if (Module1.HasJoinery && QPB.HasRafter)
                            {
                                foreach (var pending in Module1.PendingLeftRafterMortises)
                                {
                                    QPB.QPRLeft.AddMortise(pending.MortiseId);
                                    Module1.AddConnection(QPB.QPRLeft.TimberId, pending.SourceTimberId, Module1.JointType.Mortise);
                                    Module1.AddConnection(pending.SourceTimberId, QPB.QPRLeft.TimberId, Module1.JointType.Tenon);
                                }
                                foreach (var pending in Module1.PendingRightRafterMortises)
                                {
                                    QPB.QPRRight.AddMortise(pending.MortiseId);
                                    Module1.AddConnection(QPB.QPRRight.TimberId, pending.SourceTimberId, Module1.JointType.Mortise);
                                    Module1.AddConnection(pending.SourceTimberId, QPB.QPRRight.TimberId, Module1.JointType.Tenon);
                                }
                            }
                            // QPBent has no king post -- PendingKPostMortises cleared below
                            Module1.PendingLeftRafterMortises.Clear();
                            Module1.PendingRightRafterMortises.Clear();
                            Module1.PendingKPostMortises.Clear();
                        }
                        else
                        {
                            MessageBox.Show("Queen Post Bent Data Not ready", "TimberDraw", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }

                    if (RadioButtonKingPostTruss.Checked)
                    {
                        KPTruss KPT = new();
                        KPT.HasGirt = TimberDraw.Properties.Settings.Default.KPTHasGirt;
                        KPT.GirtWidth = TimberDraw.Properties.Settings.Default.KPTGirtWidth;
                        KPT.GirtDepth = TimberDraw.Properties.Settings.Default.KPTGirtDepth;

                        KPT.HasRafter = TimberDraw.Properties.Settings.Default.KPTHasRafter;
                        KPT.RafterWidth = TimberDraw.Properties.Settings.Default.KPTRafterWidth;
                        KPT.RafterDepth = TimberDraw.Properties.Settings.Default.KPTRafterDepth;

                        KPT.HasKPost = TimberDraw.Properties.Settings.Default.KPTHasKpost;
                        KPT.KpostWidth = TimberDraw.Properties.Settings.Default.KPTKpostWidth;
                        KPT.KpostDepth = TimberDraw.Properties.Settings.Default.KPTKpostDepth;

                        KPT.HasStrut = TimberDraw.Properties.Settings.Default.KPTHasStrut;
                        KPT.StrutWidth = TimberDraw.Properties.Settings.Default.KPTStrutWidth;
                        KPT.StrutDepth = TimberDraw.Properties.Settings.Default.KPTStrutDepth;

                        KPT.HasVStrut = TimberDraw.Properties.Settings.Default.KPTHasVStrut;
                        KPT.VStrutWidth = TimberDraw.Properties.Settings.Default.KPTVStrutWidth;
                        KPT.VStrutDepth = TimberDraw.Properties.Settings.Default.KPTVStrutDepth;

                        if (KPT.DataReady())
                        {
                            Module1.PlumbLength = KPT.RafterDepth / Math.Cos(Module1.Beta);
                            Module1.TOH = Module1.EaveHt + KPT.GirtDepth;
                            KPT.Draw();
                            Module1.PendingLeftPostMortises.Clear();
                            Module1.PendingRightPostMortises.Clear();
                            Module1.PendingLeftRafterMortises.Clear();
                            Module1.PendingRightRafterMortises.Clear();
                            Module1.PendingKPostMortises.Clear();
                        }
                        else
                        {
                            MessageBox.Show("King Post Truss Data Not ready", "TimberDraw", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }

                    if (RadioButtonQueenPostTruss.Checked)
                    {
                        QPTruss QPT = new();
                        QPT.HasGirt = TimberDraw.Properties.Settings.Default.QPTHasGirt;
                        QPT.GirtWidth = TimberDraw.Properties.Settings.Default.QPTGirtWidth;
                        QPT.GirtDepth = TimberDraw.Properties.Settings.Default.QPTGirtDepth;

                        QPT.HasRafter = TimberDraw.Properties.Settings.Default.QPTHasRafter;
                        QPT.RafterWidth = TimberDraw.Properties.Settings.Default.QPTRafterWidth;
                        QPT.RafterDepth = TimberDraw.Properties.Settings.Default.QPTRafterDepth;

                        QPT.HasQPost = TimberDraw.Properties.Settings.Default.QPTHasQpost;
                        QPT.QpostWidth = TimberDraw.Properties.Settings.Default.QPTQpostWidth;
                        QPT.QpostDepth = TimberDraw.Properties.Settings.Default.QPTQpostDepth;

                        QPT.HasStrut = TimberDraw.Properties.Settings.Default.QPTHasStrut;
                        QPT.StrutWidth = TimberDraw.Properties.Settings.Default.QPTStrutWidth;
                        QPT.StrutDepth = TimberDraw.Properties.Settings.Default.QPTStrutDepth;

                        QPT.HasQPGirt = TimberDraw.Properties.Settings.Default.QPTHasQPGirt;
                        QPT.UpperGirtWidth = TimberDraw.Properties.Settings.Default.QPTUpperGirtWidth;
                        QPT.UpperGirtDepth = TimberDraw.Properties.Settings.Default.QPTUpperGirtDepth;

                        QPT.HasQPBrace = TimberDraw.Properties.Settings.Default.QPTHasQPBrace;
                        QPT.QPBraceWidth = TimberDraw.Properties.Settings.Default.QPTQPBraceWidth;
                        QPT.QPBraceDepth = TimberDraw.Properties.Settings.Default.QPTQPBraceDepth;
                        QPT.QPBraceLength = TimberDraw.Properties.Settings.Default.QPTQPBraceLength;

                        if (QPT.DataReady())
                        {
                            Module1.PlumbLength = QPT.RafterDepth / Math.Cos(Module1.Beta);
                            Module1.TOH = Module1.EaveHt + QPT.GirtDepth;
                            QPT.Draw();
                            Module1.PendingLeftPostMortises.Clear();
                            Module1.PendingRightPostMortises.Clear();
                            Module1.PendingLeftRafterMortises.Clear();
                            Module1.PendingRightRafterMortises.Clear();
                            Module1.PendingKPostMortises.Clear();
                        }
                        else
                        {
                            MessageBox.Show("Queen Post Truss Data Not ready", "TimberDraw", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }

                    //Bay Members
                    if (CheckBoxGenerateBayMembers.Checked)
                    {
                        Ridge KPBRidge = new();
                        if (CheckBoxHasRidge.Checked)
                        {
                            KPBRidge.Depth = TimberDraw.Properties.Settings.Default.RidgeDepth;
                            KPBRidge.Width = TimberDraw.Properties.Settings.Default.RidgeWidth;
                            if (RadioButtonHammerBeam.Checked)
                                KPBRidge.postWidth = TimberDraw.Properties.Settings.Default.HBPostWidth;
                            if (RadioButtonKingPostBent.Checked)
                                KPBRidge.postWidth = TimberDraw.Properties.Settings.Default.KPPostWidth;
                            if (RadioButtonQueenPostBent.Checked)
                                KPBRidge.postWidth = TimberDraw.Properties.Settings.Default.QPPostWidth;
                            if (RadioButtonKingPostTruss.Checked)
                                KPBRidge.postWidth = TimberDraw.Properties.Settings.Default.KPTGirtWidth;
                            if (RadioButtonQueenPostTruss.Checked)
                                KPBRidge.postWidth = TimberDraw.Properties.Settings.Default.QPTGirtWidth;
                            KPBRidge.Length = TimberDraw.Properties.Settings.Default.BayWidth;
                            KPBRidge.Make3d = CheckBoxMake3D.Checked;
                            KPBRidge.Draw();
                            if (Module1.HasJoinery)
                            {
                                if (RadioButtonHammerBeam.Checked)
                                {
                                    if (HB.HasKPost) {
                                        HB.HBKP.AddMortise(KPBRidge.Housing1Id); Module1.DeleteJoint(KPBRidge.Housing1aId); Module1.DeleteJoint(KPBRidge.RidgeExtension1_ID); Module1.DeleteJoint(KPBRidge.RidgeExtension2Id);
                                        Module1.AddConnection(HB.HBKP.TimberId, KPBRidge.TimberId, Module1.JointType.Mortise);
                                        Module1.AddConnection(KPBRidge.TimberId, HB.HBKP.TimberId, Module1.JointType.Tenon);
                                    }
                                }
                                if (RadioButtonKingPostBent.Checked)
                                {
                                    if (KPB.HasKPost) {
                                        KPB.KP.AddMortise(KPBRidge.Housing1Id); Module1.DeleteJoint(KPBRidge.Housing1aId); Module1.DeleteJoint(KPBRidge.RidgeExtension1_ID); Module1.DeleteJoint(KPBRidge.RidgeExtension2Id);
                                        Module1.AddConnection(KPB.KP.TimberId, KPBRidge.TimberId, Module1.JointType.Mortise);
                                        Module1.AddConnection(KPBRidge.TimberId, KPB.KP.TimberId, Module1.JointType.Tenon);
                                    }
                                }
                                if (RadioButtonQueenPostBent.Checked)
                                {
                                    if (QPB.HasRafter) {
                                        QPB.QPRLeft.AddMortise(KPBRidge.Housing1Id); QPB.QPRRight.AddMortise(KPBRidge.Housing1aId); Module1.DeleteJoint(KPBRidge.RidgeExtension1_ID); Module1.DeleteJoint(KPBRidge.RidgeExtension2Id);
                                        Module1.AddConnection(QPB.QPRLeft.TimberId, KPBRidge.TimberId, Module1.JointType.Mortise);
                                        Module1.AddConnection(QPB.QPRRight.TimberId, KPBRidge.TimberId, Module1.JointType.Mortise);
                                        Module1.AddConnection(KPBRidge.TimberId, QPB.QPRLeft.TimberId, Module1.JointType.Tenon);
                                        Module1.AddConnection(KPBRidge.TimberId, QPB.QPRRight.TimberId, Module1.JointType.Tenon);
                                    }
                                }
                                // Queue far-end ridge housing for the next bent
                                if (RadioButtonHammerBeam.Checked || RadioButtonKingPostBent.Checked)
                                {
                                    Module1.PendingKPostMortises.Add((KPBRidge.Housing2Id, KPBRidge.TimberId));
                                    Module1.DeleteJoint(KPBRidge.Housing2aId);
                                }
                                else if (RadioButtonQueenPostBent.Checked)
                                {
                                    Module1.PendingLeftRafterMortises.Add((KPBRidge.Housing2Id,  KPBRidge.TimberId));
                                    Module1.PendingRightRafterMortises.Add((KPBRidge.Housing2aId, KPBRidge.TimberId));
                                }
                                else
                                {
                                    // Truss types: no traditional kpost/rafter to receive the far-end housing
                                    Module1.DeleteJoint(KPBRidge.Housing2Id);
                                    Module1.DeleteJoint(KPBRidge.Housing2aId);
                                }
                            }
                        }

                        if (Module1.Make3D & CheckBoxHasRidgeBrace.Checked)
                        {
                            BayBrace KPRBrace = new();
                            double postWidth = 0;
                            double postDepth = 0;
                            if (RadioButtonHammerBeam.Checked) { postWidth = TimberDraw.Properties.Settings.Default.HBPostWidth; postDepth = TimberDraw.Properties.Settings.Default.HBPostDepth; }
                            if (RadioButtonKingPostBent.Checked) { postWidth = TimberDraw.Properties.Settings.Default.KPPostWidth; postDepth = TimberDraw.Properties.Settings.Default.KPPostDepth; }
                            if (RadioButtonQueenPostBent.Checked) { postWidth = TimberDraw.Properties.Settings.Default.QPPostWidth; postDepth = TimberDraw.Properties.Settings.Default.QPPostDepth; }
                            if (RadioButtonKingPostTruss.Checked) { postWidth = TimberDraw.Properties.Settings.Default.KPTGirtWidth; postDepth = TimberDraw.Properties.Settings.Default.KPTGirtDepth; }
                            if (RadioButtonKingPostTruss.Checked) { postWidth = TimberDraw.Properties.Settings.Default.QPTGirtWidth; postDepth = TimberDraw.Properties.Settings.Default.QPTGirtDepth; }
                            var RidgeWidth = TimberDraw.Properties.Settings.Default.RidgeWidth;
                            var RidgeDepth = TimberDraw.Properties.Settings.Default.RidgeDepth;
                            var BayWidth = TimberDraw.Properties.Settings.Default.BayWidth;
                            KPRBrace.Width = TimberDraw.Properties.Settings.Default.RidgeBraceWidth;
                            KPRBrace.Depth = TimberDraw.Properties.Settings.Default.RidgeBraceDepth;
                            KPRBrace.Length = TimberDraw.Properties.Settings.Default.RidgeBraceLength;
                            KPRBrace.YAngle = 270;
                            KPRBrace.ZAngle = 0;
                            KPRBrace.StartPoint = new Point3d((Module1.Span / 2) + (KPRBrace.Width / 2), (Module1.EaveHt + (((Module1.Span / 2) - (RidgeWidth / 2)) * Module1.Pitch)) - (RidgeDepth - 1), postWidth);

                            KPRBrace.Peg1Length = postDepth + 1.5;
                            KPRBrace.Peg2Length = TimberDraw.Properties.Settings.Default.RidgeWidth + 1.5;
                            KPRBrace.Peg1Z = postWidth - ((postDepth / 2 - KPRBrace.Width / 2) + 0.75);
                            KPRBrace.Peg2Z = postWidth - ((RidgeWidth / 2 - KPRBrace.Width / 2) + 0.75);

                            KPRBrace.Draw();
                            if (Module1.HasJoinery)
                            {
                                if (RadioButtonHammerBeam.Checked & HB.HasKPost) {
                                    HB.HBKP.AddMortise(KPRBrace.TenonUp);
                                    Module1.AddConnection(HB.HBKP.TimberId, KPRBrace.Timber, Module1.JointType.Mortise);
                                    Module1.AddConnection(KPRBrace.Timber, HB.HBKP.TimberId, Module1.JointType.Tenon);
                                }
                                if (RadioButtonKingPostBent.Checked & KPB.HasKPost) {
                                    KPB.KP.AddMortise(KPRBrace.TenonUp);
                                    Module1.AddConnection(KPB.KP.TimberId, KPRBrace.Timber, Module1.JointType.Mortise);
                                    Module1.AddConnection(KPRBrace.Timber, KPB.KP.TimberId, Module1.JointType.Tenon);
                                }
                                if (CheckBoxHasRidge.Checked) {
                                    KPBRidge.AddMortise(KPRBrace.TenonDown);
                                    Module1.AddConnection(KPBRidge.TimberId, KPRBrace.Timber, Module1.JointType.Mortise);
                                    Module1.AddConnection(KPRBrace.Timber, KPBRidge.TimberId, Module1.JointType.Tenon);
                                }
                            }
                            KPRBrace.YAngle = 90;
                            KPRBrace.ZAngle = 0;
                            KPRBrace.StartPoint = new Point3d((Module1.Span / 2) - (KPRBrace.Width / 2), (Module1.EaveHt + (((Module1.Span / 2) - (RidgeWidth / 2)) * Module1.Pitch)) - (RidgeDepth - 1), BayWidth + postWidth);

                            KPRBrace.Peg1Z = (BayWidth + postWidth) - ((postDepth / 2 - KPRBrace.Width / 2) + 0.75);
                            KPRBrace.Peg2Z = (BayWidth + postWidth) - ((RidgeWidth / 2 - KPRBrace.Width / 2) + 0.75);

                            KPRBrace.Draw();
                            if (Module1.HasJoinery)
                            {
                                if (CheckBoxHasRidge.Checked) {
                                    KPBRidge.AddMortise(KPRBrace.TenonDown);
                                    Module1.AddConnection(KPBRidge.TimberId, KPRBrace.Timber, Module1.JointType.Mortise);
                                    Module1.AddConnection(KPRBrace.Timber, KPBRidge.TimberId, Module1.JointType.Tenon);
                                }
                            }
                        }

                        EaveGirt EGirt = new();
                        if (CheckBoxHasEaveGirt.Checked)
                        {
                            double postWidth = 0;
                            double postDepth = 0;
                            var EaveGirtWidth = TimberDraw.Properties.Settings.Default.EaveGirtWidth;
                            var EaveGirtDepth = TimberDraw.Properties.Settings.Default.EaveGirtDepth;
                            var EaveGirtBraceLength = TimberDraw.Properties.Settings.Default.EaveGirtBraceLength;
                            var BayWidth = TimberDraw.Properties.Settings.Default.BayWidth;
                            if (RadioButtonHammerBeam.Checked) { postWidth = TimberDraw.Properties.Settings.Default.HBPostWidth; postDepth = TimberDraw.Properties.Settings.Default.HBPostDepth; }
                            if (RadioButtonKingPostBent.Checked) { postWidth = TimberDraw.Properties.Settings.Default.KPPostWidth; postDepth = TimberDraw.Properties.Settings.Default.KPPostDepth; }
                            if (RadioButtonQueenPostBent.Checked) { postWidth = TimberDraw.Properties.Settings.Default.QPPostWidth; postDepth = TimberDraw.Properties.Settings.Default.QPPostDepth; }
                            if (RadioButtonKingPostTruss.Checked) { postWidth = TimberDraw.Properties.Settings.Default.KPTGirtWidth; postDepth = TimberDraw.Properties.Settings.Default.KPTGirtDepth; }
                            if (RadioButtonKingPostTruss.Checked) { postWidth = TimberDraw.Properties.Settings.Default.QPTGirtWidth; postDepth = TimberDraw.Properties.Settings.Default.QPTGirtDepth; }
                            EGirt.Depth = TimberDraw.Properties.Settings.Default.EaveGirtDepth;
                            EGirt.Width = TimberDraw.Properties.Settings.Default.EaveGirtWidth;
                            EGirt.postWidth = postWidth;
                            EGirt.postDepth = postDepth;
                            EGirt.Length = TimberDraw.Properties.Settings.Default.BayWidth;
                            EGirt.Make3d = Module1.Make3D;
                            EGirt.HasBayFlrGirt = CheckBoxHasFlrGirt.Checked;
                            EGirt.Draw();
                            if (Module1.HasJoinery)
                            {
                                if (RadioButtonHammerBeam.Checked & HB.HasPost) {
                                    HB.PLeft.AddMortise(EGirt.TenonLeftId); HB.PRight.AddMortise(EGirt.TenonRightId);
                                    Module1.AddConnection(HB.PLeft.TimberId, EGirt.TimberLeftId, Module1.JointType.Mortise);
                                    Module1.AddConnection(HB.PRight.TimberId, EGirt.TimberRightId, Module1.JointType.Mortise);
                                    Module1.AddConnection(EGirt.TimberLeftId, HB.PLeft.TimberId, Module1.JointType.Tenon);
                                    Module1.AddConnection(EGirt.TimberRightId, HB.PRight.TimberId, Module1.JointType.Tenon);
                                }
                                if (RadioButtonKingPostBent.Checked & KPB.HasPost) {
                                    KPB.PLeft.AddMortise(EGirt.TenonLeftId); KPB.PRight.AddMortise(EGirt.TenonRightId);
                                    Module1.AddConnection(KPB.PLeft.TimberId, EGirt.TimberLeftId, Module1.JointType.Mortise);
                                    Module1.AddConnection(KPB.PRight.TimberId, EGirt.TimberRightId, Module1.JointType.Mortise);
                                    Module1.AddConnection(EGirt.TimberLeftId, KPB.PLeft.TimberId, Module1.JointType.Tenon);
                                    Module1.AddConnection(EGirt.TimberRightId, KPB.PRight.TimberId, Module1.JointType.Tenon);
                                }
                                if (RadioButtonQueenPostBent.Checked & KPB.HasPost) {
                                    QPB.PLeft.AddMortise(EGirt.TenonLeftId); QPB.PRight.AddMortise(EGirt.TenonRightId);
                                    Module1.AddConnection(QPB.PLeft.TimberId, EGirt.TimberLeftId, Module1.JointType.Mortise);
                                    Module1.AddConnection(QPB.PRight.TimberId, EGirt.TimberRightId, Module1.JointType.Mortise);
                                    Module1.AddConnection(EGirt.TimberLeftId, QPB.PLeft.TimberId, Module1.JointType.Tenon);
                                    Module1.AddConnection(EGirt.TimberRightId, QPB.PRight.TimberId, Module1.JointType.Tenon);
                                }
                                // Queue far-end tenons for the next bent's posts
                                Module1.PendingLeftPostMortises.Add((EGirt.TenonLeftFarId, EGirt.TimberLeftId));
                                Module1.PendingRightPostMortises.Add((EGirt.TenonRightFarId, EGirt.TimberRightId));
                            }
                        }

                        if (Module1.Make3D & CheckBoxHasEaveGirtBrace.Checked)
                        {
                            BayBrace EBrace = new();
                            double postWidth = 0;
                            double postDepth = 0;
                            var EaveGirtWidth = TimberDraw.Properties.Settings.Default.EaveGirtWidth;
                            var EaveGirtDepth = TimberDraw.Properties.Settings.Default.EaveGirtDepth;
                            var BayWidth = TimberDraw.Properties.Settings.Default.BayWidth;
                            if (RadioButtonHammerBeam.Checked) { postWidth = TimberDraw.Properties.Settings.Default.HBPostWidth; postDepth = TimberDraw.Properties.Settings.Default.HBPostDepth; }
                            if (RadioButtonKingPostBent.Checked) { postWidth = TimberDraw.Properties.Settings.Default.KPPostWidth; postDepth = TimberDraw.Properties.Settings.Default.KPPostDepth; }
                            if (RadioButtonQueenPostBent.Checked) { postWidth = TimberDraw.Properties.Settings.Default.QPPostWidth; postDepth = TimberDraw.Properties.Settings.Default.QPPostDepth; }
                            if (RadioButtonKingPostTruss.Checked) { postWidth = TimberDraw.Properties.Settings.Default.KPTGirtWidth; postDepth = TimberDraw.Properties.Settings.Default.KPTGirtDepth; }
                            if (RadioButtonKingPostTruss.Checked) { postWidth = TimberDraw.Properties.Settings.Default.QPTGirtWidth; postDepth = TimberDraw.Properties.Settings.Default.QPTGirtDepth; }
                            //Leftside Bent Leftside Bay
                            EBrace.Width = TimberDraw.Properties.Settings.Default.EaveGirtBraceWidth;
                            EBrace.Depth = TimberDraw.Properties.Settings.Default.EaveGirtBraceDepth;
                            EBrace.Length = TimberDraw.Properties.Settings.Default.EaveGirtBraceLength;
                            EBrace.YAngle = 270;
                            EBrace.ZAngle = 0;
                            EBrace.StartPoint = new Point3d(EBrace.Width, Module1.EaveHt - EaveGirtDepth + 1, postWidth);
                            EBrace.Peg1Length = postDepth + 1.5;
                            EBrace.Peg2Length = TimberDraw.Properties.Settings.Default.EaveGirtWidth + 1.5;
                            EBrace.Peg1Z = postWidth - ((postDepth - EBrace.Width) + 0.75);
                            EBrace.Peg2Z = postWidth - ((EaveGirtWidth - EBrace.Width) + 0.75);
                            EBrace.Draw();
                            if (Module1.HasJoinery)
                            {
                                if (RadioButtonHammerBeam.Checked & HB.HasPost) {
                                    HB.PLeft.AddMortise(EBrace.TenonUp);
                                    Module1.AddConnection(HB.PLeft.TimberId, EBrace.Timber, Module1.JointType.Mortise);
                                    Module1.AddConnection(EBrace.Timber, HB.PLeft.TimberId, Module1.JointType.Tenon);
                                }
                                if (RadioButtonKingPostBent.Checked & KPB.HasPost) {
                                    KPB.PLeft.AddMortise(EBrace.TenonUp);
                                    Module1.AddConnection(KPB.PLeft.TimberId, EBrace.Timber, Module1.JointType.Mortise);
                                    Module1.AddConnection(EBrace.Timber, KPB.PLeft.TimberId, Module1.JointType.Tenon);
                                }
                                if (RadioButtonQueenPostBent.Checked & QPB.HasPost) {
                                    QPB.PLeft.AddMortise(EBrace.TenonUp);
                                    Module1.AddConnection(QPB.PLeft.TimberId, EBrace.Timber, Module1.JointType.Mortise);
                                    Module1.AddConnection(EBrace.Timber, QPB.PLeft.TimberId, Module1.JointType.Tenon);
                                }
                                if (CheckBoxHasEaveGirt.Checked) {
                                    EGirt.AddMortise(EBrace.TenonDown, EaveGirt.Side.Left);
                                    Module1.AddConnection(EGirt.TimberLeftId, EBrace.Timber, Module1.JointType.Mortise);
                                    Module1.AddConnection(EBrace.Timber, EGirt.TimberLeftId, Module1.JointType.Tenon);
                                }
                            }

                            //Leftside Bent RightSide Bay
                            EBrace.YAngle = 270;
                            EBrace.ZAngle = 0;
                            EBrace.XAngle = 90;
                            EBrace.StartPoint = new Point3d(EBrace.Width, Module1.EaveHt - EaveGirtDepth + 1, BayWidth + postWidth);
                            EBrace.Peg1Length = TimberDraw.Properties.Settings.Default.EaveGirtWidth + 1.5;
                            EBrace.Peg2Length = postDepth + 1.5;
                            EBrace.Peg1Z = (BayWidth + postWidth) - ((EaveGirtWidth - EBrace.Width) + 0.75);
                            EBrace.Peg2Z = (BayWidth + postWidth) - ((postDepth - EBrace.Width) + 0.75);
                            EBrace.Draw();
                            if (Module1.HasJoinery & CheckBoxHasEaveGirt.Checked) {
                                EGirt.AddMortise(EBrace.TenonUp, EaveGirt.Side.Left);
                                Module1.AddConnection(EGirt.TimberLeftId, EBrace.Timber, Module1.JointType.Mortise);
                                Module1.AddConnection(EBrace.Timber, EGirt.TimberLeftId, Module1.JointType.Tenon);
                            }
                            // TenonDown is the post end at the far bent
                            if (Module1.HasJoinery)
                                Module1.PendingLeftPostMortises.Add((EBrace.TenonDown, EBrace.Timber));

                            //Rightside Bent Leftside Bay Good
                            EBrace.YAngle = 90;
                            EBrace.ZAngle = 0;
                            EBrace.XAngle = 270;
                            EBrace.StartPoint = new Point3d(Module1.Span - EBrace.Width, Module1.EaveHt - EaveGirtDepth + 1, postWidth);
                            EBrace.Peg1Length = TimberDraw.Properties.Settings.Default.EaveGirtWidth + 1.5;
                            EBrace.Peg2Length = postDepth + 1.5;
                            EBrace.Peg1Z = postWidth - ((EaveGirtWidth - EBrace.Width) + 0.75);
                            EBrace.Peg2Z = postWidth - ((postDepth - EBrace.Width) + 0.75);
                            EBrace.Draw();
                            if (Module1.HasJoinery)
                            {
                                if (RadioButtonHammerBeam.Checked & HB.HasPost) {
                                    HB.PRight.AddMortise(EBrace.TenonDown);
                                    Module1.AddConnection(HB.PRight.TimberId, EBrace.Timber, Module1.JointType.Mortise);
                                    Module1.AddConnection(EBrace.Timber, HB.PRight.TimberId, Module1.JointType.Tenon);
                                }
                                if (RadioButtonKingPostBent.Checked & KPB.HasPost) {
                                    KPB.PRight.AddMortise(EBrace.TenonDown);
                                    Module1.AddConnection(KPB.PRight.TimberId, EBrace.Timber, Module1.JointType.Mortise);
                                    Module1.AddConnection(EBrace.Timber, KPB.PRight.TimberId, Module1.JointType.Tenon);
                                }
                                if (RadioButtonQueenPostBent.Checked & QPB.HasPost) {
                                    QPB.PRight.AddMortise(EBrace.TenonDown);
                                    Module1.AddConnection(QPB.PRight.TimberId, EBrace.Timber, Module1.JointType.Mortise);
                                    Module1.AddConnection(EBrace.Timber, QPB.PRight.TimberId, Module1.JointType.Tenon);
                                }
                                if (CheckBoxHasEaveGirt.Checked) {
                                    EGirt.AddMortise(EBrace.TenonUp, EaveGirt.Side.Right);
                                    Module1.AddConnection(EGirt.TimberRightId, EBrace.Timber, Module1.JointType.Mortise);
                                    Module1.AddConnection(EBrace.Timber, EGirt.TimberRightId, Module1.JointType.Tenon);
                                }
                            }
                            //Rightside Bent Leftside Bay
                            EBrace.StartPoint = new Point3d(Module1.Span - EBrace.Width, Module1.EaveHt - EaveGirtDepth + 1, BayWidth + postWidth);
                            EBrace.YAngle = 90;
                            EBrace.ZAngle = 0;
                            EBrace.XAngle = 0;
                            EBrace.Peg1Length = postDepth + 1.5;
                            EBrace.Peg2Length = TimberDraw.Properties.Settings.Default.EaveGirtWidth + 1.5;
                            EBrace.Peg1Z = (BayWidth + postWidth) - ((postDepth - EBrace.Width) + 0.75);
                            EBrace.Peg2Z = (BayWidth + postWidth) - ((EaveGirtWidth - EBrace.Width) + 0.75);
                            EBrace.Draw();
                            if (Module1.HasJoinery & CheckBoxHasEaveGirt.Checked) {
                                EGirt.AddMortise(EBrace.TenonDown, EaveGirt.Side.Right);
                                Module1.AddConnection(EGirt.TimberRightId, EBrace.Timber, Module1.JointType.Mortise);
                                Module1.AddConnection(EBrace.Timber, EGirt.TimberRightId, Module1.JointType.Tenon);
                            }
                            // TenonUp is the post end at the far bent
                            if (Module1.HasJoinery)
                                Module1.PendingRightPostMortises.Add((EBrace.TenonUp, EBrace.Timber));
                        }

                        if (CheckBoxHasPurlins.Checked)
                        {
                            double postWidth = 0;
                            double rafterDepth = 0;
                            var purlinWidth = TimberDraw.Properties.Settings.Default.PurlinWidth;
                            var purlinDepth = TimberDraw.Properties.Settings.Default.PurlinDepth;
                            var BayWidth = TimberDraw.Properties.Settings.Default.BayWidth;
                            if (RadioButtonHammerBeam.Checked) {
                                postWidth = TimberDraw.Properties.Settings.Default.HBRafterWidth;
                                rafterDepth = TimberDraw.Properties.Settings.Default.HBRafterDepth;
                            }
                            if (RadioButtonKingPostBent.Checked) {
                                postWidth = TimberDraw.Properties.Settings.Default.KPRafterWidth;
                                rafterDepth = TimberDraw.Properties.Settings.Default.KPRafterDepth;
                            }
                            if (RadioButtonQueenPostBent.Checked) {
                                postWidth = TimberDraw.Properties.Settings.Default.QPRafterWidth;
                                rafterDepth = TimberDraw.Properties.Settings.Default.QPRafterDepth;
                            }
                            if (RadioButtonKingPostTruss.Checked) {
                                postWidth = TimberDraw.Properties.Settings.Default.KPTRafterWidth;
                                rafterDepth = TimberDraw.Properties.Settings.Default.KPTRafterDepth;
                            }
                            if (RadioButtonQueenPostTruss.Checked) {
                                postWidth = TimberDraw.Properties.Settings.Default.QPTRafterWidth;
                                rafterDepth = TimberDraw.Properties.Settings.Default.QPTRafterDepth;
                            }
                            Purlins KPBPurlins = new(purlinDepth, purlinWidth, postWidth, rafterDepth, BayWidth, Module1.Make3D);
                            if (Module1.HasJoinery)
                            {
                                if (RadioButtonHammerBeam.Checked & HB.HasRafter)
                                {
                                    foreach (object ObjId_loopVariable in KPBPurlins.ExtensionLeftCol)
                                    {
                                        var ObjId = ObjId_loopVariable;
                                        HB.HBRLeft.AddMortise((ObjectId)ObjId);
                                    }
                                    foreach (object ObjId_loopVariable in KPBPurlins.ExtensionRightCol)
                                    {
                                        var ObjId = ObjId_loopVariable;
                                        HB.HBRRight.AddMortise((ObjectId)ObjId);
                                    }
                                }
                                if (RadioButtonKingPostBent.Checked & KPB.HasRafter)
                                {
                                    foreach (object ObjId_loopVariable in KPBPurlins.ExtensionLeftCol)
                                    {
                                        var ObjId = ObjId_loopVariable;
                                        KPB.RLeft.AddMortise((ObjectId)ObjId);
                                    }
                                    foreach (object ObjId_loopVariable in KPBPurlins.ExtensionRightCol)
                                    {
                                        var ObjId = ObjId_loopVariable;
                                        KPB.RRight.AddMortise((ObjectId)ObjId);
                                    }
                                }
                                if (RadioButtonQueenPostBent.Checked & QPB.HasRafter)
                                {
                                    foreach (object ObjId_loopVariable in KPBPurlins.ExtensionLeftCol)
                                    {
                                        var ObjId = ObjId_loopVariable;
                                        QPB.QPRLeft.AddMortise((ObjectId)ObjId);
                                    }
                                    foreach (object ObjId_loopVariable in KPBPurlins.ExtensionRightCol)
                                    {
                                        var ObjId = ObjId_loopVariable;
                                        QPB.QPRRight.AddMortise((ObjectId)ObjId);
                                    }
                                }
                                // Queue far-end dovetail extensions for the next bent's rafters
                                foreach (var pair in KPBPurlins.ExtensionLeftFarList)
                                    Module1.PendingLeftRafterMortises.Add((pair.ExtId, pair.PurlinId));
                                foreach (var pair in KPBPurlins.ExtensionRightFarList)
                                    Module1.PendingRightRafterMortises.Add((pair.ExtId, pair.PurlinId));
                            }
                        }

                        if (CheckBoxHasCommons.Checked)
                        {
                            double postDepth = 0;
                            double postWidth = 0;
                            var commonWidth = TimberDraw.Properties.Settings.Default.CommonWidth;
                            var commonDepth = TimberDraw.Properties.Settings.Default.CommonDepth;
                            var RidgeWidth = TimberDraw.Properties.Settings.Default.RidgeWidth;
                            var EaveGirtWidth = TimberDraw.Properties.Settings.Default.EaveGirtWidth;
                            var BayWidth = TimberDraw.Properties.Settings.Default.BayWidth;
                            if (RadioButtonHammerBeam.Checked) { postDepth = TimberDraw.Properties.Settings.Default.HBPostDepth; postWidth = TimberDraw.Properties.Settings.Default.HBPostWidth; }
                            if (RadioButtonKingPostBent.Checked) { postDepth = TimberDraw.Properties.Settings.Default.KPPostDepth; postWidth = TimberDraw.Properties.Settings.Default.KPPostWidth; }
                            if (RadioButtonQueenPostBent.Checked) { postDepth = TimberDraw.Properties.Settings.Default.QPPostDepth; postWidth = TimberDraw.Properties.Settings.Default.QPPostWidth; }
                            if (RadioButtonKingPostTruss.Checked) { postDepth = TimberDraw.Properties.Settings.Default.KPTGirtDepth; postWidth = TimberDraw.Properties.Settings.Default.KPTGirtWidth; }
                            if (RadioButtonKingPostTruss.Checked) { postDepth = TimberDraw.Properties.Settings.Default.QPTGirtDepth; postWidth = TimberDraw.Properties.Settings.Default.QPTGirtWidth; }
                            Commons Commons = new(commonDepth, commonWidth, postWidth, postDepth, RidgeWidth, EaveGirtWidth, BayWidth, Module1.Make3D, CheckBoxHasRidge.Checked);
                            if (Module1.HasJoinery)
                            {
                                if (CheckBoxHasEaveGirt.Checked)
                                {
                                    foreach (object ObjId_loopVariable in Commons.EaveHousingLeft)
                                    {
                                        var ObjId = ObjId_loopVariable;
                                        EGirt.AddMortise((ObjectId)ObjId, EaveGirt.Side.Left);
                                    }
                                    foreach (object ObjId_loopVariable in Commons.EaveHousingRight)
                                    {
                                        var ObjId = ObjId_loopVariable;
                                        EGirt.AddMortise((ObjectId)ObjId, EaveGirt.Side.Right);
                                    }
                                }
                                if (CheckBoxHasRidge.Checked)
                                {
                                    foreach (object ObjId_loopVariable in Commons.RidgeHousingLeft)
                                    {
                                        var ObjId = ObjId_loopVariable;
                                        KPBRidge.AddMortise((ObjectId)ObjId);
                                    }
                                    foreach (object ObjId_loopVariable in Commons.RidgeHousingRight)
                                    {
                                        var ObjId = ObjId_loopVariable;
                                        KPBRidge.AddMortise((ObjectId)ObjId);
                                    }
                                }
                            }
                        }

                        FloorBayGirt FlrBayGirt = new();
                        if (CheckBoxHasFlrGirt.Checked)
                        {
                            double postDepth = 0;
                            double postWidth = 0;
                            var BayWidth = TimberDraw.Properties.Settings.Default.BayWidth;
                            if (RadioButtonHammerBeam.Checked) { postDepth = TimberDraw.Properties.Settings.Default.HBPostDepth; postWidth = TimberDraw.Properties.Settings.Default.HBPostWidth; }
                            if (RadioButtonKingPostBent.Checked) { postDepth = TimberDraw.Properties.Settings.Default.KPPostDepth; postWidth = TimberDraw.Properties.Settings.Default.KPPostWidth; }
                            if (RadioButtonQueenPostBent.Checked) { postDepth = TimberDraw.Properties.Settings.Default.QPPostDepth; postWidth = TimberDraw.Properties.Settings.Default.QPPostWidth; }
                            if (RadioButtonKingPostTruss.Checked) { postDepth = TimberDraw.Properties.Settings.Default.KPTGirtDepth; postWidth = TimberDraw.Properties.Settings.Default.KPTGirtWidth; }
                            if (RadioButtonQueenPostTruss.Checked) { postDepth = TimberDraw.Properties.Settings.Default.QPTGirtDepth; postWidth = TimberDraw.Properties.Settings.Default.QPTGirtWidth; }
                            FlrBayGirt.Width = TimberDraw.Properties.Settings.Default.FlrGirtWidth;
                            FlrBayGirt.Depth = TimberDraw.Properties.Settings.Default.FlrGirtDepth;
                            FlrBayGirt.Height = TimberDraw.Properties.Settings.Default.FlrGirtHt;
                            FlrBayGirt.Length = BayWidth;
                            FlrBayGirt.postWidth = postWidth;
                            if (RadioButton1Hbeam.Checked)
                                FlrBayGirt.HammerBeamType = 0;
                            if (RadioButton2Hbeam.Checked)
                                FlrBayGirt.HammerBeamType = 1;
                            FlrBayGirt.Type = "Girt";
                            FlrBayGirt.BentNumber = ((char)(Module1.BentWallNumber)).ToString();
                            FlrBayGirt.Designation = Module1.Arabic2roman(Properties.Settings.Default.BentNumber) + "-" + Module1.Arabic2roman(Properties.Settings.Default.BentNumber + 1);
                            FlrBayGirt.Startpoint = new Point3d(0, FlrBayGirt.Height, postWidth);
                            FlrBayGirt.Draw();
                            if (Module1.HasJoinery)
                            {
                                if (RadioButtonHammerBeam.Checked & HB.HasPost) {
                                    HB.PLeft.AddMortise(FlrBayGirt.TenonLeft1Id); HB.PRight.AddMortise(FlrBayGirt.TenonRight1Id);
                                    Module1.AddConnection(HB.PLeft.TimberId, FlrBayGirt.TimberLeftId, Module1.JointType.Mortise);
                                    Module1.AddConnection(HB.PRight.TimberId, FlrBayGirt.TimberRightId, Module1.JointType.Mortise);
                                    Module1.AddConnection(FlrBayGirt.TimberLeftId, HB.PLeft.TimberId, Module1.JointType.Tenon);
                                    Module1.AddConnection(FlrBayGirt.TimberRightId, HB.PRight.TimberId, Module1.JointType.Tenon);
                                }
                                if (RadioButtonKingPostBent.Checked & KPB.HasPost) {
                                    KPB.PLeft.AddMortise(FlrBayGirt.TenonLeft1Id); KPB.PRight.AddMortise(FlrBayGirt.TenonRight1Id);
                                    Module1.AddConnection(KPB.PLeft.TimberId, FlrBayGirt.TimberLeftId, Module1.JointType.Mortise);
                                    Module1.AddConnection(KPB.PRight.TimberId, FlrBayGirt.TimberRightId, Module1.JointType.Mortise);
                                    Module1.AddConnection(FlrBayGirt.TimberLeftId, KPB.PLeft.TimberId, Module1.JointType.Tenon);
                                    Module1.AddConnection(FlrBayGirt.TimberRightId, KPB.PRight.TimberId, Module1.JointType.Tenon);
                                }
                                if (RadioButtonQueenPostBent.Checked & QPB.HasPost) {
                                    QPB.PLeft.AddMortise(FlrBayGirt.TenonLeft1Id); QPB.PRight.AddMortise(FlrBayGirt.TenonRight1Id);
                                    Module1.AddConnection(QPB.PLeft.TimberId, FlrBayGirt.TimberLeftId, Module1.JointType.Mortise);
                                    Module1.AddConnection(QPB.PRight.TimberId, FlrBayGirt.TimberRightId, Module1.JointType.Mortise);
                                    Module1.AddConnection(FlrBayGirt.TimberLeftId, QPB.PLeft.TimberId, Module1.JointType.Tenon);
                                    Module1.AddConnection(FlrBayGirt.TimberRightId, QPB.PRight.TimberId, Module1.JointType.Tenon);
                                }
                                // Queue far-end tenons for the next bent's posts
                                Module1.PendingLeftPostMortises.Add((FlrBayGirt.TenonLeft2Id, FlrBayGirt.TimberLeftId));
                                Module1.PendingRightPostMortises.Add((FlrBayGirt.TenonRight2Id, FlrBayGirt.TimberRightId));
                            }
                        }

                        if (CheckBoxHasFlrGirtBrace.Checked)
                        {
                            BayBrace EBrace = new();
                            var FBGHt = TimberDraw.Properties.Settings.Default.FlrGirtHt;
                            var FBGDp = TimberDraw.Properties.Settings.Default.FlrGirtDepth;
                            var FBGWd = TimberDraw.Properties.Settings.Default.FlrGirtWidth;
                            var BayWidth = TimberDraw.Properties.Settings.Default.BayWidth;
                            double postWidth = 0;
                            double postDepth = 0;
                            if (RadioButtonHammerBeam.Checked) { postWidth = TimberDraw.Properties.Settings.Default.HBPostWidth; postDepth = TimberDraw.Properties.Settings.Default.HBPostDepth; }
                            if (RadioButtonKingPostBent.Checked) { postWidth = TimberDraw.Properties.Settings.Default.KPPostWidth; postDepth = TimberDraw.Properties.Settings.Default.KPPostDepth; }
                            if (RadioButtonQueenPostBent.Checked) { postWidth = TimberDraw.Properties.Settings.Default.QPPostWidth; postDepth = TimberDraw.Properties.Settings.Default.QPPostDepth; }
                            if (RadioButtonKingPostTruss.Checked) { postWidth = TimberDraw.Properties.Settings.Default.KPTGirtWidth; postDepth = TimberDraw.Properties.Settings.Default.KPTGirtDepth; }
                            if (RadioButtonQueenPostTruss.Checked) { postWidth = TimberDraw.Properties.Settings.Default.QPTGirtWidth; postDepth = TimberDraw.Properties.Settings.Default.QPTGirtDepth; }
                            EBrace.Width = TimberDraw.Properties.Settings.Default.FlrGirtBraceWidth;
                            EBrace.Depth = TimberDraw.Properties.Settings.Default.FlrGirtBraceDepth;
                            EBrace.Length = TimberDraw.Properties.Settings.Default.FlrGirtBraceLength;
                            EBrace.YAngle = 270;
                            EBrace.ZAngle = 0;
                            EBrace.XAngle = 0;
                            EBrace.StartPoint = new Point3d(EBrace.Width, FBGHt - FBGDp, postWidth);
                            EBrace.Peg1Length = postDepth + 1.5;
                            EBrace.Peg2Length = TimberDraw.Properties.Settings.Default.EaveGirtWidth + 1.5;
                            EBrace.Peg1Z = postWidth - ((postDepth - EBrace.Width) + 0.75);
                            EBrace.Peg2Z = postWidth - ((FBGWd - EBrace.Width) + 0.75);
                            EBrace.Draw();
                            if (Module1.HasJoinery)
                            {
                                if (RadioButtonHammerBeam.Checked & HB.HasPost) {
                                    HB.PLeft.AddMortise(EBrace.TenonUp);
                                    Module1.AddConnection(HB.PLeft.TimberId, EBrace.Timber, Module1.JointType.Mortise);
                                    Module1.AddConnection(EBrace.Timber, HB.PLeft.TimberId, Module1.JointType.Tenon);
                                }
                                if (RadioButtonKingPostBent.Checked & KPB.HasPost) {
                                    KPB.PLeft.AddMortise(EBrace.TenonUp);
                                    Module1.AddConnection(KPB.PLeft.TimberId, EBrace.Timber, Module1.JointType.Mortise);
                                    Module1.AddConnection(EBrace.Timber, KPB.PLeft.TimberId, Module1.JointType.Tenon);
                                }
                                if (RadioButtonQueenPostBent.Checked & QPB.HasPost) {
                                    QPB.PLeft.AddMortise(EBrace.TenonUp);
                                    Module1.AddConnection(QPB.PLeft.TimberId, EBrace.Timber, Module1.JointType.Mortise);
                                    Module1.AddConnection(EBrace.Timber, QPB.PLeft.TimberId, Module1.JointType.Tenon);
                                }
                                if (CheckBoxHasFlrGirt.Checked) {
                                    FlrBayGirt.AddMortise(EBrace.TenonDown, FloorBayGirt.Side.Left);
                                    Module1.AddConnection(FlrBayGirt.TimberLeftId, EBrace.Timber, Module1.JointType.Mortise);
                                    Module1.AddConnection(EBrace.Timber, FlrBayGirt.TimberLeftId, Module1.JointType.Tenon);
                                }
                            }
                            EBrace.YAngle = 270;
                            EBrace.ZAngle = 0;
                            EBrace.XAngle = 90;
                            EBrace.StartPoint = new Point3d(EBrace.Width, FBGHt - FBGDp, postWidth + BayWidth);
                            EBrace.Peg1Length = TimberDraw.Properties.Settings.Default.EaveGirtWidth + 1.5;
                            EBrace.Peg2Length = postDepth + 1.5;
                            EBrace.Peg1Z = (BayWidth + postWidth) - ((FBGWd - EBrace.Width) + 0.75);
                            EBrace.Peg2Z = (BayWidth + postWidth) - ((postDepth - EBrace.Width) + 0.75);
                            EBrace.Draw();
                            if (Module1.HasJoinery & CheckBoxHasFlrGirt.Checked) {
                                FlrBayGirt.AddMortise(EBrace.TenonUp, FloorBayGirt.Side.Left);
                                Module1.AddConnection(FlrBayGirt.TimberLeftId, EBrace.Timber, Module1.JointType.Mortise);
                                Module1.AddConnection(EBrace.Timber, FlrBayGirt.TimberLeftId, Module1.JointType.Tenon);
                            }
                            // TenonDown is the post end at the far bent
                            if (Module1.HasJoinery)
                                Module1.PendingLeftPostMortises.Add((EBrace.TenonDown, EBrace.Timber));
                            EBrace.YAngle = 90;
                            EBrace.ZAngle = 0;
                            EBrace.XAngle = 270;
                            EBrace.StartPoint = new Point3d(Module1.Span - EBrace.Width, FBGHt - FBGDp, postWidth);
                            EBrace.Peg1Length = TimberDraw.Properties.Settings.Default.EaveGirtWidth + 1.5;
                            EBrace.Peg2Length = postDepth + 1.5;
                            EBrace.Peg1Z = postWidth - ((FBGWd - EBrace.Width) + 0.75);
                            EBrace.Peg2Z = postWidth - ((postDepth - EBrace.Width) + 0.75);
                            EBrace.Draw();
                            if (Module1.HasJoinery)
                            {
                                if (RadioButtonHammerBeam.Checked & HB.HasPost) {
                                    HB.PRight.AddMortise(EBrace.TenonDown);
                                    Module1.AddConnection(HB.PRight.TimberId, EBrace.Timber, Module1.JointType.Mortise);
                                    Module1.AddConnection(EBrace.Timber, HB.PRight.TimberId, Module1.JointType.Tenon);
                                }
                                if (RadioButtonKingPostBent.Checked & KPB.HasPost) {
                                    KPB.PRight.AddMortise(EBrace.TenonDown);
                                    Module1.AddConnection(KPB.PRight.TimberId, EBrace.Timber, Module1.JointType.Mortise);
                                    Module1.AddConnection(EBrace.Timber, KPB.PRight.TimberId, Module1.JointType.Tenon);
                                }
                                if (RadioButtonQueenPostBent.Checked & QPB.HasPost) {
                                    QPB.PRight.AddMortise(EBrace.TenonDown);
                                    Module1.AddConnection(QPB.PRight.TimberId, EBrace.Timber, Module1.JointType.Mortise);
                                    Module1.AddConnection(EBrace.Timber, QPB.PRight.TimberId, Module1.JointType.Tenon);
                                }
                                if (CheckBoxHasFlrGirt.Checked) {
                                    FlrBayGirt.AddMortise(EBrace.TenonUp, FloorBayGirt.Side.Right);
                                    Module1.AddConnection(FlrBayGirt.TimberRightId, EBrace.Timber, Module1.JointType.Mortise);
                                    Module1.AddConnection(EBrace.Timber, FlrBayGirt.TimberRightId, Module1.JointType.Tenon);
                                }
                            }
                            EBrace.YAngle = 90;
                            EBrace.ZAngle = 0;
                            EBrace.XAngle = 0;
                            EBrace.StartPoint = new Point3d(Module1.Span - EBrace.Width, FBGHt - FBGDp, postWidth + BayWidth);
                            EBrace.Peg1Length = postDepth + 1.5;
                            EBrace.Peg2Length = TimberDraw.Properties.Settings.Default.EaveGirtWidth + 1.5;
                            EBrace.Peg1Z = (BayWidth + postWidth) - ((postDepth - EBrace.Width) + 0.75);
                            EBrace.Peg2Z = (BayWidth + postWidth) - ((FBGWd - EBrace.Width) + 0.75);
                            EBrace.Draw();
                            if (Module1.HasJoinery & CheckBoxHasFlrGirt.Checked) {
                                FlrBayGirt.AddMortise(EBrace.TenonDown, FloorBayGirt.Side.Right);
                                Module1.AddConnection(FlrBayGirt.TimberRightId, EBrace.Timber, Module1.JointType.Mortise);
                                Module1.AddConnection(EBrace.Timber, FlrBayGirt.TimberRightId, Module1.JointType.Tenon);
                            }
                            // TenonUp is the post end at the far bent
                            if (Module1.HasJoinery)
                                Module1.PendingRightPostMortises.Add((EBrace.TenonUp, EBrace.Timber));
                        }
                    }
                }
                //Bay Members
            }
            Autodesk.AutoCAD.Internal.Utils.PostCommandPrompt();
        }
        private bool DataReady()
        {
            bool ready = true;
            if (Module1.Span == 0)
                ready = false;
            if (Module1.EaveHt == 0)
                ready = false;
            if (Module1.Prun == 0)
                ready = false;
            if (Module1.Prise == 0)
                ready = false;
            if (!ready)
                MessageBox.Show("Error General Data Not ready", "TimberDraw", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return ready;
        }
        # endregion

        #region Eave
        private static bool IsValidDistance(string s)
        {
            try { Converter.StringToDistance(s); return true; }
            catch { return false; }
        }

        private void TextBoxEaveHt_OnLeave(System.Object sender, System.EventArgs e)
        {
            TextBox tb = (TextBox)sender;
            if (IsValidDistance(tb.Text))
            {
                if (tb.Name == "TextBoxEaveHeight")
                {
                    Module1.EaveHt = Converter.StringToDistance(tb.Text, DistanceUnitFormat.Current);
                    TimberDraw.Properties.Settings.Default.EaveHt = Module1.EaveHt;
                }
            }
            else
            {
                MessageBox.Show("Input Error.  Check Units", "TimberDraw", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                tb.Clear();
                tb.Focus();
            }
        }
        private void TextBoxEaveHeight_MouseDown(System.Object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Right)
            {
                Editor ed = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor;
                Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
                PromptDoubleResult res = ed.GetDistance("Pick Eave Height Distance");
                if (res.Status == PromptStatus.OK)
                {
                    TimberDraw.Properties.Settings.Default.EaveHt = res.Value;
                    TextBoxEaveHeight.Text = Converter.DistanceToString(res.Value, DistanceUnitFormat.Current, -1);
                }
            }
        }
        # endregion

        #region Span
        private void TextBoxSpan_OnLeave(Object sender, EventArgs e)
        {
            TextBox tb = (TextBox)sender;
            if (IsValidDistance(tb.Text))
            {
                if (tb.Name == "TextBoxSpan")
                {
                    Module1.Span = Converter.StringToDistance(tb.Text, DistanceUnitFormat.Current);
                    TimberDraw.Properties.Settings.Default.Span = Module1.Span;
                }
            }
            else
            {
                MessageBox.Show("Input Error.  Check Units", "TimberDraw", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                tb.Clear();
                tb.Focus();
            }
        }
        private void TextBoxSpan_MouseDown(System.Object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Right)
            {
                Editor ed = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor;
                Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
                PromptDoubleResult res = ed.GetDistance("Pick Span Distance");
                if (res.Status == PromptStatus.OK)
                {
                    TimberDraw.Properties.Settings.Default.Span = res.Value;
                    TextBoxSpan.Text = Converter.DistanceToString(res.Value, DistanceUnitFormat.Current, -1);
                }
            }
        }
        # endregion

        #region Bay
        private void TextBoxBayWidth_OnLeave(System.Object sender, System.EventArgs e)
        {
            TextBox tb = (TextBox)sender;
            if (IsValidDistance(tb.Text))
            {
                if (tb.Name == "TextBoxBayWidth")
                {
                    Module1.BayWidth = Converter.StringToDistance(tb.Text, DistanceUnitFormat.Current);
                    TimberDraw.Properties.Settings.Default.BayWidth = Module1.BayWidth;
                }
            }
            else
            {
                MessageBox.Show("Input Error.  Check Units", "TimberDraw", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                tb.Clear();
                tb.Focus();
            }
        }
        private void TextBoxBayWidth_MouseDown(System.Object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Right)
            {
                Editor ed = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor;
                Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
                PromptDoubleResult res = ed.GetDistance("Pick Span Distance");
                if (res.Status == PromptStatus.OK)
                {
                    TimberDraw.Properties.Settings.Default.BayWidth = res.Value;
                    TextBoxBayWidth.Text = Converter.DistanceToString(res.Value, DistanceUnitFormat.Current, -1);
                }
            }
        }
        # endregion

        #region Roof Pitch
        private void TextBoxRoofPitchRun_OnLeave(System.Object sender, System.EventArgs e)
        {
            TextBox tb = (TextBox)sender;
            if (IsValidDistance(tb.Text))
            {
                switch (tb.Name)
                {
                    case "TextBoxRoofPitchRun":
                        Module1.Prun = Converter.StringToDistance(tb.Text, DistanceUnitFormat.Current);
                        TimberDraw.Properties.Settings.Default.RoofPitchRun = Module1.Prun;
                        break;
                }
            }
            else
            {
                MessageBox.Show("Input Error.  Check Units", "TimberDraw", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                tb.Clear();
                tb.Focus();
            }
        }
        private void TextBoxRoofPitchRise_OnLeave(System.Object sender, System.EventArgs e)
        {
            TextBox tb = (TextBox)sender;
            if (IsValidDistance(tb.Text))
            {
                switch (tb.Name)
                {
                    case "TextBoxRoofPitchRise":
                        Module1.Prise = Converter.StringToDistance(tb.Text, DistanceUnitFormat.Current);
                        TimberDraw.Properties.Settings.Default.RoofPitchRise = Module1.Prise;
                        break;
                }
            }
            else
            {
                MessageBox.Show("Input Error.  Check Units", "TimberDraw", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                tb.Clear();
                tb.Focus();
            }
        }
        #endregion

        #region Bent Type
        private void BentType_CheckedChanged(System.Object sender, System.EventArgs e)
        {
            RadioButton rbtn = (RadioButton)sender;
            switch (rbtn.Name)
            {
                case "RadioButtonHammerBeam":
                    TabControlSettings.SelectedTab = TabPageHBBentSizes;
                    TimberDraw.Properties.Settings.Default.TrussType = HammerBeamBent;
                    Module1.TrussType = HammerBeamBent;
                    break;
                case "RadioButtonKingPostBent":
                    TabControlSettings.SelectedTab = TabPageKPBentSizes;
                    TimberDraw.Properties.Settings.Default.TrussType = KingPostBent;
                    Module1.TrussType = KingPostBent;
                    break;
                case "RadioButtonQueenPostBent":
                    TabControlSettings.SelectedTab = TabPageQPBentSizes;
                    TimberDraw.Properties.Settings.Default.TrussType = QueenPostBent;
                    Module1.TrussType = QueenPostBent;
                    break;
                case "RadioButtonKingPostTruss":
                    TabControlSettings.SelectedTab = TabPageKPTrussSizes;
                    TimberDraw.Properties.Settings.Default.TrussType = KingPostTruss;
                    Module1.TrussType = KingPostTruss;
                    break;
                case "RadioButtonQueenPostTruss":
                    TabControlSettings.SelectedTab = TabPageQPTrussSizes;
                    TimberDraw.Properties.Settings.Default.TrussType = QueenPostTruss;
                    Module1.TrussType = QueenPostTruss;
                    break;
            }
        }
        private void ButtonHelp_Click(System.Object sender, System.EventArgs e)
        {
            System.Windows.Forms.Help.ShowHelp(this, "TimberDraw.chm");
        }
        private void TabControl_DrawItem(System.Object sender, System.Windows.Forms.DrawItemEventArgs e)
        {
            Graphics g = e.Graphics;
            Brush _TextBrush = null;

            // Get the item from the collection.
            TabPage _TabPage = TabControlSettings.TabPages[e.Index];

            // Get the real bounds for the tab rectangle.
            Rectangle _TabBounds = TabControlSettings.GetTabRect(e.Index);

            if ((e.State == DrawItemState.Selected))
            {
                // Draw a different background color, and don't paint a focus rectangle.
                _TextBrush = new SolidBrush(loLite);
                g.FillRectangle(Brushes.LightGray, e.Bounds);
            }
            else
            {
                _TextBrush = new System.Drawing.SolidBrush(e.ForeColor);
                e.DrawBackground();
            }

            // Use our own font.
            System.Drawing.Font _TabFont = new("Arial", 10, FontStyle.Bold, GraphicsUnit.Pixel);

            // Draw string. Center the text.
            StringFormat _StringFlags = new();
            _StringFlags.Alignment = StringAlignment.Center;
            _StringFlags.LineAlignment = StringAlignment.Center;
            _StringFlags.FormatFlags = StringFormatFlags.DirectionVertical;
            g.DrawString(_TabPage.Text, _TabFont, _TextBrush, _TabBounds, new StringFormat(_StringFlags));

        }
        #endregion

        #region Member Placement
        private void MemberPlacement_CheckedChanged(System.Object sender, System.EventArgs e)
        {
            RadioButton rbtn = (RadioButton)sender;
            switch (rbtn.Name)
            {
                case "RadioButtonFlushToBack":
                    Module1.OffsetType = Module1.Back;
                    TimberDraw.Properties.Settings.Default.BraceStrutPlacement = Module1.Back;
                    break;
                case "RadioButtonCenteredInBent":
                    Module1.OffsetType = Module1.Centered;
                    TimberDraw.Properties.Settings.Default.BraceStrutPlacement = Module1.Centered;
                    break;
                case "RadioButtonFlushToFront":
                    Module1.OffsetType = Module1.Front;
                    TimberDraw.Properties.Settings.Default.BraceStrutPlacement = Module1.Front;
                    break;
            }
        }
        #endregion

        #region Call Out
        private void VScrollBarBentNumber_ValueChanged(System.Object sender, System.EventArgs e)
        {
            TimberDraw.Properties.Settings.Default.BentNumber = 100 - VScrollBarBentNumber.Value;
            TextBoxBentNumber.Text = TimberDraw.Properties.Settings.Default.BentNumber.ToString();
        }
        private void TextBoxBentNumber_TextChanged(System.Object sender, System.EventArgs e)
        {
            TimberDraw.Properties.Settings.Default.BentNumber = Convert.ToInt32(TextBoxBentNumber.Text);
            VScrollBarBentNumber.Value = 100 - TimberDraw.Properties.Settings.Default.BentNumber;
        }
        private void VScrollBarWallNumber_ValueChanged(System.Object sender, System.EventArgs e)
        {
            TextBoxWallNumber.Text = ((char)((VScrollBarWallNumber.Maximum - VScrollBarWallNumber.Value) + 65)).ToString();
        }
        private void TextBoxWallNumber_TextChanged(System.Object sender, System.EventArgs e)
        {
            Module1.BentWallNumber = (int)(TextBoxWallNumber.Text)[0];
            TimberDraw.Properties.Settings.Default.WallNumber = VScrollBarWallNumber.Value;
            TimberDraw.Properties.Settings.Default.WallAlpha = TextBoxWallNumber.Text;
        }
        #endregion

        #region Hammer Beam Bent
        Pen HBPostPen = new(loLite, 2);
        Pen HBHBeamPen = new(loLite, 2);
        Pen HBHPostPen = new(loLite, 2);
        Pen HBKPostPen = new(loLite, 2);
        Pen HBRafterPen = new(loLite, 2);
        Pen HBGirtPen = new(loLite, 2);
        Pen HBBracePen = new(loLite, 2);
        private void TextBoxValiatedOnLeave_HB(System.Object sender, System.EventArgs e)
        {
            TextBox tb = (TextBox)sender;
            if (IsValidDistance(tb.Text))
            {
                switch (tb.Name)
                {
                    case "TextBoxHBPostWidth":
                        TimberDraw.Properties.Settings.Default.HBPostWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxHBPostDepth":
                        TimberDraw.Properties.Settings.Default.HBPostDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxHBeamWidth":
                        TimberDraw.Properties.Settings.Default.HBeamWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxHBeamDepth":
                        TimberDraw.Properties.Settings.Default.HBeamDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxHPostWidth":
                        TimberDraw.Properties.Settings.Default.HPostWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxHPostDepth":
                        TimberDraw.Properties.Settings.Default.HPostDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxHBKPostWidth":
                        TimberDraw.Properties.Settings.Default.HBKPostWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxHBKPostDepth":
                        TimberDraw.Properties.Settings.Default.HBKPostDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxHBRafterWidth":
                        TimberDraw.Properties.Settings.Default.HBRafterWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxHBRafterDepth":
                        TimberDraw.Properties.Settings.Default.HBRafterDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxHBGirtWidth":
                        TimberDraw.Properties.Settings.Default.HBGirtWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxHBGirtDepth":
                        TimberDraw.Properties.Settings.Default.HBGirtDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxHBBraceWidth":
                        TimberDraw.Properties.Settings.Default.HBBraceWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxHBBraceDepth":
                        TimberDraw.Properties.Settings.Default.HBBraceDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxHBBraceLength":
                        TimberDraw.Properties.Settings.Default.HBBraceLength = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxHBBayGirtWidth":
                        TimberDraw.Properties.Settings.Default.HBBayGirtWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxHBBayGirtDepth":
                        TimberDraw.Properties.Settings.Default.HBBayGirtDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxHBBGBraceWidth":
                        TimberDraw.Properties.Settings.Default.HBBGBraceWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxHBBGBraceDepth":
                        TimberDraw.Properties.Settings.Default.HBBGBraceDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxHBBGBraceLength":
                        TimberDraw.Properties.Settings.Default.HBBGBraceLength = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxHBFlrGirtWidth":
                        TimberDraw.Properties.Settings.Default.HBFlrGirtWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxHBFlrGirtDepth":
                        TimberDraw.Properties.Settings.Default.HBFlrGirtDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxHBFlrGirtHt":
                        TimberDraw.Properties.Settings.Default.HBFlrGirtHt = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxHBFlrBraceWidth":
                        TimberDraw.Properties.Settings.Default.HBFlrBraceWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxHBFlrBraceDepth":
                        TimberDraw.Properties.Settings.Default.HBFlrBraceDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxHBFlrBraceLength":
                        TimberDraw.Properties.Settings.Default.HBFlrBraceLength = Convert.ToDouble(tb.Text);
                        break;
                }
            }
            else
            {
                MessageBox.Show("Input Error.  Check Units", "TimberDraw", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                tb.Clear();
                tb.Focus();
            }
        }
        private void CheckBoxHBHas_CheckedChanged(System.Object sender, System.EventArgs e)
        {
            CheckBox cb = (CheckBox)sender;
            switch (cb.Name)
            {
                case "CheckBoxHBHasPost":
                    TimberDraw.Properties.Settings.Default.HBHasPost = CheckBoxHBHasPost.Checked;
                    break;
                case "CheckBoxHBHasHBeam":
                    TimberDraw.Properties.Settings.Default.HBHasHbeam = CheckBoxHBHasHBeam.Checked;
                    break;
                case "CheckBoxHBHasHPost":
                    TimberDraw.Properties.Settings.Default.HBHasHPost = CheckBoxHBHasHPost.Checked;
                    break;
                case "CheckBoxHBHasKPost":
                    TimberDraw.Properties.Settings.Default.HBHasKPost = CheckBoxHBHasKPost.Checked;
                    break;
                case "CheckBoxHBHasRafter":
                    TimberDraw.Properties.Settings.Default.HBHasRafter = CheckBoxHBHasRafter.Checked;
                    break;
                case "CheckBoxHBHasHBGirt":
                    TimberDraw.Properties.Settings.Default.HBHasHBGirt = CheckBoxHBHasHBGirt.Checked;
                    break;
                case "CheckBoxHBHasBrace":
                    TimberDraw.Properties.Settings.Default.HBHasBrace = CheckBoxHBHasBrace.Checked;
                    break;
                case "CheckBoxHBHasBayGirt":
                    TimberDraw.Properties.Settings.Default.HBHasBayGirt = CheckBoxHBHasBayGirt.Checked;
                    break;
                case "CheckBoxHBHasBGBrace":
                    TimberDraw.Properties.Settings.Default.HBHasBGBrace = CheckBoxHBHasBGBrace.Checked;
                    break;
                case "CheckBoxHBHasFlrGirt":
                    TimberDraw.Properties.Settings.Default.HBHasFlrGirt = CheckBoxHBHasFlrGirt.Checked;
                    break;
                case "CheckBoxHBHasFlrBrace":
                    TimberDraw.Properties.Settings.Default.HBHasFlrBrace = CheckBoxHBHasFlrBrace.Checked;
                    break;
            }
            PanelHBDiagram.Refresh();
        }
        private void PanelHBDiagram_Paint(System.Object sender, System.Windows.Forms.PaintEventArgs e)
        {
            //Post
            if (CheckBoxHBHasPost.Checked | HBPostPen.Color == hiLite)
            {
                e.Graphics.DrawLine(HBPostPen, 38, 126, 38, 75);
                e.Graphics.DrawLine(HBPostPen, 138, 126, 138, 75);
            }
            //Rafter
            if (CheckBoxHBHasRafter.Checked | HBRafterPen.Color == hiLite)
            {
                e.Graphics.DrawLine(HBRafterPen, 38, 75, 88, 25);
                e.Graphics.DrawLine(HBRafterPen, 88, 25, 138, 75);
            }
            if (RadioButton1Hbeam.Checked)
            {
                //HBeam
                if (CheckBoxHBHasHBeam.Checked | HBHBeamPen.Color == hiLite)
                {
                    e.Graphics.DrawLine(HBHBeamPen, 38, 78, 63, 78);
                    e.Graphics.DrawLine(HBHBeamPen, 138, 78, 113, 78);
                }
                //HPost
                if (CheckBoxHBHasHPost.Checked | HBHPostPen.Color == hiLite)
                {
                    e.Graphics.DrawLine(HBHPostPen, 63, 50, 63, 78);
                    e.Graphics.DrawLine(HBHPostPen, 113, 50, 113, 78);
                }
                //KPost
                if (CheckBoxHBHasKPost.Checked | HBKPostPen.Color == hiLite)
                {
                    e.Graphics.DrawLine(HBKPostPen, 88, 53, 88, 25);
                }
                //HBGirt
                if (CheckBoxHBHasHBGirt.Checked | HBGirtPen.Color == hiLite)
                {
                    e.Graphics.DrawLine(HBGirtPen, 63, 53, 113, 53);
                }
                //Brace
                if (CheckBoxHBHasBrace.Checked | HBBracePen.Color == hiLite)
                {
                    e.Graphics.DrawLine(HBBracePen, 38, 99, 59, 78);
                    e.Graphics.DrawLine(HBBracePen, 63, 74, 84, 53);
                    e.Graphics.DrawLine(HBBracePen, 113, 74, 92, 53);
                    e.Graphics.DrawLine(HBBracePen, 138, 99, 117, 78);
                }
            }
            if (RadioButton2Hbeam.Checked)
            {
                //HBeam
                if (CheckBoxHBHasHBeam.Checked | HBHBeamPen.Color == hiLite)
                {
                    e.Graphics.DrawLine(HBHBeamPen, 38, 78, 55, 78);
                    e.Graphics.DrawLine(HBHBeamPen, 55, 61, 71, 61);
                    e.Graphics.DrawLine(HBHBeamPen, 121, 61, 105, 61);
                    e.Graphics.DrawLine(HBHBeamPen, 138, 78, 121, 78);
                }
                //HPost
                if (CheckBoxHBHasHPost.Checked | HBHPostPen.Color == hiLite)
                {
                    e.Graphics.DrawLine(HBHPostPen, 55, 58, 55, 78);
                    e.Graphics.DrawLine(HBHPostPen, 71, 42, 71, 61);
                    e.Graphics.DrawLine(HBHPostPen, 105, 42, 105, 61);
                    e.Graphics.DrawLine(HBHPostPen, 121, 58, 121, 78);
                }
                //KPost
                if (CheckBoxHBHasKPost.Checked | HBKPostPen.Color == hiLite)
                {
                    e.Graphics.DrawLine(HBKPostPen, 88, 45, 88, 25);
                }
                //HBGirt
                if (CheckBoxHBHasHBGirt.Checked | HBGirtPen.Color == hiLite)
                {
                    e.Graphics.DrawLine(HBGirtPen, 71, 45, 105, 45);
                }
                //Brace
                if (CheckBoxHBHasBrace.Checked | HBBracePen.Color == hiLite)
                {
                    e.Graphics.DrawLine(HBBracePen, 38, 91, 51, 78);
                    e.Graphics.DrawLine(HBBracePen, 55, 74, 68, 61);
                    e.Graphics.DrawLine(HBBracePen, 71, 58, 84, 45);
                    e.Graphics.DrawLine(HBBracePen, 105, 58, 92, 45);
                    e.Graphics.DrawLine(HBBracePen, 121, 74, 108, 61);
                    e.Graphics.DrawLine(HBBracePen, 138, 91, 125, 78);
                }
            }
        }
        private void HPLabel_MouseEnter(System.Object sender, System.EventArgs e)
        {
            Label l = (Label)sender;
            switch (l.Name)
            {
                case "LabelHBPost":
                    HBPostPen.Color = hiLite;
                    break;
                case "LabelHBHBeam":
                    HBHBeamPen.Color = hiLite;
                    break;
                case "LabelHBHPost":
                    HBHPostPen.Color = hiLite;
                    break;
                case "LabelHBKPost":
                    HBKPostPen.Color = hiLite;
                    break;
                case "LabelHBRafter":
                    HBRafterPen.Color = hiLite;
                    break;
                case "LabelHBHBGirt":
                    HBGirtPen.Color = hiLite;
                    break;
                case "LabelHBBrace":
                    HBBracePen.Color = hiLite;
                    break;
            }
            PanelHBDiagram.Refresh();
        }
        private void HBLabel_MouseLeave(System.Object sender, System.EventArgs e)
        {
            Label l = (Label)sender;
            switch (l.Name)
            {
                case "LabelHBPost":
                    HBPostPen.Color = loLite;
                    break;
                case "LabelHBHBeam":
                    HBHBeamPen.Color = loLite;
                    break;
                case "LabelHBHPost":
                    HBHPostPen.Color = loLite;
                    break;
                case "LabelHBKPost":
                    HBKPostPen.Color = loLite;
                    break;
                case "LabelHBRafter":
                    HBRafterPen.Color = loLite;
                    break;
                case "LabelHBHBGirt":
                    HBGirtPen.Color = loLite;
                    break;
                case "LabelHBBrace":
                    HBBracePen.Color = loLite;
                    break;
            }
            PanelHBDiagram.Refresh();
        }
        private void NumberOfHammerBeam_CheckedChanged(System.Object sender, System.EventArgs e)
        {
            RadioButton rb = (RadioButton)sender;
            switch (rb.Name)
            {
                case "RadioButton1Hbeam":
                    TimberDraw.Properties.Settings.Default.HBDivisor = 4;
                    break;
                case "RadioButton2Hbeam":
                    TimberDraw.Properties.Settings.Default.HBDivisor = 6;
                    break;
            }
            PanelHBDiagram.Refresh();
        }
        #endregion

        #region King Post Bent
        Pen KPPostPen = new(loLite, 2);
        Pen KPGirtPen = new(loLite, 2);
        Pen KPRafterPen = new(loLite, 2);
        Pen KPKPostPen = new(loLite, 2);
        Pen KPStrutPen = new(loLite, 2);
        Pen KPVStrutPen = new(loLite, 2);
        Pen KPBracePen = new(loLite, 2);
        Pen KPFlrGirtPen = new(loLite, 2);
        Pen KPFlrBracePen = new(loLite, 2);
        private void TextBoxValiatedOnLeave_KP(System.Object sender, System.EventArgs e)
        {
            TextBox tb = (TextBox)sender;
            if (IsValidDistance(tb.Text))
            {
                switch (tb.Name)
                {
                    case "TextBoxKPPostWidth":
                        TimberDraw.Properties.Settings.Default.KPPostWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxKPPostDepth":
                        TimberDraw.Properties.Settings.Default.KPPostDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxKPGirtWidth":
                        TimberDraw.Properties.Settings.Default.KPGirtWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxKPGirtDepth":
                        TimberDraw.Properties.Settings.Default.KPGirtDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxKPRafterWidth":
                        TimberDraw.Properties.Settings.Default.KPRafterWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxKPRafterDepth":
                        TimberDraw.Properties.Settings.Default.KPRafterDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxKPKpostWidth":
                        TimberDraw.Properties.Settings.Default.KPKpostWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxKPKpostDepth":
                        TimberDraw.Properties.Settings.Default.KPKpostDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxKPStrutWidth":
                        TimberDraw.Properties.Settings.Default.KPStrutWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxKPStrutDepth":
                        TimberDraw.Properties.Settings.Default.KPStrutDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxKPStrutAngle":
                        TimberDraw.Properties.Settings.Default.KPStrutAngle = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxKPBraceAngle":
                        TimberDraw.Properties.Settings.Default.KPBraceAngle = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxKPVStrutWidth":
                        TimberDraw.Properties.Settings.Default.KPVStrutWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxKPVStrutDepth":
                        TimberDraw.Properties.Settings.Default.KPVStrutDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxKPBraceWidth":
                        TimberDraw.Properties.Settings.Default.KPBraceWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxKPBraceDepth":
                        TimberDraw.Properties.Settings.Default.KPBraceDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxKPBraceLength":
                        TimberDraw.Properties.Settings.Default.KPBraceLength = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxKPFlrGirtWidth":
                        TimberDraw.Properties.Settings.Default.KPFlrGirtWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxKPFlrGirtDepth":
                        TimberDraw.Properties.Settings.Default.KPFlrGirtDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxKPFlrGirtHt":
                        TimberDraw.Properties.Settings.Default.KPFlrGirtHt = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxKPFlrBraceWidth":
                        TimberDraw.Properties.Settings.Default.KPFlrBraceWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxKPFlrBraceDepth":
                        TimberDraw.Properties.Settings.Default.KPFlrBraceDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxKPFlrBraceLength":
                        TimberDraw.Properties.Settings.Default.KPFlrBraceLength = Convert.ToDouble(tb.Text);
                        break;
                }
            }
            else
            {
                MessageBox.Show("Input Error.  Check Units", "TimberDraw", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                tb.Focus();
                tb.Clear();
            }
        }
        private void CheckBoxKPHas_CheckedChanged(System.Object sender, System.EventArgs e)
        {
            CheckBox cb = (CheckBox)sender;
            switch (cb.Name)
            {
                case "CheckBoxKPHasPost":
                    TimberDraw.Properties.Settings.Default.KPHasPost = CheckBoxKPHasPost.Checked;
                    break;
                case "CheckBoxKPHasGirt":
                    TimberDraw.Properties.Settings.Default.KPHasGirt = CheckBoxKPHasGirt.Checked;
                    break;
                case "CheckBoxKPHasRafter":
                    TimberDraw.Properties.Settings.Default.KPHasRafter = CheckBoxKPHasRafter.Checked;
                    break;
                case "CheckBoxKPHasKPost":
                    TimberDraw.Properties.Settings.Default.KPHasKPost = CheckBoxKPHasKPost.Checked;
                    break;
                case "CheckBoxKPHasStrut":
                    TimberDraw.Properties.Settings.Default.KPHasStrut = CheckBoxKPHasStrut.Checked;
                    break;
                case "CheckBoxKPHasVStrut":
                    TimberDraw.Properties.Settings.Default.KPHasVStrut = CheckBoxKPHasVStrut.Checked;
                    break;
                case "CheckBoxKPHasBrace":
                    TimberDraw.Properties.Settings.Default.KPHasBrace = CheckBoxKPHasBrace.Checked;
                    break;
                case "CheckBoxKPHasFlrGirt":
                    TimberDraw.Properties.Settings.Default.KPHasFlrGirt = CheckBoxKPHasFlrGirt.Checked;
                    break;
                case "CheckBoxKPHasFlrBrace":
                    TimberDraw.Properties.Settings.Default.KPHasFlrBrace = CheckBoxKPHasFlrBrace.Checked;
                    break;
            }
            PanelKPDiagram.Refresh();
        }
        private void PanelKPDiagram_Paint(System.Object sender, System.Windows.Forms.PaintEventArgs e)
        {
            //Post
            if (CheckBoxKPHasPost.Checked | KPPostPen.Color == hiLite)
            {
                e.Graphics.DrawLine(KPPostPen, 38, 126, 38, 75);
                e.Graphics.DrawLine(KPPostPen, 138, 126, 138, 75);
            }
            //Girt
            if (CheckBoxKPHasGirt.Checked | KPGirtPen.Color == hiLite)
            {
                e.Graphics.DrawLine(KPGirtPen, 38, 78, 138, 78);
            }
            //Rafter
            if (CheckBoxKPHasRafter.Checked | KPRafterPen.Color == hiLite)
            {
                e.Graphics.DrawLine(KPRafterPen, 38, 75, 88, 25);
                e.Graphics.DrawLine(KPRafterPen, 88, 25, 138, 75);
            }
            //KPost
            if (CheckBoxKPHasKPost.Checked | KPKPostPen.Color == hiLite)
            {
                e.Graphics.DrawLine(KPKPostPen, 88, 78, 88, 25);
            }
            //Strut
            if (CheckBoxKPHasStrut.Checked | KPStrutPen.Color == hiLite)
            {
                e.Graphics.DrawLine(KPStrutPen, 63, 50, 88, 75);
                e.Graphics.DrawLine(KPStrutPen, 113, 50, 88, 75);
            }
            //VStrut
            if (CheckBoxKPHasVStrut.Checked | KPVStrutPen.Color == hiLite)
            {
                e.Graphics.DrawLine(KPVStrutPen, 63, 78, 63, 50);
                e.Graphics.DrawLine(KPVStrutPen, 113, 78, 113, 50);
            }
            //Brace
            if (CheckBoxKPHasBrace.Checked | KPBracePen.Color == hiLite)
            {
                e.Graphics.DrawLine(KPBracePen, 38, 93, 53, 78);
                e.Graphics.DrawLine(KPBracePen, 123, 78, 138, 93);
            }
            //FlrGirt
            if (CheckBoxKPHasFlrGirt.Checked | KPFlrGirtPen.Color == hiLite)
            {
                e.Graphics.DrawLine(KPFlrGirtPen, 38, 101, 138, 101);
            }
            //FlrBrace
            if (CheckBoxKPHasFlrBrace.Checked | KPFlrBracePen.Color == hiLite)
            {
                e.Graphics.DrawLine(KPFlrBracePen, 38, 116, 53, 101);
                e.Graphics.DrawLine(KPFlrBracePen, 138, 116, 123, 101);
            }
        }
        private void KPLabel_MouseEnter(System.Object sender, System.EventArgs e)
        {
            Label l = (Label)sender;
            switch (l.Name)
            {
                case "LabelKPPost":
                    KPPostPen.Color = hiLite;
                    break;
                case "LabelKPGirt":
                    KPGirtPen.Color = hiLite;
                    break;
                case "LabelKPRafter":
                    KPRafterPen.Color = hiLite;
                    break;
                case "LabelKPKPost":
                    KPKPostPen.Color = hiLite;
                    break;
                case "LabelKPStrut":
                    KPStrutPen.Color = hiLite;
                    break;
                case "LabelKPVStrut":
                    KPVStrutPen.Color = hiLite;
                    break;
                case "LabelKPBrace":
                    KPBracePen.Color = hiLite;
                    break;
                case "LabelKPFlrGirt":
                    KPFlrGirtPen.Color = hiLite;
                    break;
                case "LabelKPFlrBrace":
                    KPFlrBracePen.Color = hiLite;
                    break;
            }
            PanelKPDiagram.Refresh();
        }
        private void KPLabel_MouseLeave(System.Object sender, System.EventArgs e)
        {
            Label l = (Label)sender;
            switch (l.Name)
            {
                case "LabelKPPost":
                    KPPostPen.Color = loLite;
                    break;
                case "LabelKPGirt":
                    KPGirtPen.Color = loLite;
                    break;
                case "LabelKPRafter":
                    KPRafterPen.Color = loLite;
                    break;
                case "LabelKPKPost":
                    KPKPostPen.Color = loLite;
                    break;
                case "LabelKPStrut":
                    KPStrutPen.Color = loLite;
                    break;
                case "LabelKPVStrut":
                    KPVStrutPen.Color = loLite;
                    break;
                case "LabelKPBrace":
                    KPBracePen.Color = loLite;
                    break;
                case "LabelKPFlrGirt":
                    KPFlrGirtPen.Color = loLite;
                    break;
                case "LabelKPFlrBrace":
                    KPFlrBracePen.Color = loLite;
                    break;
            }
            PanelKPDiagram.Refresh();
        }
        #endregion

        #region Queen Post Bent
        Pen QPPostPen = new(loLite, 2);
        Pen QPGirtPen = new(loLite, 2);
        Pen QPRafterPen = new(loLite, 2);
        Pen QPQPostPen = new(loLite, 2);
        Pen QPQPGirtPen = new(loLite, 2);
        Pen QPStrutPen = new(loLite, 2);
        Pen QPBracePen = new(loLite, 2);
        Pen QPQPBracePen = new(loLite, 2);
        private void TextBoxValiatedOnLeave_QP(System.Object sender, System.EventArgs e)
        {
            TextBox tb = (TextBox)sender;
            if (IsValidDistance(tb.Text))
            {
                switch (tb.Name)
                {
                    case "TextBoxQPPostWidth":
                        TimberDraw.Properties.Settings.Default.QPPostWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxQPPostDepth":
                        TimberDraw.Properties.Settings.Default.QPPostDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxQPGirtWidth":
                        TimberDraw.Properties.Settings.Default.QPGirtWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxQPGirtDepth":
                        TimberDraw.Properties.Settings.Default.QPGirtDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxQPRafterWidth":
                        TimberDraw.Properties.Settings.Default.QPRafterWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxQPRafterDepth":
                        TimberDraw.Properties.Settings.Default.QPRafterDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxQPQPostWidth":
                        TimberDraw.Properties.Settings.Default.QPQPostWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxQPQPostDepth":
                        TimberDraw.Properties.Settings.Default.QPQPostDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxQPStrutWidth":
                        TimberDraw.Properties.Settings.Default.QPStrutWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxQPStrutDepth":
                        TimberDraw.Properties.Settings.Default.QPStrutDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxQPQPGirtWidth":
                        TimberDraw.Properties.Settings.Default.QPQPGirtWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxQPQPGirtDepth":
                        TimberDraw.Properties.Settings.Default.QPQPGirtDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxQPBraceWidth":
                        TimberDraw.Properties.Settings.Default.QPBraceWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxQPBraceDepth":
                        TimberDraw.Properties.Settings.Default.QPBraceDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxQPBraceLength":
                        TimberDraw.Properties.Settings.Default.QPBraceLength = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxQPQPBraceWidth":
                        TimberDraw.Properties.Settings.Default.QPQPBraceWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxQPQPBraceDepth":
                        TimberDraw.Properties.Settings.Default.QPQPBraceDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxQPQPBraceLength":
                        TimberDraw.Properties.Settings.Default.QPQPBraceLength = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxQPFlrGirtWidth":
                        TimberDraw.Properties.Settings.Default.QPFlrGirtWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxQPFlrGirtDepth":
                        TimberDraw.Properties.Settings.Default.QPFlrGirtDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxQPFlrGirtHt":
                        TimberDraw.Properties.Settings.Default.QPFlrGirtHt = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxQPFlrBraceWidth":
                        TimberDraw.Properties.Settings.Default.QPFlrBraceWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxQPFlrBraceDepth":
                        TimberDraw.Properties.Settings.Default.QPFlrBraceDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxQPFlrBraceLength":
                        TimberDraw.Properties.Settings.Default.QPFlrBraceLength = Convert.ToDouble(tb.Text);
                        break;
                }
            }
            else
            {
                MessageBox.Show("Input Error.  Check Units", "TimberDraw", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                tb.Clear();
                tb.Focus();
            }
        }
        private void CheckBoxQPHas_CheckedChanged(System.Object sender, System.EventArgs e)
        {
            CheckBox cb = (CheckBox)sender;
            switch (cb.Name)
            {
                case "CheckBoxQPHasPost":
                    TimberDraw.Properties.Settings.Default.QPHasPost = CheckBoxQPHasPost.Checked;
                    break;
                case "CheckBoxQPHasGirt":
                    TimberDraw.Properties.Settings.Default.QPHasGirt = CheckBoxQPHasGirt.Checked;
                    break;
                case "CheckBoxQPHasRafter":
                    TimberDraw.Properties.Settings.Default.QPHasRafter = CheckBoxQPHasRafter.Checked;
                    break;
                case "CheckBoxQPHasQPost":
                    TimberDraw.Properties.Settings.Default.QPHasQPost = CheckBoxQPHasQPost.Checked;
                    break;
                case "CheckBoxQPHasQPGirt":
                    TimberDraw.Properties.Settings.Default.QPHasQPGirt = CheckBoxQPHasQPGirt.Checked;
                    break;
                case "CheckBoxQPHasStrut":
                    TimberDraw.Properties.Settings.Default.QPHasStrut = CheckBoxQPHasStrut.Checked;
                    break;
                case "CheckBoxQPHasBrace":
                    TimberDraw.Properties.Settings.Default.QPHasBrace = CheckBoxQPHasBrace.Checked;
                    break;
                case "CheckBoxQPHasQPBrace":
                    TimberDraw.Properties.Settings.Default.QPHasQPBrace = CheckBoxQPHasQPBrace.Checked;
                    break;
                case "CheckBoxQPHasFlrGirt":
                    TimberDraw.Properties.Settings.Default.QPHasFlrGirt = CheckBoxQPHasFlrGirt.Checked;
                    break;
                case "CheckBoxQPHasFlrBrace":
                    TimberDraw.Properties.Settings.Default.QPHasFlrBrace = CheckBoxQPHasFlrBrace.Checked;
                    break;
            }
            PanelQPDiagram.Refresh();
        }
        private void PanelQPDiagram_Paint(System.Object sender, System.Windows.Forms.PaintEventArgs e)
        {
            //Post
            if (CheckBoxQPHasPost.Checked | QPPostPen.Color == hiLite)
            {
                e.Graphics.DrawLine(QPPostPen, 25, 75, 25, 126);
                e.Graphics.DrawLine(QPPostPen, 125, 75, 125, 126);
            }
            //Girt
            if (CheckBoxQPHasGirt.Checked | QPGirtPen.Color == hiLite)
            {
                e.Graphics.DrawLine(QPGirtPen, 25, 78, 125, 78);
            }
            //Rafter
            if (CheckBoxQPHasRafter.Checked | QPRafterPen.Color == hiLite)
            {
                e.Graphics.DrawLine(QPRafterPen, 25, 75, 75, 25);
                e.Graphics.DrawLine(QPRafterPen, 75, 25, 125, 75);
            }
            //QPost
            if (CheckBoxQPHasQPost.Checked | QPQPostPen.Color == hiLite)
            {
                e.Graphics.DrawLine(QPQPostPen, 58, 42, 58, 78);
                e.Graphics.DrawLine(QPQPostPen, 92, 42, 92, 78);
            }
            //QPGirt
            if (CheckBoxQPHasQPGirt.Checked | QPQPGirtPen.Color == hiLite)
            {
                e.Graphics.DrawLine(QPQPGirtPen, 58, 45, 92, 45);
            }
            //Strut
            if (CheckBoxQPHasStrut.Checked | QPStrutPen.Color == hiLite)
            {
                e.Graphics.DrawLine(QPStrutPen, 41, 58, 58, 75);
                e.Graphics.DrawLine(QPStrutPen, 108, 58, 92, 75);
            }
            //Brace
            if (CheckBoxQPHasBrace.Checked | QPBracePen.Color == hiLite)
            {
                e.Graphics.DrawLine(QPBracePen, 25, 93, 40, 78);
                e.Graphics.DrawLine(QPBracePen, 110, 78, 125, 93);
            }
            //QPBrace
            if (CheckBoxQPHasQPBrace.Checked | QPQPBracePen.Color == hiLite)
            {
                e.Graphics.DrawLine(QPQPBracePen, 58, 55, 68, 45);
                e.Graphics.DrawLine(QPQPBracePen, 82, 45, 92, 55);
            }
        }
        private void QPLabel_MouseEnter(System.Object sender, System.EventArgs e)
        {
            Label l = (Label)sender;
            switch (l.Name)
            {
                case "LabelQPPost":
                    QPPostPen.Color = hiLite;
                    break;
                case "LabelQPGirt":
                    QPGirtPen.Color = hiLite;
                    break;
                case "LabelQPRafter":
                    QPRafterPen.Color = hiLite;
                    break;
                case "LabelQPQPost":
                    QPQPostPen.Color = hiLite;
                    break;
                case "LabelQPQPGirt":
                    QPQPGirtPen.Color = hiLite;
                    break;
                case "LabelQPStrut":
                    QPStrutPen.Color = hiLite;
                    break;
                case "LabelQPBrace":
                    QPBracePen.Color = hiLite;
                    break;
                case "LabelQPQPBrace":
                    QPQPBracePen.Color = hiLite;
                    break;
            }
            PanelQPDiagram.Refresh();
        }
        private void QPLabel_MouseLeave(System.Object sender, System.EventArgs e)
        {
            Label l = (Label)sender;
            switch (l.Name)
            {
                case "LabelQPPost":
                    QPPostPen.Color = loLite;
                    break;
                case "LabelQPGirt":
                    QPGirtPen.Color = loLite;
                    break;
                case "LabelQPRafter":
                    QPRafterPen.Color = loLite;
                    break;
                case "LabelQPQPost":
                    QPQPostPen.Color = loLite;
                    break;
                case "LabelQPQPGirt":
                    QPQPGirtPen.Color = loLite;
                    break;
                case "LabelQPStrut":
                    QPStrutPen.Color = loLite;
                    break;
                case "LabelQPBrace":
                    QPBracePen.Color = loLite;
                    break;
                case "LabelQPQPBrace":
                    QPQPBracePen.Color = loLite;
                    break;
            }
            PanelQPDiagram.Refresh();
        }
        #endregion

        #region King Post Truss
        Pen KPTGirtPen = new(loLite, 2);
        Pen KPTRafterPen = new(loLite, 2);
        Pen KPTKPostPen = new(loLite, 2);
        Pen KPTStrutPen = new(loLite, 2);
        Pen KPTVStrutPen = new(loLite, 2);
        private void TextBoxValiatedOnLeave_KPT(System.Object sender, System.EventArgs e)
        {
            TextBox tb = (TextBox)sender;
            if (IsValidDistance(tb.Text))
            {
                switch (tb.Name)
                {
                    case "TextBoxKPTGirtWidth":
                        TimberDraw.Properties.Settings.Default.KPTGirtWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxKPTGirtDepth":
                        TimberDraw.Properties.Settings.Default.KPTGirtDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxKPTRafterWidth":
                        TimberDraw.Properties.Settings.Default.KPTRafterWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxKPTRafterDepth":
                        TimberDraw.Properties.Settings.Default.KPTRafterDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxKPTKpostWidth":
                        TimberDraw.Properties.Settings.Default.KPTKpostWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxKPTKpostDepth":
                        TimberDraw.Properties.Settings.Default.KPTKpostDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxKPTStrutWidth":
                        TimberDraw.Properties.Settings.Default.KPTStrutWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxKPTStrutDepth":
                        TimberDraw.Properties.Settings.Default.KPTStrutDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxKPTVStrutWidth":
                        TimberDraw.Properties.Settings.Default.KPTVStrutWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxKPTVStrutDepth":
                        TimberDraw.Properties.Settings.Default.KPTVStrutDepth = Convert.ToDouble(tb.Text);
                        break;
                }
            }
            else
            {
                MessageBox.Show("Input Error.  Check Units", "TimberDraw", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                tb.Clear();
                tb.Focus();
            }
        }
        private void CheckBoxKPTHas_CheckedChanged(System.Object sender, System.EventArgs e)
        {
            CheckBox cb = (CheckBox)sender;
            switch (cb.Name)
            {
                case "CheckBoxKPTHasGirt":
                    TimberDraw.Properties.Settings.Default.KPTHasGirt = CheckBoxKPTHasGirt.Checked;
                    break;
                case "CheckBoxKPTHasRafter":
                    TimberDraw.Properties.Settings.Default.KPTHasRafter = CheckBoxKPTHasRafter.Checked;
                    break;
                case "CheckBoxKPTHasKPost":
                    TimberDraw.Properties.Settings.Default.KPTHasKpost = CheckBoxKPTHasKPost.Checked;
                    break;
                case "CheckBoxKPTHasStrut":
                    TimberDraw.Properties.Settings.Default.KPTHasStrut = CheckBoxKPTHasStrut.Checked;
                    break;
                case "CheckBoxKPTHasVStrut":
                    TimberDraw.Properties.Settings.Default.KPTHasVStrut = CheckBoxKPTHasVStrut.Checked;
                    break;
            }
            PanelKPTDiagram.Refresh();
        }
        private void PanelKPTDiagram_Paint(System.Object sender, System.Windows.Forms.PaintEventArgs e)
        {
            //Girt
            if (CheckBoxKPTHasGirt.Checked | KPTGirtPen.Color == hiLite)
            {
                e.Graphics.DrawLine(KPTGirtPen, 25, 88, 138, 88);
            }
            //Rafter
            if (CheckBoxKPTHasRafter.Checked | KPTRafterPen.Color == hiLite)
            {
                e.Graphics.DrawLine(KPTRafterPen, 25, 88, 82, 31);
                e.Graphics.DrawLine(KPTRafterPen, 138, 88, 82, 31);
            }
            //KPost
            if (CheckBoxKPTHasKPost.Checked | KPTKPostPen.Color == hiLite)
            {
                e.Graphics.DrawLine(KPTKPostPen, 82, 31, 81, 88);
            }
            //Strut
            if (CheckBoxKPTHasStrut.Checked | KPTStrutPen.Color == hiLite)
            {
                e.Graphics.DrawLine(KPTStrutPen, 82, 83, 56, 57);
                e.Graphics.DrawLine(KPTStrutPen, 82, 83, 107, 57);
            }
            //VStrut
            if (CheckBoxKPTHasVStrut.Checked | KPTVStrutPen.Color == hiLite)
            {
                e.Graphics.DrawLine(KPTVStrutPen, 56, 57, 56, 88);
                e.Graphics.DrawLine(KPTVStrutPen, 107, 57, 107, 88);
            }
        }
        private void KPTLabel_MouseEnter(System.Object sender, System.EventArgs e)
        {
            Label l = (Label)sender;
            switch (l.Name)
            {
                case "LabelKPTGirt":
                    KPTGirtPen.Color = hiLite;
                    break;
                case "LabelKPTRafter":
                    KPTRafterPen.Color = hiLite;
                    break;
                case "LabelKPTKPost":
                    KPTKPostPen.Color = hiLite;
                    break;
                case "LabelKPTStrut":
                    KPTStrutPen.Color = hiLite;
                    break;
                case "LabelKPTVStrut":
                    KPTVStrutPen.Color = hiLite;
                    break;
            }
            PanelKPTDiagram.Refresh();
        }
        private void KPTLabel_MouseLeave(System.Object sender, System.EventArgs e)
        {
            Label l = (Label)sender;
            switch (l.Name)
            {
                case "LabelKPTGirt":
                    KPTGirtPen.Color = loLite;
                    break;
                case "LabelKPTRafter":
                    KPTRafterPen.Color = loLite;
                    break;
                case "LabelKPTKPost":
                    KPTKPostPen.Color = loLite;
                    break;
                case "LabelKPTStrut":
                    KPTStrutPen.Color = loLite;
                    break;
                case "LabelKPTVStrut":
                    KPTVStrutPen.Color = loLite;
                    break;
            }
            PanelKPTDiagram.Refresh();
        }
        #endregion

        #region Queen Post Truss
        Pen QPTGirtPen = new(loLite, 2);
        Pen QPTRafterPen = new(loLite, 2);
        Pen QPTQPostPen = new(loLite, 2);
        Pen QPTQPGirtPen = new(loLite, 2);
        Pen QPTStrutPen = new(loLite, 2);
        Pen QPTQPBracePen = new(loLite, 2);
        private void TextBoxValiatedOnLeave_QPT(System.Object sender, System.EventArgs e)
        {
            TextBox tb = (TextBox)sender;
            if (IsValidDistance(tb.Text))
            {
                switch (tb.Name)
                {
                    case "TextBoxQPTGirtWidth":
                        TimberDraw.Properties.Settings.Default.QPTGirtWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxQPTGirtDepth":
                        TimberDraw.Properties.Settings.Default.QPTGirtDepth = Convert.ToDouble(tb.Text);

                        break;
                    case "TextBoxQPTRafterWidth":
                        TimberDraw.Properties.Settings.Default.QPTRafterWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxQPTRafterDepth":
                        TimberDraw.Properties.Settings.Default.QPTRafterDepth = Convert.ToDouble(tb.Text);

                        break;
                    case "TextBoxQPTQpostWidth":
                        TimberDraw.Properties.Settings.Default.QPTQpostWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxQPTQpostDepth":
                        TimberDraw.Properties.Settings.Default.QPTQpostDepth = Convert.ToDouble(tb.Text);

                        break;
                    case "TextBoxQPTStrutWidth":
                        TimberDraw.Properties.Settings.Default.QPTStrutWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxQPTStrutDepth":
                        TimberDraw.Properties.Settings.Default.QPTStrutDepth = Convert.ToDouble(tb.Text);

                        break;
                    case "TextBoxQPTUpperGirtWidth":
                        TimberDraw.Properties.Settings.Default.QPTUpperGirtWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxQPTUpperGirtDepth":
                        TimberDraw.Properties.Settings.Default.QPTUpperGirtDepth = Convert.ToDouble(tb.Text);

                        break;
                    case "TextBoxQPTQPBraceWidth":
                        TimberDraw.Properties.Settings.Default.QPTQPBraceWidth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxQPTQPBraceDepth":
                        TimberDraw.Properties.Settings.Default.QPTQPBraceDepth = Convert.ToDouble(tb.Text);
                        break;
                    case "TextBoxQPTQPBraceLength":
                        TimberDraw.Properties.Settings.Default.QPTQPBraceLength = Convert.ToDouble(tb.Text);
                        break;
                }
            }
            else
            {
                MessageBox.Show("Input Error.  Check Units", "TimberDraw", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                tb.Clear();
                tb.Focus();
            }
        }
        private void CheckBoxQPTHas_CheckedChanged(System.Object sender, System.EventArgs e)
        {
            CheckBox cb = (CheckBox)sender;
            switch (cb.Name)
            {
                case "CheckBoxQPTHasGirt":
                    TimberDraw.Properties.Settings.Default.QPTHasGirt = CheckBoxQPTHasGirt.Checked;
                    break;
                case "CheckBoxQPTHasRafter":
                    TimberDraw.Properties.Settings.Default.QPTHasRafter = CheckBoxQPTHasRafter.Checked;
                    break;
                case "CheckBoxQPTHasQPost":
                    TimberDraw.Properties.Settings.Default.QPTHasQpost = CheckBoxQPTHasQPost.Checked;
                    break;
                case "CheckBoxQPTHasQPGirt":
                    TimberDraw.Properties.Settings.Default.QPTHasQPGirt = CheckBoxQPTHasQPGirt.Checked;
                    break;
                case "CheckBoxQPTHasStrut":
                    TimberDraw.Properties.Settings.Default.QPTHasStrut = CheckBoxQPTHasStrut.Checked;
                    break;
                case "CheckBoxQPTHasQPBrace":
                    TimberDraw.Properties.Settings.Default.QPTHasQPBrace = CheckBoxQPTHasQPBrace.Checked;
                    break;
            }
            PanelQPTDiagram.Refresh();
        }
        private void PanelQPTDiagram_Paint(System.Object sender, System.Windows.Forms.PaintEventArgs e)
        {
            //Girt
            if (CheckBoxQPTHasGirt.Checked | QPTGirtPen.Color == hiLite)
            {
                e.Graphics.DrawLine(QPTGirtPen, 25, 88, 138, 88);
            }
            //Rafter
            if (CheckBoxQPTHasRafter.Checked | QPTRafterPen.Color == hiLite)
            {
                e.Graphics.DrawLine(QPTRafterPen, 25, 88, 82, 31);
                e.Graphics.DrawLine(QPTRafterPen, 138, 88, 82, 31);
            }
            //QPost
            if (CheckBoxQPTHasQPost.Checked | QPTQPostPen.Color == hiLite)
            {
                e.Graphics.DrawLine(QPTQPostPen, 63, 88, 63, 50);
                e.Graphics.DrawLine(QPTQPostPen, 101, 88, 101, 50);
            }
            //QPGirt
            if (CheckBoxQPTHasQPGirt.Checked | QPTQPGirtPen.Color == hiLite)
            {
                e.Graphics.DrawLine(QPTQPGirtPen, 63, 54, 101, 54);
            }
            //Strut
            if (CheckBoxQPTHasStrut.Checked | QPTStrutPen.Color == hiLite)
            {
                e.Graphics.DrawLine(QPTStrutPen, 63, 83, 46, 67);
                e.Graphics.DrawLine(QPTStrutPen, 101, 83, 117, 67);
            }
            //QPBrace
            if (CheckBoxQPTHasQPBrace.Checked | QPTQPBracePen.Color == hiLite)
            {
                e.Graphics.DrawLine(QPTQPBracePen, 63, 67, 75, 54);
                e.Graphics.DrawLine(QPTQPBracePen, 101, 67, 88, 54);
            }
        }
        private void QPTLabel_MouseEnter(System.Object sender, System.EventArgs e)
        {
            Label l = (Label)sender;
            switch (l.Name)
            {
                case "LabelQPTGirt":
                    QPTGirtPen.Color = hiLite;
                    break;
                case "LabelQPTRafter":
                    QPTRafterPen.Color = hiLite;
                    break;
                case "LabelQPTQPost":
                    QPTQPostPen.Color = hiLite;
                    break;
                case "LabelQPTQPGirt":
                    QPTQPGirtPen.Color = hiLite;
                    break;
                case "LabelQPTStrut":
                    QPTStrutPen.Color = hiLite;
                    break;
                case "LabelQPTQPBrace":
                    QPTQPBracePen.Color = hiLite;
                    break;
            }
            PanelQPTDiagram.Refresh();
        }
        private void QPTLabel_MouseLeave(System.Object sender, System.EventArgs e)
        {
            Label l = (Label)sender;
            switch (l.Name)
            {
                case "LabelQPTGirt":
                    QPTGirtPen.Color = loLite;
                    break;
                case "LabelQPTRafter":
                    QPTRafterPen.Color = loLite;
                    break;
                case "LabelQPTQPost":
                    QPTQPostPen.Color = loLite;
                    break;
                case "LabelQPTQPGirt":
                    QPTQPGirtPen.Color = loLite;
                    break;
                case "LabelQPTStrut":
                    QPTStrutPen.Color = loLite;
                    break;
                case "LabelQPTQPBrace":
                    QPTQPBracePen.Color = loLite;
                    break;
            }
            PanelQPTDiagram.Refresh();
        }
        #endregion

        #region Bay Members
        private void TextBoxValiatedOnLeave_Bay(System.Object sender, System.EventArgs e)
        {
            TextBox tb = (TextBox)sender;
            if (IsValidDistance(tb.Text))
            {
                switch (tb.Name)
                {
                    case "TextBoxRidgeWidth":
                        TimberDraw.Properties.Settings.Default.RidgeWidth = Converter.StringToDistance(tb.Text, DistanceUnitFormat.Current);
                        break;
                    case "TextBoxRidgeDepth":
                        TimberDraw.Properties.Settings.Default.RidgeDepth = Converter.StringToDistance(tb.Text, DistanceUnitFormat.Current);
                        break;
                    case "TextBoxRidgeBraceWidth":
                        TimberDraw.Properties.Settings.Default.RidgeBraceWidth = Converter.StringToDistance(tb.Text, DistanceUnitFormat.Current);
                        break;
                    case "TextBoxRidgeBraceDepth":
                        TimberDraw.Properties.Settings.Default.RidgeBraceDepth = Converter.StringToDistance(tb.Text, DistanceUnitFormat.Current);
                        break;
                    case "TextBoxRidgeBraceLength":
                        TimberDraw.Properties.Settings.Default.RidgeBraceLength = Converter.StringToDistance(tb.Text, DistanceUnitFormat.Current);
                        break;
                    case "TextBoxEaveGirtWidth":
                        TimberDraw.Properties.Settings.Default.EaveGirtWidth = Converter.StringToDistance(tb.Text, DistanceUnitFormat.Current);
                        break;
                    case "TextBoxEaveGirtDepth":
                        TimberDraw.Properties.Settings.Default.EaveGirtDepth = Converter.StringToDistance(tb.Text, DistanceUnitFormat.Current);
                        break;
                    case "TextBoxEaveGirtBraceWidth":
                        TimberDraw.Properties.Settings.Default.EaveGirtBraceWidth = Converter.StringToDistance(tb.Text, DistanceUnitFormat.Current);
                        break;
                    case "TextBoxEaveGirtBraceDepth":
                        TimberDraw.Properties.Settings.Default.EaveGirtBraceDepth = Converter.StringToDistance(tb.Text, DistanceUnitFormat.Current);
                        break;
                    case "TextBoxEaveGirtBraceLength":
                        TimberDraw.Properties.Settings.Default.EaveGirtBraceLength = Converter.StringToDistance(tb.Text, DistanceUnitFormat.Current);
                        break;
                    case "TextBoxPurlinWidth":
                        TimberDraw.Properties.Settings.Default.PurlinWidth = Converter.StringToDistance(tb.Text, DistanceUnitFormat.Current);
                        break;
                    case "TextBoxPurlinDepth":
                        TimberDraw.Properties.Settings.Default.PurlinDepth = Converter.StringToDistance(tb.Text, DistanceUnitFormat.Current);
                        break;
                    case "TextBoxCommonWidth":
                        TimberDraw.Properties.Settings.Default.CommonWidth = Converter.StringToDistance(tb.Text, DistanceUnitFormat.Current);
                        break;
                    case "TextBoxCommonDepth":
                        TimberDraw.Properties.Settings.Default.CommonDepth = Converter.StringToDistance(tb.Text, DistanceUnitFormat.Current);
                        break;
                    case "TextBoxFlrGirtWidth":
                        TimberDraw.Properties.Settings.Default.FlrGirtWidth = Converter.StringToDistance(tb.Text, DistanceUnitFormat.Current);
                        break;
                    case "TextBoxFlrGirtDepth":
                        TimberDraw.Properties.Settings.Default.FlrGirtDepth = Converter.StringToDistance(tb.Text, DistanceUnitFormat.Current);
                        break;
                    case "TextBoxFlrGirtHt":
                        TimberDraw.Properties.Settings.Default.FlrGirtHt = Converter.StringToDistance(tb.Text, DistanceUnitFormat.Current);
                        break;
                    case "TextBoxFlrGirtBraceWidth":
                        TimberDraw.Properties.Settings.Default.FlrGirtBraceWidth = Converter.StringToDistance(tb.Text, DistanceUnitFormat.Current);
                        break;
                    case "TextBoxFlrGirtBraceDepth":
                        TimberDraw.Properties.Settings.Default.FlrGirtBraceDepth = Converter.StringToDistance(tb.Text, DistanceUnitFormat.Current);
                        break;
                    case "TextBoxFlrGirtBraceLength":
                        TimberDraw.Properties.Settings.Default.FlrGirtBraceLength = Converter.StringToDistance(tb.Text, DistanceUnitFormat.Current);
                        break;
                }
            }
            else
            {
                MessageBox.Show("Input Error.  Check Units", "TimberDraw", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                tb.Clear();
                tb.Focus();
            }
        }
        private void CheckBoxBay_CheckedChanged(System.Object sender, System.EventArgs e)
        {
            CheckBox cb = (CheckBox)sender;
            switch (cb.Name)
            {
                case "CheckBoxHasRidge":
                    TimberDraw.Properties.Settings.Default.HasRidge = CheckBoxHasRidge.Checked;
                    break;
                case "CheckBoxHasRidgeBrace":
                    TimberDraw.Properties.Settings.Default.HasRidgeBrace = CheckBoxHasRidgeBrace.Checked;
                    break;
                case "CheckBoxHasEaveGirt":
                    TimberDraw.Properties.Settings.Default.HasEaveGirt = CheckBoxHasEaveGirt.Checked;
                    break;
                case "CheckBoxHasEaveGirtBrace":
                    TimberDraw.Properties.Settings.Default.HasEaveGirtBrace = CheckBoxHasEaveGirtBrace.Checked;
                    break;
                case "CheckBoxHasPurlins":
                    TimberDraw.Properties.Settings.Default.HasPurlins = CheckBoxHasPurlins.Checked;
                    break;
                case "CheckBoxHasCommons":
                    TimberDraw.Properties.Settings.Default.HasCommons = CheckBoxHasCommons.Checked;
                    break;
                case "CheckBoxHasFlrGirt":
                    TimberDraw.Properties.Settings.Default.HasFlrGirt = CheckBoxHasFlrGirt.Checked;
                    break;
                case "CheckBoxHasFlrGirtBrace":
                    TimberDraw.Properties.Settings.Default.HasFlrGirtBrace = CheckBoxHasFlrGirtBrace.Checked;
                    break;
            }
        }
        private void ButtonFloorGirtHt_Click(System.Object sender, System.EventArgs e)
        {
            Editor ed = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor;
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            PromptDoubleResult res = ed.GetDistance("Pick Floor Girt Distance");
            if (res.Status == PromptStatus.OK)
            {
                TextBoxFlrGirtHt.Text = res.Value.ToString();
            }
        }
        private void CheckBoxGenerateBayMembers_CheckedChanged(System.Object sender, System.EventArgs e)
        {
            TimberDraw.Properties.Settings.Default.GenerateBayMembers = CheckBoxGenerateBayMembers.Checked;
            Module1.GenerateBayMembers = CheckBoxGenerateBayMembers.Checked;
        }
        #endregion

        #region Roof Loads
        private void ButtonCheckLoading_Click(System.Object sender, System.EventArgs e)
        {
            double W = 0;
            double i = 0;
            double l = 0;
            var RoofLoad = TimberDraw.Properties.Settings.Default.RoofLoad;
            var BayWidth = TimberDraw.Properties.Settings.Default.BayWidth;
            var Span = TimberDraw.Properties.Settings.Default.Span;
            //Ridge
            if (CheckBoxHasRidge.Checked)
            {
                var RidgeWidth = TimberDraw.Properties.Settings.Default.RidgeWidth;
                var RidgeDepth = TimberDraw.Properties.Settings.Default.RidgeDepth;
                if (CheckBoxHasPurlins.Checked)
                    W = ((RoofLoad / 144) * 48) * BayWidth;
                if (CheckBoxHasCommons.Checked)
                    W = ((RoofLoad / 144) * (Span / 2)) * BayWidth;
                i = (RidgeWidth * (Math.Pow(RidgeDepth, 3))) / 12;
                l = BayWidth;
                while (Module1.MaxDeflection(W, Module1.Emod, i, l) > BayWidth / Module1.AllowableDeflection)
                {
                    RidgeDepth = RidgeDepth + 1;
                    i = (RidgeWidth * (Math.Pow(RidgeDepth, 3))) / 12;
                }
                TextBoxRidgeDepth.Text = Convert.ToString(RidgeDepth);
            }
            //Eave
            if (CheckBoxHasEaveGirt.Checked)
            {
                var EaveGirtWidth = TimberDraw.Properties.Settings.Default.EaveGirtWidth;
                var EaveGirtDepth = TimberDraw.Properties.Settings.Default.EaveGirtDepth;
                if (CheckBoxHasPurlins.Checked)
                    W = ((RoofLoad / 144) * 24) * BayWidth;
                if (CheckBoxHasCommons.Checked)
                    W = ((RoofLoad / 144) * (Span / 4)) * BayWidth;
                i = (EaveGirtWidth * (Math.Pow(EaveGirtDepth, 3))) / 12;
                l = BayWidth;
                while (Module1.MaxDeflection(W, Module1.Emod, i, l) > BayWidth / Module1.AllowableDeflection)
                {
                    EaveGirtDepth = EaveGirtDepth + 1;
                    i = (EaveGirtWidth * (Math.Pow(EaveGirtDepth, 3))) / 12;
                }
                TextBoxEaveGirtDepth.Text = Convert.ToString(EaveGirtDepth);
            }
            //Purlins
            if (CheckBoxHasPurlins.Checked)
            {
                double purlinWidth = TimberDraw.Properties.Settings.Default.PurlinWidth;
                double purlinDepth = TimberDraw.Properties.Settings.Default.PurlinDepth;
                W = ((RoofLoad / 144) * 48) * BayWidth;
                i = (purlinWidth * (Math.Pow(purlinDepth, 3))) / 12;
                l = BayWidth;
                while (Module1.MaxDeflection(W, Module1.Emod, i, l) > BayWidth / Module1.AllowableDeflection)
                {
                    purlinDepth = purlinDepth + 1;
                    i = (purlinWidth * (Math.Pow(purlinDepth, 3))) / 12;
                }
                TextBoxPurlinDepth.Text = Convert.ToString(purlinDepth);
            }
            //Common Rafter
            if (CheckBoxHasCommons.Checked)
            {
                double commonWidth = TimberDraw.Properties.Settings.Default.CommonWidth;
                double commonDepth = TimberDraw.Properties.Settings.Default.CommonDepth;
                int spaces = Convert.ToInt32(Math.Round(BayWidth / 48));
                double spacing = (BayWidth - (commonWidth * (spaces - 1))) / spaces;
                W = ((RoofLoad / 144) * spacing) * BayWidth;
                i = (commonWidth * (Math.Pow(commonDepth, 3))) / 12;
                l = Span / 2;
                while (Module1.MaxDeflection(W, Module1.Emod, i, l) > (Span / 2) / Module1.AllowableDeflection)
                {
                    commonDepth = commonDepth + 1;
                    i = (commonWidth * (Math.Pow(commonDepth, 3))) / 12;
                }
                TextBoxCommonDepth.Text = Convert.ToString(commonDepth);
            }
        }
        private void TextBoxRoofLoad_TextChanged(System.Object sender, System.EventArgs e)
        {
            Module1.RoofLoad = Convert.ToDouble(TextBoxRoofLoad.Text);
            TimberDraw.Properties.Settings.Default.RoofLoad = (int)Module1.RoofLoad;
        }
        private void TextBoxEmod_TextChanged(System.Object sender, System.EventArgs e)
        {
            Module1.Emod = Convert.ToDouble(TextBoxEmod.Text);
            TimberDraw.Properties.Settings.Default.Emod = (int)Module1.Emod;
        }
        private void TextBoxAllowableDeflection_TextChanged(System.Object sender, System.EventArgs e)
        {
            Module1.AllowableDeflection = Convert.ToDouble(TextBoxAllowableDeflection.Text);
            TimberDraw.Properties.Settings.Default.AllowableDeflection = (int)Module1.AllowableDeflection;
        }
        #endregion

        #region General Settings
        private void CheckBoxDeletePolylines_CheckedChanged(System.Object sender, System.EventArgs e)
        {
            TimberDraw.Properties.Settings.Default.DeletePolylines = CheckBoxDeletePolylines.Checked;
            Module1.DeletePolylines = TimberDraw.Properties.Settings.Default.DeletePolylines;
        }
        private void CheckBoxHasJoinery_CheckedChanged(System.Object sender, System.EventArgs e)
        {
            TimberDraw.Properties.Settings.Default.HasJoinery = CheckBoxHasJoinery.Checked;
            Module1.HasJoinery = TimberDraw.Properties.Settings.Default.HasJoinery;
        }
        private void CheckBoxMake3D_CheckStateChanged(System.Object sender, System.EventArgs e)
        {
            TimberDraw.Properties.Settings.Default.Make3D = CheckBoxMake3D.Checked;
            Module1.Make3D = TimberDraw.Properties.Settings.Default.Make3D;
        }
        private void CheckBoxShowEndMarkers_CheckedChanged(System.Object sender, System.EventArgs e)
        {
            Module1.ShowEndMarkers = CheckBoxShowEndMarkers.Checked;
        }
        private void CheckBoxShowToolTips_CheckedChanged(System.Object sender, System.EventArgs e)
        {
            TimberDraw.Properties.Settings.Default.ToolTips = CheckBoxShowToolTips.Checked;
        }
        #endregion

        public UserControl1()
        {
            Load += UserControl1_Load;
            InitializeComponent();
        }

    }
}
