using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{
    // Frame-shaped palette for the TFrame node/edge model. Persists through
    // Settings.Default (same store the legacy palette uses) and drives the
    // Commands.DrawFrame / SaveFrame / LoadFrame entry points via the buttons.
    // KingPost bent only -- the only bent type the Frame core renders today.
    public partial class FrameControl
    {
        private bool _loading;   // suppress flush handlers while populating from settings

        public FrameControl()
        {
            InitializeComponent();
            Load += FrameControl_Load;
        }

        private static TimberDraw.Properties.Settings S => TimberDraw.Properties.Settings.Default;

        private void FrameControl_Load(object sender, EventArgs e)
        {
            LoadSettings();
            WireEvents();
            UpdateRoofVisible();
        }

        // ---------------------------------------------------------------- load
        private void LoadSettings()
        {
            _loading = true;
            try
            {
                // Geometry (distance fields use the drawing's unit format)
                TextBoxSpan.Text = Converter.DistanceToString(S.Span, DistanceUnitFormat.Current, -1);
                TextBoxEaveHeight.Text = Converter.DistanceToString(S.EaveHt, DistanceUnitFormat.Current, -1);
                TextBoxPitchRise.Text = Converter.DistanceToString(S.RoofPitchRise, DistanceUnitFormat.Current, -1);
                TextBoxPitchRun.Text = Converter.DistanceToString(S.RoofPitchRun, DistanceUnitFormat.Current, -1);
                CheckBoxMake3D.Checked = S.Make3D;

                // Bays
                LoadBayGrid();

                // Bent members (plain inch values)
                TextBoxPostWidth.Text = Num(S.KPPostWidth);
                TextBoxPostDepth.Text = Num(S.KPPostDepth);
                TextBoxGirtWidth.Text = Num(S.KPGirtWidth);
                TextBoxGirtDepth.Text = Num(S.KPGirtDepth);
                TextBoxRafterWidth.Text = Num(S.KPRafterWidth);
                TextBoxRafterDepth.Text = Num(S.KPRafterDepth);
                TextBoxKpostWidth.Text = Num(S.KPKpostWidth);
                TextBoxKpostDepth.Text = Num(S.KPKpostDepth);

                CheckBoxHasStrut.Checked = S.KPHasStrut;
                TextBoxStrutWidth.Text = Num(S.KPStrutWidth);
                TextBoxStrutDepth.Text = Num(S.KPStrutDepth);
                TextBoxStrutAngle.Text = Num(S.KPStrutAngle);

                CheckBoxHasVStrut.Checked = S.KPHasVStrut;
                TextBoxVStrutWidth.Text = Num(S.KPVStrutWidth);
                TextBoxVStrutDepth.Text = Num(S.KPVStrutDepth);

                CheckBoxHasBrace.Checked = S.KPHasBrace;
                TextBoxBraceWidth.Text = Num(S.KPBraceWidth);
                TextBoxBraceDepth.Text = Num(S.KPBraceDepth);
                TextBoxBraceLength.Text = Num(S.KPBraceLength);
                TextBoxBraceAngle.Text = Num(S.KPBraceAngle);

                switch (S.BraceStrutPlacement)
                {
                    case 0: RadioButtonBack.Checked = true; break;
                    case 1: RadioButtonCentered.Checked = true; break;
                    case 2: RadioButtonFront.Checked = true; break;
                }

                // Roof type starts UNSELECTED so neither sub-block shows until the
                // user picks a type (per UI preference). The inner mode/values still
                // load here so they're correct the moment a type is revealed.
                RadioButtonCommons.Checked = false;
                RadioButtonPurlins.Checked = false;

                RadioButtonCommonByCount.Checked = S.CommonMode == 0;
                RadioButtonCommonBySpacing.Checked = S.CommonMode != 0;
                TextBoxCommonCount.Text = S.CommonCount.ToString();
                TextBoxCommonSpacing.Text = Num(S.CommonSpacing);
                TextBoxCommonWidth.Text = Num(S.CommonWidth);
                TextBoxCommonDepth.Text = Num(S.CommonDepth);

                RadioButtonPurlinByCount.Checked = S.PurlinMode == 0;
                RadioButtonPurlinBySpacing.Checked = S.PurlinMode != 0;
                TextBoxPurlinCount.Text = S.PurlinCount.ToString();
                TextBoxPurlinSpacing.Text = Num(S.PurlinSpacing);
                TextBoxPurlinWidth.Text = Num(S.PurlinWidth);
                TextBoxPurlinDepth.Text = Num(S.PurlinDepth);

                // Ridge
                TextBoxRidgeWidth.Text = Num(S.RidgeWidth);
                TextBoxRidgeDepth.Text = Num(S.RidgeDepth);
            }
            finally { _loading = false; }
        }

        private void LoadBayGrid()
        {
            DataGridViewBays.Rows.Clear();
            foreach (string tok in (S.BaySchedule ?? "").Split(','))
            {
                string t = tok.Trim();
                if (t.Length == 0) continue;
                DataGridViewBays.Rows.Add(t);
            }
            UpdateBentCount();
        }

        private static string Num(double v) => v.ToString(CultureInfo.CurrentCulture);

        // -------------------------------------------------------------- events
        private void WireEvents()
        {
            // distance fields
            TextBoxSpan.Validated += (s, e) => FlushDistance(TextBoxSpan, v => S.Span = v);
            TextBoxEaveHeight.Validated += (s, e) => FlushDistance(TextBoxEaveHeight, v => S.EaveHt = v);
            TextBoxPitchRise.Validated += (s, e) => FlushDistance(TextBoxPitchRise, v => S.RoofPitchRise = v);
            TextBoxPitchRun.Validated += (s, e) => FlushDistance(TextBoxPitchRun, v => S.RoofPitchRun = v);
            CheckBoxMake3D.CheckedChanged += (s, e) => { if (!_loading) S.Make3D = CheckBoxMake3D.Checked; };

            // bent member sizes
            TextBoxPostWidth.Validated += (s, e) => FlushDouble(TextBoxPostWidth, v => S.KPPostWidth = v);
            TextBoxPostDepth.Validated += (s, e) => FlushDouble(TextBoxPostDepth, v => S.KPPostDepth = v);
            TextBoxGirtWidth.Validated += (s, e) => FlushDouble(TextBoxGirtWidth, v => S.KPGirtWidth = v);
            TextBoxGirtDepth.Validated += (s, e) => FlushDouble(TextBoxGirtDepth, v => S.KPGirtDepth = v);
            TextBoxRafterWidth.Validated += (s, e) => FlushDouble(TextBoxRafterWidth, v => S.KPRafterWidth = v);
            TextBoxRafterDepth.Validated += (s, e) => FlushDouble(TextBoxRafterDepth, v => S.KPRafterDepth = v);
            TextBoxKpostWidth.Validated += (s, e) => FlushDouble(TextBoxKpostWidth, v => S.KPKpostWidth = v);
            TextBoxKpostDepth.Validated += (s, e) => FlushDouble(TextBoxKpostDepth, v => S.KPKpostDepth = v);

            CheckBoxHasStrut.CheckedChanged += (s, e) => { if (!_loading) S.KPHasStrut = CheckBoxHasStrut.Checked; };
            TextBoxStrutWidth.Validated += (s, e) => FlushDouble(TextBoxStrutWidth, v => S.KPStrutWidth = v);
            TextBoxStrutDepth.Validated += (s, e) => FlushDouble(TextBoxStrutDepth, v => S.KPStrutDepth = v);
            TextBoxStrutAngle.Validated += (s, e) => FlushDouble(TextBoxStrutAngle, v => S.KPStrutAngle = v);

            CheckBoxHasVStrut.CheckedChanged += (s, e) => { if (!_loading) S.KPHasVStrut = CheckBoxHasVStrut.Checked; };
            TextBoxVStrutWidth.Validated += (s, e) => FlushDouble(TextBoxVStrutWidth, v => S.KPVStrutWidth = v);
            TextBoxVStrutDepth.Validated += (s, e) => FlushDouble(TextBoxVStrutDepth, v => S.KPVStrutDepth = v);

            CheckBoxHasBrace.CheckedChanged += (s, e) => { if (!_loading) S.KPHasBrace = CheckBoxHasBrace.Checked; };
            TextBoxBraceWidth.Validated += (s, e) => FlushDouble(TextBoxBraceWidth, v => S.KPBraceWidth = v);
            TextBoxBraceDepth.Validated += (s, e) => FlushDouble(TextBoxBraceDepth, v => S.KPBraceDepth = v);
            TextBoxBraceLength.Validated += (s, e) => FlushDouble(TextBoxBraceLength, v => S.KPBraceLength = v);
            TextBoxBraceAngle.Validated += (s, e) => FlushDouble(TextBoxBraceAngle, v => S.KPBraceAngle = v);

            RadioButtonBack.CheckedChanged += (s, e) => { if (!_loading && RadioButtonBack.Checked) S.BraceStrutPlacement = 0; };
            RadioButtonCentered.CheckedChanged += (s, e) => { if (!_loading && RadioButtonCentered.Checked) S.BraceStrutPlacement = 1; };
            RadioButtonFront.CheckedChanged += (s, e) => { if (!_loading && RadioButtonFront.Checked) S.BraceStrutPlacement = 2; };

            // roof mode
            RadioButtonCommons.CheckedChanged += (s, e) => { if (RadioButtonCommons.Checked) { if (!_loading) S.HasPurlins = false; UpdateRoofVisible(); } };
            RadioButtonPurlins.CheckedChanged += (s, e) => { if (RadioButtonPurlins.Checked) { if (!_loading) S.HasPurlins = true; UpdateRoofVisible(); } };

            RadioButtonCommonByCount.CheckedChanged += (s, e) => { if (!_loading && RadioButtonCommonByCount.Checked) S.CommonMode = 0; };
            RadioButtonCommonBySpacing.CheckedChanged += (s, e) => { if (!_loading && RadioButtonCommonBySpacing.Checked) S.CommonMode = 1; };
            TextBoxCommonCount.Validated += (s, e) => FlushInt(TextBoxCommonCount, v => S.CommonCount = v);
            TextBoxCommonSpacing.Validated += (s, e) => FlushDouble(TextBoxCommonSpacing, v => S.CommonSpacing = v);
            TextBoxCommonWidth.Validated += (s, e) => FlushDouble(TextBoxCommonWidth, v => S.CommonWidth = v);
            TextBoxCommonDepth.Validated += (s, e) => FlushDouble(TextBoxCommonDepth, v => S.CommonDepth = v);

            RadioButtonPurlinByCount.CheckedChanged += (s, e) => { if (!_loading && RadioButtonPurlinByCount.Checked) S.PurlinMode = 0; };
            RadioButtonPurlinBySpacing.CheckedChanged += (s, e) => { if (!_loading && RadioButtonPurlinBySpacing.Checked) S.PurlinMode = 1; };
            TextBoxPurlinCount.Validated += (s, e) => FlushInt(TextBoxPurlinCount, v => S.PurlinCount = v);
            TextBoxPurlinSpacing.Validated += (s, e) => FlushDouble(TextBoxPurlinSpacing, v => S.PurlinSpacing = v);
            TextBoxPurlinWidth.Validated += (s, e) => FlushDouble(TextBoxPurlinWidth, v => S.PurlinWidth = v);
            TextBoxPurlinDepth.Validated += (s, e) => FlushDouble(TextBoxPurlinDepth, v => S.PurlinDepth = v);

            // ridge
            TextBoxRidgeWidth.Validated += (s, e) => FlushDouble(TextBoxRidgeWidth, v => S.RidgeWidth = v);
            TextBoxRidgeDepth.Validated += (s, e) => FlushDouble(TextBoxRidgeDepth, v => S.RidgeDepth = v);

            // bay grid
            DataGridViewBays.CellEndEdit += (s, e) => FlushBaySchedule();
            DataGridViewBays.RowsRemoved += (s, e) => FlushBaySchedule();
            DataGridViewBays.UserDeletingRow += (s, e) => BeginInvoke((Action)FlushBaySchedule);

            // actions
            ButtonDraw.Click += (s, e) => Commands.DrawFrame();
            ButtonSave.Click += (s, e) => Commands.SaveFrame();
            ButtonLoad.Click += (s, e) => Commands.LoadFrame();
        }

        // Only the chosen roof type's sub-block is shown; until a type is picked,
        // neither is visible (no irrelevant options on screen).
        private void UpdateRoofVisible()
        {
            panelCommons.Visible = RadioButtonCommons.Checked;
            panelPurlins.Visible = RadioButtonPurlins.Checked;
        }

        // -------------------------------------------------------------- flush
        private void FlushDistance(TextBox tb, Action<double> set)
        {
            if (_loading) return;
            try { set(Converter.StringToDistance(tb.Text, DistanceUnitFormat.Current)); }
            catch { Warn(tb); }
        }

        private void FlushDouble(TextBox tb, Action<double> set)
        {
            if (_loading) return;
            if (double.TryParse(tb.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out double v)) set(v);
            else Warn(tb);
        }

        private void FlushInt(TextBox tb, Action<int> set)
        {
            if (_loading) return;
            if (int.TryParse(tb.Text, out int v)) set(v);
            else Warn(tb);
        }

        private void FlushBaySchedule()
        {
            if (_loading) return;
            var parts = new List<string>();
            foreach (DataGridViewRow row in DataGridViewBays.Rows)
            {
                if (row.IsNewRow) continue;
                string raw = Convert.ToString(row.Cells[0].Value)?.Trim();
                if (string.IsNullOrEmpty(raw)) continue;
                if (double.TryParse(raw, NumberStyles.Any, CultureInfo.CurrentCulture, out double v) && v > 0)
                    parts.Add(v.ToString(CultureInfo.InvariantCulture));
            }
            S.BaySchedule = string.Join(",", parts);
            UpdateBentCount();
        }

        private void UpdateBentCount()
        {
            int bays = 0;
            foreach (DataGridViewRow row in DataGridViewBays.Rows)
                if (!row.IsNewRow && !string.IsNullOrWhiteSpace(Convert.ToString(row.Cells[0].Value))) bays++;
            LabelBentCount.Text = "Bents: " + (bays + 1);
        }

        private static void Warn(TextBox tb)
        {
            MessageBox.Show("Input Error.  Check Units", "TimberDraw", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            tb.Focus();
        }
    }
}
