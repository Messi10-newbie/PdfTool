using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Xunit;

namespace PdfTool.Tests
{
    public class PdfOpsTests : IClassFixture<TestFiles>
    {
        private readonly TestFiles files;
        public PdfOpsTests(TestFiles files) => this.files = files;

        [Fact]
        public void PageCount_reads_the_number_of_pages()
        {
            string src = files.MakePdf("count.pdf", 100, 200, 300);
            Assert.Equal(3, PdfOps.PageCount(src));
        }

        [Fact]
        public void Merge_keeps_list_order_and_all_pages()
        {
            string a = files.MakePdf("merge_a.pdf", 100, 110);
            string b = files.MakePdf("merge_b.pdf", 200);
            string c = files.MakePdf("merge_c.pdf", 300, 310, 320);
            string outPath = files.PathFor("merged.pdf");

            int pages = PdfOps.Merge(new[] { c, a, b }, outPath);

            Assert.Equal(6, pages);
            using var merged = TestFiles.Open(outPath);
            var widths = new double[merged.PageCount];
            for (int i = 0; i < merged.PageCount; i++) widths[i] = TestFiles.WidthOf(merged.Pages[i]);
            Assert.Equal(new double[] { 300, 310, 320, 100, 110, 200 }, widths);
        }

        [Fact]
        public void Split_writes_one_numbered_file_per_page()
        {
            string src = files.MakePdf("report.pdf", 100, 200, 300);
            string folder = Directory.CreateDirectory(files.PathFor("split")).FullName;

            var written = PdfOps.Split(src, folder);

            Assert.Equal(3, written.Count);
            for (int i = 0; i < 3; i++)
            {
                Assert.Equal(Path.Combine(folder, $"report_page{i + 1}.pdf"), written[i]);
                using var single = TestFiles.Open(written[i]);
                Assert.Equal(1, single.PageCount);
                Assert.Equal(100 * (i + 1), TestFiles.WidthOf(single.Pages[0]));
            }
        }

        [Fact]
        public void Rotate_turns_only_the_chosen_pages()
        {
            string src = files.MakePdf("rot.pdf", 100, 200, 300, 400);
            string outPath = files.PathFor("rot_out.pdf");

            PdfOps.Rotate(src, outPath, 90, new[] { 2, 4 });

            using var doc = TestFiles.Open(outPath);
            var angles = new int[doc.PageCount];
            for (int i = 0; i < doc.PageCount; i++) angles[i] = doc.Pages[i].Rotate;
            Assert.Equal(new[] { 0, 90, 0, 90 }, angles);
        }

        [Fact]
        public void Rotate_wraps_past_360()
        {
            string src = files.MakePdf("rot_wrap.pdf", 100);
            string once = files.PathFor("rot_wrap_270.pdf");
            string twice = files.PathFor("rot_wrap_90.pdf");

            PdfOps.Rotate(src, once, 270, new[] { 1 });
            PdfOps.Rotate(once, twice, 180, new[] { 1 }); // 270 + 180 = 450 -> 90

            using var doc = TestFiles.Open(twice);
            Assert.Equal(90, doc.Pages[0].Rotate);
        }

        [Fact]
        public void Rotate_refuses_to_overwrite_its_source()
        {
            string src = files.MakePdf("rot_self.pdf", 100);
            byte[] before = File.ReadAllBytes(src);

            Assert.Throws<IOException>(() => PdfOps.Rotate(src, src, 90, new[] { 1 }));
            Assert.Equal(before, File.ReadAllBytes(src)); // untouched
        }

        [Fact]
        public void ImagesToPdf_sizes_each_page_to_its_image()
        {
            // 200 x 100 px at 144 DPI = 100 x 50 pt;  72 x 144 px at 72 DPI = 72 x 144 pt.
            string wide = MakePng("wide.png", 200, 100, 144);
            string tall = MakePng("tall.png", 72, 144, 72);
            string outPath = files.PathFor("images.pdf");

            PdfOps.ImagesToPdf(new[] { wide, tall }, outPath);

            using var doc = TestFiles.Open(outPath);
            Assert.Equal(2, doc.PageCount);
            Assert.Equal(100, doc.Pages[0].Width.Point, 1);
            Assert.Equal(50, doc.Pages[0].Height.Point, 1);
            Assert.Equal(72, doc.Pages[1].Width.Point, 1);
            Assert.Equal(144, doc.Pages[1].Height.Point, 1);
        }

        [Fact]
        public void Watermark_stamps_every_page_and_keeps_page_count()
        {
            string src = files.MakePdf("wm.pdf", 300, 600, 400);
            string outPath = files.PathFor("wm_out.pdf");

            int pages = PdfOps.Watermark(src, outPath, "CONFIDENTIAL", 30, Color.Red);

            Assert.Equal(3, pages);
            using var before = TestFiles.Open(src);
            using var after = TestFiles.Open(outPath);
            Assert.Equal(3, after.PageCount);
            for (int i = 0; i < 3; i++)
                Assert.True(TestFiles.ContentLength(after.Pages[i]) > TestFiles.ContentLength(before.Pages[i]), $"page {i + 1} has no watermark");
        }

        [Fact]
        public void Watermark_refuses_to_overwrite_its_source()
        {
            string src = files.MakePdf("wm_self.pdf", 300);
            Assert.Throws<IOException>(() => PdfOps.Watermark(src, src, "X", 50, Color.Gray));
        }

        private string MakePng(string name, int w, int h, float dpi)
        {
            string path = files.PathFor(name);
            using var bmp = new Bitmap(w, h);
            bmp.SetResolution(dpi, dpi);
            using (var g = Graphics.FromImage(bmp)) g.Clear(Color.SteelBlue);
            bmp.Save(path, ImageFormat.Png);
            return path;
        }
    }
}
