using System.Drawing;
using System.Windows.Forms;

namespace TimberDraw
{
    // Managed-timber assembly palette (TPanel / the shell's Assembly tab). Two flat sections
    // (the sticky Section catalogue and the Brace spec) over ONE bottom action bar carrying
    // every verb as full-span rows (the Joints-tab look). The assign two-box is GONE -- the
    // Browser tab is the single assign surface. Layout is hand-coded here.
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

        // Verbs (bottom action bar): orientation presets, place & connect, edit & nodes.
        private Button btnPlan, btnBent, btnWall;
        private Button btnPlace, btnSpan, btnJoin, btnFit, btnScarf, btnJoist;
        private Button btnMove, btnRotate, btnScan, btnSection;

        // Footer
        private Label lblBuild;

        // Container: a scrolling top-down flow of the two spec sections; verbs live in the
        // bottom bar, not in the flow.
        private FlowLayoutPanel flowEdit;
        private Panel groupSection;

        // A flat section: bold header band docked over a pinned-dark panel. The header must
        // dock TOP here: PaneRows.HeaderCell is Dock=Fill for table cells, and Fill inside a
        // plain Panel would cover the whole section and swallow every click.
        private static Panel Section(string title, int w, int h)
        {
            var p = new Panel { Size = new Size(w, h), BackColor = Theme.Bg };
            System.Windows.Forms.Label hdr = PaneRows.HeaderCell(title);
            hdr.Dock = DockStyle.Top;
            p.Controls.Add(hdr);
            return p;
        }

        // Pixel-placed control helpers for the section interiors, styled through the Theme.
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

            const int GW = 234;   // section width

            // ---- Section (tree; data entry stacked; catalog buttons beside their fields) ----
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
            btnAdd    = Btn("Add",     8, 304, 70);
            btnUpdate = Btn("Update", 82, 304, 70);
            btnRemove = Btn("Remove", 156, 304, 70);
            groupSection = Section("Section", GW, 342);
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

            // ---- Brace (foot/head legs + angle; check two, the third is computed) ----
            chkFoot = Chk("Foot leg", 12, 28);
            chkHead = Chk("Head leg", 12, 56);
            chkAngle = Chk("Angle (deg)", 12, 84);
            txtFoot = new TextBox { Location = new Point(150, 25), Size = new Size(60, 22) };
            txtHead = new TextBox { Location = new Point(150, 53), Size = new Size(60, 22) };
            txtAngle = new TextBox { Location = new Point(150, 81), Size = new Size(60, 22) };
            groupBrace = Section("Brace (knee)", GW, 112);
            groupBrace.Controls.Add(chkFoot);
            groupBrace.Controls.Add(chkHead);
            groupBrace.Controls.Add(chkAngle);
            groupBrace.Controls.Add(txtFoot);
            groupBrace.Controls.Add(txtHead);
            groupBrace.Controls.Add(txtAngle);

            // ---- Bottom action bar: every verb, full-span rows (the Joints-tab look) ----
            btnPlan   = Theme.Button("Plan");
            btnBent   = Theme.Button("Bent");
            btnWall   = Theme.Button("Wall");
            btnPlace  = Theme.Button("Place");
            btnSpan   = Theme.Button("Span");
            btnJoin   = Theme.Button("Brace");    // TJoin -- connect two picked timbers with a brace
            btnFit    = Theme.Button("Fit");
            btnScarf  = Theme.Button("Scarf");
            btnJoist  = Theme.Button("Joist");
            btnSection = Theme.Button("Section"); // re-section any managed timber in place (TSection)
            btnMove   = Theme.Button("Move");
            btnRotate = Theme.Button("Rotate");
            btnScan   = Theme.Button("Scan");
            Panel bar = ActionBar.Build(
                ActionBar.Row(btnPlan, btnBent, btnWall),        // UCS presets
                ActionBar.Row(btnPlace, btnSpan, btnJoin),
                ActionBar.Row(btnFit, btnScarf, btnJoist),
                ActionBar.Row(btnSection, btnMove, btnRotate),
                ActionBar.Row(btnScan));

            // ---- Footer: build stamp (so a stale NETLOAD is obvious) ----
            lblBuild = new Label
            {
                Text = "Build ...",
                AutoSize = true,
                ForeColor = Theme.SubtleFg,
                Margin = new Padding(6, 6, 0, 6),
                Dock = DockStyle.Bottom
            };

            // ---- Root: sections flow on top; bar + footer dock at the bottom. Later-added
            //      bottom docks sit LOWER, so add footer after the bar. ----
            flowEdit = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(2)
            };
            flowEdit.Controls.Add(groupSection);
            flowEdit.Controls.Add(groupBrace);

            this.Controls.Add(flowEdit);
            this.Controls.Add(bar);
            this.Controls.Add(lblBuild);
            flowEdit.SendToBack();
            this.Name = "TimberPaletteControl";
            this.Size = new Size(GW + 24, 560);
            Theme.Apply(this);   // pins every container + inputs to the shared palette
        }
    }
}
