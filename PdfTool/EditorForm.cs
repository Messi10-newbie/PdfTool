using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Storage.Streams;
using WinPdfDoc = Windows.Data.Pdf.PdfDocument;

namespace PdfTool
{
    // PDF editor: view pages, add text, replace text, cover old content with white boxes.
    // Windows.Data.Pdf (built into Windows 10/11) draws the pages on screen;
    // PdfEditWriter (PageEdits.cs) writes the edits into the PDF on save.
    public class EditorForm : Form
    {
        private readonly string srcPath;
        private WinPdfDoc winDoc;
        private readonly List<PageEdit> edits = new List<PageEdit>();
        private readonly EditHistory history = new EditHistory();

        private int pageIndex = 0;
        private float zoom = 1.0f;
        // pixels per point: 100% zoom = real size on a 96 DPI screen
        private float PxPerPt => zoom * 96f / 72f;

        private enum Tool { Text, Replace, Whiteout, Select }
        private Tool tool = Tool.Text;

        private PageEdit selected;
        private bool dragging;
        private PointF dragStart;              // in points
        private PointF dragOffset;             // mouse position minus selected item's corner, in points
        private List<PageEdit> beforeDrag;     // snapshot for undo, taken when a move starts
        private RectangleF? boxPreview;        // rectangle being dragged out (Whiteout / Replace)
        private PageEdit styleRecordedFor;     // stops every size-box click becoming its own undo step
        private bool syncingToolbar;           // true while we copy a selection's style INTO the toolbar
        private string savedFingerprint = "";  // see EditsFingerprint()

        private Panel scroller;
        private PictureBox canvas;
        private Label lblPage, lblStatus;
        private ComboBox cmbFont;
        private NumericUpDown numSize;
        private CheckBox chkBold, chkItalic, chkUnderline;
        private Button btnColor, btnUndo, btnRedo, btnRevert;
        private Color currentColor = Color.Black;
        private string lastGoodFont = "Arial";
        private readonly ToolTip tips = new ToolTip();

        public EditorForm(string pdfPath)
        {
            srcPath = pdfPath;
            WindowsFontResolver.Install(); // so every Windows font can be embedded on save
            Text = "Edit - " + Path.GetFileName(pdfPath);
            Font = Theme.Body;
            BackColor = Theme.Surface;
            Size = new Size(Theme.S(this, 1100), Theme.S(this, 820));
            MinimumSize = new Size(Theme.S(this, 760), Theme.S(this, 500));
            StartPosition = FormStartPosition.CenterParent;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            KeyPreview = true; // form sees key presses first, so shortcuts work anywhere
            KeyDown += Editor_KeyDown;

            // Two toolbar rows stacked at the top: tools/history/pages, then text formatting.
            var top = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                BackColor = Theme.Surface, Padding = new Padding(Theme.S(this, 12), Theme.S(this, 8), Theme.S(this, 12), Theme.S(this, 6))
            };
            top.Paint += (s, e) => { using var pen = new Pen(Theme.Border); e.Graphics.DrawLine(pen, 0, top.Height - 1, top.Width, top.Height - 1); };
            top.Controls.Add(BuildToolRow());
            top.Controls.Add(BuildFormatRow());
            Controls.Add(top);

