using System;
using System.IO;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace PdfTool.Tests
{
    // A fresh temp folder per test class, deleted afterwards,
    // plus helpers to build small PDFs whose pages we can tell apart.
    public sealed class TestFiles : IDisposable
    {
        static TestFiles()
        {
            // Same as Program.Main: must run before any XFont is created.
            WindowsFontResolver.Install();
        }

        public string Folder { get; } = Path.Combine(Path.GetTempPath(), "PdfToolTests_" + Guid.NewGuid().ToString("N"));

        public TestFiles() => Directory.CreateDirectory(Folder);

        public string PathFor(string name) => Path.Combine(Folder, name);

        // One page per width (in points), all 500 pt tall. Page widths act as page "IDs",
        // so tests can check that pages ended up in the right order.
        public string MakePdf(string name, params double[] widths)
        {
            string path = PathFor(name);
            using var doc = new PdfDocument();
            foreach (double w in widths)
            {
                var page = doc.AddPage();
                page.Width = XUnit.FromPoint(w);
                page.Height = XUnit.FromPoint(500);
            }
            doc.Save(path);
            return path;
        }

        public static PdfDocument Open(string path) => PdfReader.Open(path, PdfDocumentOpenMode.Import);

        // Unrotated page width - unlike page.Width it doesn't depend on the page's Rotate value.
        public static double WidthOf(PdfPage page) => page.MediaBox.Width;

        // Total bytes in a page's content streams - grows when something is drawn on it.
        public static int ContentLength(PdfPage page)
        {
            int total = 0;
            foreach (var item in page.Contents.Elements)
                if (item is PdfReference r && r.Value is PdfDictionary d && d.Stream != null)
                    total += d.Stream.Value.Length;
            return total;
        }

        public void Dispose()
        {
            try { Directory.Delete(Folder, recursive: true); } catch { /* best effort */ }
        }
    }
}
