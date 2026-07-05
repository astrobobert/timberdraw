using System.Drawing;
using System.Windows.Forms;

namespace TimberDraw
{
    // The ONE modal-dialog surface: themed prompt dialogs (text / architectural distance) and
    // the template manager, plus MessageBox wrappers so every alert carries the same caption
    // and wording style. UI code calls these instead of hand-building Forms or MessageBox.Show.
    internal static class Dialogs
    {
        private const string Caption = "TimberDraw";

        // ---- message wrappers -------------------------------------------------------------

        public static void Info(string msg) => MessageBox.Show(msg, Caption);

        public static void Warn(string msg)
            => MessageBox.Show(msg, Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);

        public static bool Confirm(string msg)
            => MessageBox.Show(msg, Caption, MessageBoxButtons.OKCancel) == DialogResult.OK;

        public static bool ConfirmWarn(string msg)
            => MessageBox.Show(msg, Caption, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK;

        // ---- prompts ----------------------------------------------------------------------

        // Small modal text prompt. False on cancel / empty.
        public static bool PromptText(Control owner, string prompt, string initial, out string value)
        {
            value = "";
            using (var dlg = NewDialog(Caption, 330, 116))
            {
                var lbl = new Label { Text = prompt, Location = new Point(10, 12), Size = new Size(310, 20) };
                var txt = new TextBox { Text = initial ?? "", Location = new Point(10, 40), Size = new Size(310, 24) };
                var ok = OkButton(160, 82);
                var cancel = CancelButton(245, 82);
                dlg.Controls.AddRange(new Control[] { lbl, txt, ok, cancel });
                dlg.AcceptButton = ok; dlg.CancelButton = cancel;
                Theme.Apply(dlg);
                txt.SelectAll();
                if (dlg.ShowDialog(owner) != DialogResult.OK) return false;
                value = txt.Text.Trim();
                return value.Length > 0;
            }
        }

        // Small modal distance prompt; accepts AutoCAD architectural input (1'2-1/2") via
        // UnitInput. `max > 0` enforces a strict upper bound (the gap being split).
        public static bool PromptDistance(Control owner, string prompt, double max, out double value)
        {
            value = 0.0;
            using (var dlg = NewDialog(Caption, 330, 116))
            {
                var lbl = new Label { Text = prompt, Location = new Point(10, 10), Size = new Size(310, 36) };
                var txt = new TextBox { Location = new Point(10, 50), Size = new Size(310, 24) };
                var ok = OkButton(160, 82);
                var cancel = CancelButton(245, 82);
                dlg.Controls.AddRange(new Control[] { lbl, txt, ok, cancel });
                dlg.AcceptButton = ok; dlg.CancelButton = cancel;
                Theme.Apply(dlg);
                if (dlg.ShowDialog(owner) != DialogResult.OK) return false;
                if (!UnitInput.TryParseDistance(txt.Text, out value) || value <= 0.0)
                { Info("Enter a positive distance (e.g. 8'6\")."); return false; }
                if (max > 0.0 && value >= max)
                { Info("Must be less than " + FmtInches(max) + "."); return false; }
                return true;
            }
        }

        // List + Rename / Delete / Set-as-type-default for the named template library.
        public static void ManageTemplates(Control owner)
        {
            using (var dlg = NewDialog("Manage Templates", 430, 286))
            {
                var list = new ListBox { Location = new Point(10, 10), Size = new Size(290, 266) };
                void Reload()
                {
                    list.Items.Clear();
                    foreach ((string Name, string Type) t in TemplateLibrary.All())
                        list.Items.Add(t.Name + "   (" + t.Type + ")");
                }
                Reload();
                string Sel()
                {
                    if (list.SelectedIndex < 0) return null;
                    string s = (string)list.SelectedItem;
                    int p = s.IndexOf("   (");
                    return p > 0 ? s.Substring(0, p) : s;
                }
                var rename = SideButton("Rename...", 10);
                var del    = SideButton("Delete", 44);
                var setdef = SideButton("Set as type default", 88);
                var close  = SideButton("Close", 250);
                close.DialogResult = DialogResult.OK;
                rename.Click += (s, a) =>
                {
                    string n = Sel();
                    if (n != null && PromptText(dlg, "New name:", n, out string nn))
                    {
                        if (TemplateLibrary.Rename(n, nn)) Reload();
                        else Info("That name is already taken.");
                    }
                };
                del.Click += (s, a) =>
                {
                    string n = Sel();
                    if (n != null && Confirm("Delete template \"" + n + "\"?"))
                    { TemplateLibrary.Remove(n); Reload(); }
                };
                setdef.Click += (s, a) =>
                {
                    string n = Sel();
                    if (n != null && TemplateLibrary.TryGet(n, out FrameElement el))
                    {
                        string k = TypeDefaults.Save(el);
                        if (k != null) Info("\"" + n + "\" is now the default for new " + k + " elements.");
                    }
                };
                dlg.Controls.AddRange(new Control[] { list, rename, del, setdef, close });
                dlg.AcceptButton = close;
                Theme.Apply(dlg);
                dlg.ShowDialog(owner);
            }
        }

        public static string FmtInches(double inches) => inches.ToString("0.##") + "\"";

        // ---- shared pieces ------------------------------------------------------------------

        private static Form NewDialog(string title, int w, int h) => new Form
        {
            Text = title, FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false,
            ClientSize = new Size(w, h),
        };

        private static Button OkButton(int x, int y)
        {
            Button b = Theme.Button("OK");
            b.DialogResult = DialogResult.OK;
            b.Location = new Point(x, y); b.Size = new Size(75, 24);
            return b;
        }

        private static Button CancelButton(int x, int y)
        {
            Button b = Theme.Button("Cancel");
            b.DialogResult = DialogResult.Cancel;
            b.Location = new Point(x, y); b.Size = new Size(75, 24);
            return b;
        }

        private static Button SideButton(string text, int y)
        {
            Button b = Theme.Button(text);
            b.Location = new Point(310, y); b.Size = new Size(110, 26);
            return b;
        }
    }
}