            // Bottom bar: status message on the left, Save on the right.
            var bottom = new Panel
            {
                Dock = DockStyle.Bottom, Height = Theme.S(this, 52), BackColor = Theme.Surface,
                Padding = new Padding(Theme.S(this, 12), Theme.S(this, 9), Theme.S(this, 12), Theme.S(this, 9))
            };
            bottom.Paint += (s, e) => { using var pen = new Pen(Theme.Border); e.Graphics.DrawLine(pen, 0, 0, bottom.Width, 0); };
            lblStatus = new Label { Dock = DockStyle.Fill, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            var save = Theme.PrimaryButton("Save as PDF...");
            save.AutoSize = false;
            save.Dock = DockStyle.Right;
            save.Width = Theme.S(this, 140);
            save.Click += BtnSave_Click;
            bottom.Controls.Add(lblStatus);
            bottom.Controls.Add(save);
            Controls.Add(bottom);

            scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(222, 225, 230) };
            canvas = new PictureBox { Left = Theme.S(this, 20), Top = Theme.S(this, 20), SizeMode = PictureBoxSizeMode.Normal, BackColor = Color.White };
            canvas.Cursor = Cursors.IBeam; // "Add text" is the starting tool
            canvas.Paint += Canvas_Paint;
            canvas.MouseDown += Canvas_MouseDown;
            canvas.MouseMove += Canvas_MouseMove;
            canvas.MouseUp += Canvas_MouseUp;
            canvas.MouseDoubleClick += Canvas_MouseDoubleClick;
            scroller.Controls.Add(canvas);
            Controls.Add(scroller);
            scroller.BringToFront(); // Fill control must be on top of the z-order so the bars dock around it

            UpdateHistoryButtons();
            Load += async (s, e) => await OpenPdfAsync();
        }

        // ---------- toolbar ----------

        private Control BuildToolRow()
        {
            var row = Row();
            // Radio buttons styled as toggle chips: only one tool active at a time.
            var rbText = ToolRadio("", "Add text", Tool.Text, "Click on the page to type new text");
            var rbReplace = ToolRadio("", "Replace text", Tool.Replace, "Drag a box over existing text, then type what it should say");
            var rbWhiteout = ToolRadio("", "Whiteout", Tool.Whiteout, "Drag a box to cover (hide) something");
            var rbSelect = ToolRadio("", "Select / move", Tool.Select, "Click an item to select it, drag to move, double-click text to change it");
            rbText.Checked = true;
            row.Controls.AddRange(new Control[] { rbText, rbReplace, rbWhiteout, rbSelect, Separator() });

            btnUndo = IconBtn("", "Undo  (Ctrl+Z)", (s, e) => DoUndo());
            btnRedo = IconBtn("", "Redo  (Ctrl+Y)", (s, e) => DoRedo());
            btnRevert = Theme.SecondaryButton("Revert all");
            tips.SetToolTip(btnRevert, "Remove every edit and go back to the original PDF (can be undone)");
            btnRevert.Click += (s, e) => RevertAll();
            row.Controls.AddRange(new Control[] { btnUndo, btnRedo, btnRevert,
                IconBtn("", "Delete selected  (Del)", (s, e) => DeleteSelected()), Separator() });

            row.Controls.Add(IconBtn("", "Previous page  (PgUp)", (s, e) => GoToPage(pageIndex - 1)));
            lblPage = new Label { AutoSize = true, Text = "Page - / -", Margin = new Padding(Theme.S(this, 4), Theme.S(this, 7), Theme.S(this, 4), 0) };
            row.Controls.Add(lblPage);
            row.Controls.Add(IconBtn("", "Next page  (PgDn)", (s, e) => GoToPage(pageIndex + 1)));
            row.Controls.Add(IconBtn("", "Zoom out", (s, e) => SetZoom(zoom - 0.25f)));
            row.Controls.Add(IconBtn("", "Zoom in", (s, e) => SetZoom(zoom + 0.25f)));
            return row;
        }

        private Control BuildFormatRow()
        {
            var row = Row();
            row.Margin = new Padding(0, Theme.S(this, 6), 0, 0);

            row.Controls.Add(MutedLabel("Font"));
            cmbFont = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(this, 200), DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = Theme.S(this, 22), MaxDropDownItems = 16, Margin = new Padding(0, Theme.S(this, 2), Theme.S(this, 10), 0)
            };
            FillFontList();
            cmbFont.DrawItem += CmbFont_DrawItem;
            cmbFont.SelectedIndexChanged += CmbFont_Changed;
            row.Controls.Add(cmbFont);

            row.Controls.Add(MutedLabel("Size"));
            numSize = new NumericUpDown
            {
                Minimum = 4, Maximum = 144, Value = 12, DecimalPlaces = 1, Increment = 0.5m,
                Width = Theme.S(this, 64), Margin = new Padding(0, Theme.S(this, 3), Theme.S(this, 10), 0)
            };
            numSize.ValueChanged += (s, e) => ApplyStyleToSelected();
            row.Controls.Add(numSize);

