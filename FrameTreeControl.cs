using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Forms;
using Autodesk.AutoCAD.Geometry;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TimberDraw
{
    // Tree-based structural editor for the Frame model. The FrameSpec is the source of truth.
    // The tree starts EMPTY; right-click builds it: Add Bent / Add Bay (alternating; a bay may
    // lead or trail the frame). Set a bent's Bent Type / a bay's Roof Type in the PropertyGrid;
    // that fills the element's folder with ALL its timbers as checkboxes (all checked). Unchecking
    // a timber drops it. The PropertyGrid shows only the SELECTED item's properties (a timber leaf
    // shows that timber's sizes; a bent/bay node shows its top-level props). Edits persist the spec;
    // the Draw button renders. Save/Save As/Load handle named .framespec templates.
    public partial class FrameTreeControl
    {
        private FrameSpec _spec;
        private string _currentPath;     // last Save/Load path (for plain Save)
        private bool _building;          // suppress select/check handlers during (re)build
        private ContextMenuStrip _menu;

        // Multi-select of timber leaves (Ctrl/Shift-click). The membership checkboxes are a separate
        // mechanism; these are the leaves whose COMMON properties are co-edited in the grid.
        private readonly List<TreeNode> _selLeaves = new List<TreeNode>();
        private TreeNode _anchorLeaf;    // anchor for Shift-range selection

        // Tree leaf tag for a timber: which owner (bent/bay) and the universal Timber itself.
        private class TimberLeaf { public IMemberOwner Owner; public Timber Timber; }

        // Tree leaf tag for a DRAWING-DERIVED timber under a Free Assembly element: a read-only view of
        // a managed timber assigned via TAssign (get-only props -> the PropertyGrid shows them read-only).
        private class DrawnLeaf
        {
            private readonly string _type, _desig, _size;
            public DrawnLeaf(Module1.DataStructure xd)
            { _type = xd.Type ?? ""; _desig = xd.Designation ?? ""; _size = xd.Size ?? ""; }
            [Category("Assigned"), DisplayName("Type")]        public string Type => _type;
            [Category("Assigned"), DisplayName("Designation")] public string Designation => _desig;
            [Category("Assigned"), DisplayName("Size")]        public string Size => _size;
            public static string LabelFor(Module1.DataStructure xd)
            {
                string t = string.IsNullOrEmpty(xd.Type) ? "Timber" : xd.Type;
                string d = string.IsNullOrEmpty(xd.Designation) ? "" : " " + xd.Designation;
                string s = string.IsNullOrEmpty(xd.Size) ? "" : " (" + xd.Size + ")";
                return t + d + s;
            }
        }

        // Stable tags for the two container folders (Bents / Walls) so expand-state survives rebuilds
        // and selecting them clears the grid rather than binding the raw list.
        private readonly object _bentsTag = new object();
        private readonly object _wallsTag = new object();

        public FrameTreeControl()
        {
            InitializeComponent();
            Load += FrameTreeControl_Load;
        }

        private static TimberDraw.Properties.Settings S => TimberDraw.Properties.Settings.Default;

        private void FrameTreeControl_Load(object sender, EventArgs e)
        {
            SetupMenu();
            WireEvents();
            ResetToEmpty();       // the deterministic start; then...
            TryRecallDrawing();   // ...refill from the ACTIVE drawing's stamped recipe when it has one

            // Re-render distance cells when the drawing's units change (DistanceConverter formats via
            // LUNITS/LUPREC). Reset to the empty start when the active drawing changes (the start is
            // deterministic + drawing-clean). Drop both handlers with the control to avoid dangling subs.
            AcadApp.SystemVariableChanged += OnSysVarChanged;
            AcadApp.DocumentManager.DocumentActivated += OnDocumentActivated;
            Disposed += (s, ev) =>
            {
                AcadApp.SystemVariableChanged -= OnSysVarChanged;
                AcadApp.DocumentManager.DocumentActivated -= OnDocumentActivated;
            };
        }

        private void OnSysVarChanged(object sender, Autodesk.AutoCAD.ApplicationServices.SystemVariableChangedEventArgs e)
        {
            if (e.Name == "LUNITS" || e.Name == "LUPREC") BindGrid();   // re-format distances per units
        }

        // Switching drawings restarts the tree from the empty start (drawing-clean contract), then
        // recalls the NEW drawing's own frame when it carries one (batch-3 #3).
        private void OnDocumentActivated(object sender, Autodesk.AutoCAD.ApplicationServices.DocumentCollectionEventArgs e)
        {
            if (e.Document == null) return;   // last document closed -> nothing to reflect
            try { ResetToEmpty(); TryRecallDrawing(); } catch { /* best-effort; activation can fire mid-init */ }
        }

        // Recall-on-open (Robert's ask, batch-3 #3): a drawing whose frame was drawn by the tree
        // carries its recipe in the frame registry record (stamped at every Draw), so activating
        // that drawing refills the tree with ITS frame -- exact recall, nothing derived from the
        // solids (the spec is a generator, not the model). Drawings from before the stamp, or
        // with no drawn frame, keep the empty start.
        private void TryRecallDrawing()
        {
            try
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                string json = FrameRegistry.FirstSpecJson(doc.Database);
                if (string.IsNullOrEmpty(json)) return;
                _spec = FrameSpecStore.FromJson(json);
                _currentPath = null;
                Persist();   // sync the FrameSpecJson signal (TGrid's frame tag) to the recalled frame
                BuildTree();
                RefreshFrozenState();
            }
            catch (System.Exception ex)
            { Diag.Warn("FrameTree.Recall", "drawing spec recall failed (tree stays empty): " + ex.Message); }
        }

        // The start state everywhere (open, doc-switch, AND the New button): an EMPTY tree.
        // No seed exists anymore -- grow via right-click Add Bent / Add Wall or Load a
        // .framespec. Deliberately does NOT Persist: FrameSpecJson (the active-frame-tag signal
        // sibling commands like TGrid read) keeps the last real frame until a new spec exists.
        public void ResetToEmpty()
        {
            _spec = new FrameSpec();
            _currentPath = null;
            propPane.Clear();
            BuildTree();
            RefreshFrozenState();   // empty gate: Draw/Freeze disabled until the tree has content
        }

        private void WireEvents()
        {
            treeView.AfterSelect += (s, e) => { if (!_building) SyncFromSingle(e.Node); };
            treeView.AfterCheck  += TreeView_AfterCheck;
            treeView.NodeMouseClick += TreeView_NodeMouseClick;
            propPane.ValueCommitted += HandlePropChange;
            // Brace rows render their labels as the solve-for checkboxes -- ONLY on brace leaves
            // (a strut's Angle row keeps its plain label).
            propPane.RowChecked = name =>
            {
                if (name != "Foot" && name != "Head" && name != "Angle") return null;
                if (!(treeView.SelectedNode?.Tag is TimberLeaf tl) || !IsBraceRole(tl.Timber.Role)) return null;
                return _braceMask.Contains(name);
            };
            propPane.RowCheckToggled += OnBraceMaskToggle;
            ButtonDraw.Click   += (s, e) => DrawFrame();
            ButtonNew.Click    += (s, e) => NewFrame();
            ButtonSave.Click   += (s, e) => SaveSpec(false);
            ButtonSaveAs.Click += (s, e) => SaveSpec(true);
            ButtonLoad.Click   += (s, e) => LoadFromFile();
            ButtonSetDefault.Click += (s, e) => SetAsDefault();
            ButtonFreeze.Click += (s, e) => FreezeFrame();
            // TGrid reads the drawing + FrameSpecJson in a command context -- fire it like the
            // other palette verbs rather than calling into the DB from a WinForms handler.
            ButtonGrid.Click += (s, e) =>
                AcadApp.DocumentManager.MdiActiveDocument?.SendStringToExecute("TGrid ", true, false, true);
        }

        // Reflect the active frame's freeze gate (the break): a frozen frame's parametric generator is
        // locked, so Draw won't re-emit and there is nothing left to freeze. Called on load and whenever
        // the palette is (re)shown. The authoritative lock is DrawFrame's runtime guard; this is the UI.
        public void RefreshFrozenState()
        {
            // Empty tree (the open / doc-switch start state): nothing to draw or freeze yet.
            if (SpecIsEmpty)
            {
                ButtonDraw.Enabled = false;
                ButtonFreeze.Enabled = false;
                ButtonFreeze.Text = "Freeze";
                return;
            }
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                // Zero-document state: nothing can draw or freeze. A defined state matters here --
                // the empty branch above may have disabled the buttons, and without this a New/Load
                // in a doc-less session would leave them stuck. Document activation re-runs this.
                ButtonDraw.Enabled = false;
                ButtonFreeze.Enabled = false;
                ButtonFreeze.Text = "Freeze";
                return;
            }
            bool frozen;
            try { frozen = FrameRegistry.IsFrozen(doc.Database, FrameTagSafe()); }
            catch { return; }   // leave the buttons as-is if the frame can't be read
            ButtonDraw.Enabled = !frozen;     // a frozen frame won't re-emit (generator locked)
            ButtonFreeze.Enabled = !frozen;   // one-way; nothing to do once frozen
            ButtonFreeze.Text = frozen ? "Frozen" : "Freeze";
        }

        // Empty = no structural content yet (the start state). A spec becomes non-empty the
        // moment a bent or wall exists, whether via New's starter seed, Load, or right-click.
        private bool SpecIsEmpty =>
            _spec == null || (_spec.Bents.Count == 0 && _spec.Walls.Count == 0);

        // Freeze (the BREAK): lock this frame's parametric generator. Afterward Draw refuses to re-emit
        // and the skeleton timbers carry on as the managed-editor truth -- geometry is untouched, only
        // the gate flips. One-way. Mirrors the TFreeze command, but freezes THIS frame (the spec's tag)
        // directly, with no command-line tag prompt.
        private void FreezeFrame()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;
            string frame = FrameTagSafe();

            if (FrameRegistry.IsFrozen(db, frame))
            { Dialogs.Info("Frame " + frame + " is already frozen."); RefreshFrozenState(); return; }

            if (ManagedTimber.EnumerateFrameFrames(db, frame).Count == 0)
            { Dialogs.Info("Nothing to freeze in frame " + frame + " -- draw it first."); return; }

            if (!Dialogs.ConfirmWarn(
                    "Freeze frame " + frame + "? This is the one-way break: the parametric palette locks, "
                    + "Draw stops re-emitting, and the timbers carry on as the managed-editor truth "
                    + "(edit them with the managed verbs)."))
                return;

            FrameRegistry.SetFrozen(db, frame, true);
            // The tree still holds this frame's recipe at the break -- stamp it (recall-on-open)
            // in case the drawing was drawn before the Draw-time stamp existed.
            try
            {
                FrameRecord rec = FrameRegistry.Load(db, frame) ?? new FrameRecord { Frozen = true };
                if (string.IsNullOrEmpty(rec.SpecJson))
                { rec.SpecJson = FrameSpecStore.ToJson(_spec); FrameRegistry.Save(db, frame, rec); }
            }
            catch (System.Exception ex)
            { Diag.Warn("FrameTree.Freeze", "spec stamp failed (recall-on-open unavailable): " + ex.Message); }
            doc.Editor.WriteMessage("\nTFreeze: frame " + frame +
                " frozen -- parametric palette locked; edit via the managed verbs.");
            RefreshFrozenState();
        }

        // The PropertyGrid shows ONLY the selected item's properties:
        //  - Frame root  -> the FrameSpec (its Browsable props are just frame geometry).
        //  - Bent node   -> Name, Bent Type, + type-global params (Divisor / Queen position).
        //  - Bay node    -> Name, Spacing, Roof Type.
        //  - Timber leaf -> just that timber's size fields (edits write through to the owner).
        private object SelectedTarget(TreeNode n)
        {
            object tag = n?.Tag;
            if (tag is FrameSpec fs)
                return new FilteredView(fs, new[] { "FrameTag", "Span", "EaveHt", "PitchRise" });
            if (ReferenceEquals(tag, _bentsTag)) return BentRoster();
            if (ReferenceEquals(tag, _wallsTag)) return WallRoster();
            if (tag is BentSpec b) return new FilteredView(b, BentNodeProps(b));
            if (tag is WallSpec w) return new FilteredView(w, new[] { "Name", "Separation" });
            if (tag is BaySpec y)  return new FilteredView(y, new[] { "Name", "BraceCentering" });
            if (tag is TimberLeaf tl) return new TimberMemberView(LeafRows(tl));
            return tag;
        }

        // ------------------------------------------------------- multi-select
        // Left-click with Ctrl/Shift extends the leaf selection; a plain click / keyboard nav goes
        // through AfterSelect -> SyncFromSingle (single-select). Right-click selects for the menu.
        private void TreeView_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { treeView.SelectedNode = e.Node; return; }
            if (e.Button != MouseButtons.Left) return;

            Keys mod = Control.ModifierKeys;
            bool ctrl = (mod & Keys.Control) != 0;
            bool shift = (mod & Keys.Shift) != 0;
            if ((!ctrl && !shift) || !(e.Node.Tag is TimberLeaf)) return;   // AfterSelect handled it

            if (ctrl)
            {
                if (_selLeaves.Contains(e.Node)) _selLeaves.Remove(e.Node);
                else _selLeaves.Add(e.Node);
                _anchorLeaf = e.Node;
            }
            else // shift: contiguous run of leaves between the anchor and this node, in tree order
            {
                var leaves = new List<TreeNode>();
                foreach (TreeNode n in AllNodes(treeView.Nodes))
                    if (n.Tag is TimberLeaf) leaves.Add(n);
                int a = _anchorLeaf != null ? leaves.IndexOf(_anchorLeaf) : -1;
                int b = leaves.IndexOf(e.Node);
                if (a < 0) a = b;
                if (a > b) { int t = a; a = b; b = t; }
                _selLeaves.Clear();
                for (int i = a; i <= b; i++) _selLeaves.Add(leaves[i]);
            }
            RepaintSelection();
            BindGrid();
        }

        // Plain click / keyboard nav: the selection is just this node (a leaf -> single co-edit set).
        private void SyncFromSingle(TreeNode node)
        {
            _selLeaves.Clear();
            if (node?.Tag is TimberLeaf) _selLeaves.Add(node);
            _anchorLeaf = node;
            RepaintSelection();
            BindGrid();
        }

        // Bind the grid to the current selection: 1+ leaves -> common props across all of them;
        // a non-leaf node -> its own top-level props. Everything (braces included) is the one grid.
        private void BindGrid()
        {
            if (_building) return;

            if (_selLeaves.Count > 0)
            {
                var views = new List<TimberMemberView>();
                foreach (TreeNode n in _selLeaves)
                    views.Add(new TimberMemberView(LeafRows((TimberLeaf)n.Tag)));
                propPane.Bind(views.ToArray());
            }
            else
            {
                propPane.Bind(SelectedTarget(treeView.SelectedNode));
            }
        }

        private static bool IsBraceRole(string role)
            => role == "Brace" || role == "FloorBrace" || role == "EaveBrace"
               || role == "QueenBrace" || role == "CollarBrace"
               || role == "RidgeBrace" || role == "QueenGirtBrace" || role == "HPostGirtBrace";

        // Brace solve-for mask (the Assembly-tab mechanic): exactly TWO of Foot/Head/Angle are the
        // inputs; the third is derived + read-only. Session-sticky and shared across brace leaves
        // (the VALUES stay per member); insertion order = the Assembly rule, checking a third drops
        // the oldest. Angle convention follows the Assembly tab: tan(angle) = head / foot.
        private static readonly List<string> _braceMask = new List<string> { "Foot", "Head" };

        // Keep exactly two checked: checking a third drops the oldest; unchecking one of two is
        // refused (you switch by CHECKING the third) -- BindGrid re-renders the refused box checked.
        private void OnBraceMaskToggle(string name, bool on)
        {
            if (on && !_braceMask.Contains(name))
            {
                _braceMask.Add(name);
                while (_braceMask.Count > 2) _braceMask.RemoveAt(0);
            }
            foreach (TreeNode ln in _selLeaves)
                if (ln.Tag is TimberLeaf tl && IsBraceRole(tl.Timber.Role))
                    RecomputeBrace(tl.Timber.Size);
            Persist();
            BindGrid();
        }

        // Derive the masked-out value from the two active ones (the Assembly RecomputeBrace rules);
        // Length always follows. Angle clamps to (0.1, 89.9) and rounds to 0.01.
        private static void RecomputeBrace(MemberSize s)
        {
            bool f = _braceMask.Contains("Foot"), h = _braceMask.Contains("Head");
            if (f && h)
                s.Angle = System.Math.Round(Math.Atan2(s.Head, s.Foot) * 180.0 / Math.PI, 2);
            else
            {
                s.Angle = ClampRoundAngle(s.Angle);
                double t = Math.Tan(s.Angle * Math.PI / 180.0);
                if (f) s.Head = s.Foot * t;
                else   s.Foot = s.Head / t;
            }
            s.Length = Math.Sqrt(s.Foot * s.Foot + s.Head * s.Head);
        }

        private static double ClampRoundAngle(double v)
            => System.Math.Round(Math.Max(0.1, Math.Min(89.9, v)), 2);

        // --------------------------------------------------- group-layer isolation
        private string FrameTagSafe() => string.IsNullOrWhiteSpace(_spec.FrameTag) ? "A" : _spec.FrameTag.Trim();
        private string GroupLayerFor(BentSpec b)
            => ManagedTimber.GroupLayer(FrameTagSafe(), (_spec.Bents.IndexOf(b) + 1).ToString(), "");
        private string GroupLayerFor(WallSpec w)
            => ManagedTimber.GroupLayer(FrameTagSafe(), "", w.Letter);

        private bool IsGroupVisible(string layer)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            return doc == null || ManagedTimber.IsGroupVisible(doc.Database, layer);
        }

        private void SetGroupVisible(string layer, bool visible)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc != null) ManagedTimber.SetGroupVisible(doc.Database, layer, visible);
        }

        private void ToggleGroupLayer(string layer, bool visible) { SetGroupVisible(layer, visible); RegenView(); }

        private static void RegenView()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            try { doc?.Editor.Regen(); } catch { /* layer On/Off auto-updates; regen is best-effort */ }
        }

        private static bool AllChecked(TreeNode parent)
        {
            foreach (TreeNode c in parent.Nodes) if (!c.Checked) return false;
            return parent.Nodes.Count > 0;
        }

        // Paint the multi-select set (blue highlight; the user is red/green colour-blind, never green).
        private void RepaintSelection()
        {
            foreach (TreeNode n in AllNodes(treeView.Nodes))
                if (n.Tag is TimberLeaf)
                {
                    bool sel = _selLeaves.Contains(n);
                    n.BackColor = sel ? Theme.Accent : System.Drawing.Color.Empty;
                    n.ForeColor = sel ? System.Drawing.Color.Black : System.Drawing.Color.Empty;
                }
        }

        // ----------------------------------------------------------------- tree
        private void BuildTree()
        {
            _building = true;
            try
            {
                // Preserve the user's expand/collapse state across rebuilds (element + root Tags are the
                // same object refs after the rebuild; timber leaves have no children). Don't ExpandAll.
                var expanded = new System.Collections.Generic.HashSet<object>();
                bool firstBuild = treeView.Nodes.Count == 0;
                foreach (TreeNode n in AllNodes(treeView.Nodes))
                    if (n.IsExpanded && n.Tag != null) expanded.Add(n.Tag);

                _selLeaves.Clear(); _anchorLeaf = null;   // nodes are recreated below
                treeView.Nodes.Clear();
                TreeNode root = new TreeNode(FrameLabel(_spec)) { Tag = _spec };
                treeView.Nodes.Add(root);

                // Axis 1: Bents -> Bent N -> cross-section member leaves.
                TreeNode bentsFolder = new TreeNode("Bents") { Tag = _bentsTag };
                root.Nodes.Add(bentsFolder);
                foreach (BentSpec b in _spec.Bents)
                {
                    // Node checkbox = its group LAYER visibility (isolation), read from the drawing.
                    TreeNode node = new TreeNode(NodeLabel(b)) { Tag = b, Checked = IsGroupVisible(GroupLayerFor(b)) };
                    bentsFolder.Nodes.Add(node);
                    AddTimberLeaves(node, b);
                }
                bentsFolder.Checked = AllChecked(bentsFolder);

                // Axis 2: Walls -> Wall A/B -> Bay I/II -> longitudinal member leaves.
                TreeNode wallsFolder = new TreeNode("Walls") { Tag = _wallsTag };
                root.Nodes.Add(wallsFolder);
                foreach (WallSpec w in _spec.Walls)
                {
                    TreeNode wnode = new TreeNode(NodeLabel(w)) { Tag = w, Checked = IsGroupVisible(GroupLayerFor(w)) };
                    wallsFolder.Nodes.Add(wnode);
                    // A Free Assembly wall has no parametric bays -- list its assigned timbers directly.
                    if (w.FreeAssembly) { AddDrawnLeaves(wnode, "", w.Letter); continue; }
                    // An addressing-only line (a KingPost vstrut B/D) carries no longitudinal members --
                    // show the line, but no (empty) bay nodes.
                    if (w.Role == BayRole.Vstrut) continue;
                    foreach (BaySpec y in w.Bays)
                    {
                        TreeNode ynode = new TreeNode(NodeLabel(y)) { Tag = y };
                        wnode.Nodes.Add(ynode);
                        AddTimberLeaves(ynode, y);
                    }
                }
                wallsFolder.Checked = AllChecked(wallsFolder);

                if (firstBuild) { root.Expand(); bentsFolder.Expand(); wallsFolder.Expand(); }
                else
                    foreach (TreeNode n in AllNodes(treeView.Nodes))
                        if (n.Tag != null && expanded.Contains(n.Tag)) n.Expand();
            }
            finally { _building = false; }

            // Re-apply the empty gate: the first Add Bent (right-click) or a Load flips the tree
            // non-empty, and Draw/Freeze must follow.
            RefreshFrozenState();
        }

        private void TreeView_AfterCheck(object sender, TreeViewEventArgs e)
        {
            if (_building) return;
            object ntag = e.Node.Tag;

            // Bent/Wall node + Bents/Walls container checkboxes toggle that group's LAYER visibility
            // (isolation) -- not a spec change. Timber-leaf checkboxes (below) are member enablement.
            if (ntag is BentSpec nb) { ToggleGroupLayer(GroupLayerFor(nb), e.Node.Checked); return; }
            if (ntag is WallSpec nw) { ToggleGroupLayer(GroupLayerFor(nw), e.Node.Checked); return; }
            if (ReferenceEquals(ntag, _bentsTag) || ReferenceEquals(ntag, _wallsTag))
            {
                bool vis = e.Node.Checked;
                _building = true;
                foreach (TreeNode child in e.Node.Nodes)
                {
                    child.Checked = vis;
                    string ln = (child.Tag is BentSpec cb) ? GroupLayerFor(cb)
                              : (child.Tag is WallSpec cw) ? GroupLayerFor(cw) : null;
                    if (ln != null) SetGroupVisible(ln, vis);
                }
                _building = false;
                RegenView();
                return;
            }

            if (!(e.Node.Tag is TimberLeaf leaf))
            {
                // Frame root / other: no meaningful check (active-frame deferred); revert silently.
                _building = true; e.Node.Checked = false; _building = false;
                return;
            }

            leaf.Timber.Enabled = e.Node.Checked;

            // A bay's Commons/Purlins are mutually exclusive: checking one unchecks the other.
            string key = leaf.Timber.Key;
            if (e.Node.Checked && leaf.Owner is BaySpec && (key == "Commons:X" || key == "Purlins:X"))
            {
                string otherKey = key == "Commons:X" ? "Purlins:X" : "Commons:X";
                foreach (TreeNode sib in e.Node.Parent.Nodes)
                    if (sib.Tag is TimberLeaf sl && sl.Timber.Key == otherKey)
                    {
                        if (sib.Checked) { _building = true; sib.Checked = false; _building = false; }
                        sl.Timber.Enabled = false;
                    }
            }

            // A bay derives its RoofType from the roof checkboxes; resync + relabel, and if the roof
            // actually changed overlay that roof's per-type default (like setting a bent's type).
            if (leaf.Owner is BaySpec bay)
            {
                string before = bay.RoofType;
                bay.SyncRoofType();
                if (e.Node.Parent != null) e.Node.Parent.Text = NodeLabel(bay);
                if (bay.RoofType != before)
                {
                    // Roof checkbox changed: overlay ONLY the roof members/params -- the full
                    // overlay was unchecking the bay's floor girt + braces.
                    TypeDefaults.ApplyRoofOnly(bay);
                    Persist();
                    BuildTree();
                    SelectByTag(bay);
                    return;
                }
            }
            Persist();
        }

        // Once typed, an owner's folder lists ALL its timbers as checkboxes; checked = enabled. A Free
        // Assembly bent has no parametric timbers -- it lists the managed timbers assigned to its number.
        private void AddTimberLeaves(TreeNode node, IMemberOwner owner)
        {
            if (!owner.TypeIsSet) return;
            if (owner is BentSpec bs && bs.IsFreeAssembly)
            {
                AddDrawnLeaves(node, (_spec.Bents.IndexOf(bs) + 1).ToString(), "");
                return;
            }
            foreach (Timber t in owner.Timbers)
                node.Nodes.Add(new TreeNode(t.Label)
                {
                    Tag = new TimberLeaf { Owner = owner, Timber = t },
                    Checked = t.Enabled
                });
        }

        // List (read-only) the managed timbers in the drawing assigned to this frame + bent number
        // (bentTag) or wall letter (wallTag) via TAssign. The tree is a VIEW of the drawing here --
        // geometry editing stays in the managed commands (TPlace/TSpan/...).
        private void AddDrawnLeaves(TreeNode node, string bentTag, string wallTag)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            string frame = string.IsNullOrWhiteSpace(_spec.FrameTag) ? "A" : _spec.FrameTag.Trim();
            foreach (var (id, _) in ManagedTimber.Enumerate(doc.Database))
            {
                Module1.DataStructure xd = Module1.GetXdata(id);
                if (xd == null) continue;
                string ft = string.IsNullOrWhiteSpace(xd.FrameTag) ? "" : xd.FrameTag.Trim();
                if (ft != frame) continue;
                bool match = bentTag.Length > 0
                    ? (xd.BentNumber ?? "") == bentTag
                    : string.Equals(xd.WallTag ?? "", wallTag, StringComparison.OrdinalIgnoreCase);
                if (!match) continue;
                node.Nodes.Add(new TreeNode(DrawnLeaf.LabelFor(xd)) { Tag = new DrawnLeaf(xd) });
            }
        }

        // The Frame root label carries its Name, e.g. "Frame (North Barn)"; unnamed -> "Frame (set name)".
        private static string FrameLabel(FrameSpec s)
            => "Frame (" + (string.IsNullOrWhiteSpace(s.FrameTag) ? "set name" : s.FrameTag.Trim()) + ")";

        // Read-only rosters shown when the Bents / Walls container is selected: each member + its
        // offset from origin (the running sum of Separation). Offsets honor the drawing's units.
        private object BentRoster()
        {
            var rows = new List<(string, double)>();
            double z = 0;
            foreach (BentSpec b in _spec.Bents) { z += b.Separation; rows.Add((b.Name, z)); }   // Separation = gap from the previous bent
            return new RosterView("Bents", rows);
        }
        private object WallRoster()
        {
            var rows = new List<(string, double)>();
            double x = 0;
            foreach (WallSpec w in _spec.Walls) { rows.Add((w.Name, x)); x += w.Separation; }
            return new RosterView("Walls", rows);
        }

        private static string NodeLabel(FrameElement el)
        {
            if (el is BentSpec b) return "Bent " + b.Name + (b.TypeIsSet ? " (" + b.BentType + ")" : " (None)");
            if (el is WallSpec w) return "Wall " + w.Name;
            // Roof tag on EAVE bays (each eave owns its slope's commons/purlins); the center / vstrut
            // lines carry no commons, so they read plain.
            if (el is BaySpec y)  return y.Role == BayRole.Eave ? y.Name + " (" + y.RoofShort() + ")" : y.Name;
            return el.Name;
        }

        // A property pane edit committed (arg = the descriptor's Name). Type change -> rebuild the
        // folder. Name change -> relabel the node. A leaf Foot/Head -> recompute reported Length/Angle.
        // Otherwise just persist.
        private void HandlePropChange(string name)
        {
            TreeNode node = treeView.SelectedNode;
            object tag = node?.Tag;

            if ((name == "BentType" || name == "RoofType") && tag is IMemberOwner owner)
            {
                owner.RebuildTimbers();      // factory emits the type's default timbers (all enabled)
                TypeDefaults.Apply(owner);   // overlay the saved per-type template if one exists
                if (name == "BentType") _spec.SyncWallRoles();   // re-role B/D (queen / hammer / vstrut)
                Persist();
                BuildTree();
                SelectByTag(tag);
                return;
            }

            // A HammerBeam divisor change can add/remove interior wall lines (div6 stacks two hammer-post
            // tiers per side -> A-G), so reconcile the wall set and redraw the tree.
            if (name == "HBDivisor" && tag is BentSpec)
            {
                _spec.SyncWallRoles();
                Persist();
                BuildTree();
                SelectByTag(tag);
                return;
            }

            // Leaf edits: the descriptor Name is the canonical label (Width/Foot/Head/Label/...).
            if (_selLeaves.Count > 0)
            {
                if (name == "Foot" || name == "Head" || name == "Angle")
                {
                    // Brace solve-for: the two mask-active inputs drive the derived third (the
                    // Assembly-tab mechanic). Struts' Angle edits pass through untouched.
                    foreach (TreeNode ln in _selLeaves)
                        if (ln.Tag is TimberLeaf tl && IsBraceRole(tl.Timber.Role))
                            RecomputeBrace(tl.Timber.Size);
                    BindGrid();   // re-render the derived rows
                }
                else if (name == "Name")
                {
                    foreach (TreeNode ln in _selLeaves)
                        if (ln.Tag is TimberLeaf tl) ln.Text = tl.Timber.Label;
                }
                Persist();
                return;
            }

            if (name == "Name" && node != null && tag is FrameElement fe)
                node.Text = NodeLabel(fe);
            if (name == "FrameTag" && node != null && tag is FrameSpec fs2)
                node.Text = FrameLabel(fs2);
            Persist();
        }

        // --------------------------------------------------------- context menu
        private void SetupMenu()
        {
            _menu = new ContextMenuStrip(this.components);
            _menu.Opening += (s, e) => BuildMenu(e);
            treeView.ContextMenuStrip = _menu;
        }

        private void BuildMenu(System.ComponentModel.CancelEventArgs e)
        {
            _menu.Items.Clear();
            object tag = treeView.SelectedNode?.Tag;

            // First-element affordances (the empty start state): the root and the Bents/Walls
            // containers offer the initial Add so the tree can grow incrementally without the
            // New starter seed. Once an element exists, Insert Before/After takes over.
            if (tag is FrameSpec || ReferenceEquals(tag, _bentsTag) || ReferenceEquals(tag, _wallsTag))
            {
                if (_spec.Bents.Count == 0 && !ReferenceEquals(tag, _wallsTag))
                    _menu.Items.Add(new ToolStripMenuItem("Add Bent", null, (s, a) => AddFirstBent()));
                if (_spec.Walls.Count == 0 && !ReferenceEquals(tag, _bentsTag))
                    _menu.Items.Add(new ToolStripMenuItem("Add Wall", null, (s, a) => AddFirstWall()));
            }

            // Menus otherwise only on Bent/Wall element nodes -- Insert Before/After (split the gap or
            // extend at an end) + Remove (kept >= 1 so the tree always has a node to grow from).
            // Timber leaves get no menu.
            if (tag is BentSpec bent)
            {
                _menu.Items.Add(new ToolStripMenuItem("Insert Bent Before", null, (s, a) => InsertBentCmd(bent, true)));
                _menu.Items.Add(new ToolStripMenuItem("Insert Bent After",  null, (s, a) => InsertBentCmd(bent, false)));
                AddInsertFromTemplate(bent);
                if (_spec.Bents.Count > 1)
                    _menu.Items.Add(new ToolStripMenuItem("Remove Bent", null, (s, a) => RemoveBent(bent)));
                _menu.Items.Add(new ToolStripSeparator());
                AddTemplateItems(bent);
            }
            else if (tag is WallSpec wall)
            {
                _menu.Items.Add(new ToolStripMenuItem("Insert Wall Before", null, (s, a) => InsertWallCmd(wall, true)));
                _menu.Items.Add(new ToolStripMenuItem("Insert Wall After",  null, (s, a) => InsertWallCmd(wall, false)));
                if (_spec.Walls.Count > 1)
                    _menu.Items.Add(new ToolStripMenuItem("Remove Wall", null, (s, a) => RemoveWallCmd(wall)));
            }
            else if (tag is BaySpec bay)
            {
                AddTemplateItems(bay);   // bays come from walls -> apply a template onto an existing bay cell
            }

            if (_menu.Items.Count == 0) e.Cancel = true;   // nothing to show -> suppress the menu
        }

        // ---- named template library (Save / Apply / Insert-from / Manage) -------------------------
        // The named TemplateLibrary, distinct from the per-type "Set as Default" button (which writes
        // TypeDefaults). Templates carry the bent/bay CONFIG (sizes + which members are on + the type params);
        // the frame supplies span / eave / pitch / spacing, so a template re-fits any frame.
        private void AddTemplateItems(FrameElement el)
        {
            _menu.Items.Add(new ToolStripMenuItem("Save as Template...", null, (s, a) => SaveTemplate(el)));
            var apply = new ToolStripMenuItem("Apply Template");
            foreach (string name in TemplateLibrary.CompatibleNames(el))
            {
                string n = name;
                apply.DropDownItems.Add(new ToolStripMenuItem(n, null, (s, a) => ApplyTemplate((IMemberOwner)el, n)));
            }
            apply.Enabled = apply.DropDownItems.Count > 0;
            _menu.Items.Add(apply);
            _menu.Items.Add(new ToolStripMenuItem("Manage Templates...", null, (s, a) => Dialogs.ManageTemplates(this)));
        }

        // "Insert Bent from Template >": every Bent template; inserts a NEW bent after `sel`, types it, overlays.
        private void AddInsertFromTemplate(BentSpec sel)
        {
            var ins = new ToolStripMenuItem("Insert Bent from Template");
            foreach ((string Name, string Type) t in TemplateLibrary.All())
                if (t.Type != null && t.Type.StartsWith("Bent:"))
                {
                    string n = t.Name;
                    ins.DropDownItems.Add(new ToolStripMenuItem(n, null, (s, a) => InsertBentFromTemplate(sel, n)));
                }
            ins.Enabled = ins.DropDownItems.Count > 0;
            _menu.Items.Add(ins);
        }

        private void SaveTemplate(FrameElement el)
        {
            if (TypeDefaults.Key(el) == null)
            { Dialogs.Info("Set the bent/bay type first, then Save as Template."); return; }
            string defName = el is BentSpec b ? "My " + b.BentType + " Bent"
                           : el is BaySpec y ? "My " + y.RoofShort() + " Bay" : "My Template";
            if (!Dialogs.PromptText(this, "Template name:", defName, out string name)) return;
            if (TemplateLibrary.Exists(name) &&
                !Dialogs.Confirm("Replace the existing template \"" + name + "\"?"))
                return;
            if (TemplateLibrary.Save(name, el))
                AcadApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\nSaved template \"" + name + "\".");
        }

        private void ApplyTemplate(IMemberOwner owner, string name)
        {
            if (!TemplateLibrary.TryGet(name, out FrameElement saved)) return;
            TypeDefaults.ApplyElement(owner, saved);   // overlays sizes + config; never span/eave/pitch/Separation
            Persist(); BuildTree(); SelectByTag(owner);
        }

        private void InsertBentFromTemplate(BentSpec sel, string name)
        {
            if (!TemplateLibrary.TryGet(name, out FrameElement saved) || !(saved is BentSpec sb)) return;
            BentSpec nb = _spec.InsertBent(sel, false, DefaultBentInsertDistance(sel, false));
            if (nb == null) { Dialogs.Info("Could not insert the bent (invalid distance)."); return; }
            nb.BentType = sb.BentType;
            nb.RebuildTimbers();                    // canonical members for the type
            TypeDefaults.ApplyElement(nb, saved);   // overlay the template's sizes + config
            Persist(); BuildTree(); SelectByTag(nb);
        }

        // --------------------------------------------------------- mutations
        private void RemoveBent(BentSpec b)
        {
            if (!_spec.RemoveBent(b)) return;
            Persist();
            BuildTree();
        }

        // The first bent / first wall of an empty tree (the root/container menu items). No
        // distance prompt -- there is nothing to measure from yet; separations come with the
        // second element via Insert Before/After.
        private void AddFirstBent()
        {
            BentSpec nb = _spec.AddBent();
            Persist(); BuildTree(); SelectByTag(nb);
        }

        private void AddFirstWall()
        {
            WallSpec nw = _spec.AddWall();
            Persist(); BuildTree(); SelectByTag(nw);
        }

        // NO distance dialog (Robert's call -- Separation is already a bent property in the pane):
        // the insert takes a DEFAULT and the pane fine-tunes it. An interior insert splits the bay
        // in half; extending the frame repeats its bay rhythm (the last non-zero separation).
        private double DefaultBentInsertDistance(BentSpec sel, bool before)
        {
            double gap = _spec.GapForBentInsert(sel, before, out bool interior);
            if (interior) return gap / 2.0;
            for (int i = _spec.Bents.Count - 1; i >= 0; i--)
                if (_spec.Bents[i].Separation > 1e-9) return _spec.Bents[i].Separation;
            return 96.0;   // a lone-bent frame has no rhythm yet: the stock 8' bay
        }

        private void InsertBentCmd(BentSpec sel, bool before)
        {
            BentSpec nb = _spec.InsertBent(sel, before, DefaultBentInsertDistance(sel, before));
            if (nb == null) { Dialogs.Info("Could not insert the bent (invalid distance)."); return; }
            Persist(); BuildTree(); SelectByTag(nb);
        }

        private void InsertWallCmd(WallSpec sel, bool before)
        {
            double gap = _spec.GapForWallInsert(sel, before, out bool interior);
            string prompt = interior
                ? "Distance from the upstream wall (< " + Fmt(gap) + "), splits the span:"
                : "Separation for the new wall (extends the span):";
            if (!Dialogs.PromptDistance(this, prompt, interior ? gap : 0.0, out double d)) return;
            WallSpec nw = _spec.InsertWall(sel, before, d);
            if (nw == null) { Dialogs.Info("Could not insert the wall (invalid distance)."); return; }
            Persist(); BuildTree(); SelectByTag(nw);
        }

        private void RemoveWallCmd(WallSpec w)
        {
            if (!_spec.RemoveWall(w)) return;
            Persist();
            BuildTree();
        }

        private static string Fmt(double inches) => inches.ToString("0.##") + "\"";

        private void SelectByTag(object tag)
        {
            foreach (TreeNode n in AllNodes(treeView.Nodes))
                if (ReferenceEquals(n.Tag, tag)) { treeView.SelectedNode = n; return; }
        }

        private static IEnumerable<TreeNode> AllNodes(TreeNodeCollection nodes)
        {
            foreach (TreeNode n in nodes)
            {
                yield return n;
                foreach (TreeNode c in AllNodes(n.Nodes)) yield return c;
            }
        }

        // ------------------------------------------------------------- actions
        private void DrawFrame()
        {
            // One-way gate: a frozen frame's generator is locked -- never re-emit over hand edits. The
            // button is disabled when frozen (RefreshFrozenState); this is the matching runtime guard in
            // case the gate flipped elsewhere (the TFreeze command / the TPanel button) while shown.
            var gate = AcadApp.DocumentManager.MdiActiveDocument;
            if (gate != null && FrameRegistry.IsFrozen(gate.Database, FrameTagSafe()))
            {
                Dialogs.Info("Frame " + FrameTagSafe() + " is frozen -- the parametric generator is locked. "
                    + "Edit the timbers with the managed verbs, or start a new frame.");
                RefreshFrozenState();
                return;
            }

            FrameGraph g = KingPostBentGraph.Build(_spec);
            // Emit MANAGED timbers (editable) instead of legacy solids. Place through the current UCS
            // (graph coords interpreted in the UCS -> WCS) so the frame honors the user's UCS origin +
            // orientation, instead of always landing at the world origin.
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            Matrix3d placement = doc != null ? doc.Editor.CurrentUserCoordinateSystem : Matrix3d.Identity;
            placement = ManagedFrameEmitter.Compose(placement);   // stand the frame up Z-up (model basis)
            // Draw/redraw: clear only THIS frame's managed timbers (by FrameTag) before emitting, so
            // other managed frames + non-managed solids stay. Each emitted timber is stamped with the
            // frame tag (+ per-edge bent/bay tags from the graph walk) -- the grouping layer.
            string frameTag = string.IsNullOrWhiteSpace(_spec.FrameTag) ? "A" : _spec.FrameTag.Trim();
            int cleared = 0;
            if (doc != null) { cleared = ManagedTimber.EraseFrame(doc.Database, frameTag); ManagedTimber.EraseGrid(doc.Database, frameTag); }
            int drawn = ManagedFrameEmitter.Emit(g, placement, frameTag, out FrameGrid emitGrid);
            // Structural grid: DRAWING-DERIVED so it includes any TPlace'd sub timbers assigned to this
            // frame and shifts the numbering. Only floor-meeting timbers draw a line. (emitGrid is still
            // used inside Emit to stamp each timber's installer label.)
            FrameGrid grid = doc != null ? FrameGrid.BuildFromDrawing(doc.Database, frameTag, placement) : emitGrid;
            // Free Assembly elements emit no geometry, but the RECIPE knows their stations -- fold
            // them into the grid as FIRST-CLASS lines (Robert's call: same solid line, bubble, and
            // sequence number as a post-backed bent; replaced the dashed provisional lines).
            var freeBentZ = new List<double>();
            var freeWallX = new List<double>();
            _spec.FreeAssemblyStations(freeBentZ, freeWallX);
            grid.MergeSpecStations(freeBentZ, freeWallX);
            grid.Draw(placement, frameTag);   // flat under the frame (model basis)
            if (doc != null) ManagedCommands.RelabelBraces(doc.Database);   // brace symbols (*, **) by size+shape
            Persist();
            // Stamp the emitted recipe into the frame's registry record (batch-3 #3), so re-opening
            // this DRAWING refills the tree with THIS frame -- the spec follows the drawing, not the
            // machine. Other record fields (the freeze gate, the TRoughIn seed) are preserved.
            if (doc != null)
            {
                try
                {
                    FrameRecord rec = FrameRegistry.Load(doc.Database, frameTag) ?? new FrameRecord();
                    rec.SpecJson = FrameSpecStore.ToJson(_spec);
                    FrameRegistry.Save(doc.Database, frameTag, rec);
                }
                catch (System.Exception ex)
                { Diag.Warn("FrameTree.Draw", "spec stamp failed (recall-on-open unavailable): " + ex.Message); }
            }
            doc?.Editor.WriteMessage(
                "\nTDraw: frame " + frameTag + " -- cleared " + cleared + " managed timber(s); emitted "
                + drawn + " managed timbers across " + g.Nodes.Count + " nodes (grid "
                + grid.ColX.Count + " cols x " + grid.BentZ.Count + " bents).");
        }

        // Start a fresh frame project: an UN-SEEDED empty tree (Robert's call) -- grow it with
        // right-click Add Bent / Add Wall, or Load a .framespec. Confirms first when it would
        // discard work (Save first to keep it).
        private void NewFrame()
        {
            if (!SpecIsEmpty && !Dialogs.Confirm(
                    "Start a new frame project? The current frame will be cleared (Save first if you want to keep it)."))
                return;

            ResetToEmpty();
        }

        // "Set as Default" saves the SELECTED bent/bay (a node, or a timber leaf via its owner) as the
        // per-TYPE template (member sizes + checkboxes + type-global params), so new bents/bays of that
        // type seed from it (TypeDefaults.Apply runs when the type is (re)set).
        private void SetAsDefault()
        {
            object tag = treeView.SelectedNode?.Tag;
            FrameElement el = (tag as FrameElement) ?? (tag as TimberLeaf)?.Owner as FrameElement;
            if (!(el is BentSpec || el is BaySpec))
            {
                Dialogs.Info("Select a bent or bay (with its type set), then Set as Default.");
                return;
            }
            string key = TypeDefaults.Save(el);
            if (key == null)
            {
                Dialogs.Info("Set the bent/bay type first, then Set as Default.");
                return;
            }
            AcadApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                "\nSaved " + key + " default (sizes + checkboxes); new " + key + " elements seed from it.");
        }

        private void SaveSpec(bool saveAs)
        {
            string path = _currentPath;
            if (saveAs || string.IsNullOrEmpty(path))
            {
                using var dlg = new SaveFileDialog { Filter = "Frame template (*.framespec)|*.framespec", DefaultExt = "framespec" };
                if (dlg.ShowDialog() != DialogResult.OK) return;
                path = dlg.FileName;
            }
            try
            {
                System.IO.File.WriteAllText(path, FrameSpecStore.ToJson(_spec));
                _currentPath = path;
                Persist();
                AcadApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\nSaved frame template to " + path);
            }
            catch (System.Exception ex) { Dialogs.Info("Save failed: " + ex.Message); }
        }

        private void LoadFromFile()
        {
            using var dlg = new OpenFileDialog { Filter = "Frame template (*.framespec)|*.framespec" };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            try
            {
                _spec = FrameSpecStore.FromJson(System.IO.File.ReadAllText(dlg.FileName));
                _currentPath = dlg.FileName;
                Persist();
                BuildTree();
            }
            catch (System.Exception ex) { Dialogs.Info("Load failed: " + ex.Message); }
        }

        private void Persist()
        {
            // The tree's autosave: a failure means the working spec is NOT persisted across a
            // doc switch/restart -- log it (settings write denied / serialize fault) but never
            // interrupt editing.
            try { S.FrameSpecJson = FrameSpecStore.ToJson(_spec); }
            catch (System.Exception ex) { Diag.Warn("FrameTree.Persist", "spec autosave failed: " + ex.Message); }
        }

        // ------------------------------------------------ per-item property maps
        // Top-level props shown when a bent NODE is selected (sizes live on the leaves).
        private static string[] BentNodeProps(BentSpec b)
        {
            var list = new List<string> { "Name", "BentType", "Separation" };
            if (b.TypeIsSet && !b.IsFreeAssembly) list.Add("GirtDrop");   // tie elevation (every typed bent has a tie)
            if (b.TypeIsSet && !b.IsFreeAssembly) list.Add("BraceCentering");   // this bent's brace/strut/vstrut centering
            if (b.BentType == "HammerBeam") list.Add("HBDivisor");
            if (b.BentType == "QueenPost" || b.BentType == "QueenPostTruss") list.Add("QueenOffset");
            return list.ToArray();
        }

        // One row in a timber leaf's property view: which OWNER object holds the property, its real
        // name on that owner, the label to show, the category band, and whether it's read-only.
        private class Row
        {
            public readonly object Owner;
            public readonly string Real, Label, Category;
            public readonly bool ReadOnly;
            public Row(object owner, string real, string label, string category, bool readOnly)
            { Owner = owner; Real = real; Label = label; Category = category; ReadOnly = readOnly; }
        }

        // The property rows for a timber leaf. All timbers share Label + Width + Depth; braces add the
        // two legs (Foot/Head) plus a reported (read-only) Length/Angle and Placement; struts add Angle
        // + Placement; the bent floor girt adds Height. Commons/Purlins keep their flat schedule on the
        // owning BaySpec. Mixing owners (Label on the Timber, dims on its Size) is fine -- each row
        // binds its own owner, so multi-select still co-edits by common label.
        private static List<Row> LeafRows(TimberLeaf tl)
        {
            Timber t = tl.Timber;
            // Identity: Type (the role, read-only) + Name (the descriptive label, retitles the leaf).
            var rows = new List<Row>
            {
                new Row(t, "Role",  "Type", "Identity", true),
                new Row(t, "Label", "Name", "Identity", false),
            };

            if (tl.Owner is BaySpec y && (t.Role == "Commons" || t.Role == "Purlins"))
            {
                string p = t.Role == "Commons" ? "Common" : "Purlin";
                rows.Add(new Row(y, p + "Mode", "Mode", "Size", false));
                rows.Add(new Row(y, p + "Count", "Count", "Size", false));
                rows.Add(new Row(y, p + "Spacing", "Spacing", "Size", false));
                rows.Add(new Row(y, p + "W", "Width", "Size", false));
                rows.Add(new Row(y, p + "D", "Depth", "Size", false));
                if (t.Role == "Commons")   // commons carry the eave overhang (tail run + end cut)
                {
                    rows.Add(new Row(y, "CommonTail", "Tail", "Size", false));
                    rows.Add(new Row(y, "CommonTailCut", "Tail Cut", "Size", false));
                }
                return rows;
            }

            MemberSize s = t.Size;
            rows.Add(new Row(s, "W", "Width", "Size", false));
            rows.Add(new Row(s, "D", "Depth", "Size", false));
            switch (t.Role)
            {
                case "Brace": case "QueenBrace": case "CollarBrace": case "EaveBrace": case "FloorBrace":
                case "RidgeBrace": case "QueenGirtBrace": case "HPostGirtBrace":
                    // The Assembly-tab solve-for mechanic: the two mask-checked rows are the inputs,
                    // the third is derived + read-only; Length stays reported. The labels render as
                    // checkboxes via propPane.RowChecked.
                    s.Angle = System.Math.Round(s.Angle, 2);   // normalize legacy full-precision angles
                    rows.Add(new Row(s, "Foot",  "Foot",  "Size", !_braceMask.Contains("Foot")));
                    rows.Add(new Row(s, "Head",  "Head",  "Size", !_braceMask.Contains("Head")));
                    rows.Add(new Row(s, "Angle", "Angle", "Size", !_braceMask.Contains("Angle")));
                    rows.Add(new Row(s, "Length", "Length", "Size", true));   // reported
                    rows.Add(new Row(s, "Place", "Placement", "Size", false));
                    break;
                case "Strut":
                    rows.Add(new Row(s, "Angle", "Angle", "Size", false));
                    rows.Add(new Row(s, "Place", "Placement", "Size", false));
                    break;
                case "VStrut":
                    rows.Add(new Row(s, "Place", "Placement", "Size", false));
                    break;
                case "FloorGirt":
                    // Both the bent floor girt (FloorGirt:FG) and the bay floor girt (FloorGirt:S) carry
                    // their own elevation.
                    if (t.Key == "FloorGirt:FG" || t.Key == "FloorGirt:S") rows.Add(new Row(s, "Ht", "Height", "Size", false));
                    break;
                case "EaveGirt":   // bay eave girt: its top elevation (seed EaveHt+1 reveal); lower to drop the girt
                    rows.Add(new Row(s, "Ht", "Height", "Size", false));
                    break;
                case "Sill":       // sill TOP elevation: 0 = right under the post feet; negative = deeper
                    rows.Add(new Row(s, "Ht", "Height", "Size", false));
                    break;
            }
            return rows;
        }

        // Distance fields accept AutoCAD architectural input (1'2-1/2") via DistanceConverter; other
        // doubles (Angle/Pitch) and non-doubles keep their default converter.
        private static readonly System.Collections.Generic.HashSet<string> DistanceNames =
            new System.Collections.Generic.HashSet<string>
            { "W", "D", "Length", "Foot", "Head", "Ht", "Span", "EaveHt", "Separation", "QueenOffset",
              "GirtDrop", "CommonTail", "CommonSpacing", "PurlinSpacing", "CommonW", "CommonD", "PurlinW", "PurlinD" };
        private static readonly DistanceConverter SharedDistance = new DistanceConverter();
        private static bool IsDistanceField(string realName, Type t)
            => t == typeof(double) && DistanceNames.Contains(realName);

        // Strip a leading sort number ("1 Geometry" -> "Geometry"); plain names pass through. Removing
        // the number lets categories sort alphabetically (the digits were only an ordering hint).
        private static string CleanCategory(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            int i = 0;
            while (i < raw.Length && char.IsDigit(raw[i])) i++;
            return i > 0 ? raw.Substring(i).TrimStart() : raw;
        }

        // PropertyGrid adapter that exposes only a named subset of the target's properties (in order).
        // Each is wrapped in a DisplayDescriptor so category numbers are stripped and distance fields
        // get the architectural-input converter, while [DisplayName] + write-through are preserved.
        private class FilteredView : ICustomTypeDescriptor
        {
            private readonly object _t;
            private readonly string[] _names;
            public FilteredView(object target, string[] names) { _t = target; _names = names; }

            public PropertyDescriptorCollection GetProperties() => GetProperties(null);
            public PropertyDescriptorCollection GetProperties(Attribute[] attributes)
            {
                PropertyDescriptorCollection all = TypeDescriptor.GetProperties(_t, attributes);
                var list = new List<PropertyDescriptor>();
                foreach (string n in _names) { PropertyDescriptor pd = all[n]; if (pd != null) list.Add(new DisplayDescriptor(pd)); }
                return new PropertyDescriptorCollection(list.ToArray());
            }
            public object GetPropertyOwner(PropertyDescriptor pd) => _t;
            public AttributeCollection GetAttributes() => TypeDescriptor.GetAttributes(_t);
            public string GetClassName() => TypeDescriptor.GetClassName(_t);
            public string GetComponentName() => TypeDescriptor.GetComponentName(_t);
            public TypeConverter GetConverter() => TypeDescriptor.GetConverter(_t);
            public EventDescriptor GetDefaultEvent() => TypeDescriptor.GetDefaultEvent(_t);
            public PropertyDescriptor GetDefaultProperty() => null;
            public object GetEditor(Type editorBaseType) => TypeDescriptor.GetEditor(_t, editorBaseType);
            public EventDescriptorCollection GetEvents() => TypeDescriptor.GetEvents(_t);
            public EventDescriptorCollection GetEvents(Attribute[] attributes) => TypeDescriptor.GetEvents(_t, attributes);
        }

        // Pass-through wrapper for a real node property: keeps the inner PD's owner/value/DisplayName,
        // but cleans the category label (no sort number) and swaps in the distance converter for
        // architectural input. Used by FilteredView (the node's owner is supplied via GetPropertyOwner).
        private class DisplayDescriptor : PropertyDescriptor
        {
            private readonly PropertyDescriptor _inner;
            private readonly bool _useDistance;
            private readonly string _category;
            public DisplayDescriptor(PropertyDescriptor inner) : base(inner.Name, AttrsOf(inner))
            {
                _inner = inner;
                _useDistance = IsDistanceField(inner.Name, inner.PropertyType);
                _category = CleanCategory(inner.Category);
            }
            private static Attribute[] AttrsOf(PropertyDescriptor pd)
            {
                var arr = new Attribute[pd.Attributes.Count];
                pd.Attributes.CopyTo(arr, 0);
                return arr;
            }
            public override string Category => _category;
            public override TypeConverter Converter => _useDistance ? SharedDistance : _inner.Converter;
            public override Type ComponentType => _inner.ComponentType;
            public override Type PropertyType => _inner.PropertyType;
            public override bool IsReadOnly => _inner.IsReadOnly;
            public override object GetValue(object c) => _inner.GetValue(c);
            public override void SetValue(object c, object v) { _inner.SetValue(c, v); OnValueChanged(c, EventArgs.Empty); }
            public override bool CanResetValue(object c) => _inner.CanResetValue(c);
            public override void ResetValue(object c) => _inner.ResetValue(c);
            public override bool ShouldSerializeValue(object c) => _inner.ShouldSerializeValue(c);
        }

        // A PropertyDescriptor bound to a FIXED owner: GetValue/SetValue ignore the `component`
        // (the array element PropertyGrid passes under SelectedObjects) and read/write the captured
        // owner. This is what lets one row drive several owners at once. Equality by Name + PropertyType
        // makes the SAME labelled row across any owners/kinds merge to one (the multi-select intersection).
        private class OwnerBoundPropertyDescriptor : PropertyDescriptor
        {
            private readonly object _owner;
            private readonly PropertyDescriptor _inner;
            private readonly bool _readOnly;
            private readonly bool _useDistance;
            public OwnerBoundPropertyDescriptor(object owner, PropertyDescriptor inner, string label, string category, bool readOnly)
                : base(label, new Attribute[] { new CategoryAttribute(category) })
            {
                _owner = owner; _inner = inner; _readOnly = readOnly;
                _useDistance = IsDistanceField(inner.Name, inner.PropertyType);
            }

            public override Type ComponentType => _inner.ComponentType;
            public override Type PropertyType => _inner.PropertyType;
            public override bool IsReadOnly => _readOnly || _inner.IsReadOnly;
            public override TypeConverter Converter => _useDistance ? SharedDistance : _inner.Converter;
            public override object GetValue(object component) => _inner.GetValue(_owner);
            public override void SetValue(object component, object value)
            {
                if (_readOnly) return;
                _inner.SetValue(_owner, value);
                OnValueChanged(_owner, EventArgs.Empty);
            }
            public override void ResetValue(object c) { }
            public override bool CanResetValue(object c) => false;
            public override bool ShouldSerializeValue(object c) => false;

            public override bool Equals(object obj)
                => obj is OwnerBoundPropertyDescriptor o && o.Name == Name && o.PropertyType == PropertyType;
            public override int GetHashCode() => Name.GetHashCode() ^ PropertyType.GetHashCode();
        }

        // PropertyGrid adapter for a timber leaf: exposes its rows (each bound to its own owner -- Label
        // on the Timber, dims on the Size) as owner-bound, labelled descriptors. Works for one leaf
        // (SelectedObject) or several (SelectedObjects) -- in the multi case PropertyGrid shows only the
        // rows common to all (the intersection by label + type).
        private class TimberMemberView : ICustomTypeDescriptor
        {
            private readonly List<Row> _rows;
            public TimberMemberView(List<Row> rows) { _rows = rows; }
            private object First => _rows.Count > 0 ? _rows[0].Owner : null;

            public PropertyDescriptorCollection GetProperties() => GetProperties(null);
            public PropertyDescriptorCollection GetProperties(Attribute[] attributes)
            {
                var list = new List<PropertyDescriptor>();
                foreach (Row r in _rows)
                {
                    PropertyDescriptor pd = TypeDescriptor.GetProperties(r.Owner)[r.Real];
                    if (pd != null) list.Add(new OwnerBoundPropertyDescriptor(r.Owner, pd, r.Label, r.Category, r.ReadOnly));
                }
                return new PropertyDescriptorCollection(list.ToArray());
            }
            public object GetPropertyOwner(PropertyDescriptor pd) => First;
            public AttributeCollection GetAttributes() => TypeDescriptor.GetAttributes(First);
            public string GetClassName() => TypeDescriptor.GetClassName(First);
            public string GetComponentName() => TypeDescriptor.GetComponentName(First);
            public TypeConverter GetConverter() => TypeDescriptor.GetConverter(First);
            public EventDescriptor GetDefaultEvent() => TypeDescriptor.GetDefaultEvent(First);
            public PropertyDescriptor GetDefaultProperty() => null;
            public object GetEditor(Type editorBaseType) => TypeDescriptor.GetEditor(First, editorBaseType);
            public EventDescriptorCollection GetEvents() => TypeDescriptor.GetEvents(First);
            public EventDescriptorCollection GetEvents(Attribute[] attributes) => TypeDescriptor.GetEvents(First, attributes);
        }

        // A read-only PropertyGrid row: a member name + its offset (double, units-formatted). No backing
        // object -- GetValue ignores the component and returns the captured offset.
        private class RosterDescriptor : PropertyDescriptor
        {
            private readonly double _v;
            public RosterDescriptor(string name, string category, double v)
                : base(name, new Attribute[] { new CategoryAttribute(category) }) { _v = v; }
            public override Type ComponentType => typeof(RosterView);
            public override Type PropertyType => typeof(double);
            public override bool IsReadOnly => true;
            public override TypeConverter Converter => SharedDistance;   // offsets in the drawing's units
            public override object GetValue(object c) => _v;
            public override void SetValue(object c, object value) { }
            public override void ResetValue(object c) { }
            public override bool CanResetValue(object c) => false;
            public override bool ShouldSerializeValue(object c) => false;
        }

        // Read-only PropertyGrid adapter for a Bents/Walls roster: one row per member (name -> offset).
        private class RosterView : ICustomTypeDescriptor
        {
            private readonly string _category;
            private readonly List<(string name, double offset)> _rows;
            public RosterView(string category, List<(string, double)> rows) { _category = category; _rows = rows; }

            public PropertyDescriptorCollection GetProperties() => GetProperties(null);
            public PropertyDescriptorCollection GetProperties(Attribute[] attributes)
            {
                var list = new List<PropertyDescriptor>();
                var seen = new System.Collections.Generic.HashSet<string>();
                foreach (var (name, offset) in _rows)
                {
                    string n = name; int dup = 2;
                    while (!seen.Add(n)) n = name + " (" + dup++ + ")";   // descriptor names must be unique
                    list.Add(new RosterDescriptor(n, _category, offset));
                }
                return new PropertyDescriptorCollection(list.ToArray());
            }
            public object GetPropertyOwner(PropertyDescriptor pd) => this;
            public AttributeCollection GetAttributes() => AttributeCollection.Empty;
            public string GetClassName() => null;
            public string GetComponentName() => null;
            public TypeConverter GetConverter() => null;
            public EventDescriptor GetDefaultEvent() => null;
            public PropertyDescriptor GetDefaultProperty() => null;
            public object GetEditor(Type editorBaseType) => null;
            public EventDescriptorCollection GetEvents() => EventDescriptorCollection.Empty;
            public EventDescriptorCollection GetEvents(Attribute[] attributes) => EventDescriptorCollection.Empty;
        }
    }
}
