using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TimberDraw
{
    // The Joints pane: pick a timber pair, choose a connection type, and edit joinery params on ONE STABLE GRID
    // that lists EVERY element + param across ALL connection types (a 2-column grid: a bold enable-checkbox header
    // per element, then a label + typed editor per param, each with a glossary TOOLTIP). The selected type decides
    // which rows are AVAILABLE -- the params it supports are editable, the rest are GRAYED. The grid is built ONCE
    // (the union); selecting a type only re-evaluates enable state + values, so the layout never jumps. The REAL
    // joint re-cuts LIVE as a value / element changes (the joint id replaces in place). DB writes run in a command
    // context, so the pane only drives TJoinPick / TJoinApply (via SendStringToExecute) -- never the drawing directly.
    public class JoinPaletteControl : UserControl
    {
        private readonly ComboBox _cmbType = new ComboBox();
        private readonly Button _btnPick = new Button();
        private readonly Label _lblPair = new Label();
        private readonly Label _lblStatus = new Label();          // last apply result / diagnostic
        private readonly Panel _stackHost = new Panel();          // scroll host for the grid
        private readonly TableLayoutPanel _grid = PaneRows.MakeGrid();   // the shared 2-col row idiom
        private readonly Button _btnApply = new Button();
        private readonly Button _btnSetDefault = new Button();    // persist the active values as the type's default
        private readonly Button _btnResetDefault = new Button();  // drop the saved default (disabled = none saved)
        private readonly ToolTip _tip = new ToolTip { AutoPopDelay = 20000, InitialDelay = 400, ReshowDelay = 100, ShowAlways = true };

        private readonly List<ConnectionType> _presets = ConnectionType.BuiltIns();
        private ConnectionType _active;
        private bool _loading;   // suppress the live re-cut while BuildGrid / ApplyActive set Checked / Value

        // The fixed union grid: one header per element kind + one cell per (kind, param) across all presets.
        private readonly Dictionary<ElementKind, CheckBox> _headers = new Dictionary<ElementKind, CheckBox>();
        private readonly List<ParamCell> _paramCells = new List<ParamCell>();

        // The focused numeric field (by kind + name), so ProcessCmdKey can commit it on Enter.
        private TextBox _activeText;
        private ElementKind _activeKind;
        private string _activeName;

        // Element groups in display order; only kinds that some preset actually uses appear.
        private static readonly ElementKind[] ElementOrder =
            { ElementKind.Tenon, ElementKind.Housing, ElementKind.Shoulder, ElementKind.Dovetail, ElementKind.Pegs };

        private sealed class ParamCell
        {
            public ElementKind Kind;
            public JointParam Desc;     // the canonical descriptor (Name / Kind / range / choices / default)
            public Label Label;
            public Control Editor;
        }

        public JoinPaletteControl()
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            ForeColor = Theme.Fg;

            // Actions cluster at the BOTTOM (Pick pair / Apply full-span + the defaults row) --
            // the look every tab follows now.
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(Theme.Pad) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));   // type selector
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));   // pair label
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // element grid
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));   // status line
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));   // pick pair
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));   // apply
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));   // defaults (set / reset)

            _cmbType.Dock = DockStyle.Fill;
            _cmbType.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (ConnectionType ct in _presets) _cmbType.Items.Add(ct.Name);
            _cmbType.SelectedIndexChanged += (s, e) => SelectPreset(_cmbType.SelectedIndex);

            _btnPick.Dock = DockStyle.Fill; _btnPick.Text = "Pick pair";
            _btnPick.Click += (s, e) => Send("TJoinPick");

            _lblPair.Dock = DockStyle.Fill; _lblPair.TextAlign = ContentAlignment.MiddleLeft; _lblPair.Text = "Picked: (none)";

            _lblStatus.Dock = DockStyle.Fill; _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            _lblStatus.AutoEllipsis = true; _lblStatus.Text = "";

            // The grid: the SHARED 2-col row idiom (PaneRows), same metrics as PropertyPane.
            _stackHost.Dock = DockStyle.Fill; _stackHost.AutoScroll = true; _stackHost.BorderStyle = BorderStyle.FixedSingle;
            _stackHost.BackColor = Theme.Bg;
            _stackHost.Controls.Add(_grid);

            _btnApply.Dock = DockStyle.Fill; _btnApply.Text = "Apply";
            _btnApply.Click += (s, e) => OnApply();

            // The DEFAULTS row: persist / drop the selected type's user default. A store gesture only --
            // it never re-cuts the held pair. Clicking either button focus-commits a pending text edit
            // first (the TextBox Leave -> CommitText path), so what you see is what gets saved.
            _btnSetDefault.Dock = DockStyle.Fill; _btnSetDefault.Text = "Set as default";
            _btnSetDefault.Click += (s, e) => OnSetDefault();
            _btnResetDefault.Dock = DockStyle.Fill; _btnResetDefault.Text = "Reset to factory";
            _btnResetDefault.Click += (s, e) => OnResetDefault();
            var defaultsRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            defaultsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            defaultsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            defaultsRow.Controls.Add(_btnSetDefault, 0, 0);
            defaultsRow.Controls.Add(_btnResetDefault, 1, 0);

            layout.Controls.Add(_cmbType, 0, 0);
            layout.Controls.Add(_lblPair, 0, 1);
            layout.Controls.Add(_stackHost, 0, 2);
            layout.Controls.Add(_lblStatus, 0, 3);
            layout.Controls.Add(_btnPick, 0, 4);
            layout.Controls.Add(_btnApply, 0, 5);
            layout.Controls.Add(defaultsRow, 0, 6);
            Controls.Add(layout);

            BuildGrid();   // the stable union grid, once
            Theme.Apply(this);   // inputs + buttons follow the shared palette (dark or light)

            JoinSession.Changed += OnSessionChanged;
            Disposed += (s, e) => JoinSession.Changed -= OnSessionChanged;

            if (_presets.Count > 0) _cmbType.SelectedIndex = 0;
        }

        // Selecting a preset clones it (so edits are per-instance) and re-evaluates the grid (enable + values) for
        // it. Guarded by _loading so a PROGRAMMATIC dropdown set (loading an existing joint) does not re-cut.
        private void SelectPreset(int idx)
        {
            if (_loading) return;
            if (idx < 0 || idx >= _presets.Count) return;
            _active = _presets[idx].Clone();
            JoinSession.Active = _active;
            ApplyActive();
            UpdateDefaultButtons();
            if (JointDefaults.HasSaved(_active.Name)) SetStatus(_active.Name + " (saved default)");
            Regen();   // the dropdown is king: changing the type instantly re-cuts the held pair as the new type
        }

        // ---- user defaults (the "troublesome baked-in values" fix): persist / drop the selected type's
        //      default. Store gestures only -- they never re-cut the held pair. -------------------------

        // Reset enabled = a saved default exists for the selected type (the minimal indicator).
        private void UpdateDefaultButtons()
        {
            _btnResetDefault.Enabled = _active != null && JointDefaults.HasSaved(_active.Name);
            _btnSetDefault.Enabled = _active != null;
        }

        // Persist the ACTIVE values (including a just-typed field -- the button click focus-commits it via
        // Leave -> CommitText) as this type's default. JointDefaults.Save also re-seeds the console sticky,
        // so TJoint/TStrut/... use the new values immediately; the presets refresh so re-selecting the type
        // seeds from the save. _active itself IS the saved state -- no reload, no re-cut.
        private void OnSetDefault()
        {
            if (_active == null) return;
            if (!ConnectionType.SaveAsDefault(_active)) { SetStatus("no default slot for " + _active.Name); return; }
            RefreshPresets();
            UpdateDefaultButtons();
            SetStatus(_active.Name + ": saved as default");
        }

        // Drop the saved default and show the factory values -- WITHOUT SelectPreset, whose Regen() would
        // silently re-cut a held pair with the factory values (a destructive surprise for a store gesture).
        private void OnResetDefault()
        {
            if (_active == null) return;
            string name = _active.Name;
            JointDefaults.Reset(name);
            RefreshPresets();
            int idx = _presets.FindIndex(p => p.Name == name);
            if (idx >= 0)
            {
                _active = _presets[idx].Clone();
                JoinSession.Active = _active;
                ApplyActive();
            }
            UpdateDefaultButtons();
            SetStatus(name + ": factory defaults restored");
        }

        // Re-pull the presets so they reflect the stored defaults (the list is readonly -- refresh in place).
        // The dropdown items are NOT rebuilt: names are stable, and resetting them would fire SelectPreset
        // -> Regen and re-cut a held pair.
        private void RefreshPresets()
        {
            _presets.Clear();
            _presets.AddRange(ConnectionType.BuiltIns());
        }

        // Build the fixed grid ONCE: the union of every element kind + param name across all presets (first-seen
        // descriptor per param), one header per kind and one labelled editor per param. No values yet -- ApplyActive
        // fills them per selected type.
        private void BuildGrid()
        {
            _loading = true;
            _grid.SuspendLayout();
            _grid.Controls.Clear(); _grid.RowStyles.Clear(); _grid.RowCount = 0;
            _headers.Clear(); _paramCells.Clear();

            var union = new Dictionary<ElementKind, List<JointParam>>();
            foreach (ConnectionType ct in _presets)
                foreach (JointElement el in ct.Elements)
                {
                    if (!union.TryGetValue(el.Kind, out List<JointParam> ps)) { ps = new List<JointParam>(); union[el.Kind] = ps; }
                    foreach (JointParam p in el.Params)
                        if (!ps.Exists(q => q.Name == p.Name)) ps.Add(p);
                }

            foreach (ElementKind kind in ElementOrder)
            {
                if (!union.TryGetValue(kind, out List<JointParam> ps)) continue;

                ElementKind kc = kind;
                CheckBox chk = PaneRows.HeaderCheck(kind.ToString());
                _tip.SetToolTip(chk, JointGlossary.ElementTip(kind));
                chk.CheckedChanged += (s, e) =>
                {
                    if (_loading) return;
                    JointElement el = _active?.E(kc);
                    if (el == null) return;
                    el.Enabled = chk.Checked;
                    ApplyActive();   // gray / ungray its params
                    Regen();
                };
                _headers[kind] = chk;
                AddHeaderRow(chk);

                foreach (JointParam desc in ps)
                {
                    Label lbl = PaneRows.RowLabel(desc.Name, 8);
                    Control editor = BuildEditor(kind, desc);
                    string tip = JointGlossary.ParamTip(desc.Name);
                    if (tip.Length > 0) { _tip.SetToolTip(lbl, tip); _tip.SetToolTip(editor, tip); }
                    _paramCells.Add(new ParamCell { Kind = kind, Desc = desc, Label = lbl, Editor = editor });
                    AddParamRow(lbl, editor);
                }
            }
            _grid.ResumeLayout();
            _loading = false;
        }

        // Re-evaluate the whole grid for the active type: a header is enabled only if the type HAS that element
        // (and checked = its Enabled); a param is editable only if the type has it AND its element is on, else it is
        // GRAYED and shows the canonical default. No rebuild -- the layout is fixed.
        private void ApplyActive()
        {
            _loading = true;
            foreach (KeyValuePair<ElementKind, CheckBox> h in _headers)
            {
                JointElement el = _active?.E(h.Key);
                bool has = el != null;
                h.Value.Enabled = has;
                h.Value.Checked = has && el.Enabled;
            }
            foreach (ParamCell c in _paramCells)
            {
                JointElement el = _active?.E(c.Kind);
                JointParam p = el?.P(c.Desc.Name);
                bool enabled = p != null && el.Enabled;
                SetEditorValue(c.Editor, c.Desc, p != null ? p.Value : c.Desc.Default);
                c.Editor.Enabled = enabled;
                c.Label.Enabled = enabled;
            }
            _loading = false;
        }

        private void AddHeaderRow(Control c) => PaneRows.AddFullRow(_grid, c);

        private void AddParamRow(Control label, Control editor) => PaneRows.AddParamRow(_grid, label, editor);

        // A typed editor for a param DESCRIPTOR (its value is set later by ApplyActive). Handlers resolve the LIVE
        // param from the active type by (kind, name) at edit time, so the same persistent control drives whichever
        // type is selected. CONSISTENT commit model: numeric fields commit on Enter OR click-away (clamped; Count
        // rounds); Choice (dropdown) + Toggle (checkbox) commit immediately.
        private Control BuildEditor(ElementKind kind, JointParam desc)
        {
            ElementKind kc = kind; string name = desc.Name;

            if (desc.Kind == ParamKind.Choice && desc.Choices != null && desc.Choices.Length > 0)
            {
                var combo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 1, 1, 1) };
                combo.Items.AddRange(desc.Choices);
                combo.SelectedIndexChanged += (s, e) => { if (_loading) return; JointParam p = Param(kc, name); if (p == null) return; p.Value = combo.SelectedIndex; Regen(); };
                return combo;
            }

            if (desc.Kind == ParamKind.Toggle)
            {
                var cb = new CheckBox { Dock = DockStyle.Fill, Height = 23, Margin = new Padding(2, 1, 0, 1) };
                cb.CheckedChanged += (s, e) => { if (_loading) return; JointParam p = Param(kc, name); if (p == null) return; p.Value = cb.Checked ? 1.0 : 0.0; Regen(); };
                return cb;
            }

            var txt = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 1, 1, 1) };
            txt.Enter += (s, e) => { _activeText = txt; _activeKind = kc; _activeName = name; };
            txt.Leave += (s, e) => { CommitText(txt, kc, name); if (_activeText == txt) { _activeText = null; _activeName = null; } };
            txt.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { CommitText(txt, kc, name); e.SuppressKeyPress = true; } };
            return txt;
        }

        private JointParam Param(ElementKind kind, string name) => _active?.E(kind)?.P(name);

        // AutoCAD's palette host can swallow Enter before a hosted TextBox raises KeyDown (so the field only ever
        // committed on click-away). ProcessCmdKey runs during message PRE-processing, ahead of that, so commit the
        // focused numeric field here; returning true consumes the keystroke so it doesn't also bubble to AutoCAD.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Enter && _activeText != null && _activeName != null && _activeText.Focused)
            {
                CommitText(_activeText, _activeKind, _activeName);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // Render a numeric value (Count as a whole number) for display.
        private static string Show(ParamKind kind, double v)
            => (kind == ParamKind.Count ? System.Math.Round(v) : v).ToString("0.###", CultureInfo.InvariantCulture);

        private static void SetEditorValue(Control editor, JointParam desc, double v)
        {
            switch (editor)
            {
                case ComboBox combo:
                    int idx = (int)System.Math.Round(v);
                    combo.SelectedIndex = idx >= 0 && idx < combo.Items.Count ? idx : 0;
                    break;
                case CheckBox cb:
                    cb.Checked = v >= 0.5;
                    break;
                case TextBox txt:
                    txt.Text = Show(desc.Kind, v);
                    break;
            }
        }

        // Commit a text field into the LIVE param (resolved by kind + name): parse, clamp to [Min,Max] (round Count),
        // store, reflect the stored value back, and live re-cut only if it actually changed. Bad input beeps + reverts.
        private void CommitText(TextBox txt, ElementKind kind, string name)
        {
            if (_loading) return;
            JointParam p = Param(kind, name);
            if (p == null) return;
            if (!double.TryParse(txt.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
            {
                System.Media.SystemSounds.Beep.Play();
                _loading = true; txt.Text = Show(p.Kind, p.Value); _loading = false;
                return;
            }
            double c = Clamp(v, p.Min, p.Max);
            if (p.Kind == ParamKind.Count) c = System.Math.Round(c);
            bool changed = c != p.Value;
            p.Value = c;
            _loading = true; txt.Text = Show(p.Kind, c); _loading = false;   // show the stored value
            if (changed) Regen();
        }

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);

        // Live re-cut on a value / element change: stash the model and re-cut the REAL joint through the command
        // context. The joint id makes each re-cut replace in place. Silent when no pair is held yet (pick first).
        // A live edit always KEEPS the pair (ReleaseOnApply cleared here in case a flagged Apply never ran).
        private void Regen()
        {
            if (_loading || _active == null) return;
            if (!JoinSession.HasPair) { SetStatus("Pick a pair first"); return; }
            JoinSession.Active = _active;
            JoinSession.ReleaseOnApply = false;
            Send("TJoinApply");
        }

        // APPLY = the finalize gesture: affix the joint (cut + persist its spec) and RELEASE the pair, so the
        // pane is free to change type/params for the NEXT joint without re-cutting this one. (Live edits before
        // Apply still re-cut in place -- clicking Apply also commits the focused text field via its Leave.)
        private void OnApply()
        {
            if (_active == null) return;
            JoinSession.Active = _active;
            if (!JoinSession.HasPair) { MessageBox.Show("Press \"Pick pair\" and select two timbers first.", "TimberDraw"); return; }
            JoinSession.ReleaseOnApply = true;
            Send("TJoinApply");
        }

        private void OnSessionChanged()
        {
            if (!IsHandleCreated) { return; }
            BeginInvoke(new Action(() =>
            {
                _lblPair.Text = JoinSession.HasPair ? "Picked: pair set" : "Picked: (none)";
                SetStatus(JoinSession.LastDiag);
                LoadExistingJoint();
            }));
        }

        // Show a status message via the Theme convention: blue for a hint / "nothing to cut", plain
        // otherwise (never green -- the user is red/green colorblind). The full text is also a
        // tooltip, since long diagnostics get ellipsized.
        private void SetStatus(string msg)
        {
            bool issue = !string.IsNullOrEmpty(msg) &&
                (msg.StartsWith("nothing", StringComparison.OrdinalIgnoreCase) ||
                 msg.StartsWith("Pick a pair", StringComparison.OrdinalIgnoreCase));
            Theme.SetStatus(_lblStatus, msg, issue);
            _tip.SetToolTip(_lblStatus, _lblStatus.Text);
        }

        // If the picked pair carried a saved joint, repopulate the pane with it (preset + values) WITHOUT
        // triggering a re-cut. Consumes JoinSession.LoadedState; no-op when there's none (keep the current preset).
        private void LoadExistingJoint()
        {
            string st = JoinSession.LoadedState;
            if (string.IsNullOrEmpty(st)) return;
            JoinSession.LoadedState = null;
            ConnectionType loaded = ConnectionType.FromState(_presets, st);
            if (loaded == null) return;
            _loading = true;
            _active = loaded;
            JoinSession.Active = _active;
            int idx = _presets.FindIndex(p => p.Name == loaded.Name);
            if (idx >= 0 && _cmbType.SelectedIndex != idx) _cmbType.SelectedIndex = idx;   // SelectPreset is _loading-guarded
            _loading = false;
            ApplyActive();
            UpdateDefaultButtons();
        }

        // Launch an AutoCAD command from the pane. SendStringToExecute is required -- interactive prompts + DB
        // writes do not run directly from a WinForms handler.
        private static void Send(string command)
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            doc?.SendStringToExecute(command + " ", true, false, true);
        }
    }
}
