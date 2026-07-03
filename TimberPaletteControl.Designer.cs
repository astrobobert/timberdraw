using System.Drawing;
using System.Windows.Forms;

namespace TimberDraw
{
    // Managed-timber assembly palette (TPanel). A sticky "section" (type + W x D) plus verb
    // buttons that fire the managed commands (TPlace/TSpan/TJoin/TFit/TScarf/TScan/TMove/TRotate)
    // and orientation presets that set the UCS. Layout is hand-coded here; tidy in the VS designer.
    partial class TimberPaletteControl : System.Windows.Forms.UserControl
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        // Section editor
        private TreeView treeSections;
        private TextBox txtType, txtWidth, txtDepth;
        private Button btnAdd, btnUpdate, btnRemove;

        // Brace spec (foot/head legs + angle; the two checked rows compute the third)
        private CheckBox chkFoot, chkHead, chkAngle;
        private TextBox txtFoot, txtHead, txtAngle;
        private GroupBox groupBrace;

        // Orientation presets
        private Button btnPlan, btnBent, btnWall;

        // Place & connect verbs
        private Button btnPlace, btnSpan, btnJoin, btnFit, btnScarf;

        // Edit verbs
        private Button btnMove, btnRotate, btnScan, btnAssign, btnSection;

        // Footer
        private Label lblBuild;

        // Containers: a TabControl (Edit | TBD). The "Edit" tab holds every group (section, orientation,
        // place & connect, brace, edit & nodes); "TBD" is an empty placeholder for future tools. (The
        // Rough-in / Generate tab is gone -- generation + the freeze break live in TDraw's tree editor.)
        private TabControl tabs;
        private TabPage pageEdit, pageTbd;
        private FlowLayoutPanel flowEdit, flowTbd;
        private GroupBox groupSection, groupOrient, groupPlace, groupEdit;

