using Xunit;

namespace PdfTool.Tests
{
    public class PathHelperTests
    {
        [Theory]
        [InlineData(@"C:\docs\report.pdf", "rotated", "report_rotated.pdf")]
        [InlineData(@"C:\docs\my.report.PDF", "watermarked", "my.report_watermarked.pdf")]
        [InlineData("scan", "rotated", "scan_rotated.pdf")]
        public void OutputName_adds_suffix_before_extension(string src, string suffix, string expected)
        {
            Assert.Equal(expected, PdfOps.OutputName(src, suffix));
        }

        [Theory]
        [InlineData(@"C:\docs\a.pdf", @"C:\docs\a.pdf")]
        [InlineData(@"C:\docs\a.pdf", @"c:\DOCS\A.PDF")]          // Windows paths ignore case
        [InlineData(@"C:\docs\a.pdf", @"C:\docs\sub\..\a.pdf")]   // normalised before comparing
        public void SamePath_true_for_the_same_file(string a, string b)
        {
            Assert.True(PdfOps.SamePath(a, b));
        }

        [Fact]
        public void SamePath_false_for_different_files()
        {
            Assert.False(PdfOps.SamePath(@"C:\docs\a.pdf", @"C:\docs\b.pdf"));
        }
    }
}
