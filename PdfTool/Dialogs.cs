using System;
using System.Drawing;
using System.Windows.Forms;

namespace PdfTool
{
    // Small styled option windows used by the tools.
    // Each one is built from rows stacked in an auto-sizing layout, so it fits any DPI.
    internal static class Dialogs
    {
        // Rotate: pick an angle and which pages. Returns false if cancelled.
        public static bool AskRotate(IWin32Window owner, string fileName, int pageCount, out int angle, out string pages)
        {
            angle = 90; pages = "all";
            using var f = NewDialog("Rotate pages", fileName);
            var rows = Rows(f);

            rows.Controls.Add(Label("Rotate by"));
            var choices = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
            var rRight = Chip("↻  90° right", true);
            var r180 = Chip("180°", false);
            var rLeft = Chip("↺  90° left", false);
            choices.Controls.AddRange(new Control[] { rRight, r180, rLeft });
            rows.Controls.Add(choices);

            rows.Controls.Add(Label("Pages"));
            var tb = TextBox("all");
            rows.Controls.Add(tb);
            rows.Controls.Add(Hint($"Type  all  or pages like  1,3-5   (this PDF has {pageCount} page{(pageCount == 1 ? "" : "s")})"));

            if (!ShowWithButtons(f, owner, rows, "Rotate")) return false;
            angle = rRight.Checked ? 90 : r180.Checked ? 180 : 270; // 270 clockwise = 90 left
            pages = tb.Text;
            return true;
        }

        // Watermark: text, opacity and colour. Returns false if cancelled.
        public static bool AskWatermark(IWin32Window owner, string fileName, out string text, out int opacity, out Color color)
        {
            text = null; opacity = 25; color = Color.Red;
            using var f = NewDialog("Add watermark", fileName);
            var rows = Rows(f);

            rows.Controls.Add(Label("Text"));
            var tb = TextBox("CONFIDENTIAL");
            rows.Controls.Add(tb);

            rows.Controls.Add(Label("Opacity"));
            var opRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
            var track = new TrackBar { Minimum = 10, Maximum = 80, Value = 25, TickFrequency = 10, Width = 260, AutoSize = true };
            var opLabel = new Label { AutoSize = true, Text = "25%", Font = Theme.Body, Margin = new Padding(6, 8, 0, 0) };
            track.ValueChanged += (s, e) => opLabel.Text = track.Value + "%";
            opRow.Controls.Add(track); opRow.Controls.Add(opLabel);
            rows.Controls.Add(opRow);

            rows.Controls.Add(Label("Colour"));
            var colours = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
            var cRed = Chip("Red", true); var cGray = Chip("Grey", false); var cBlue = Chip("Blue", false);
            colours.Controls.AddRange(new Control[] { cRed, cGray, cBlue });
            rows.Controls.Add(colours);
            rows.Controls.Add(Hint("Text is drawn diagonally across every page and sized to fit."));

            if (!ShowWithButtons(f, owner, rows, "Add watermark")) return false;
            if (string.IsNullOrWhiteSpace(tb.Text)) return false;
            text = tb.Text.Trim();
            opacity = track.Value;
            color = cRed.Checked ? Color.FromArgb(229, 50, 45) : cGray.Checked ? Color.FromArgb(90, 90, 90) : Color.FromArgb(37, 99, 235);
            return true;
        }

        // ---------- building blocks ----------

        private static Form NewDialog(string title, string subtitle)
        {
            var f = new Form
            {
                Text = title,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false,
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Theme.Surface,
                Font = Theme.Body,
                AutoSize = true,                              // grow to fit the content...
                AutoSizeMode = AutoSizeMode.GrowAndShrink,    // ...and nothing more
                Padding = new Padding(20, 16, 20, 16)
            };
            f.Tag = subtitle;
            return f;
        }

        private static TableLayoutPanel Rows(Form f)
        {
            // Not docked: the form sizes itself to this panel, so the panel must size itself to its content.
            var rows = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Location = new Point(20, 16) };
            var head = new Label { AutoSize = true, Text = f.Text, Font = Theme.Title, ForeColor = Theme.Text, Margin = new Padding(0, 0, 0, 0) };
            var sub = new Label { AutoSize = true, Text = (string)f.Tag, Font = Theme.Small, ForeColor = Theme.Muted, Margin = new Padding(2, 0, 0, 14), MaximumSize = new Size(380, 0) };
            rows.Controls.Add(head);
            rows.Controls.Add(sub);
            return rows;
        }

        private static bool ShowWithButtons(Form f, IWin32Window owner, TableLayoutPanel rows, string okText)
        {
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 16, 0, 0) };
            var ok = Theme.PrimaryButton(okText);
            ok.DialogResult = DialogResult.OK;
            ok.Margin = new Padding(0);
            var cancel = Theme.SecondaryButton("Cancel");
            cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
            rows.Controls.Add(buttons);

            f.Controls.Add(rows);
            f.AcceptButton = ok;   // Enter = OK
            f.CancelButton = cancel; // Esc = Cancel
            return f.ShowDialog(owner) == DialogResult.OK;
        }

        private static Label Label(string text) =>
            new Label { AutoSize = true, Text = text, Font = Theme.BodyBold, ForeColor = Theme.Text, Margin = new Padding(0, 0, 0, 6) };

        private static Label Hint(string text) =>
            new Label { AutoSize = true, Text = text, Font = Theme.Small, ForeColor = Theme.Muted, Margin = new Padding(0, 4, 0, 0) };

        private static TextBox TextBox(string text) =>
            new TextBox { Text = text, Width = 380, Font = Theme.Body, Margin = new Padding(0, 0, 0, 0) };

        // A radio button that looks like a toggle chip; one per group is selected.
        private static RadioButton Chip(string text, bool isChecked)
        {
            var rb = new RadioButton
            {
                Text = text, Checked = isChecked, Appearance = Appearance.Button, AutoSize = true,
                FlatStyle = FlatStyle.Flat, Font = Theme.Body, Cursor = Cursors.Hand,
                Padding = new Padding(10, 3, 10, 3), Margin = new Padding(0, 0, 6, 0),
                TextAlign = ContentAlignment.MiddleCenter
            };
            rb.FlatAppearance.BorderColor = Theme.Border;
            rb.FlatAppearance.CheckedBackColor = Theme.Selected;
            rb.FlatAppearance.MouseOverBackColor = Theme.Hover;
            void Restyle() => rb.ForeColor = rb.Checked ? Theme.Accent : Theme.Text;
            rb.CheckedChanged += (s, e) => Restyle();
            Restyle();
            return rb;
        }
    }
}
