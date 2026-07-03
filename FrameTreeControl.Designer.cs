using System.Drawing;
using System.Windows.Forms;

namespace TimberDraw
{
    // Tree-based structural editor for the Frame model. TreeView (top) over a PropertyGrid
    // (bottom); a Draw/Save/Save As/Load button row at the very bottom. All add/subtract is on
    // the tree's right-click context menu (built dynamically in code).
    partial class FrameTreeControl : System.Windows.Forms.UserControl
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private SplitContainer split;
        private TreeView treeView;
        private PropertyPane propPane;
        private Panel panelButtons;
        private Button ButtonDraw, ButtonNew, ButtonSave, ButtonSaveAs, ButtonLoad, ButtonSetDefault, ButtonFreeze;

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.SuspendLayout();

            this.treeView = new TreeView
            {
                Name = "treeView",
                Dock = DockStyle.Fill,
                HideSelection = true,
                CheckBoxes = true
            };

            this.propPane = new PropertyPane
            {
                Name = "propPane",
                Dock = DockStyle.Fill
            };

            this.split = new SplitContainer
            {
                Name = "split",
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 300
            };
            this.split.Panel1.Controls.Add(this.treeView);
            this.split.Panel2.Controls.Add(this.propPane);
            this.split.Panel2.AutoScroll = true;

            this.ButtonDraw   = new Button { Name = "ButtonDraw",   Text = "Generate Frame", Location = new Point(6, 6),  Size = new Size(248, 30) };
            this.ButtonNew    = new Button { Name = "ButtonNew",    Text = "New",        Location = new Point(6, 40),   Size = new Size(54, 26) };
            this.ButtonSave   = new Button { Name = "ButtonSave",   Text = "Save",       Location = new Point(64, 40),  Size = new Size(54, 26) };
            this.ButtonSaveAs = new Button { Name = "ButtonSaveAs", Text = "Save As",    Location = new Point(122, 40), Size = new Size(64, 26) };
            this.ButtonLoad   = new Button { Name = "ButtonLoad",   Text = "Load",       Location = new Point(190, 40), Size = new Size(54, 26) };
            this.ButtonSetDefault = new Button { Name = "ButtonSetDefault", Text = "Set as Default", Location = new Point(6, 70), Size = new Size(120, 26) };
            // Freeze: the one-way break -- locks this frame's parametric generator (Draw stops re-emitting)
            // and hands the skeleton timbers to the managed editor. Mirrors TPanel's Freeze button.
            this.ButtonFreeze = new Button { Name = "ButtonFreeze", Text = "Freeze", Location = new Point(132, 70), Size = new Size(116, 26) };
            this.panelButtons = new Panel { Name = "panelButtons", Dock = DockStyle.Bottom, Height = 104 };
            this.panelButtons.Controls.Add(this.ButtonDraw);
            this.panelButtons.Controls.Add(this.ButtonNew);
            this.panelButtons.Controls.Add(this.ButtonSave);
            this.panelButtons.Controls.Add(this.ButtonSaveAs);
            this.panelButtons.Controls.Add(this.ButtonLoad);
            this.panelButtons.Controls.Add(this.ButtonSetDefault);
            this.panelButtons.Controls.Add(this.ButtonFreeze);

            this.Controls.Add(this.split);
            this.Controls.Add(this.panelButtons);
            this.Name = "FrameTreeControl";
            this.Size = new Size(300, 640);
            this.ResumeLayout(false);
        }
    }
}
