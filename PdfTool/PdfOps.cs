using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PdfTool
{
    // The PDF operations behind the main window's tools. No UI code here: each method takes
    // file paths and options, writes the output, and throws on failure. MainForm asks the
    // user for input, calls these, and reports the result. That split is what makes the
    // logic unit-testable (see PdfTool.Tests).
    public static class PdfOps
    {
        public static int PageCount(string src)
        {
            using var doc = PdfReader.Open(src, PdfDocumentOpenMode.Import);
            return doc.PageCount;
        }

        // Combines the PDFs in the given order. Returns the total page count.
        public static int Merge(IEnumerable<string> pdfs, string outPath)
        {
            using var output = new PdfDocument();
            foreach (var file in pdfs)
            {
                using var input = PdfReader.Open(file, PdfDocumentOpenMode.Import);
                for (int i = 0; i < input.PageCount; i++)
                    output.AddPage(input.Pages[i]);
            }
            int pageCount = output.PageCount; // read before Save - PDFsharp locks the document once saved
            output.Save(outPath);
            return pageCount;
        }

        // One file per page: "report.pdf" -> report_page1.pdf, report_page2.pdf, ...
        // Returns the paths written, in page order.
        public static List<string> Split(string src, string folder)
        {
            var written = new List<string>();
            string baseName = Path.GetFileNameWithoutExtension(src);
            using var input = PdfReader.Open(src, PdfDocumentOpenMode.Import);
            for (int i = 0; i < input.PageCount; i++)
            {
                using var single = new PdfDocument();
                single.AddPage(input.Pages[i]);
                string path = Path.Combine(folder, $"{baseName}_page{i + 1}.pdf");
                single.Save(path);
                written.Add(path);
            }
            return written;
        }

        // One page per image, each page sized to its image (using the image's DPI).
        public static void ImagesToPdf(IEnumerable<string> images, string outPath)
        {
            using var doc = new PdfDocument();
            foreach (var file in images)
            {
                using XImage img = XImage.FromFile(file);
                var page = doc.AddPage();
                page.Width = XUnit.FromPoint(img.PixelWidth * 72.0 / img.HorizontalResolution);
                page.Height = XUnit.FromPoint(img.PixelHeight * 72.0 / img.VerticalResolution);
                using var gfx = XGraphics.FromPdfPage(page);
                gfx.DrawImage(img, 0, 0, page.Width.Point, page.Height.Point);
            }
            doc.Save(outPath);
        }

        // Diagonal text across every page. opacity is 0-100 %. Returns the page count.
        public static int Watermark(string src, string outPath, string text, int opacity, Color color)
        {
            RefuseOverwrite(src, outPath);
            using var doc = PdfReader.Open(src, PdfDocumentOpenMode.Modify);
            // opacity % -> alpha 0-255
            var brush = new XSolidBrush(XColor.FromArgb(opacity * 255 / 100, color.R, color.G, color.B));

            for (int i = 0; i < doc.PageCount; i++)
            {
                var page = doc.Pages[i];
                using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                double w = page.Width.Point, h = page.Height.Point;

                // Size the text so it spans ~70% of the page diagonal.
                var probe = new XFont("Arial", 100, XFontStyleEx.Bold);
                double fontSize = 100 * (Math.Sqrt(w * w + h * h) * 0.7) / gfx.MeasureString(text, probe).Width;
                var font = new XFont("Arial", Math.Max(12, Math.Min(200, fontSize)), XFontStyleEx.Bold);
                var size = gfx.MeasureString(text, font);

                gfx.TranslateTransform(w / 2, h / 2);
                gfx.RotateTransform(-Math.Atan2(h, w) * 180 / Math.PI); // along the diagonal
                gfx.DrawString(text, font, brush, new XPoint(-size.Width / 2, size.Height / 4));
            }
            int pageCount = doc.PageCount; // read before Save - PDFsharp locks the document once saved
            doc.Save(outPath);
            return pageCount;
        }

        // Turns the given 1-based pages by angle (90, 180 or 270 degrees clockwise).
        public static void Rotate(string src, string outPath, int angle, IEnumerable<int> pages)
        {
            RefuseOverwrite(src, outPath);
            using var doc = PdfReader.Open(src, PdfDocumentOpenMode.Modify);
            foreach (int p in pages)
            {
                var page = doc.Pages[p - 1]; // list is 1-based, Pages[] is 0-based
                // Rotate is stored in the PDF as 0/90/180/270; add and wrap around.
                page.Rotate = (page.Rotate + angle) % 360;
            }
            doc.Save(outPath);
        }

        // Turns "all" or "1,3-5" into a sorted list of 1-based page numbers.
        // Returns null if the text is invalid or out of range. Reusable for Delete/Reorder later.
        public static List<int> ParsePages(string text, int pageCount)
        {
            var result = new SortedSet<int>(); // SortedSet = no duplicates, kept in order
            text = (text ?? "").Trim().ToLower();

            if (text == "all" || text == "")
            {
                for (int i = 1; i <= pageCount; i++) result.Add(i);
                return new List<int>(result);
            }

            foreach (var part in text.Split(','))
            {
                var bits = part.Trim().Split('-');
                if (bits.Length == 1 && int.TryParse(bits[0], out int single))
                {
                    if (single < 1 || single > pageCount) return null;
                    result.Add(single);
                }
                else if (bits.Length == 2 && int.TryParse(bits[0], out int from) && int.TryParse(bits[1], out int to))
                {
                    if (from < 1 || to > pageCount || from > to) return null;
                    for (int i = from; i <= to; i++) result.Add(i);
                }
                else return null;
            }
            return new List<int>(result);
        }

        // "report.pdf" + "rotated" -> "report_rotated.pdf"
        public static string OutputName(string src, string suffix) =>
            Path.GetFileNameWithoutExtension(src) + "_" + suffix + ".pdf";

        public static bool SamePath(string a, string b) =>
            string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

        // Modify-mode tools read the source while writing the output, so they can't overwrite it.
        private static void RefuseOverwrite(string src, string outPath)
        {
            if (SamePath(src, outPath))
                throw new IOException("Pick a different output name - can't overwrite the file being read.");
        }
    }
}
