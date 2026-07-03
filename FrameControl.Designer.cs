using System.Drawing;
using System.Windows.Forms;

namespace TimberDraw
{
    // Frame params palette (TFrame model). Rough layout -- intended to be tidied
    // in the VS WinForms designer. Only interactive controls are stored as fields;
    // static captions are added as local Labels.
    partial class FrameControl : System.Windows.Forms.UserControl
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        // Geometry
        private TextBox TextBoxSpan;
        private TextBox TextBoxEaveHeight;
        private TextBox TextBoxPitchRise;
        private TextBox TextBoxPitchRun;
        private CheckBox CheckBoxMake3D;

        // Bays
        private DataGridView DataGridViewBays;
        private DataGridViewTextBoxColumn colSpacing;
        private Label LabelBentCount;

        // Bent members
        private TextBox TextBoxPostWidth, TextBoxPostDepth;
        private TextBox TextBoxGirtWidth, TextBoxGirtDepth;
        private TextBox TextBoxRafterWidth, TextBoxRafterDepth;
        private TextBox TextBoxKpostWidth, TextBoxKpostDepth;
        private CheckBox CheckBoxHasStrut;
        private TextBox TextBoxStrutWidth, TextBoxStrutDepth, TextBoxStrutAngle;
        private CheckBox CheckBoxHasVStrut;
        private TextBox TextBoxVStrutWidth, TextBoxVStrutDepth;
        private CheckBox CheckBoxHasBrace;
        private TextBox TextBoxBraceWidth, TextBoxBraceDepth, TextBoxBraceLength, TextBoxBraceAngle;
        private RadioButton RadioButtonBack, RadioButtonCentered, RadioButtonFront;

        // Roof
        private RadioButton RadioButtonCommons, RadioButtonPurlins;
        private Panel panelCommons, panelPurlins;
        private RadioButton RadioButtonCommonByCount, RadioButtonCommonBySpacing;
        private TextBox TextBoxCommonCount, TextBoxCommonSpacing, TextBoxCommonWidth, TextBoxCommonDepth;
        private RadioButton RadioButtonPurlinByCount, RadioButtonPurlinBySpacing;
        private TextBox TextBoxPurlinCount, TextBoxPurlinSpacing, TextBoxPurlinWidth, TextBoxPurlinDepth;

        // Ridge
        private TextBox TextBoxRidgeWidth, TextBoxRidgeDepth;

        // Actions
        private Button ButtonDraw, ButtonSave, ButtonLoad;

        // Containers
        private FlowLayoutPanel panelRoot;
        private GroupBox groupGeometry, groupBays, groupBent, groupRoof, groupRidge;
        private Panel panelActions;

        private static Label Cap(string text, int x, int y)
        {
            return new Label { Text = text, Location = new Point(x, y), AutoSize = true };
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.SuspendLayout();

            // ---- root ----
            this.panelRoot = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true
            };

            int gbWidth = 400;
            // Two-column field metrics.
            int lLbl = 8, lW = 100, lD = 150;     // left column: label / width / depth
            int rLbl = 205, rW = 297, rD = 347;   // right column

            // ================= Geometry =================
            this.groupGeometry = new GroupBox { Text = "Geometry", Width = gbWidth, Height = 110 };
            this.TextBoxSpan = new TextBox { Name = "TextBoxSpan", Location = new Point(100, 22), Width = 90 };
            this.TextBoxEaveHeight = new TextBox { Name = "TextBoxEaveHeight", Location = new Point(300, 22), Width = 90 };
            this.TextBoxPitchRise = new TextBox { Name = "TextBoxPitchRise", Location = new Point(100, 50), Width = 90 };
            this.TextBoxPitchRun = new TextBox { Name = "TextBoxPitchRun", Location = new Point(300, 50), Width = 90 };
            this.CheckBoxMake3D = new CheckBox { Name = "CheckBoxMake3D", Text = "Make 3D", Location = new Point(12, 80), AutoSize = true };
            this.groupGeometry.Controls.Add(Cap("Span", 8, 25));
            this.groupGeometry.Controls.Add(Cap("Eave Height", 205, 25));
            this.groupGeometry.Controls.Add(Cap("Pitch Rise", 8, 53));
            this.groupGeometry.Controls.Add(Cap("Pitch Run", 205, 53));
            this.groupGeometry.Controls.Add(this.TextBoxSpan);
            this.groupGeometry.Controls.Add(this.TextBoxEaveHeight);
            this.groupGeometry.Controls.Add(this.TextBoxPitchRise);
            this.groupGeometry.Controls.Add(this.TextBoxPitchRun);
            this.groupGeometry.Controls.Add(this.CheckBoxMake3D);

