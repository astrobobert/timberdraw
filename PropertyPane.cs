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

        private const int LabelW = 150;
        private static readonly Color HeaderBack = SystemColors.ControlLight;
        private static readonly Color ReadOnlyFore = SystemColors.GrayText;

        public PropertyPane()
        {
            AutoScroll = true;
            _table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows,
                Padding = new Padding(0, 0, 0, 4)
            };
            _table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelW));
            _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
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
        private void AddHeader(string text)
        {
            var lbl = new Label
            {
                Text = text, Dock = DockStyle.Fill, Height = 20, BackColor = HeaderBack,
                Font = new Font(Font, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 0, 0), Margin = new Padding(0)
            };
            int r = _table.RowCount++;
            _table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _table.Controls.Add(lbl, 0, r);
            _table.SetColumnSpan(lbl, 2);
        }

        private void AddSeparator()
        {
            var sep = new Panel { Dock = DockStyle.Fill, Height = 7, Margin = new Padding(0) };
            sep.Paint += (s, e) =>
            {
                int y = sep.Height / 2;
                using (var p = new Pen(SystemColors.ControlDark))
                    e.Graphics.DrawLine(p, 2, y, sep.Width - 2, y);
            };
            int r = _table.RowCount++;
            _table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _table.Controls.Add(sep, 0, r);
            _table.SetColumnSpan(sep, 2);
        }

        private void AddRow(DisplayRow row)
        {
            int r = _table.RowCount++;
            _table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var lbl = new Label
            {
                Text = row.Label, Dock = DockStyle.Fill, Height = 23, AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0),
                Margin = new Padding(0), ForeColor = row.ReadOnly ? ReadOnlyFore : ForeColor
            };
            _table.Controls.Add(lbl, 0, r);
            _table.Controls.Add(BuildEditor(row), 1, r);
        }

        private Control BuildEditor(DisplayRow row)
        {
            object val = CommonValue(row, out bool mixed);

            if (row.ReadOnly)
                return new Label
                {
                    Text = mixed ? "" : Display(row, val), Dock = DockStyle.Fill, Height = 23,
                    TextAlign = ContentAlignment.MiddleLeft, ForeColor = ReadOnlyFore,
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
                var combo = new ComboBox
                {
                    Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList,
                    Margin = new Padding(0, 1, 1, 1), FormattingEnabled = true
                };
                combo.Format += (s, e) => e.Value = Display(row, e.ListItem);
                combo.Items.AddRange(items);
                if (!mixed && val != null) combo.SelectedItem = val;
                combo.SelectedIndexChanged += (s, e) => { if (!_loading && combo.SelectedItem != null) Commit(row, combo.SelectedItem); };
                return combo;
            }

            var txt = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 1, 1, 1), Text = mixed ? "" : Display(row, val) };
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
