using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using PdfSharp.Drawing;
using PdfSharp.Pdf.IO;
using SharpDoc = PdfSharp.Pdf.PdfDocument;

namespace PdfTool
{
    // One thing the user placed on a page (a text item or a whiteout box).
    // Positions are in PDF points (1/72 inch) from the page's top-left corner
    // as the user SEES it (after any page rotation).
    public class PageEdit
    {
        public int Page;            // 0-based page index
        public bool IsWhiteout;     // true = white box, false = text
        public RectangleF Box;      // whiteout: the box. text: top-left + measured size (for clicking)
        public string Text = "";
        public string FontFamily = "Arial";
        public float FontSize = 12;
        public bool Bold, Italic, Underline;
        public Color Color = Color.Black;

        // Every field is a value or a string, so a member-by-member copy is a complete copy.
        public PageEdit Clone() => (PageEdit)MemberwiseClone();

        public string[] Lines => Text.Replace("\r", "").Split('\n');

        public string Fingerprint() =>
            $"{Page}|{IsWhiteout}|{Box}|{Text}|{FontFamily}|{FontSize}|{Bold}{Italic}{Underline}|{Color.ToArgb()}";

        public FontStyle ScreenStyle =>
            (Bold ? FontStyle.Bold : 0) | (Italic ? FontStyle.Italic : 0) | (Underline ? FontStyle.Underline : 0);

        public XFontStyleEx PdfStyle =>
            (Bold ? XFontStyleEx.Bold : 0) | (Italic ? XFontStyleEx.Italic : 0) | (Underline ? XFontStyleEx.Underline : 0);
    }

    // Text measuring/drawing shared by the screen preview, so it matches the saved PDF.
    internal static class TextLayout
    {
        public const float LineSpacing = 1.15f; // line height = font size * this (same on screen and in the PDF)

        // Size of the text block in points - used as its clickable area.
        public static SizeF Measure(PageEdit ed)
        {
            using var bmp = new Bitmap(1, 1);
            using var g = Graphics.FromImage(bmp);
            using var font = new Font(ed.FontFamily, ed.FontSize, ed.ScreenStyle, GraphicsUnit.Pixel); // 1 px = 1 pt here
            float w = 0;
            foreach (var line in ed.Lines)
                w = Math.Max(w, g.MeasureString(line, font, PointF.Empty, StringFormat.GenericTypographic).Width);
            return new SizeF(Math.Max(w, 4), ed.Lines.Length * ed.FontSize * LineSpacing);
        }

        public static void DrawOnScreen(Graphics g, PageEdit ed, float pxPerPt)
        {
            // GraphicsUnit.Pixel lets us size the font exactly as points * pxPerPt.
            using var font = new Font(ed.FontFamily, ed.FontSize * pxPerPt, ed.ScreenStyle, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(ed.Color);
            string[] lines = ed.Lines;
            for (int i = 0; i < lines.Length; i++)
            {
                float y = ed.Box.Y + i * ed.FontSize * LineSpacing;
                g.DrawString(lines[i], font, brush, ed.Box.X * pxPerPt, y * pxPerPt, StringFormat.GenericTypographic);
            }
        }

        // Can PDFsharp embed this font? (A couple of odd icon fonts can't.) Results are cached.
        private static readonly Dictionary<string, bool> usable = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        public static bool CanEmbed(string family)
        {
            if (usable.TryGetValue(family, out bool ok)) return ok;
            try
            {
                using var doc = new SharpDoc();
                var page = doc.AddPage();
                using (var gfx = XGraphics.FromPdfPage(page))
                    gfx.DrawString("Ag", new XFont(family, 12), XBrushes.Black, 0, 20);
                doc.Save(new MemoryStream());
                ok = true;
            }
            catch { ok = false; }
            usable[family] = ok;
            return ok;
        }
    }