            // ================= Bays =================
            this.groupBays = new GroupBox { Text = "Bays (center-to-center)", Width = gbWidth, Height = 152 };
            this.colSpacing = new DataGridViewTextBoxColumn { HeaderText = "Spacing (in)", Name = "colSpacing", Width = 130 };
            this.DataGridViewBays = new DataGridView
            {
                Name = "DataGridViewBays",
                Location = new Point(8, 20),
                Size = new Size(200, 120),
                AllowUserToAddRows = true,
                AllowUserToResizeRows = false,
                RowHeadersWidth = 50,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
            };
            this.DataGridViewBays.Columns.Add(this.colSpacing);
            this.LabelBentCount = new Label { Name = "LabelBentCount", Text = "Bents: 1", Location = new Point(228, 26), AutoSize = true, Font = new Font("Microsoft Sans Serif", 9f, FontStyle.Bold) };
            this.groupBays.Controls.Add(this.DataGridViewBays);
            this.groupBays.Controls.Add(this.LabelBentCount);
            this.groupBays.Controls.Add(Cap("One row per bay.\r\nSpacing is post-face\r\nto same post-face\r\non the next bent.", 228, 52));

            // ================= Bent (King Post) =================
            this.groupBent = new GroupBox { Text = "Bent (King Post)", Width = gbWidth, Height = 192 };
            this.groupBent.Controls.Add(Cap("W", lW, 18));
            this.groupBent.Controls.Add(Cap("D", lD, 18));
            this.groupBent.Controls.Add(Cap("W", rW, 18));
            this.groupBent.Controls.Add(Cap("D", rD, 18));
            // Left column: principal members
            this.TextBoxPostWidth = new TextBox { Name = "TextBoxPostWidth", Location = new Point(lW, 36), Width = 44 };
            this.TextBoxPostDepth = new TextBox { Name = "TextBoxPostDepth", Location = new Point(lD, 36), Width = 44 };
            this.TextBoxGirtWidth = new TextBox { Name = "TextBoxGirtWidth", Location = new Point(lW, 62), Width = 44 };
            this.TextBoxGirtDepth = new TextBox { Name = "TextBoxGirtDepth", Location = new Point(lD, 62), Width = 44 };
            this.TextBoxRafterWidth = new TextBox { Name = "TextBoxRafterWidth", Location = new Point(lW, 88), Width = 44 };
            this.TextBoxRafterDepth = new TextBox { Name = "TextBoxRafterDepth", Location = new Point(lD, 88), Width = 44 };
            this.TextBoxKpostWidth = new TextBox { Name = "TextBoxKpostWidth", Location = new Point(lW, 114), Width = 44 };
            this.TextBoxKpostDepth = new TextBox { Name = "TextBoxKpostDepth", Location = new Point(lD, 114), Width = 44 };
            this.groupBent.Controls.Add(Cap("Post", lLbl, 39));
            this.groupBent.Controls.Add(Cap("Girt", lLbl, 65));
            this.groupBent.Controls.Add(Cap("Rafter", lLbl, 91));
            this.groupBent.Controls.Add(Cap("King Post", lLbl, 117));
            this.groupBent.Controls.Add(this.TextBoxPostWidth);
            this.groupBent.Controls.Add(this.TextBoxPostDepth);
            this.groupBent.Controls.Add(this.TextBoxGirtWidth);
            this.groupBent.Controls.Add(this.TextBoxGirtDepth);
            this.groupBent.Controls.Add(this.TextBoxRafterWidth);
            this.groupBent.Controls.Add(this.TextBoxRafterDepth);
            this.groupBent.Controls.Add(this.TextBoxKpostWidth);
            this.groupBent.Controls.Add(this.TextBoxKpostDepth);
            // Right column: diagonals
            this.CheckBoxHasStrut = new CheckBox { Name = "CheckBoxHasStrut", Text = "Strut", Location = new Point(rLbl, 38), AutoSize = true };
            this.TextBoxStrutWidth = new TextBox { Name = "TextBoxStrutWidth", Location = new Point(rW, 36), Width = 44 };
            this.TextBoxStrutDepth = new TextBox { Name = "TextBoxStrutDepth", Location = new Point(rD, 36), Width = 44 };
            this.TextBoxStrutAngle = new TextBox { Name = "TextBoxStrutAngle", Location = new Point(rW, 62), Width = 44 };
            this.groupBent.Controls.Add(this.CheckBoxHasStrut);
            this.groupBent.Controls.Add(this.TextBoxStrutWidth);
            this.groupBent.Controls.Add(this.TextBoxStrutDepth);
            this.groupBent.Controls.Add(Cap("Strut Angle", rLbl + 8, 65));
            this.groupBent.Controls.Add(this.TextBoxStrutAngle);
            this.CheckBoxHasVStrut = new CheckBox { Name = "CheckBoxHasVStrut", Text = "Vert Strut", Location = new Point(rLbl, 90), AutoSize = true };
            this.TextBoxVStrutWidth = new TextBox { Name = "TextBoxVStrutWidth", Location = new Point(rW, 88), Width = 44 };
            this.TextBoxVStrutDepth = new TextBox { Name = "TextBoxVStrutDepth", Location = new Point(rD, 88), Width = 44 };
            this.groupBent.Controls.Add(this.CheckBoxHasVStrut);
            this.groupBent.Controls.Add(this.TextBoxVStrutWidth);
            this.groupBent.Controls.Add(this.TextBoxVStrutDepth);
            this.CheckBoxHasBrace = new CheckBox { Name = "CheckBoxHasBrace", Text = "Brace", Location = new Point(rLbl, 116), AutoSize = true };
            this.TextBoxBraceWidth = new TextBox { Name = "TextBoxBraceWidth", Location = new Point(rW, 114), Width = 44 };
            this.TextBoxBraceDepth = new TextBox { Name = "TextBoxBraceDepth", Location = new Point(rD, 114), Width = 44 };
            this.TextBoxBraceLength = new TextBox { Name = "TextBoxBraceLength", Location = new Point(rW, 140), Width = 44 };
            this.TextBoxBraceAngle = new TextBox { Name = "TextBoxBraceAngle", Location = new Point(rD, 140), Width = 44 };
            this.groupBent.Controls.Add(this.CheckBoxHasBrace);
            this.groupBent.Controls.Add(this.TextBoxBraceWidth);
            this.groupBent.Controls.Add(this.TextBoxBraceDepth);
            this.groupBent.Controls.Add(Cap("Len / Ang", rLbl + 8, 143));
            this.groupBent.Controls.Add(this.TextBoxBraceLength);
            this.groupBent.Controls.Add(this.TextBoxBraceAngle);
            // Placement (full width, bottom)
            this.groupBent.Controls.Add(Cap("Brace/Strut Placement", 8, 167));
            this.RadioButtonBack = new RadioButton { Name = "RadioButtonBack", Text = "Back", Location = new Point(150, 165), AutoSize = true };
            this.RadioButtonCentered = new RadioButton { Name = "RadioButtonCentered", Text = "Centered", Location = new Point(225, 165), AutoSize = true };
            this.RadioButtonFront = new RadioButton { Name = "RadioButtonFront", Text = "Front", Location = new Point(320, 165), AutoSize = true };
            this.groupBent.Controls.Add(this.RadioButtonBack);
            this.groupBent.Controls.Add(this.RadioButtonCentered);
            this.groupBent.Controls.Add(this.RadioButtonFront);

