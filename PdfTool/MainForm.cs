using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PdfTool
{
    public class MainForm : Form
    {
        private FileList lstFiles;
        private DropZone dropZone;
        private Label lblFilesHeader, lblStatus;
        private LinkLabel lnkShowFile;
        private ProgressBar progress;
        private FlowLayoutPanel toolsPanel;   // vertical stack of sections
        private FlowLayoutPanel currentRow;   // the wrapping row of cards in the section being built
        private readonly ToolTip tips = new ToolTip();
        private string lastOutputPath; // for the "Show in folder" link

        // Each tool card plus a check that returns null when the tool can run,
        // or a short hint (shown on the card) saying what it still needs.
        private readonly List<(ToolCard card, Func<string> needs)> tools = new List<(ToolCard, Func<string>)>();

        public MainForm(string[] startupFiles = null)
        {
            Text = "PdfTool";
            Font = Theme.Body;
            BackColor = Theme.Background;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(Theme.S(this, 1060), Theme.S(this, 680));
            MinimumSize = new Size(Theme.S(this, 860), Theme.S(this, 560));
            KeyPreview = true;
            KeyDown += MainForm_KeyDown;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            // Docked controls are laid out in reverse order of adding, so: fill first, edges after.
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(Theme.S(this, 16)) };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            body.Controls.Add(BuildFilesPanel(), 0, 0);
            body.Controls.Add(BuildToolsPanel(), 1, 0);
            Controls.Add(body);
            Controls.Add(BuildStatusBar());
            Controls.Add(BuildHeader());

            AllowDrop = true;
            DragEnter += Files_DragEnter;
            DragDrop += Files_DragDrop;

            if (startupFiles != null)
                foreach (var f in startupFiles) AddFile(f);
            RefreshUi();
            if (lstFiles.Items.Count > 0) Info($"Opened with {lstFiles.Items.Count} file(s) - pick a tool on the right.");
            else Info("Add PDF, image or Word files to get started.");
        }

        // ---------- layout ----------

        private Control BuildHeader()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = Theme.S(this, 68), BackColor = Theme.Surface };
            var lockFont = Theme.Icon(10f);
            header.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                int pad = Theme.S(this, 20), logo = Theme.S(this, 38);
                var logoRect = new RectangleF(pad, (header.Height - logo) / 2f, logo, logo);
                using (var path = Theme.RoundRect(logoRect, Theme.S(this, 8)))
                using (var fill = new SolidBrush(Theme.Accent))
                    g.FillPath(fill, path);
                TextRenderer.DrawText(g, "PDF", Theme.SmallBold, Rectangle.Round(logoRect), Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                int x = (int)logoRect.Right + Theme.S(this, 12);
                TextRenderer.DrawText(g, "PdfTool", Theme.Title, new Point(x, Theme.S(this, 10)), Theme.Text);
                TextRenderer.DrawText(g, "Merge, split, convert and edit PDFs", Theme.Small, new Point(x + 2, Theme.S(this, 40)), Theme.Muted);

                string note = "Works offline  ·  files never leave this PC";
                var size = TextRenderer.MeasureText(note, Theme.Small);
                int nx = header.Width - pad - size.Width;
                TextRenderer.DrawText(g, note, Theme.Small, new Point(nx, (header.Height - size.Height) / 2), Theme.Muted);
                Theme.DrawGlyph(g, "", lockFont, Theme.Success, new RectangleF(nx - Theme.S(this, 22), 0, Theme.S(this, 20), header.Height));

                using var line = new Pen(Theme.Border);
                g.DrawLine(line, 0, header.Height - 1, header.Width, header.Height - 1);
            };
            header.Resize += (s, e) => header.Invalidate();
            return header;
        }

        private Control BuildFilesPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, Theme.S(this, 8), 0) };

            var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty, Padding = new Padding(0, 0, 0, Theme.S(this, 8)) };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            lblFilesHeader = new Label { AutoSize = true, Font = Theme.Section, ForeColor = Theme.Muted, Anchor = AnchorStyles.Left, Text = "YOUR FILES" };
            top.Controls.Add(lblFilesHeader, 0, 0);

            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            var add = Theme.PrimaryButton("+  Add files");
            add.Click += BtnAdd_Click;
            var up = Theme.IconButton("", "Move up (merge order)", tips);
            up.Click += (s, e) => MoveSelected(-1);
            var down = Theme.IconButton("", "Move down (merge order)", tips);
            down.Click += (s, e) => MoveSelected(1);
            var remove = Theme.IconButton("", "Remove selected  (Del)", tips);
            remove.Click += (s, e) => RemoveSelected();
            var clear = Theme.IconButton("", "Clear the list", tips);
            clear.Click += (s, e) => { lstFiles.Items.Clear(); RefreshUi(); };
            clear.Margin = Padding.Empty;
            buttons.Controls.AddRange(new Control[] { add, up, down, remove, clear });
            top.Controls.Add(buttons, 1, 0);

            // White rounded-looking card holding the list (or the drop zone when empty).
            var card = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(1) };
            card.Paint += (s, e) => { using var pen = new Pen(Theme.Border); e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1); };

            lstFiles = new FileList { Dock = DockStyle.Fill };
            lstFiles.SelectedIndexChanged += (s, e) => RefreshTools();
            lstFiles.OrderChanged += (s, e) => RefreshUi();
            lstFiles.DoubleClick += (s, e) => OpenWithDefaultApp(lstFiles.SelectedItem?.ToString());
            lstFiles.DragEnter += Files_DragEnter;
            lstFiles.DragDrop += Files_DragDrop;

            dropZone = new DropZone { Dock = DockStyle.Fill, AllowDrop = true };
            dropZone.Click += BtnAdd_Click;
            dropZone.DragEnter += (s, e) => { Files_DragEnter(s, e); dropZone.Highlight = true; };
            dropZone.DragLeave += (s, e) => dropZone.Highlight = false;
            dropZone.DragDrop += (s, e) => { dropZone.Highlight = false; Files_DragDrop(s, e); };

            card.Controls.Add(lstFiles);
            card.Controls.Add(dropZone);

            var tip = new Label
            {
                Dock = DockStyle.Bottom, AutoSize = false, Height = Theme.S(this, 26), Font = Theme.Small, ForeColor = Theme.Muted,
                TextAlign = ContentAlignment.BottomLeft,
                Text = "Drag rows to reorder  ·  double-click to open  ·  Del to remove"
            };

            panel.Controls.Add(card);
            panel.Controls.Add(tip);
            panel.Controls.Add(top);
            return panel;
        }

        private Control BuildToolsPanel()
        {
            toolsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, AutoScroll = true, Margin = new Padding(Theme.S(this, 8), 0, 0, 0),
                FlowDirection = FlowDirection.TopDown, WrapContents = false // one section under another
            };
            // Each section's card row wraps at the panel's width, so keep that width in sync.
            // Also stretch the cards so each row is filled: as many columns as fit (min ~210px each).
            toolsPanel.Resize += (s, e) =>
            {
                int width = toolsPanel.ClientSize.Width - Theme.S(this, 4);
                int gap = Theme.S(this, 8); // card margin, left + right
                int cols = Math.Max(1, width / Theme.S(this, 220));
                int cardWidth = width / cols - gap;
                foreach (Control c in toolsPanel.Controls)
                {
                    if (!(c is FlowLayoutPanel row)) continue;
                    row.MaximumSize = new Size(width, 0);
                    foreach (Control card in row.Controls) card.Width = cardWidth;
                }
            };

            Section("ORGANIZE", first: true);
            AddTool("", "Merge PDFs", "Combine PDFs into one, in list order", Theme.Red, BtnMerge_Click,
                () => CountKind("PDF") >= 2 ? null : "Add 2 or more PDFs");
            AddTool("", "Split PDF", "Save every page as its own PDF", Theme.Orange, BtnSplit_Click, NeedsOnePdf);
            AddTool("", "Rotate pages", "Turn all or chosen pages", Theme.Purple, BtnRotate_Click, NeedsOnePdf);

            Section("CONVERT");
            AddTool("", "Images to PDF", "JPG / PNG, one page per image", Theme.Amber, BtnImages_Click,
                () => CountKind("JPG") + CountKind("PNG") > 0 ? null : "Add JPG or PNG images");
            AddTool("", "Word to PDF", "DOCX, DOC, RTF, ODT documents", Theme.Blue, BtnDocs_Click,
                () => CountKind("DOC") > 0 ? null : "Add Word / RTF / ODT files");

            Section("EDIT");
            AddTool("", "Edit PDF", "Add text, cover up and replace text", Theme.Teal, BtnEdit_Click, NeedsOnePdf);
            AddTool("", "Watermark", "Stamp text across every page", Theme.Pink, BtnWatermark_Click, NeedsOnePdf);

            return toolsPanel;
        }

        private void Section(string title, bool first = false)
        {
            // A heading, then a row of cards that wraps onto more lines when the window is narrow.
            var lbl = new Label
            {
                AutoSize = true, Text = title, Font = Theme.Section, ForeColor = Theme.Muted,
                Margin = new Padding(Theme.S(this, 4), Theme.S(this, first ? 4 : 14), 0, Theme.S(this, 4))
            };
            currentRow = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = Padding.Empty };
            toolsPanel.Controls.Add(lbl);
            toolsPanel.Controls.Add(currentRow);
        }

        private void AddTool(string glyph, string title, string description, Color color, EventHandler onClick, Func<string> needs)
        {
            var card = new ToolCard
            {
                Glyph = glyph, Title = title, Description = description, IconColor = color,
                Size = new Size(Theme.S(this, 214), Theme.S(this, 82)),
                Margin = new Padding(Theme.S(this, 4))
            };
            card.Click += onClick;
            card.UnavailableClick += (s, e) => Warn($"{title}: {card.UnavailableHint.ToLower()} first.");
            currentRow.Controls.Add(card);
            tools.Add((card, needs));
        }

        private Control BuildStatusBar()
        {
            var bar = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(this, 38), BackColor = Theme.Surface, Padding = new Padding(Theme.S(this, 16), 0, Theme.S(this, 16), 0) };
            bar.Paint += (s, e) => { using var pen = new Pen(Theme.Border); e.Graphics.DrawLine(pen, 0, 0, bar.Width, 0); };

            lblStatus = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Font = Theme.Body };
            lnkShowFile = new LinkLabel
            {
                Dock = DockStyle.Right, AutoSize = true, Text = "Show in folder", TextAlign = ContentAlignment.MiddleRight,
                LinkColor = Theme.Info, ActiveLinkColor = Theme.Accent, LinkBehavior = LinkBehavior.HoverUnderline,
                Padding = new Padding(0, Theme.S(this, 10), 0, 0), Visible = false
            };
            lnkShowFile.LinkClicked += (s, e) => ShowInExplorer(lastOutputPath);
            progress = new ProgressBar
            {
                Dock = DockStyle.Right, Width = Theme.S(this, 140), Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30, Visible = false
            };

            bar.Controls.Add(lblStatus);
            bar.Controls.Add(lnkShowFile);
            bar.Controls.Add(progress);
            return bar;
        }

        // ---------- file list ----------

        private void Files_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        }

        private void Files_DragDrop(object sender, DragEventArgs e)
        {
            if (!(e.Data.GetData(DataFormats.FileDrop) is string[] files)) return;
            int before = lstFiles.Items.Count;
            foreach (var f in files) AddFile(f);
            int added = lstFiles.Items.Count - before;
            RefreshUi();
            if (added == 0) Warn("Nothing added - only PDF, JPG, PNG and Word files are supported (duplicates are skipped).");
            else Info($"Added {added} file(s).");
        }

        private void BtnAdd_Click(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Multiselect = true,
                InitialDirectory = Settings.LastFolder,
                Filter = "Supported files|*.pdf;*.jpg;*.jpeg;*.png;*.doc;*.docx;*.rtf;*.odt|PDF files|*.pdf|Images|*.jpg;*.jpeg;*.png|Documents|*.doc;*.docx;*.rtf;*.odt"
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            foreach (var f in dlg.FileNames) AddFile(f);
            Settings.Remember(dlg.FileNames[0]);
            RefreshUi();
        }

        // Only accept PDFs/images/documents, and skip files already in the list.
        private void AddFile(string path)
        {
            if (!File.Exists(path)) return; // also filters out dropped folders
            string ext = Path.GetExtension(path).ToLower();
            bool ok = ext == ".pdf" || ext == ".jpg" || ext == ".jpeg" || ext == ".png"
                      || Array.IndexOf(DocConverter.Extensions, ext) >= 0;
            if (!ok) return;

            foreach (var o in lstFiles.Items)
                if (string.Equals(o.ToString(), path, StringComparison.OrdinalIgnoreCase)) return;

            lstFiles.Items.Add(path);
        }

        private void RemoveSelected()
        {
            for (int i = lstFiles.SelectedIndices.Count - 1; i >= 0; i--)
                lstFiles.Items.RemoveAt(lstFiles.SelectedIndices[i]);
            RefreshUi();
        }

        private void MoveSelected(int delta)
        {
            int i = lstFiles.SelectedIndex;
            if (i < 0) return;
            int j = i + delta;
            if (j < 0 || j >= lstFiles.Items.Count) return;
            var item = lstFiles.Items[i];
            lstFiles.Items.RemoveAt(i);
            lstFiles.Items.Insert(j, item);
            lstFiles.ClearSelected();
            lstFiles.SelectedIndex = j;
            RefreshUi();
        }

        private void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete && lstFiles.Focused) RemoveSelected();
            else if (e.Control && e.KeyCode == Keys.O) BtnAdd_Click(this, EventArgs.Empty);
        }

        private List<string> AllFiles()
        {
            var list = new List<string>();
            foreach (var o in lstFiles.Items) list.Add(o.ToString());
            return list;
        }

        private List<string> FilesOfKind(params string[] kinds) =>
            AllFiles().FindAll(f => Array.IndexOf(kinds, FileList.Kind(f)) >= 0);

        private int CountKind(string kind) => FilesOfKind(kind).Count;

        // Single-PDF tools use: the selected PDF if exactly one PDF is selected,
        // otherwise the only PDF in the list. null if it's ambiguous or there's none.
        private string SinglePdf()
        {
            var selectedPdfs = new List<string>();
            foreach (var o in lstFiles.SelectedItems)
                if (FileList.Kind(o.ToString()) == "PDF") selectedPdfs.Add(o.ToString());
            if (selectedPdfs.Count == 1) return selectedPdfs[0];
            var all = FilesOfKind("PDF");
            return all.Count == 1 ? all[0] : null;
        }

        private string NeedsOnePdf()
        {
            if (SinglePdf() != null) return null;
            return CountKind("PDF") == 0 ? "Add a PDF" : "Select one PDF in the list";
        }

        // Updates everything that depends on the list: empty state, header count, tool cards.
        private void RefreshUi()
        {
            bool empty = lstFiles.Items.Count == 0;
            dropZone.Visible = empty;
            lstFiles.Visible = !empty;
            lblFilesHeader.Text = empty ? "YOUR FILES" : $"YOUR FILES  ·  {lstFiles.Items.Count}";
            lstFiles.Invalidate(); // row numbers may have changed
            RefreshTools();
        }

        private void RefreshTools()
        {
            foreach (var (card, needs) in tools)
            {
                card.UnavailableHint = needs();
                card.Invalidate();
            }
        }

        // ---------- shared dialogs ----------

        private string AskSavePath(string defaultName)
        {
            using var dlg = new SaveFileDialog { Filter = "PDF file|*.pdf", FileName = defaultName, InitialDirectory = Settings.LastFolder };
            if (dlg.ShowDialog() != DialogResult.OK) return null;
            Settings.Remember(dlg.FileName);
            return dlg.FileName;
        }

        private string AskFolder(string description)
        {
            using var fbd = new FolderBrowserDialog { Description = description, UseDescriptionForTitle = true, InitialDirectory = Settings.LastFolder };
            if (fbd.ShowDialog() != DialogResult.OK) return null;
            Settings.Remember(fbd.SelectedPath);
            return fbd.SelectedPath;
        }

        // ---------- MERGE ----------
        private void BtnMerge_Click(object sender, EventArgs e)
        {
            var pdfs = FilesOfKind("PDF");
            string outPath = AskSavePath("merged.pdf");
            if (outPath == null) return;

            try
            {
                int pages = PdfOps.Merge(pdfs, outPath);
                Done($"Merged {pdfs.Count} PDFs ({pages} pages) into {Path.GetFileName(outPath)}", outPath);
            }
            catch (Exception ex) { Warn("Merge failed: " + ex.Message); }
        }

        // ---------- SPLIT ----------
        private void BtnSplit_Click(object sender, EventArgs e)
        {
            string src = SinglePdf();
            string folder = AskFolder("Choose a folder for the split pages");
            if (folder == null) return;

            try
            {
                var files = PdfOps.Split(src, folder);
                // select the first file so "Show in folder" points at the output
                Done($"Split {Path.GetFileName(src)} into {files.Count} files", files.Count > 0 ? files[0] : folder);
            }
            catch (Exception ex) { Warn("Split failed: " + ex.Message); }
        }

        // ---------- IMAGES TO PDF ----------
        private void BtnImages_Click(object sender, EventArgs e)
        {
            var imgs = FilesOfKind("JPG", "PNG");
            string outPath = AskSavePath("images.pdf");
            if (outPath == null) return;

            try
            {
                PdfOps.ImagesToPdf(imgs, outPath);
                Done($"Created {Path.GetFileName(outPath)} from {imgs.Count} image(s)", outPath);
            }
            catch (Exception ex) { Warn("Images to PDF failed: " + ex.Message); }
        }

        // ---------- WATERMARK ----------
        private void BtnWatermark_Click(object sender, EventArgs e)
        {
            string src = SinglePdf();
            if (!Dialogs.AskWatermark(this, Path.GetFileName(src), out string text, out int opacity, out Color color)) return;

            string outPath = AskSavePath(PdfOps.OutputName(src, "watermarked"));
            if (outPath == null) return;

            try
            {
                int pages = PdfOps.Watermark(src, outPath, text, opacity, color);
                Done($"Watermarked {pages} page(s) into {Path.GetFileName(outPath)}", outPath);
            }
            catch (Exception ex) { Warn("Watermark failed: " + ex.Message); }
        }

        // ---------- ROTATE ----------
        private void BtnRotate_Click(object sender, EventArgs e)
        {
            string src = SinglePdf();
            try
            {
                int pageCount = PdfOps.PageCount(src);
                if (!Dialogs.AskRotate(this, Path.GetFileName(src), pageCount, out int angle, out string pagesText)) return;

                var pages = PdfOps.ParsePages(pagesText, pageCount);
                if (pages == null) { Warn($"Bad page list \"{pagesText}\". Use e.g. 1,3-5 (this PDF has {pageCount} pages)."); return; }

                string outPath = AskSavePath(PdfOps.OutputName(src, "rotated"));
                if (outPath == null) return;

                PdfOps.Rotate(src, outPath, angle, pages);
                Done($"Rotated {pages.Count} page(s) into {Path.GetFileName(outPath)}", outPath);
            }
            catch (Exception ex) { Warn("Rotate failed: " + ex.Message); }
        }

        // ---------- EDIT (opens the editor window) ----------
        private void BtnEdit_Click(object sender, EventArgs e)
        {
            try
            {
                using var editor = new EditorForm(SinglePdf());
                editor.ShowDialog(this); // blocks until the editor is closed
            }
            catch (Exception ex) { Warn("Editor failed: " + ex.Message); }
        }

        // ---------- WORD TO PDF ----------
        private async void BtnDocs_Click(object sender, EventArgs e)
        {
            var docs = FilesOfKind("DOC");
            string folder = AskFolder("Choose a folder for the PDFs");
            if (folder == null) return;

            SetBusy(true); // stop the user starting a second job mid-conversion
            try
            {
                // progress is called from the background thread; BeginInvoke hops back to the UI thread.
                var errors = await DocConverter.ConvertAsync(docs, folder,
                    msg => BeginInvoke(new Action(() => Info(msg))));

                int ok = docs.Count - errors.Count;
                string firstPdf = Path.Combine(folder, Path.GetFileNameWithoutExtension(docs[0]) + ".pdf");
                if (errors.Count == 0) Done($"Converted {ok} document(s) to PDF", File.Exists(firstPdf) ? firstPdf : folder);
                else Warn($"Converted {ok}/{docs.Count}. Failed: " + string.Join("; ", errors));
            }
            catch (Exception ex) { Warn("Word to PDF failed: " + ex.Message); }
            finally { SetBusy(false); }
        }

        // ---------- helpers ----------

        private void SetBusy(bool busy)
        {
            foreach (var (card, _) in tools) card.Enabled = !busy;
            lstFiles.Enabled = !busy;
            dropZone.Enabled = !busy;
            AllowDrop = !busy;
            progress.Visible = busy;
            if (busy) lnkShowFile.Visible = false;
            UseWaitCursor = busy;
        }

        private static void OpenWithDefaultApp(string path)
        {
            if (path == null) return;
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
        }

        // Opens Explorer with the file selected (or just opens the folder).
        private static void ShowInExplorer(string path)
        {
            if (path == null) return;
            try
            {
                if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
                else if (Directory.Exists(path)) Process.Start("explorer.exe", $"\"{path}\"");
            }
            catch { }
        }

        // Status messages. Done = green with optional "Show in folder"; Warn = red; Info = neutral.
        private void Done(string msg, string outputPath = null)
        {
            lblStatus.ForeColor = Theme.Success;
            lblStatus.Text = "✔  " + msg;
            lastOutputPath = outputPath;
            lnkShowFile.Visible = outputPath != null;
        }

        private void Warn(string msg)
        {
            lblStatus.ForeColor = Theme.Error;
            lblStatus.Text = "⚠  " + msg;
            lnkShowFile.Visible = false;
        }

        private void Info(string msg)
        {
            lblStatus.ForeColor = Theme.Muted;
            lblStatus.Text = msg;
        }
    }
}
