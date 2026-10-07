using System.Collections.Generic;
using System.Drawing;
using Xunit;

namespace PdfTool.Tests
{
    public class PdfEditWriterTests : IClassFixture<TestFiles>
    {
        private readonly TestFiles files;
        public PdfEditWriterTests(TestFiles files) => this.files = files;

        private static List<PageEdit> SampleEdits(int page) => new List<PageEdit>
        {
            new PageEdit { Page = page, IsWhiteout = true, Box = new RectangleF(10, 10, 50, 20) },
            new PageEdit { Page = page, Text = "Hello\nWorld", FontFamily = "Arial", FontSize = 14, Bold = true, Box = new RectangleF(20, 40, 0, 0) },
        };

        [Fact]
        public void Edits_only_change_the_pages_they_belong_to()
        {
            string src = files.MakePdf("edit.pdf", 300, 300);
            string outPath = files.PathFor("edit_out.pdf");

            PdfEditWriter.ApplyEdits(src, outPath, SampleEdits(page: 1));

            using var before = TestFiles.Open(src);
            using var after = TestFiles.Open(outPath);
            Assert.Equal(2, after.PageCount);
            Assert.Equal(TestFiles.ContentLength(before.Pages[0]), TestFiles.ContentLength(after.Pages[0])); // page 1 untouched
            Assert.True(TestFiles.ContentLength(after.Pages[1]) > TestFiles.ContentLength(before.Pages[1])); // page 2 drawn on
        }

        // Regression test: drawing on a rotated page switches Rotate off temporarily.
        // It must be put back, and the page size must not change (setting Orientation used to corrupt it).
        [Theory]
        [InlineData(90)]
        [InlineData(180)]
        [InlineData(270)]
        public void Rotated_pages_keep_their_rotation_and_size(int angle)
        {
            string plain = files.MakePdf($"edit_rot{angle}_src.pdf", 300);
            string rotated = files.PathFor($"edit_rot{angle}.pdf");
            PdfOps.Rotate(plain, rotated, angle, new[] { 1 });
            string outPath = files.PathFor($"edit_rot{angle}_out.pdf");

            PdfEditWriter.ApplyEdits(rotated, outPath, SampleEdits(page: 0));

            using var doc = TestFiles.Open(outPath);
            Assert.Equal(angle, doc.Pages[0].Rotate);
            Assert.Equal(300, doc.Pages[0].MediaBox.Width, 1);
            Assert.Equal(500, doc.Pages[0].MediaBox.Height, 1);
            Assert.True(TestFiles.ContentLength(doc.Pages[0]) > 0);
        }

        [Fact]
        public void No_edits_still_writes_a_valid_copy()
        {
            string src = files.MakePdf("edit_none.pdf", 300, 300, 300);
            string outPath = files.PathFor("edit_none_out.pdf");

            PdfEditWriter.ApplyEdits(src, outPath, new List<PageEdit>());

            Assert.Equal(3, PdfOps.PageCount(outPath));
        }
    }
}