        // A scrolling top-down flow panel that fills its tab page.
        private static FlowLayoutPanel TabFlow()
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(2)
            };
        }

        private static Label Cap(string text, int x, int y)
        {
            return new Label { Text = text, Location = new Point(x, y), AutoSize = true };
        }

        private static Button Btn(string text, int x, int y, int w)
        {
            return new Button { Text = text, Location = new Point(x, y), Size = new Size(w, 25) };
        }

        private static CheckBox Chk(string text, int x, int y)
        {
            return new CheckBox { Text = text, Location = new Point(x, y), AutoSize = true };
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();

            const int GW = 234;   // group width

            // ---- Section group (tree doubled in height; data entry stacked; buttons in a row) ----
            treeSections = new TreeView
            {
                Location = new Point(8, 18),
                Size = new Size(GW - 16, 184),   // doubled height (was 92)
                HideSelection = false,
                ShowLines = true,
                ShowRootLines = true
            };
            // Stacked data entry: Type, then W, then D -- each label left, field on its own row.
            txtType  = new TextBox { Location = new Point(48, 210), Size = new Size(174, 22) };
            txtWidth = new TextBox { Location = new Point(48, 238), Size = new Size(60, 22) };
            txtDepth = new TextBox { Location = new Point(48, 266), Size = new Size(60, 22) };
            // Buttons in a row (3 columns) below the fields.
            btnAdd    = Btn("Add",     8, 298, 70);
            btnUpdate = Btn("Update", 82, 298, 70);
            btnRemove = Btn("Remove", 156, 298, 70);
            groupSection = new GroupBox { Text = "Section", Location = new Point(8, 6), Size = new Size(GW, 336) };
            groupSection.Controls.Add(treeSections);
            groupSection.Controls.Add(Cap("Type", 12, 213));
            groupSection.Controls.Add(txtType);
            groupSection.Controls.Add(Cap("W", 12, 241));
            groupSection.Controls.Add(txtWidth);
            groupSection.Controls.Add(Cap("D", 12, 269));
            groupSection.Controls.Add(txtDepth);
            groupSection.Controls.Add(btnAdd);
            groupSection.Controls.Add(btnUpdate);
            groupSection.Controls.Add(btnRemove);

            // ---- Orientation group ----
            btnPlan = Btn("Plan", 8, 18, 70);
            btnBent = Btn("Bent", 82, 18, 70);
            btnWall = Btn("Wall", 156, 18, 70);
            groupOrient = new GroupBox { Text = "Orientation (UCS)", Location = new Point(8, 218), Size = new Size(GW, 52) };
            groupOrient.Controls.Add(btnPlan);
            groupOrient.Controls.Add(btnBent);
            groupOrient.Controls.Add(btnWall);

            // ---- Place & connect group (Brace moved to the Brace group) ----
            btnPlace = Btn("Place", 8, 18, 70);
            btnSpan  = Btn("Span", 82, 18, 70);
            btnFit   = Btn("Fit", 156, 18, 70);
            btnScarf = Btn("Scarf", 8, 47, 70);
            groupPlace = new GroupBox { Text = "Place & connect", Location = new Point(8, 276), Size = new Size(GW, 82) };
            groupPlace.Controls.Add(btnPlace);
            groupPlace.Controls.Add(btnSpan);
            groupPlace.Controls.Add(btnFit);
            groupPlace.Controls.Add(btnScarf);

            // ---- Brace group (foot/head legs + angle; check two, the third is computed). The Brace
            //      verb button (TJoin) lives here now, beside the spec it consumes. ----
            chkFoot = Chk("Foot leg", 12, 22);
            chkHead = Chk("Head leg", 12, 50);
            chkAngle = Chk("Angle (deg)", 12, 78);
            txtFoot = new TextBox { Location = new Point(150, 19), Size = new Size(60, 22) };
            txtHead = new TextBox { Location = new Point(150, 47), Size = new Size(60, 22) };
            txtAngle = new TextBox { Location = new Point(150, 75), Size = new Size(60, 22) };
            btnJoin = Btn("Brace", 8, 106, 70);   // TJoin -- connect two picked timbers with a brace
            groupBrace = new GroupBox { Text = "Brace (knee)", Location = new Point(8, 0), Size = new Size(GW, 141) };
            groupBrace.Controls.Add(chkFoot);
            groupBrace.Controls.Add(chkHead);
            groupBrace.Controls.Add(chkAngle);
            groupBrace.Controls.Add(txtFoot);
            groupBrace.Controls.Add(txtHead);
            groupBrace.Controls.Add(txtAngle);
            groupBrace.Controls.Add(btnJoin);

            // ---- Edit group ----
            btnMove = Btn("Move", 8, 18, 70);
            btnRotate = Btn("Rotate", 82, 18, 70);
            btnScan = Btn("Scan", 156, 18, 70);
            btnAssign = Btn("Assign", 8, 47, 70);   // assign selected timbers to a frame/bent/wall (TAssign)
            btnSection = Btn("Section", 82, 47, 70); // re-section any managed timber in place (TSection)
            groupEdit = new GroupBox { Text = "Edit & nodes", Location = new Point(8, 364), Size = new Size(GW, 82) };
            groupEdit.Controls.Add(btnMove);
            groupEdit.Controls.Add(btnRotate);
            groupEdit.Controls.Add(btnScan);
            groupEdit.Controls.Add(btnAssign);
            groupEdit.Controls.Add(btnSection);

            // ---- Footer: build stamp (so a stale NETLOAD is obvious) ----
            lblBuild = new Label
            {
                Text = "Build ...",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(6, 6, 0, 6)
            };

            // ---- Root: two tabs (Edit | TBD). The "Edit" tab holds every group; "TBD" is empty. ----
            flowEdit = TabFlow();
            flowEdit.Controls.Add(groupSection);
            flowEdit.Controls.Add(groupOrient);
            flowEdit.Controls.Add(groupPlace);
            flowEdit.Controls.Add(groupBrace);
            flowEdit.Controls.Add(groupEdit);

            flowTbd = TabFlow();   // empty placeholder for future tools

            pageEdit = new TabPage("Edit") { UseVisualStyleBackColor = true };
            pageEdit.Controls.Add(flowEdit);
            pageTbd  = new TabPage("TBD")  { UseVisualStyleBackColor = true };
            pageTbd.Controls.Add(flowTbd);

            tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(pageEdit);
            tabs.TabPages.Add(pageTbd);

            // Footer docks at the bottom (below the tabs). SendToBack puts the fill control at the
            // back of the z-order so the bottom strip is reserved first, then the tabs fill the rest.
            lblBuild.Dock = DockStyle.Bottom;
            this.Controls.Add(tabs);
            this.Controls.Add(lblBuild);
            tabs.SendToBack();
            this.Name = "TimberPaletteControl";
            this.Size = new Size(GW + 24, 560);
        }
    }
}
