using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace TimberDraw
{
    // A lightweight, fully-ordered property panel: renders an object's (or several objects')
    // PropertyDescriptors in the EXACT order returned -- with section headers, separators, per-row
    // styling, and type-appropriate editors. Replaces the stock PropertyGrid (which forces alphabetical
    // order + grid-wide-only styling, and can't do separators/blank rows). It consumes the SAME
    // descriptor sets the tree already builds (FilteredView / TimberMemberView / RosterView); values and
    // parse/format go through each descriptor's Converter (so DistanceConverter handles unit input/display).
    public class PropertyPane : UserControl
    {
        private readonly TableLayoutPanel _table;
        private bool _loading;

        // Raised (deferred to the message loop) after a successful edit; arg = the descriptor's Name.
        public event Action<string> ValueCommitted;

        // Optional solve-for toggles (the brace two-of-three mechanic): the host returns a checked
        // state for rows whose LABEL should render as a CHECKBOX, null for a plain label. Toggles
        // raise RowCheckToggled (deferred -- the handler typically re-Binds, disposing the sender).
        public Func<string, bool?> RowChecked;
        public event Action<string, bool> RowCheckToggled;

        public PropertyPane()
        {
            AutoScroll = true;
            BackColor = Theme.Bg;
            ForeColor = Theme.Fg;
            _table = PaneRows.MakeGrid();   // the shared 2-col row idiom (Theme.LabelW label column)
            Controls.Add(_table);
        }

        public void Clear() => Bind(Array.Empty<object>());
        public void Bind(object target) => Bind(target == null ? Array.Empty<object>() : new[] { target });

        public void Bind(object[] targets)
        {
            _loading = true;
            _table.SuspendLayout();
            _table.Controls.Clear();
            _table.RowStyles.Clear();
            _table.RowCount = 0;
            try
            {
                string lastCat = null;
                bool first = true;
                foreach (DisplayRow r in BuildRows(targets))
                {
                    if (r.Category != lastCat)
                    {
                        if (!first) AddSeparator();
                        AddHeader(r.Category);
                        lastCat = r.Category;
                        first = false;
                    }
                    AddRow(r);
                }
            }
            finally { _table.ResumeLayout(); _loading = false; }
        }

        // ------------------------------------------------------------- row model
        private class DisplayRow
        {
            public string Label, Category;
            public Type Type;
            public TypeConverter Converter;
            public bool ReadOnly;
            public readonly List<(PropertyDescriptor pd, object owner)> Cells = new List<(PropertyDescriptor, object)>();
        }

        private static IEnumerable<(PropertyDescriptor pd, object owner)> Props(object target)
        {
            PropertyDescriptorCollection pds = TypeDescriptor.GetProperties(target);
            var ictd = target as ICustomTypeDescriptor;
            foreach (PropertyDescriptor pd in pds)
                yield return (pd, ictd?.GetPropertyOwner(pd) ?? target);
        }

        private static string Key(PropertyDescriptor pd) => pd.DisplayName + "|" + pd.PropertyType.FullName;

        // First target defines rows + order; other targets must have a matching (DisplayName, type) row
        // (multi-select intersection). Each surviving row collects one (pd, owner) per target.
        private static List<DisplayRow> BuildRows(object[] targets)
        {
            var rows = new List<DisplayRow>();
            if (targets == null || targets.Length == 0) return rows;

            var others = new List<Dictionary<string, (PropertyDescriptor pd, object owner)>>();
            for (int i = 1; i < targets.Length; i++)
            {
                var d = new Dictionary<string, (PropertyDescriptor, object)>();
                foreach (var x in Props(targets[i])) d[Key(x.pd)] = x;
                others.Add(d);
            }

            foreach (var (pd, owner) in Props(targets[0]))
            {
                string k = Key(pd);
                var row = new DisplayRow
                {
                    Label = pd.DisplayName, Category = pd.Category ?? "", Type = pd.PropertyType,
                    Converter = pd.Converter, ReadOnly = pd.IsReadOnly
                };
                row.Cells.Add((pd, owner));
                bool ok = true;
                foreach (var d in others)
                {
                    if (d.TryGetValue(k, out var c)) row.Cells.Add(c);
                    else { ok = false; break; }
                }
                if (ok) rows.Add(row);
            }
            return rows;
        }

        // ------------------------------------------------------------- rendering
        private void AddHeader(string text) => PaneRows.AddFullRow(_table, PaneRows.HeaderCell(text));

        private void AddSeparator() => PaneRows.AddSeparator(_table);

        private void AddRow(DisplayRow row)
        {
            string name = row.Cells[0].pd.Name;
            bool? chk = RowChecked?.Invoke(name);
            Control label;
            if (chk.HasValue)
            {
                var cb = new CheckBox
                {
                    Text = row.Label, Checked = chk.Value,
                    Dock = DockStyle.Fill, Height = Theme.RowH,
                    ForeColor = row.ReadOnly ? Theme.SubtleFg : Theme.Fg,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(4, 0, 0, 0), Margin = new Padding(0),
                    TabStop = false,   // Tab walks the VALUE fields only (Robert's call)
                };
                cb.CheckedChanged += (s, e) =>
                {
                    if (_loading) return;
                    bool v = cb.Checked;
                    if (IsHandleCreated) BeginInvoke((Action)(() => RowCheckToggled?.Invoke(name, v)));
                    else RowCheckToggled?.Invoke(name, v);
                };
                label = cb;
            }
            else label = PaneRows.RowLabel(row.Label, 6, subtle: row.ReadOnly);
            PaneRows.AddParamRow(_table, label, BuildEditor(row));
        }

        private Control BuildEditor(DisplayRow row)
        {
            object val = CommonValue(row, out bool mixed);

            if (row.ReadOnly)
                return new Label
                {
                    Text = mixed ? "" : Display(row, val), Dock = DockStyle.Fill, Height = Theme.RowH,
                    TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.SubtleFg,
                    Padding = new Padding(3, 0, 0, 0), Margin = new Padding(0, 1, 0, 1)
                };

            if (row.Type == typeof(bool))
            {
                var cb = new CheckBox
                {
                    Dock = DockStyle.Fill, Height = 23, ThreeState = false, Margin = new Padding(2, 1, 0, 1),
                    CheckState = mixed ? CheckState.Indeterminate : ((bool)val ? CheckState.Checked : CheckState.Unchecked)
                };
                cb.CheckedChanged += (s, e) => { if (!_loading) Commit(row, cb.Checked); };
                return cb;
            }

            object[] items = StandardItems(row);
            if (items != null)
            {
                // Items are the converter's raw standard values (the stored objects, e.g. ints 4/6); the
                // Format event renders each through the converter so a mapping converter can show display
                // text (e.g. "2"/"3") while SelectedItem -> Commit still passes the stored value.
                // Editors are rebuilt on every Bind -- after the one-time Theme.Apply walk -- so
                // theme them here (a ComboBox never inherits ambient colors; Flat because the
                // standard renderer ignores BackColor for the closed portion).
                var combo = new ComboBox
                {
                    Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList,
                    Margin = new Padding(0, 1, 1, 1), FormattingEnabled = true,
                    BackColor = Theme.Surface, ForeColor = Theme.Fg,
                    FlatStyle = FlatStyle.Flat
                };
                combo.Format += (s, e) => e.Value = Display(row, e.ListItem);
                combo.Items.AddRange(items);
                if (!mixed && val != null) combo.SelectedItem = val;
                combo.SelectedIndexChanged += (s, e) => { if (!_loading && combo.SelectedItem != null) Commit(row, combo.SelectedItem); };
                return combo;
            }

            var txt = new TextBox
            {
                Dock = DockStyle.Fill, Margin = new Padding(0, 1, 1, 1),
                BackColor = Theme.Surface, ForeColor = Theme.Fg,
                BorderStyle = BorderStyle.FixedSingle,   // flat -- the 3D bevel ignores BackColor
                Text = mixed ? "" : Display(row, val)
            };
            txt.Leave += (s, e) => CommitText(row, txt);
            txt.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { CommitText(row, txt); e.SuppressKeyPress = true; } };
            return txt;
        }

        // Dropdown items: enum values, or a string property whose converter exposes standard values
        // (BentType / RoofType). Null -> a free TextBox.
        private static object[] StandardItems(DisplayRow row)
        {
            if (row.Type.IsEnum) return ToArray(Enum.GetValues(row.Type));
            TypeConverter c = row.Converter;
            if (c != null && c.GetStandardValuesSupported())
            {
                var sv = c.GetStandardValues();
                if (sv != null) return ToArray(sv);
            }
            return null;
        }

        private static object[] ToArray(System.Collections.IEnumerable e)
        {
            var l = new List<object>();
            foreach (object o in e) l.Add(o);
            return l.ToArray();
        }

        private static object CommonValue(DisplayRow row, out bool mixed)
        {
            mixed = false;
            object first = row.Cells[0].pd.GetValue(row.Cells[0].owner);
            for (int i = 1; i < row.Cells.Count; i++)
                if (!Equals(first, row.Cells[i].pd.GetValue(row.Cells[i].owner))) { mixed = true; return null; }
            return first;
        }

        private static string Display(DisplayRow row, object val)
        {
            if (val == null) return "";
            try { return row.Converter != null ? row.Converter.ConvertToString(val) : val.ToString(); }
            catch { return val.ToString(); }
        }

        private void CommitText(DisplayRow row, TextBox txt)
        {
            if (_loading) return;
            object parsed;
            try { parsed = row.Converter != null ? row.Converter.ConvertFromString(txt.Text) : Convert.ChangeType(txt.Text, row.Type); }
            catch { System.Media.SystemSounds.Beep.Play(); return; }   // invalid input -> keep editing
            Commit(row, parsed);
            // Reflect the STORED value back into the cell: a setter may clamp or normalize (e.g. the Girt
            // Offset 6" minimum), so what was kept isn't necessarily what was typed. Re-read synchronously
            // (before Commit's deferred host reaction) and guard _loading so this doesn't re-commit.
            try
            {
                var (pd, owner) = row.Cells[0];
                _loading = true;
                txt.Text = Display(row, pd.GetValue(owner));
            }
            catch { }
            finally { _loading = false; }
        }

        private void Commit(DisplayRow row, object value)
        {
            foreach (var (pd, owner) in row.Cells)
                if (!pd.IsReadOnly) { try { pd.SetValue(owner, value); } catch { } }
            // Defer the host reaction (it may rebuild the tree, disposing this editor) until the current
            // UI event unwinds.
            string name = row.Cells[0].pd.Name;
            if (IsHandleCreated) BeginInvoke((Action)(() => ValueCommitted?.Invoke(name)));
            else ValueCommitted?.Invoke(name);
        }
    }
}