            // ================= Roof =================
            this.groupRoof = new GroupBox { Text = "Roof", Width = gbWidth, Height = 150 };
            this.groupRoof.Controls.Add(Cap("Roof Type", 8, 24));
            this.RadioButtonCommons = new RadioButton { Name = "RadioButtonCommons", Text = "Common Rafters", Location = new Point(100, 22), AutoSize = true };
            this.RadioButtonPurlins = new RadioButton { Name = "RadioButtonPurlins", Text = "Purlins", Location = new Point(250, 22), AutoSize = true };
            this.groupRoof.Controls.Add(this.RadioButtonCommons);
            this.groupRoof.Controls.Add(this.RadioButtonPurlins);

            // Commons sub-panel (hidden until Commons chosen)
            this.panelCommons = new Panel { Name = "panelCommons", Location = new Point(8, 48), Size = new Size(384, 92), Visible = false };
            this.RadioButtonCommonByCount = new RadioButton { Name = "RadioButtonCommonByCount", Text = "By Count", Location = new Point(96, 4), AutoSize = true };
            this.RadioButtonCommonBySpacing = new RadioButton { Name = "RadioButtonCommonBySpacing", Text = "By Spacing", Location = new Point(196, 4), AutoSize = true };
            this.TextBoxCommonCount = new TextBox { Name = "TextBoxCommonCount", Location = new Point(132, 32), Width = 44 };
            this.TextBoxCommonSpacing = new TextBox { Name = "TextBoxCommonSpacing", Location = new Point(186, 32), Width = 44 };
            this.TextBoxCommonWidth = new TextBox { Name = "TextBoxCommonWidth", Location = new Point(132, 60), Width = 44 };
            this.TextBoxCommonDepth = new TextBox { Name = "TextBoxCommonDepth", Location = new Point(186, 60), Width = 44 };
            this.panelCommons.Controls.Add(Cap("Spacing mode", 4, 6));
            this.panelCommons.Controls.Add(this.RadioButtonCommonByCount);
            this.panelCommons.Controls.Add(this.RadioButtonCommonBySpacing);
            this.panelCommons.Controls.Add(Cap("Count / Spacing", 4, 35));
            this.panelCommons.Controls.Add(this.TextBoxCommonCount);
            this.panelCommons.Controls.Add(this.TextBoxCommonSpacing);
            this.panelCommons.Controls.Add(Cap("Size  W / D", 4, 63));
            this.panelCommons.Controls.Add(this.TextBoxCommonWidth);
            this.panelCommons.Controls.Add(this.TextBoxCommonDepth);
            this.groupRoof.Controls.Add(this.panelCommons);

