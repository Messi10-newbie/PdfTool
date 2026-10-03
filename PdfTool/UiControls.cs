using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace PdfTool
{
    // A clickable card for one tool: coloured icon, title, and a one-line description.
    // If the tool can't run yet (e.g. "Add 2+ PDFs"), UnavailableHint is shown instead
    // and the card is greyed out - clicking it raises UnavailableClick so the form can explain.
    internal class ToolCard : Control
    {
        public string Glyph;
        public string Title;
        public string Description;
        public Color IconColor;
        public string UnavailableHint; // null = ready to use
        public event EventHandler UnavailableClick;

        private bool hover;
        private static readonly Font GlyphFont = Theme.Icon(15f);

        public ToolCard()
        {
            // Custom painting with double buffering = smooth, flicker-free redraws.
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Font = Theme.Body;
        }

        public bool Ready => UnavailableHint == null && Enabled;

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnClick(EventArgs e)
        {
            if (!Enabled) return;
            if (UnavailableHint != null) { UnavailableClick?.Invoke(this, e); return; }
            base.OnClick(e); // fires the normal Click event -> runs the tool
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int pad = Theme.S(this, 14);

            var card = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.RoundRect(card, Theme.S(this, 10)))
            {
                using var fill = new SolidBrush(Theme.Surface);
                g.FillPath(fill, path);
                using var border = new Pen(hover && Ready ? IconColor : Theme.Border, hover && Ready ? 1.5f : 1f);
                g.DrawPath(border, path);
            }

            bool dim = !Ready;
            int icon = Theme.S(this, 40);
            var iconRect = new RectangleF(pad, pad, icon, icon);
            using (var path = Theme.RoundRect(iconRect, Theme.S(this, 8)))
            {
                using var fill = new SolidBrush(dim ? Color.FromArgb(70, IconColor) : IconColor);
                g.FillPath(fill, path);
            }
            Theme.DrawGlyph(g, Glyph, GlyphFont, Color.White, iconRect);

            float textX = pad + icon + Theme.S(this, 12);
            var titleRect = new RectangleF(textX, pad - 2, Width - textX - pad, icon / 2f + 4);
            TextRenderer.DrawText(g, Title, Theme.BodyBold, Rectangle.Round(titleRect),
                dim ? Theme.Muted : Theme.Text, TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis);

            string sub = UnavailableHint ?? Description;
            var subRect = new RectangleF(textX, pad + icon / 2f + 2, Width - textX - pad, Height - pad - icon / 2f - pad / 2f);
            TextRenderer.DrawText(g, sub, Theme.Small, Rectangle.Round(subRect),
                UnavailableHint != null ? Theme.Orange : Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }
    }

    // Big dashed "drop files here" area shown while the file list is empty.
    internal class DropZone : Control
    {
        private bool hover;
        private static readonly Font BigGlyph = Theme.Icon(28f);

        public DropZone()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            Cursor = Cursors.Hand;
        }

        public bool Highlight { set { hover = value; Invalidate(); } }
        protected override void OnMouseEnter(EventArgs e) { Highlight = true; base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Highlight = false; base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int m = Theme.S(this, 8);
            var r = new RectangleF(m, m, Width - 2 * m - 1, Height - 2 * m - 1);
            using (var path = Theme.RoundRect(r, Theme.S(this, 12)))
            {
                using var fill = new SolidBrush(hover ? Theme.Selected : Theme.Surface);
                g.FillPath(fill, path);
                using var pen = new Pen(hover ? Theme.Accent : Theme.Border, 1.5f) { DashStyle = DashStyle.Dash };
                g.DrawPath(pen, path);
            }

            float cy = Height / 2f;
            int iconSize = Theme.S(this, 56);
            Theme.DrawGlyph(g, "", BigGlyph, Theme.Accent, new RectangleF(0, cy - iconSize - Theme.S(this, 10), Width, iconSize));
            var line1 = new Rectangle(0, (int)cy - Theme.S(this, 4), Width, Theme.S(this, 24));
            TextRenderer.DrawText(g, "Drop files here, or click to browse", Theme.BodyBold, line1, Theme.Text, TextFormatFlags.HorizontalCenter);
            var line2 = new Rectangle(0, (int)cy + Theme.S(this, 20), Width, Theme.S(this, 20));
            TextRenderer.DrawText(g, "PDF  ·  JPG / PNG  ·  Word (.docx .doc .rtf .odt)", Theme.Small, line2, Theme.Muted, TextFormatFlags.HorizontalCenter);
        }
    }

    // The file list: each row shows a coloured type badge, the file name, and its folder + size.
    // Rows can be dragged up/down to change the order (used by Merge).
    internal class FileList : ListBox
    {
        private readonly Dictionary<string, string> infoCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private int dragIndex = -1;
        private Point dragStart;
        public event EventHandler OrderChanged;

        // Marker object so a row being dragged inside the list isn't confused with files dropped from Explorer.
        private sealed class RowDrag { public int Index; }

        public FileList()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            BorderStyle = BorderStyle.None;
            BackColor = Theme.Surface;
            Font = Theme.Body;
            SelectionMode = SelectionMode.MultiExtended;
            IntegralHeight = false;
            AllowDrop = true;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ItemHeight = Theme.S(this, 52); // set after handle exists so DeviceDpi is correct
        }

        public static string Kind(string path)
        {
            string ext = Path.GetExtension(path).ToLower();
            if (ext == ".pdf") return "PDF";
            if (ext == ".jpg" || ext == ".jpeg") return "JPG";
            if (ext == ".png") return "PNG";
            return "DOC";
        }

        private static Color KindColor(string kind) => kind switch
        {
            "PDF" => Theme.Red,
            "JPG" or "PNG" => Theme.Amber,
            _ => Theme.Blue
        };

        private string Info(string path)
        {
            if (infoCache.TryGetValue(path, out var s)) return s;
            try
            {
                long bytes = new FileInfo(path).Length;
                string size = bytes >= 1 << 20 ? $"{bytes / 1048576.0:0.0} MB" : $"{Math.Max(1, bytes / 1024)} KB";
                s = $"{size}  ·  {Path.GetDirectoryName(path)}";
            }
            catch { s = Path.GetDirectoryName(path); }
            infoCache[path] = s;
            return s;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            string path = Items[e.Index].ToString();
            bool selected = (e.State & DrawItemState.Selected) != 0;
            var b = e.Bounds;

            using (var bg = new SolidBrush(selected ? Theme.Selected : Theme.Surface)) g.FillRectangle(bg, b);
            if (selected)
                using (var bar = new SolidBrush(Theme.Accent)) g.FillRectangle(bar, b.X, b.Y + 6, Theme.S(this, 3), b.Height - 12);
            using (var line = new Pen(Theme.Border)) g.DrawLine(line, b.X + Theme.S(this, 12), b.Bottom - 1, b.Right - Theme.S(this, 12), b.Bottom - 1);

            // Type badge
            string kind = Kind(path);
            int badge = Theme.S(this, 34);
            var badgeRect = new RectangleF(b.X + Theme.S(this, 14), b.Y + (b.Height - badge) / 2f, badge, badge);
            using (var p = Theme.RoundRect(badgeRect, Theme.S(this, 6)))
            using (var fill = new SolidBrush(KindColor(kind)))
                g.FillPath(fill, p);
            TextRenderer.DrawText(g, kind, Theme.SmallBold, Rectangle.Round(badgeRect), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            // Number (merge order), name, details
            int x = (int)badgeRect.Right + Theme.S(this, 12);
            int w = b.Right - x - Theme.S(this, 12);
            TextRenderer.DrawText(g, $"{e.Index + 1}.  {Path.GetFileName(path)}", Theme.BodyBold,
                new Rectangle(x, b.Y + Theme.S(this, 8), w, Theme.S(this, 20)), Theme.Text, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, Info(path), Theme.Small,
                new Rectangle(x, b.Y + Theme.S(this, 28), w, Theme.S(this, 18)), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.PathEllipsis);
        }

        // ---------- drag to reorder ----------

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            dragIndex = e.Button == MouseButtons.Left ? IndexFromPoint(e.Location) : -1;
            dragStart = e.Location;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragIndex < 0 || e.Button != MouseButtons.Left) return;
            var drag = SystemInformation.DragSize; // how far the mouse must move before it counts as a drag
            if (Math.Abs(e.X - dragStart.X) < drag.Width && Math.Abs(e.Y - dragStart.Y) < drag.Height) return;

            var data = new DataObject();
            data.SetData(typeof(RowDrag), new RowDrag { Index = dragIndex });
            dragIndex = -1;
            DoDragDrop(data, DragDropEffects.Move);
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(RowDrag))) e.Effect = DragDropEffects.Move;
            base.OnDragOver(e); // the form handles files dragged in from Explorer
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            if (e.Data.GetData(typeof(RowDrag)) is RowDrag row)
            {
                int target = IndexFromPoint(PointToClient(new Point(e.X, e.Y)));
                if (target < 0) target = Items.Count - 1;
                if (target != row.Index)
                {
                    var item = Items[row.Index];
                    Items.RemoveAt(row.Index);
                    Items.Insert(target, item);
                    ClearSelected();
                    SelectedIndex = target;
                    OrderChanged?.Invoke(this, EventArgs.Empty);
                }
                return;
            }
            base.OnDragDrop(e);
        }
    }
}
