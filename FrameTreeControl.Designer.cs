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
        private Button ButtonDraw, ButtonNew, ButtonSave, ButtonSaveAs, ButtonLoad, ButtonSetDefault, ButtonFreeze, ButtonGrid;

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

            // The bottom action bar: full-span rows (the Joints-tab look). Generate is the hero
            // row; New/Save/Save As/Load manage the recipe; Set Default / Freeze (the one-way
            // break) / Redraw Grid (fires TGrid) round it out.
            this.ButtonDraw   = Theme.Button("Generate Frame");
            this.ButtonDraw.Name = "ButtonDraw";
            this.ButtonNew    = Theme.Button("New");
            this.ButtonNew.Name = "ButtonNew";
            this.ButtonSave   = Theme.Button("Save");
            this.ButtonSave.Name = "ButtonSave";
            this.ButtonSaveAs = Theme.Button("Save As");
            this.ButtonSaveAs.Name = "ButtonSaveAs";
            this.ButtonLoad   = Theme.Button("Load");
            this.ButtonLoad.Name = "ButtonLoad";
            this.ButtonSetDefault = Theme.Button("Set Default");
            this.ButtonSetDefault.Name = "ButtonSetDefault";
            this.ButtonFreeze = Theme.Button("Freeze");
            this.ButtonFreeze.Name = "ButtonFreeze";
            this.ButtonGrid   = Theme.Button("Redraw Grid");
            this.ButtonGrid.Name = "ButtonGrid";
            this.panelButtons = ActionBar.Build(
                ActionBar.Row(this.ButtonDraw),
                ActionBar.Row(this.ButtonNew, this.ButtonSave, this.ButtonSaveAs, this.ButtonLoad),
                ActionBar.Row(this.ButtonSetDefault, this.ButtonFreeze, this.ButtonGrid));
            this.panelButtons.Name = "panelButtons";

            // Plain-language tooltips on every button (Robert's ask -- the tab explains itself).
            var tip = new ToolTip(this.components) { AutoPopDelay = 20000, InitialDelay = 400, ReshowDelay = 100, ShowAlways = true };
            tip.SetToolTip(this.ButtonDraw, "Clear this frame's skeleton and re-emit it from the recipe. Hand-placed timbers survive; their joints to the old skeleton are stripped (recipes kept -- TJointSync re-attaches).");
            tip.SetToolTip(this.ButtonNew, "Start a fresh, empty frame recipe.");
            tip.SetToolTip(this.ButtonSave, "Save the recipe to its .framespec file.");
            tip.SetToolTip(this.ButtonSaveAs, "Save the recipe to a new .framespec file.");
            tip.SetToolTip(this.ButtonLoad, "Load a saved .framespec recipe.");
            tip.SetToolTip(this.ButtonSetDefault, "Save the selected bent or bay's settings as that type's default for new ones.");
            tip.SetToolTip(this.ButtonFreeze, "ONE-WAY: end the parametric phase. The generator locks and the timbers carry on as the source of truth, edited with the Assembly verbs.");
            tip.SetToolTip(this.ButtonGrid, "Redraw the structural grid from the timbers in the drawing (TGrid).");

            this.Controls.Add(this.split);
            this.Controls.Add(this.panelButtons);
            this.Name = "FrameTreeControl";
            this.Size = new Size(300, 640);
            this.ResumeLayout(false);
            Theme.Apply(this);   // inputs + buttons follow the shared palette
        }
    }
}