            chkBold = StyleToggle("B", FontStyle.Bold, "Bold  (Ctrl+B)");
            chkItalic = StyleToggle("I", FontStyle.Italic, "Italic  (Ctrl+I)");
            chkUnderline = StyleToggle("U", FontStyle.Underline, "Underline  (Ctrl+U)");
            row.Controls.AddRange(new Control[] { chkBold, chkItalic, chkUnderline });

            btnColor = Theme.SecondaryButton("■  Colour");
            btnColor.Margin = new Padding(Theme.S(this, 6), 0, 0, 0);
            btnColor.Click += BtnColor_Click;
            btnColor.ForeColor = currentColor;
            row.Controls.Add(btnColor);
            return row;
        }

        // Common document fonts first (if installed), then every other font A-Z.
        private static readonly string[] CommonFonts =
            { "Arial", "Calibri", "Cambria", "Times New Roman", "Segoe UI", "Verdana", "Georgia", "Tahoma", "Courier New", "Comic Sans MS" };
        private int commonCount;

        private void FillFontList()
        {
            var all = new HashSet<string>(WindowsFontResolver.Families, StringComparer.OrdinalIgnoreCase);
            foreach (var f in CommonFonts)
                if (all.Contains(f)) cmbFont.Items.Add(f);
            commonCount = cmbFont.Items.Count;
            foreach (var f in WindowsFontResolver.Families)
                if (Array.IndexOf(CommonFonts, f) < 0) cmbFont.Items.Add(f);
            cmbFont.SelectedItem = "Arial";
        }

        // Each font name is drawn in its own font, with a line under the "common" group.
        private readonly Dictionary<string, Font> previewFonts = new Dictionary<string, Font>();
        private void CmbFont_DrawItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0) return;
            string name = cmbFont.Items[e.Index].ToString();
            bool inEditBox = (e.State & DrawItemState.ComboBoxEdit) != 0;
            Font font = Theme.Body;
            if (!inEditBox)
            {
                if (!previewFonts.TryGetValue(name, out font))
                {
                    try { font = new Font(name, 10f); } catch { font = Theme.Body; }
                    previewFonts[name] = font;
                }
            }
            TextRenderer.DrawText(e.Graphics, name, font, e.Bounds, (e.State & DrawItemState.Selected) != 0 ? SystemColors.HighlightText : Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (!inEditBox && e.Index == commonCount - 1)
                using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        }

        private void CmbFont_Changed(object sender, EventArgs e)
        {
            string family = cmbFont.SelectedItem?.ToString();
            if (family == null) return;
            if (!TextLayout.CanEmbed(family))
            {
                Warn($"\"{family}\" can't be saved into a PDF - pick another font.");
                syncingToolbar = true;
                cmbFont.SelectedItem = lastGoodFont;
                syncingToolbar = false;
                return;
            }
            lastGoodFont = family;
            ApplyStyleToSelected();
        }

        private string CurrentFont => cmbFont.SelectedItem?.ToString() ?? "Arial";

        // Copies the toolbar's current font settings onto a text item.
        private void ApplyToolbarStyle(PageEdit ed)
        {
            ed.FontFamily = CurrentFont;
            ed.FontSize = (float)numSize.Value;
            ed.Bold = chkBold.Checked;
            ed.Italic = chkItalic.Checked;
            ed.Underline = chkUnderline.Checked;
            ed.Color = currentColor;
            ed.Box = new RectangleF(ed.Box.Location, TextLayout.Measure(ed));
        }

        // Shows a selected text item's settings in the toolbar (without treating it as a change).
        private void SyncToolbarFrom(PageEdit ed)
        {
            syncingToolbar = true;
            if (cmbFont.Items.Contains(ed.FontFamily)) cmbFont.SelectedItem = ed.FontFamily;
            numSize.Value = Math.Max(numSize.Minimum, Math.Min(numSize.Maximum, (decimal)ed.FontSize));
            chkBold.Checked = ed.Bold;
            chkItalic.Checked = ed.Italic;
            chkUnderline.Checked = ed.Underline;
            SetColor(ed.Color, applyToSelected: false);
            syncingToolbar = false;
        }

        // ---------- loading & rendering ----------

        private async Task OpenPdfAsync()
        {
            try
            {
                winDoc = await LoadWinPdfAsync(srcPath);
                await ShowPageAsync();
                Done("Add text: click the page.  Replace text: drag a box over old text.  Ctrl+Z undoes anything.");
            }
            catch (Exception ex) { Warn("Could not open PDF: " + ex.Message); }
        }

        // Reads the whole file into memory first, so the original file is not kept locked.
        public static async Task<WinPdfDoc> LoadWinPdfAsync(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            var mem = new InMemoryRandomAccessStream();
            await mem.WriteAsync(bytes.AsBuffer());
            mem.Seek(0);
            return await WinPdfDoc.LoadFromStreamAsync(mem);
        }

        // Renders one page to a Bitmap at the given pixels-per-point scale.
        public static async Task<Bitmap> RenderPageAsync(WinPdfDoc doc, int index, float scale)
        {
            using var page = doc.GetPage((uint)index);
            var opts = new Windows.Data.Pdf.PdfPageRenderOptions
            {
                // Windows reports size in DIPs (1/96 inch); * 72/96 converts to points.
                DestinationWidth = (uint)Math.Round(page.Size.Width * 72 / 96 * scale),
                DestinationHeight = (uint)Math.Round(page.Size.Height * 72 / 96 * scale)
            };
            using var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream, opts);
            stream.Seek(0);

            using var ms = new MemoryStream();
            stream.AsStreamForRead().CopyTo(ms);
            ms.Position = 0;
            using var tmp = new Bitmap(ms);
            return new Bitmap(tmp); // copy, so the Bitmap doesn't depend on the stream staying open
        }

        private async Task ShowPageAsync()
        {
            if (winDoc == null) return;
            var bmp = await RenderPageAsync(winDoc, pageIndex, PxPerPt);
            canvas.Image?.Dispose();
            canvas.Image = bmp;
            canvas.Size = bmp.Size;
            lblPage.Text = $"Page {pageIndex + 1} / {winDoc.PageCount}";
            canvas.Invalidate();
        }

        private async void GoToPage(int i)
        {
            if (winDoc == null || i < 0 || i >= winDoc.PageCount || i == pageIndex) return;
            pageIndex = i;
            selected = null;
            await ShowPageAsync();
        }

        private async void SetZoom(float z)
        {
            zoom = Math.Max(0.5f, Math.Min(3f, z));
            await ShowPageAsync();
            Done($"Zoom {zoom * 100:0}%");
        }

        // ---------- drawing the edits on screen ----------

        private void Canvas_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            // Draw in list order, same as Save, so what you see is what you get.
            foreach (var ed in edits)
            {
                if (ed.Page != pageIndex) continue;
                if (ed.IsWhiteout) g.FillRectangle(Brushes.White, ToPx(ed.Box));
                else TextLayout.DrawOnScreen(g, ed, PxPerPt);
            }

            if (boxPreview.HasValue)
            {
                var r = ToPx(boxPreview.Value);
                using var fill = new SolidBrush(Color.FromArgb(200, Color.White));
                g.FillRectangle(fill, r);
                using var pen = new Pen(tool == Tool.Replace ? Theme.Accent : Color.Gray) { DashStyle = DashStyle.Dash };
                g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            }

            if (selected != null && selected.Page == pageIndex)
            {
                var r = ToPx(selected.Box);
                r.Inflate(3, 3);
                using var pen = new Pen(Color.DodgerBlue, 1.5f) { DashStyle = DashStyle.Dash };
                g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            }
        }

        private RectangleF ToPx(RectangleF pt) => new RectangleF(pt.X * PxPerPt, pt.Y * PxPerPt, pt.Width * PxPerPt, pt.Height * PxPerPt);
        private PointF ToPt(Point px) => new PointF(px.X / PxPerPt, px.Y / PxPerPt);

        // ---------- mouse ----------

        private void Canvas_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || winDoc == null) return;
            var pt = ToPt(e.Location);

            if (tool == Tool.Text)
            {
                string text = AskText("Add text", "");
                if (string.IsNullOrWhiteSpace(text)) return;
                var ed = new PageEdit { Page = pageIndex, Text = text, Box = new RectangleF(pt, SizeF.Empty) };
                ApplyToolbarStyle(ed);
                Change(() => edits.Add(ed));
                SelectEdit(ed);
            }
            else if (tool == Tool.Whiteout || tool == Tool.Replace)
            {
                dragging = true;
                dragStart = pt;
                boxPreview = new RectangleF(pt, SizeF.Empty);
            }
            else // Select
            {
                SelectEdit(HitTest(pt));
                if (selected != null)
                {
                    dragging = true;
                    beforeDrag = EditHistory.Copy(edits); // so the move can be undone
                    dragOffset = new PointF(pt.X - selected.Box.X, pt.Y - selected.Box.Y);
                }
            }
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            var pt = ToPt(e.Location);

            if (tool == Tool.Whiteout || tool == Tool.Replace)
            {
                // Build a rectangle from the two corners, whichever way the user dragged.
                float x = Math.Min(dragStart.X, pt.X), y = Math.Min(dragStart.Y, pt.Y);
                boxPreview = new RectangleF(x, y, Math.Abs(pt.X - dragStart.X), Math.Abs(pt.Y - dragStart.Y));
            }
            else if (tool == Tool.Select && selected != null)
            {
                selected.Box = new RectangleF(pt.X - dragOffset.X, pt.Y - dragOffset.Y, selected.Box.Width, selected.Box.Height);
            }
            canvas.Invalidate();
        }

        private void Canvas_MouseUp(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            dragging = false;

            if ((tool == Tool.Whiteout || tool == Tool.Replace) && boxPreview.HasValue)
            {
                var r = boxPreview.Value;
                boxPreview = null;
                canvas.Invalidate();
                if (r.Width < 3 || r.Height < 3) return; // ignore accidental clicks
                if (tool == Tool.Whiteout)
                {
                    var box = new PageEdit { Page = pageIndex, IsWhiteout = true, Box = r };
                    Change(() => edits.Add(box));
                    SelectEdit(box);
                }
                else ReplaceText(r);
            }
            else if (tool == Tool.Select && selected != null && beforeDrag != null)
            {
                // Only record a move if the item actually moved.
                var old = beforeDrag.Find(x => x.Fingerprint() == selected.Fingerprint());
                if (old == null) { history.Push(beforeDrag); AfterChange(); }
                beforeDrag = null;
            }
        }

        // Replace text = a whiteout over the old text + new text in the same spot, as ONE undo step.
        private void ReplaceText(RectangleF r)
        {
            string text = AskText("Replace with", "");
            if (string.IsNullOrWhiteSpace(text)) return;

            // Guess the font size from the box height (a line of text is about size * LineSpacing tall).
            float size = (float)Math.Round(r.Height / TextLayout.LineSpacing * 2, MidpointRounding.AwayFromZero) / 2f;
            size = Math.Max(4, Math.Min(144, size));
            syncingToolbar = true;
            numSize.Value = (decimal)size;
            syncingToolbar = false;

            var cover = new PageEdit { Page = pageIndex, IsWhiteout = true, Box = RectangleF.Inflate(r, 1, 1) };
            var ed = new PageEdit { Page = pageIndex, Text = text, Box = new RectangleF(r.Location, SizeF.Empty) };
            ApplyToolbarStyle(ed);
            Change(() => { edits.Add(cover); edits.Add(ed); });
            SelectEdit(ed);
            Done($"Replaced. Font size {size} - adjust the font or size in the toolbar if it doesn't match.");
        }

        private void Canvas_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (tool != Tool.Select) return;
            var hit = HitTest(ToPt(e.Location));
            if (hit == null || hit.IsWhiteout) return;

            string text = AskText("Edit text", hit.Text);
            if (text == null || text == hit.Text) return;
            Change(() =>
            {
                if (string.IsNullOrWhiteSpace(text)) { edits.Remove(hit); selected = null; }
                else { hit.Text = text; hit.Box = new RectangleF(hit.Box.Location, TextLayout.Measure(hit)); }
            });
        }

        // Topmost item under the point (last drawn = on top, so search backwards).
        private PageEdit HitTest(PointF pt)
        {
            for (int i = edits.Count - 1; i >= 0; i--)
            {
                var ed = edits[i];
                var box = ed.Box;
                box.Inflate(3, 3); // a little slack makes small items easier to grab
                if (ed.Page == pageIndex && box.Contains(pt)) return ed;
            }
            return null;
        }

        private void SelectEdit(PageEdit ed)
        {
            selected = ed;
            styleRecordedFor = null;
            if (ed != null && !ed.IsWhiteout) SyncToolbarFrom(ed);
            canvas.Invalidate();
        }

        // ---------- changes & undo/redo ----------

        // Every change to the edit list goes through here, so it can be undone.
        private void Change(Action action)
        {
            history.Record(edits);
            styleRecordedFor = null;
            action();
            AfterChange();
        }

        private void AfterChange()
        {
            UpdateHistoryButtons();
            canvas.Invalidate();
        }

        private void UpdateHistoryButtons()
        {
            btnUndo.Enabled = history.CanUndo;
            btnRedo.Enabled = history.CanRedo;
            btnRevert.Enabled = edits.Count > 0;
        }

        private void DoUndo()
        {
            if (!history.CanUndo) return;
            RestoreEdits(history.Undo(edits));
            Done("Undone.  (Ctrl+Y to redo)");
        }

        private void DoRedo()
        {
            if (!history.CanRedo) return;
            RestoreEdits(history.Redo(edits));
            Done("Redone.");
        }

        private void RestoreEdits(List<PageEdit> restored)
        {
            int changedPage = FirstChangedPage(edits, restored);
            edits.Clear();
            edits.AddRange(restored);
            selected = null;
            styleRecordedFor = null;
            AfterChange();
            if (changedPage >= 0 && changedPage != pageIndex) GoToPage(changedPage); // show where it happened
        }

        // Which page differs between two versions of the edit list? (-1 if none)
        private static int FirstChangedPage(List<PageEdit> a, List<PageEdit> b)
        {
            var inA = new HashSet<string>(a.ConvertAll(x => x.Fingerprint()));
            var inB = new HashSet<string>(b.ConvertAll(x => x.Fingerprint()));
            foreach (var x in a) if (!inB.Contains(x.Fingerprint())) return x.Page;
            foreach (var x in b) if (!inA.Contains(x.Fingerprint())) return x.Page;
            return -1;
        }

        private void RevertAll()
        {
            if (edits.Count == 0) return;
            Change(() => { edits.Clear(); selected = null; });
            Done("All edits removed - this is the original PDF again.  Ctrl+Z brings them back.");
        }

        private void DeleteSelected()
        {
            if (selected == null) return;
            var target = selected;
            Change(() => { edits.Remove(target); selected = null; });
        }

        // If a text item is selected, font/size/style/colour changes apply to it.
        private void ApplyStyleToSelected()
        {
            if (syncingToolbar || selected == null || selected.IsWhiteout) return;
            // One undo step per selected item, however many times the size arrows are clicked.
            if (styleRecordedFor != selected) { history.Record(edits); styleRecordedFor = selected; }
            ApplyToolbarStyle(selected);
            AfterChange();
        }

        private void Editor_KeyDown(object sender, KeyEventArgs e)
        {
            bool typing = ActiveControl is NumericUpDown || ActiveControl is ComboBox;
            if (e.KeyCode == Keys.Delete && !typing) DeleteSelected();
            else if (e.Control && e.KeyCode == Keys.Z) DoUndo();
            else if (e.Control && e.KeyCode == Keys.Y) DoRedo();
            else if (e.Control && e.KeyCode == Keys.B) chkBold.Checked = !chkBold.Checked;
            else if (e.Control && e.KeyCode == Keys.I) chkItalic.Checked = !chkItalic.Checked;
            else if (e.Control && e.KeyCode == Keys.U) chkUnderline.Checked = !chkUnderline.Checked;
            else if (e.KeyCode == Keys.PageUp) GoToPage(pageIndex - 1);
            else if (e.KeyCode == Keys.PageDown) GoToPage(pageIndex + 1);
            else return;
            e.Handled = true;
        }

        private void BtnColor_Click(object sender, EventArgs e)
        {
            using var dlg = new ColorDialog { Color = currentColor };
            if (dlg.ShowDialog() == DialogResult.OK) SetColor(dlg.Color, applyToSelected: true);
        }

        private void SetColor(Color c, bool applyToSelected)
        {
            currentColor = c;
            btnColor.ForeColor = c;
            if (applyToSelected) ApplyStyleToSelected();
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            if (edits.Count == 0) { Warn("Nothing to save yet - add or replace some text first."); return; }

            using var dlg = new SaveFileDialog
            {
                Filter = "PDF file|*.pdf",
                FileName = Path.GetFileNameWithoutExtension(srcPath) + "_edited.pdf",
                InitialDirectory = Settings.LastFolder
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            if (MainForm.SamePath(dlg.FileName, srcPath)) { Warn("Pick a different output name - can't overwrite the file being edited."); return; }

            try
            {
                PdfEditWriter.ApplyEdits(srcPath, dlg.FileName, edits);
                Settings.Remember(dlg.FileName);
                savedFingerprint = EditsFingerprint();
                Done($"Saved {edits.Count} edit(s) -> {dlg.FileName}");
            }
            catch (Exception ex) { Warn("Save failed: " + ex.Message); }
        }

        // ---------- small UI helpers ----------

        private FlowLayoutPanel Row() => new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, BackColor = Theme.Surface };

        private Label MutedLabel(string text) =>
            new Label { AutoSize = true, Text = text, ForeColor = Theme.Muted, Margin = new Padding(0, Theme.S(this, 7), Theme.S(this, 6), 0) };

        private Button IconBtn(string glyph, string tooltip, EventHandler onClick)
        {
            var b = Theme.IconButton(glyph, tooltip, tips);
            b.Click += onClick;
            return b;
        }

        // Thin vertical line between toolbar groups.
        private Control Separator() => new Panel
        {
            Width = 1, Height = Theme.S(this, 26), BackColor = Theme.Border,
            Margin = new Padding(Theme.S(this, 8), Theme.S(this, 3), Theme.S(this, 14), 0)
        };

        // B / I / U toggle: a checkbox that looks like a button, its letter drawn in that style.
        private CheckBox StyleToggle(string letter, FontStyle style, string tooltip)
        {
            var cb = new CheckBox
            {
                Text = letter, Appearance = Appearance.Button, FlatStyle = FlatStyle.Flat, AutoSize = false,
                Size = new Size(Theme.S(this, 32), Theme.S(this, 30)), TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 10f, style | FontStyle.Bold), Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, Theme.S(this, 4), 0)
            };
            cb.FlatAppearance.BorderColor = Theme.Border;
            cb.FlatAppearance.CheckedBackColor = Theme.Selected;
            cb.FlatAppearance.MouseOverBackColor = Theme.Hover;
            cb.CheckedChanged += (s, e) => { cb.ForeColor = cb.Checked ? Theme.Accent : Theme.Text; ApplyStyleToSelected(); };
            tips.SetToolTip(cb, tooltip);
            return cb;
        }

        // Tool chip: icon + label, highlighted red when active.
        private RadioButton ToolRadio(string glyph, string text, Tool t, string tooltip)
        {
            var rb = new RadioButton
            {
                Text = text, Appearance = Appearance.Button, AutoSize = true, FlatStyle = FlatStyle.Flat,
                Font = Theme.Body, Cursor = Cursors.Hand, TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(8, 2, 10, 2), Margin = new Padding(0, 0, Theme.S(this, 6), 0),
                TextImageRelation = TextImageRelation.ImageBeforeText, ImageAlign = ContentAlignment.MiddleLeft
            };
            rb.FlatAppearance.BorderColor = Theme.Border;
            rb.FlatAppearance.CheckedBackColor = Theme.Selected;
            rb.FlatAppearance.MouseOverBackColor = Theme.Hover;
            rb.Image = GlyphImage(glyph, Theme.Text);
            tips.SetToolTip(rb, tooltip);
            rb.CheckedChanged += (s, e) =>
            {
                rb.ForeColor = rb.Checked ? Theme.Accent : Theme.Text;
                rb.Image = GlyphImage(glyph, rb.Checked ? Theme.Accent : Theme.Text);
                if (!rb.Checked) return;
                tool = t;
                if (canvas == null) return; // still building the form
                canvas.Cursor = t == Tool.Select ? Cursors.SizeAll : t == Tool.Text ? Cursors.IBeam : Cursors.Cross;
                Done(tooltip + ".");
            };
            return rb;
        }

        // Renders an icon-font character to a small bitmap, so it can sit next to text on a button.
        private Bitmap GlyphImage(string glyph, Color color)
        {
            int size = Theme.S(this, 18);
            var bmp = new Bitmap(size, size);
            using var g = Graphics.FromImage(bmp);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var font = Theme.Icon(10f);
            Theme.DrawGlyph(g, glyph, font, color, new RectangleF(0, 0, size, size));
            return bmp;
        }

        // Multi-line text box dialog. Enter = new line, Ctrl+Enter or OK = done.
        private static string AskText(string caption, string initial)
        {
            using var f = new Form
            {
                Text = caption, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, BackColor = Theme.Surface, Font = Theme.Body,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(16)
            };
            var rows = new TableLayoutPanel { AutoSize = true, ColumnCount = 1 };
            var tb = new TextBox { Width = 420, Height = 150, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Text = initial, Margin = Padding.Empty };
            var hint = new Label { AutoSize = true, Text = "Enter = new line  ·  Ctrl+Enter = done", ForeColor = Theme.Muted, Font = Theme.Small, Margin = new Padding(0, 6, 0, 12) };
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = Padding.Empty };
            var ok = Theme.PrimaryButton("OK"); ok.DialogResult = DialogResult.OK; ok.Margin = Padding.Empty; ok.MinimumSize = new Size(80, 0);
            var cancel = Theme.SecondaryButton("Cancel"); cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
            tb.KeyDown += (s, e) => { if (e.Control && e.KeyCode == Keys.Enter) { f.DialogResult = DialogResult.OK; e.SuppressKeyPress = true; } };
            rows.Controls.Add(tb); rows.Controls.Add(hint); rows.Controls.Add(buttons);
            f.Controls.Add(rows);
            f.CancelButton = cancel;
            return f.ShowDialog() == DialogResult.OK ? tb.Text : null;
        }

        private void Done(string msg) { lblStatus.ForeColor = Theme.Success; lblStatus.Text = msg; }
        private void Warn(string msg) { lblStatus.ForeColor = Theme.Error; lblStatus.Text = "⚠  " + msg; }

        // A text "fingerprint" of all edits; if it differs from the one taken at the last save,
        // there are unsaved changes.
        private string EditsFingerprint() => string.Join("\n", edits.ConvertAll(e => e.Fingerprint()));

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (EditsFingerprint() != savedFingerprint)
            {
                // A yes/no question needs a real dialog, so MessageBox is fine here.
                var answer = MessageBox.Show(this, "You have unsaved edits. Close anyway?", "Unsaved edits",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer == DialogResult.No) e.Cancel = true;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            canvas.Image?.Dispose();
            foreach (var f in previewFonts.Values) if (f != Theme.Body) f.Dispose();
            base.OnFormClosed(e);
        }
    }
}
