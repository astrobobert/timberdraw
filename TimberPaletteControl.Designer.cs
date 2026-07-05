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
        private Panel groupBrace;

        // Orientation presets
        private Button btnPlan, btnBent, btnWall;

        // Place & connect verbs
        private Button btnPlace, btnSpan, btnJoin, btnFit, btnScarf, btnJoist;

        // Edit verbs
        private Button btnMove, btnRotate, btnScan, btnAssign, btnSection;

        // Assembly (the TAssign target names, visible + editable). The second coordinate box is
        // dual-purpose: the COLUMN letter for a Bent owner (grid intersection 2C), the BAY for a Wall.
        private ComboBox cmbAsmKind;
        private TextBox txtAsmFrame, txtAsmOwner, txtAsmBay;
        private Label lblAsmExtra;
        private Panel groupAssembly;

        // Footer
        private Label lblBuild;

        // Container: ONE scrolling top-down flow of flat SECTIONS (section, orientation, place &
        // connect, brace, assembly, edit & nodes) -- a bold header band over pinned-dark content,
        // the same idiom as the property grids and the Browser. GroupBox is banned here: the
        // visual-styles renderer paints its frame/caption via UxTheme and ignores control colors.
        private FlowLayoutPanel flowEdit;
        private Panel groupSection, groupOrient, groupPlace, groupEdit;

        // A flat section: bold header band docked over a pinned-dark panel. Children keep the
        // same inner coordinates a GroupBox gave them (its caption inset and the header band are
        // both ~18-20px), so the pixel layouts below carry over unchanged. The header must dock
        // TOP here: PaneRows.HeaderCell is Dock=Fill for table cells, and Fill inside a plain
        // Panel would cover the whole section and swallow every click.
        private static Panel Section(string title, int x, int y, int w, int h)
        {
            var p = new Panel { Location = new Point(x, y), Size = new Size(w, h), BackColor = Theme.Bg };
            System.Windows.Forms.Label hdr = PaneRows.HeaderCell(title);
            hdr.Dock = DockStyle.Top;
            p.Controls.Add(hdr);
            return p;
        }

        // A scrolling top-down flow panel that fills the control.
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

        // Pixel-placed control helpers, styled through the shared Theme.
        private static Label Cap(string text, int x, int y)
        {
            Label l = Theme.Caption(text);
            l.Location = new Point(x, y);
            l.Padding = new Padding(0);
            return l;
        }

        private static Button Btn(string text, int x, int y, int w)
        {
            Button b = Theme.Button(text);
            b.Location = new Point(x, y);
            b.Size = new Size(w, 25);
            return b;
        }

        private static CheckBox Chk(string text, int x, int y)
        {
            CheckBox c = Theme.Check(text);
            c.Location = new Point(x, y);
            return c;
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();

            const int GW = 234;   // group width

            // Inner coordinates: each section's content sits below the 20px header band -- the old
            // GroupBox layouts carried over shifted +6px (caption inset was ~18px).

            // ---- Section (tree; data entry stacked; buttons in a row) ----
            treeSections = new TreeView
            {
                Location = new Point(8, 24),
                Size = new Size(GW - 16, 184),
                HideSelection = false,
                ShowLines = true,
                ShowRootLines = true
            };
            // Stacked data entry: Type, then W, then D -- each label left, field on its own row.
            txtType  = new TextBox { Location = new Point(48, 216), Size = new Size(174, 22) };
            txtWidth = new TextBox { Location = new Point(48, 244), Size = new Size(60, 22) };
            txtDepth = new TextBox { Location = new Point(48, 272), Size = new Size(60, 22) };
            // Buttons in a row (3 columns) below the fields.
            btnAdd    = Btn("Add",     8, 304, 70);
            btnUpdate = Btn("Update", 82, 304, 70);
            btnRemove = Btn("Remove", 156, 304, 70);
            groupSection = Section("Section", 8, 6, GW, 342);
            groupSection.Controls.Add(treeSections);
            groupSection.Controls.Add(Cap("Type", 12, 219));
            groupSection.Controls.Add(txtType);
            groupSection.Controls.Add(Cap("W", 12, 247));
            groupSection.Controls.Add(txtWidth);
            groupSection.Controls.Add(Cap("D", 12, 275));
            groupSection.Controls.Add(txtDepth);
            groupSection.Controls.Add(btnAdd);
            groupSection.Controls.Add(btnUpdate);
            groupSection.Controls.Add(btnRemove);

            // ---- Orientation ----
            btnPlan = Btn("Plan", 8, 24, 70);
            btnBent = Btn("Bent", 82, 24, 70);
            btnWall = Btn("Wall", 156, 24, 70);
            groupOrient = Section("Orientation (UCS)", 8, 218, GW, 58);
            groupOrient.Controls.Add(btnPlan);
            groupOrient.Controls.Add(btnBent);
            groupOrient.Controls.Add(btnWall);

            // ---- Place & connect (Brace moved to the Brace section) ----
            btnPlace = Btn("Place", 8, 24, 70);
            btnSpan  = Btn("Span", 82, 24, 70);
            btnFit   = Btn("Fit", 156, 24, 70);
            btnScarf = Btn("Scarf", 8, 53, 70);
            btnJoist = Btn("Joist", 82, 53, 70);
            groupPlace = Section("Place & connect", 8, 276, GW, 88);
            groupPlace.Controls.Add(btnPlace);
            groupPlace.Controls.Add(btnSpan);
            groupPlace.Controls.Add(btnFit);
            groupPlace.Controls.Add(btnScarf);
            groupPlace.Controls.Add(btnJoist);

            // ---- Brace (foot/head legs + angle; check two, the third is computed). The Brace
            //      verb button (TJoin) lives here, beside the spec it consumes. ----
            chkFoot = Chk("Foot leg", 12, 28);
            chkHead = Chk("Head leg", 12, 56);
            chkAngle = Chk("Angle (deg)", 12, 84);
            txtFoot = new TextBox { Location = new Point(150, 25), Size = new Size(60, 22) };
            txtHead = new TextBox { Location = new Point(150, 53), Size = new Size(60, 22) };
            txtAngle = new TextBox { Location = new Point(150, 81), Size = new Size(60, 22) };
            btnJoin = Btn("Brace", 8, 112, 70);   // TJoin -- connect two picked timbers with a brace
            groupBrace = Section("Brace (knee)", 8, 0, GW, 147);
            groupBrace.Controls.Add(chkFoot);
            groupBrace.Controls.Add(chkHead);
            groupBrace.Controls.Add(chkAngle);
            groupBrace.Controls.Add(txtFoot);
            groupBrace.Controls.Add(txtHead);
            groupBrace.Controls.Add(txtAngle);
            groupBrace.Controls.Add(btnJoin);

            // ---- Assembly (the TAssign target names, visible + editable; the Assign verb
            //      lives beside the names it consumes, like the Brace button in its section) ----
            txtAsmFrame = new TextBox { Location = new Point(60, 25), Size = new Size(40, 22) };
            cmbAsmKind  = new ComboBox { Location = new Point(12, 53), Size = new Size(70, 22), DropDownStyle = ComboBoxStyle.DropDownList };
            txtAsmOwner = new TextBox { Location = new Point(86, 53), Size = new Size(48, 22) };
            txtAsmBay   = new TextBox { Location = new Point(176, 53), Size = new Size(46, 22) };
            btnAssign   = Btn("Assign", 8, 82, 70);   // assign selected timbers (TAssign) to these names
            groupAssembly = Section("Assembly", 8, 0, GW, 116);
            lblAsmExtra = Cap("Col", 142, 56);
            groupAssembly.Controls.Add(Cap("Frame", 12, 28));
            groupAssembly.Controls.Add(txtAsmFrame);
            groupAssembly.Controls.Add(cmbAsmKind);
            groupAssembly.Controls.Add(txtAsmOwner);
            groupAssembly.Controls.Add(lblAsmExtra);
            groupAssembly.Controls.Add(txtAsmBay);
            groupAssembly.Controls.Add(btnAssign);

            // ---- Edit & nodes (Assign moved to the Assembly section) ----
            btnMove = Btn("Move", 8, 24, 70);
            btnRotate = Btn("Rotate", 82, 24, 70);
            btnScan = Btn("Scan", 156, 24, 70);
            btnSection = Btn("Section", 8, 53, 70); // re-section any managed timber in place (TSection)
            groupEdit = Section("Edit & nodes", 8, 364, GW, 88);
            groupEdit.Controls.Add(btnMove);
            groupEdit.Controls.Add(btnRotate);
            groupEdit.Controls.Add(btnScan);
            groupEdit.Controls.Add(btnSection);

            // ---- Footer: build stamp (so a stale NETLOAD is obvious) ----
            lblBuild = new Label
            {
                Text = "Build ...",
                AutoSize = true,
                ForeColor = Theme.SubtleFg,
                Margin = new Padding(6, 6, 0, 6)
            };

            // ---- Root: the flow of groups fills the control; the shell supplies the tabs. ----
            flowEdit = TabFlow();
            flowEdit.Controls.Add(groupSection);
            flowEdit.Controls.Add(groupOrient);
            flowEdit.Controls.Add(groupPlace);
            flowEdit.Controls.Add(groupBrace);
            flowEdit.Controls.Add(groupAssembly);
            flowEdit.Controls.Add(groupEdit);

            // Footer docks at the bottom (below the flow). SendToBack puts the fill control at the
            // back of the z-order so the bottom strip is reserved first, then the flow fills the rest.
            lblBuild.Dock = DockStyle.Bottom;
            this.Controls.Add(flowEdit);
            this.Controls.Add(lblBuild);
            flowEdit.SendToBack();
            this.Name = "TimberPaletteControl";
            this.Size = new Size(GW + 24, 560);
            Theme.Apply(this);   // inputs + buttons follow the shared palette
        }
    }
}