            // Purlins sub-panel (hidden until Purlins chosen; shares the same location)
            this.panelPurlins = new Panel { Name = "panelPurlins", Location = new Point(8, 48), Size = new Size(384, 92), Visible = false };
            this.RadioButtonPurlinByCount = new RadioButton { Name = "RadioButtonPurlinByCount", Text = "By Count", Location = new Point(96, 4), AutoSize = true };
            this.RadioButtonPurlinBySpacing = new RadioButton { Name = "RadioButtonPurlinBySpacing", Text = "By Spacing", Location = new Point(196, 4), AutoSize = true };
            this.TextBoxPurlinCount = new TextBox { Name = "TextBoxPurlinCount", Location = new Point(132, 32), Width = 44 };
            this.TextBoxPurlinSpacing = new TextBox { Name = "TextBoxPurlinSpacing", Location = new Point(186, 32), Width = 44 };
            this.TextBoxPurlinWidth = new TextBox { Name = "TextBoxPurlinWidth", Location = new Point(132, 60), Width = 44 };
            this.TextBoxPurlinDepth = new TextBox { Name = "TextBoxPurlinDepth", Location = new Point(186, 60), Width = 44 };
            this.panelPurlins.Controls.Add(Cap("Spacing mode", 4, 6));
            this.panelPurlins.Controls.Add(this.RadioButtonPurlinByCount);
            this.panelPurlins.Controls.Add(this.RadioButtonPurlinBySpacing);
            this.panelPurlins.Controls.Add(Cap("Count / Spacing", 4, 35));
            this.panelPurlins.Controls.Add(this.TextBoxPurlinCount);
            this.panelPurlins.Controls.Add(this.TextBoxPurlinSpacing);
            this.panelPurlins.Controls.Add(Cap("Size  W / D", 4, 63));
            this.panelPurlins.Controls.Add(this.TextBoxPurlinWidth);
            this.panelPurlins.Controls.Add(this.TextBoxPurlinDepth);
            this.groupRoof.Controls.Add(this.panelPurlins);

            // ================= Ridge =================
            this.groupRidge = new GroupBox { Text = "Ridge", Width = gbWidth, Height = 56 };
            this.TextBoxRidgeWidth = new TextBox { Name = "TextBoxRidgeWidth", Location = new Point(140, 22), Width = 44 };
            this.TextBoxRidgeDepth = new TextBox { Name = "TextBoxRidgeDepth", Location = new Point(194, 22), Width = 44 };
            this.groupRidge.Controls.Add(Cap("Ridge  W / D", 8, 25));
            this.groupRidge.Controls.Add(this.TextBoxRidgeWidth);
            this.groupRidge.Controls.Add(this.TextBoxRidgeDepth);

            // ================= Actions =================
            this.panelActions = new Panel { Width = gbWidth, Height = 84 };
            this.ButtonDraw = new Button { Name = "ButtonDraw", Text = "Draw Frame", Location = new Point(8, 8), Size = new Size(384, 30) };
            this.ButtonSave = new Button { Name = "ButtonSave", Text = "Save", Location = new Point(8, 44), Size = new Size(188, 28) };
            this.ButtonLoad = new Button { Name = "ButtonLoad", Text = "Load", Location = new Point(204, 44), Size = new Size(188, 28) };
            this.panelActions.Controls.Add(this.ButtonDraw);
            this.panelActions.Controls.Add(this.ButtonSave);
            this.panelActions.Controls.Add(this.ButtonLoad);

            // ---- assemble ----
            this.panelRoot.Controls.Add(this.groupGeometry);
            this.panelRoot.Controls.Add(this.groupBays);
            this.panelRoot.Controls.Add(this.groupBent);
            this.panelRoot.Controls.Add(this.groupRoof);
            this.panelRoot.Controls.Add(this.groupRidge);
            this.panelRoot.Controls.Add(this.panelActions);

            this.Controls.Add(this.panelRoot);
            this.Name = "FrameControl";
            this.Size = new Size(430, 700);
            this.ResumeLayout(false);
        }
    }
}
