using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TimberDraw
{
    // Managed-timber assembly palette (TPanel). Sections are shown as a tree grouped by member
    // type (Post / Girt / ...), with each cross-section (W x D) as a leaf. Selecting a leaf makes
    // it the sticky ManagedSection so the placement verbs stop re-prompting for dimensions. Verb
    // buttons fire the managed commands via SendStringToExecute; orientation buttons fire the UCS
    // preset commands.
    public partial class TimberPaletteControl
    {
        // One catalogue entry: a member type and its cross-section.
        private struct Sec { public string Type; public double W; public double D; }

        private readonly List<Sec> _sections = new List<Sec>();
        private bool _loading;

        private bool _braceLoading;
        private readonly List<CheckBox> _braceOrder = new List<CheckBox>();

        public TimberPaletteControl()
        {
            InitializeComponent();
            Load += (s, e) =>
            {
                WireEvents();
                LoadSections();
                LoadBraceSpec();
                lblBuild.Text = "Build " + Commands.BuildStamp();
            };
        }

        private static TimberDraw.Properties.Settings S => TimberDraw.Properties.Settings.Default;

        // ---------------------------------------------------------------- load / save
        private void LoadSections()
        {
            _loading = true;
            try
            {
                _sections.Clear();
                foreach (string tok in (S.TimberSections ?? "").Split(','))
                {
                    string t = tok.Trim();
                    if (t.Length == 0) continue;
                    string[] f = t.Split(':');
                    if (f.Length < 3) continue;
                    if (!double.TryParse(f[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double w)) continue;
                    if (!double.TryParse(f[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double d)) continue;
                    _sections.Add(new Sec { Type = f[0], W = w, D = d });
                }
            }
            finally { _loading = false; }

            int idx = S.CurrentSection;
            if (idx < 0 || idx >= _sections.Count) idx = _sections.Count > 0 ? 0 : -1;
            RefreshTree(idx);
            ApplySelectionToSection();   // push the loaded selection to the sticky section
            ShowSelection(idx);          // fill the edit boxes + highlight (AfterSelect is suppressed at load)
        }

        // Populate the edit boxes from a flat index and make the matching leaf the visible selection.
        // Used at load, where AfterSelect is suppressed by the _loading guard AND a SelectedNode set
        // during Load does not reliably stick -- so we drive the side effects directly and defer the
        // highlight to after the handle is realized.
        private void ShowSelection(int idx)
        {
            if (idx < 0 || idx >= _sections.Count) return;
            Sec sec = _sections[idx];
            _loading = true;
            try { txtType.Text = sec.Type; txtWidth.Text = Trim(sec.W); txtDepth.Text = Trim(sec.D); }
            finally { _loading = false; }
            if (IsHandleCreated) BeginInvoke(new Action(() => SelectLeafByIndex(idx)));
        }

        private void SelectLeafByIndex(int idx)
        {
            foreach (TreeNode parent in treeSections.Nodes)
                foreach (TreeNode leaf in parent.Nodes)
                    if (leaf.Tag is int t && t == idx) { treeSections.SelectedNode = leaf; return; }
        }

        // Write the section catalogue (CSV of "type:w:d") back to Settings.
        private void SaveCsv()
        {
            var parts = new List<string>();
            foreach (Sec s in _sections)
                parts.Add(s.Type + ":" + s.W.ToString(CultureInfo.InvariantCulture)
                                  + ":" + s.D.ToString(CultureInfo.InvariantCulture));
            S.TimberSections = string.Join(",", parts);
            S.Save();
        }

        // Rebuild the tree from _sections: a parent node per type (alphabetical), one leaf per
        // cross-section (sorted by W then D). Each leaf's Tag is its flat index into _sections.
        // If selectFlatIndex names a section, that leaf is selected after the rebuild.
        private void RefreshTree(int selectFlatIndex)
        {
            _loading = true;
            try
            {
                treeSections.BeginUpdate();
                treeSections.Nodes.Clear();

                var byType = new SortedDictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < _sections.Count; i++)
                {
                    string t = _sections[i].Type;
                    if (!byType.TryGetValue(t, out List<int> lst)) { lst = new List<int>(); byType[t] = lst; }
                    lst.Add(i);
                }

                TreeNode toSelect = null;
                foreach (KeyValuePair<string, List<int>> kv in byType)
                {
                    var parent = new TreeNode(kv.Key);
                    kv.Value.Sort((a, b) =>
                    {
                        int c = _sections[a].W.CompareTo(_sections[b].W);
                        return c != 0 ? c : _sections[a].D.CompareTo(_sections[b].D);
                    });
                    foreach (int idx in kv.Value)
                    {
                        Sec s = _sections[idx];
                        var leaf = new TreeNode(Trim(s.W) + " x " + Trim(s.D)) { Tag = idx };
                        parent.Nodes.Add(leaf);
                        if (idx == selectFlatIndex) toSelect = leaf;
                    }
                    treeSections.Nodes.Add(parent);
                }
                treeSections.ExpandAll();
                treeSections.EndUpdate();
                treeSections.SelectedNode = toSelect;
            }
            finally { _loading = false; }
        }

        private static string Trim(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        // Flat index of the selected cross-section leaf, or -1 if a type node / nothing is selected.
        private int SelectedIndex()
            => (treeSections.SelectedNode?.Tag is int i) ? i : -1;

        // -------------------------------------------------------------- events
        private void WireEvents()
        {
            treeSections.AfterSelect += (s, e) =>
            {
                if (_loading) return;
                if (e.Node?.Tag is int idx && idx >= 0 && idx < _sections.Count)
                {
                    Sec sec = _sections[idx];
                    txtType.Text = sec.Type;
                    txtWidth.Text = Trim(sec.W);
                    txtDepth.Text = Trim(sec.D);
                    ManagedSection.Set(sec.Type, sec.W, sec.D);
                    S.CurrentSection = idx;
                    S.Save();
                }
                else if (e.Node != null && e.Node.Tag == null)
                {
                    // A type (parent) node: prefill the type so Add creates another size under it.
                    txtType.Text = e.Node.Text;
                }
            };

            btnAdd.Click += (s, e) =>
            {
                if (!ReadFields(out Sec sec)) return;
                _sections.Add(sec);
                SaveCsv();
                RefreshTree(_sections.Count - 1);   // selecting it sets ManagedSection + CurrentSection
                ApplySelectionToSection();
            };

            btnUpdate.Click += (s, e) =>
            {
                int i = SelectedIndex();
                if (i < 0) { Warn("Select a cross-section (leaf) to update."); return; }
                if (!ReadFields(out Sec sec)) return;
                _sections[i] = sec;
                SaveCsv();
                RefreshTree(i);
                ApplySelectionToSection();
            };

            btnRemove.Click += (s, e) =>
            {
                int i = SelectedIndex();
                if (i < 0) { Warn("Select a cross-section (leaf) to remove."); return; }
                _sections.RemoveAt(i);
                SaveCsv();
                RefreshTree(Math.Min(i, _sections.Count - 1));
                ApplySelectionToSection();
            };

            // Orientation presets
            btnPlan.Click += (s, e) => Send("TUcsPlan");
            btnBent.Click += (s, e) => Send("TUcsBent");
            btnWall.Click += (s, e) => Send("TUcsWall");

            // Placement / connection verbs (push the section first, then launch the command)
            btnPlace.Click += (s, e) => { ApplySelectionToSection(); Send("TPlace"); };
            btnSpan.Click += (s, e) => { ApplySelectionToSection(); Send("TSpan"); };
            btnJoin.Click += (s, e) => { ApplySelectionToSection(); Send("TJoin"); };
            btnFit.Click += (s, e) => Send("TFit");
            btnScarf.Click += (s, e) => Send("TScarf");

            // Edit / nodes -- Move/Rotate are now stock AutoCAD: the ManagedTransformOverrule keeps each
            // managed timber's frame + scarf node in lockstep, so no special TMove/TRotate is needed.
            btnMove.Click += (s, e) => Send("_MOVE");
            btnRotate.Click += (s, e) => Send("_ROTATE");
            btnScan.Click += (s, e) => Send("TScan");
            btnAssign.Click += (s, e) => Send("TAssign");
            // Re-section reads the picked timber's current section and prompts -- it does NOT adopt the
            // palette's active section, so no ApplySelectionToSection() here.
            btnSection.Click += (s, e) => Send("TSection");

            // Brace spec: exactly two checkboxes drive the third; edits recompute + restock ManagedBrace.
            chkFoot.CheckedChanged += (s, e) => OnBraceCheck(chkFoot);
            chkHead.CheckedChanged += (s, e) => OnBraceCheck(chkHead);
            chkAngle.CheckedChanged += (s, e) => OnBraceCheck(chkAngle);
            txtFoot.TextChanged += (s, e) => OnBraceEdit();
            txtHead.TextChanged += (s, e) => OnBraceEdit();
            txtAngle.TextChanged += (s, e) => OnBraceEdit();
        }

        // ---------------------------------------------------------------- brace spec
        // Keep exactly two checkboxes checked: checking a third unchecks the oldest; you switch which
        // two by CHECKING the third, never by unchecking (unchecking one of two is refused).
        private void OnBraceCheck(CheckBox cb)
        {
            if (_braceLoading) return;
            _braceLoading = true;
            try
            {
                if (cb.Checked)
                {
                    if (!_braceOrder.Contains(cb)) _braceOrder.Add(cb);
                    while (_braceOrder.Count > 2)
                    {
                        CheckBox old = _braceOrder[0];
                        _braceOrder.RemoveAt(0);
                        old.Checked = false;
                    }
                }
                else if (_braceOrder.Count <= 2) { cb.Checked = true; }   // never fewer than two
                else { _braceOrder.Remove(cb); }

                SyncBraceEnabled();
                RecomputeBrace();
                SaveBraceSpec();
            }
            finally { _braceLoading = false; }
        }

        private void OnBraceEdit()
        {
            if (_braceLoading) return;
            _braceLoading = true;
            try { RecomputeBrace(); SaveBraceSpec(); }
            finally { _braceLoading = false; }
        }

        private void SyncBraceEnabled()
        {
            txtFoot.Enabled = chkFoot.Checked;
            txtHead.Enabled = chkHead.Checked;
            txtAngle.Enabled = chkAngle.Checked;
        }

        // Compute the one UNCHECKED value from the two checked, then restock ManagedBrace with the two
        // leg runs. Square-corner assumption; the angle is measured between the FOOT leg and the brace
        // (tan(angle) = head / foot).
        private void RecomputeBrace()
        {
            if (chkFoot.Checked && chkHead.Checked)
            {
                if (TryNum(txtFoot, out double foot) && TryNum(txtHead, out double head) && foot > 0 && head > 0)
                    SetNum(txtAngle, Math.Atan2(head, foot) * 180.0 / Math.PI);
            }
            else if (chkFoot.Checked && chkAngle.Checked)
            {
                if (TryNum(txtFoot, out double foot) && TryNum(txtAngle, out double ang) && foot > 0)
                    SetNum(txtHead, foot * Math.Tan(ClampAngle(ang) * Math.PI / 180.0));
            }
            else if (chkHead.Checked && chkAngle.Checked)
            {
                if (TryNum(txtHead, out double head) && TryNum(txtAngle, out double ang) && head > 0)
                    SetNum(txtFoot, head / Math.Tan(ClampAngle(ang) * Math.PI / 180.0));
            }
            if (TryNum(txtFoot, out double f) && TryNum(txtHead, out double h) && f > 0 && h > 0)
                ManagedBrace.Set(f, h);
        }

        // Persist "foot:head:mask" (mask = the two checked letters, F/H/A). foot:head are always stored;
        // the angle is recoverable as atan2(head, foot).
        private void SaveBraceSpec()
        {
            TryNum(txtFoot, out double f);
            TryNum(txtHead, out double h);
            string mask = (chkFoot.Checked ? "F" : "") + (chkHead.Checked ? "H" : "") + (chkAngle.Checked ? "A" : "");
            S.BraceSpec = f.ToString("0.##", CultureInfo.InvariantCulture) + ":" +
                          h.ToString("0.##", CultureInfo.InvariantCulture) + ":" + mask;
            S.Save();
        }

        private void LoadBraceSpec()
        {
            _braceLoading = true;
            try
            {
                double foot = 18, head = 18;
                string mask = "FH";
                string[] f = (S.BraceSpec ?? "").Split(':');
                if (f.Length >= 1) double.TryParse(f[0], NumberStyles.Any, CultureInfo.InvariantCulture, out foot);
                if (f.Length >= 2) double.TryParse(f[1], NumberStyles.Any, CultureInfo.InvariantCulture, out head);
                if (f.Length >= 3)
                {
                    string m = f[2].Trim().ToUpperInvariant();
                    bool hF = m.Contains("F"), hH = m.Contains("H"), hA = m.Contains("A");
                    if ((hF ? 1 : 0) + (hH ? 1 : 0) + (hA ? 1 : 0) == 2)
                        mask = (hF ? "F" : "") + (hH ? "H" : "") + (hA ? "A" : "");
                }
                if (foot <= 0) foot = 18;
                if (head <= 0) head = 18;

                // All three boxes set consistently (angle recovered from the two legs), then the mask
                // chooses which two are editable.
                SetNum(txtFoot, foot);
                SetNum(txtHead, head);
                SetNum(txtAngle, Math.Atan2(head, foot) * 180.0 / Math.PI);

                chkFoot.Checked = mask.Contains("F");
                chkHead.Checked = mask.Contains("H");
                chkAngle.Checked = mask.Contains("A");
                _braceOrder.Clear();
                if (chkFoot.Checked) _braceOrder.Add(chkFoot);
                if (chkHead.Checked) _braceOrder.Add(chkHead);
                if (chkAngle.Checked) _braceOrder.Add(chkAngle);

                SyncBraceEnabled();
                ManagedBrace.Set(foot, head);
            }
            finally { _braceLoading = false; }
        }

        private static double ClampAngle(double a) => Math.Max(0.1, Math.Min(89.9, a));
        private static bool TryNum(TextBox t, out double v)
            => double.TryParse(t.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out v);
        private static void SetNum(TextBox t, double v)
            => t.Text = v.ToString("0.##", CultureInfo.InvariantCulture);

        // Read the three textboxes into a Sec; warn + reject on bad input.
        private bool ReadFields(out Sec sec)
        {
            sec = default;
            string type = (txtType.Text ?? "").Trim();
            if (type.Length == 0) { Warn("Enter a type."); return false; }
            if (type.IndexOfAny(new[] { ':', ',' }) >= 0) { Warn("Type cannot contain ':' or ','."); return false; }
            if (!double.TryParse(txtWidth.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double w) || w <= 0)
            { Warn("Width must be a positive number."); return false; }
            if (!double.TryParse(txtDepth.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double d) || d <= 0)
            { Warn("Depth must be a positive number."); return false; }
            sec = new Sec { Type = type, W = w, D = d };
            return true;
        }

        // Push the currently selected leaf section into the sticky ManagedSection so the managed
        // verbs use it without prompting. Falls back to the persisted index when the TreeView's
        // SelectedNode hasn't stuck yet (startup), so the section is live without a first click.
        private void ApplySelectionToSection()
        {
            int i = SelectedIndex();
            if (i < 0) i = S.CurrentSection;
            if (i < 0 || i >= _sections.Count) { ManagedSection.Clear(); return; }
            Sec sec = _sections[i];
            ManagedSection.Set(sec.Type, sec.W, sec.D);
        }

        // Launch an AutoCAD command from the palette. SendStringToExecute is required here --
        // Editor.Command / interactive prompts do not run directly from a WinForms handler.
        private static void Send(string command)
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            doc?.SendStringToExecute(command + " ", true, false, true);
        }

        private static void Warn(string msg)
        {
            MessageBox.Show(msg, "TimberDraw", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