    // Undo / redo by snapshots: before every change we store a copy of the whole edit list.
    // Undo swaps the current list for the previous copy (and keeps the current one for redo).
    // Simple and impossible to get out of sync - edit lists are tiny, so copying is cheap.
    internal class EditHistory
    {
        private readonly Stack<List<PageEdit>> undo = new Stack<List<PageEdit>>();
        private readonly Stack<List<PageEdit>> redo = new Stack<List<PageEdit>>();

        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;

        public static List<PageEdit> Copy(List<PageEdit> edits) => edits.ConvertAll(e => e.Clone());

        // Call BEFORE changing the edit list.
        public void Record(List<PageEdit> current) => Push(Copy(current));

        // For changes where the "before" copy was taken earlier (e.g. at the start of a drag).
        public void Push(List<PageEdit> before)
        {
            undo.Push(before);
            redo.Clear(); // a new change makes the old "future" invalid
        }

        public List<PageEdit> Undo(List<PageEdit> current)
        {
            redo.Push(Copy(current));
            return undo.Pop();
        }

        public List<PageEdit> Redo(List<PageEdit> current)
        {
            undo.Push(Copy(current));
            return redo.Pop();
        }
    }

    // Writes the edits into a copy of the PDF.
    public static class PdfEditWriter
    {
        public static void ApplyEdits(string src, string dst, List<PageEdit> edits)
        {
            using SharpDoc doc = PdfReader.Open(src, PdfDocumentOpenMode.Modify);

            for (int p = 0; p < doc.PageCount; p++)
            {
                var pageEdits = edits.FindAll(ed => ed.Page == p);
                if (pageEdits.Count == 0) continue;

                var page = doc.Pages[p];

                // Rotated pages: edits are positioned as the user SEES the page, but the page's
                // content lives in its unrotated layout. Switch rotation off while drawing,
                // map our coordinates onto the unrotated page ourselves, then switch it back on.
                int rotate = ((page.Rotate % 360) + 360) % 360;
                page.Rotate = 0;
                double w = page.MediaBox.Width, h = page.MediaBox.Height; // unrotated size in points

                // PDFsharp quirk: for 90/270 pages it still reports Height as the swapped (rotated)
                // value and uses it to flip the y-axis, which shifts everything down by the
                // difference. Do NOT fix this by setting page.Orientation - that corrupts the page
                // on save. Instead shift back up by the same amount (0 for normal pages).
                double yFix = page.Height.Point - h;

                // Append = draw on top of the existing content instead of replacing it.
                using (var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append))
                {
                    gfx.TranslateTransform(0, yFix);
                    // Each pair = "move origin to the corner that appears top-left, then turn".
                    if (rotate == 90) { gfx.TranslateTransform(0, h); gfx.RotateTransform(-90); }
                    else if (rotate == 180) { gfx.TranslateTransform(w, h); gfx.RotateTransform(180); }
                    else if (rotate == 270) { gfx.TranslateTransform(w, 0); gfx.RotateTransform(90); }
                    DrawEdits(gfx, pageEdits);
                }
                page.Rotate = rotate;
            }
            doc.Save(dst);
        }

        private static void DrawEdits(XGraphics gfx, List<PageEdit> pageEdits)
        {
            foreach (var ed in pageEdits)
            {
                if (ed.IsWhiteout)
                {
                    gfx.DrawRectangle(XBrushes.White, ed.Box.X, ed.Box.Y, ed.Box.Width, ed.Box.Height);
                    continue;
                }

                var font = new XFont(ed.FontFamily, ed.FontSize, ed.PdfStyle);
                var brush = new XSolidBrush(XColor.FromArgb(ed.Color.A, ed.Color.R, ed.Color.G, ed.Color.B));
                string[] lines = ed.Lines;
                for (int i = 0; i < lines.Length; i++)
                {
                    double y = ed.Box.Y + i * ed.FontSize * TextLayout.LineSpacing;
                    // TopLeft: (x, y) is the top-left of the text, matching the screen preview.
                    gfx.DrawString(lines[i], font, brush, new XPoint(ed.Box.X, y), XStringFormats.TopLeft);
                }
            }
        }
    }
}
