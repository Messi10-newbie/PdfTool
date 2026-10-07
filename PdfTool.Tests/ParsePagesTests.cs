using Xunit;

namespace PdfTool.Tests
{
    public class ParsePagesTests
    {
        [Theory]
        [InlineData("all", new[] { 1, 2, 3, 4, 5 })]
        [InlineData("ALL", new[] { 1, 2, 3, 4, 5 })]
        [InlineData("", new[] { 1, 2, 3, 4, 5 })]
        [InlineData("   ", new[] { 1, 2, 3, 4, 5 })]
        [InlineData("3", new[] { 3 })]
        [InlineData("1,3-5", new[] { 1, 3, 4, 5 })]
        [InlineData(" 2 , 4 ", new[] { 2, 4 })]
        [InlineData("5,1,3", new[] { 1, 3, 5 })]       // sorted
        [InlineData("1-3,2-4", new[] { 1, 2, 3, 4 })]  // overlaps merged, no duplicates
        [InlineData("2-2", new[] { 2 })]
        public void Valid_input_gives_sorted_page_numbers(string text, int[] expected)
        {
            Assert.Equal(expected, PdfOps.ParsePages(text, 5));
        }

        [Theory]
        [InlineData("0")]       // pages are 1-based
        [InlineData("6")]       // past the end
        [InlineData("4-6")]
        [InlineData("3-1")]     // backwards range
        [InlineData("abc")]
        [InlineData("1,,2")]
        [InlineData("1-2-3")]
        [InlineData("-1")]
        public void Invalid_input_returns_null(string text)
        {
            Assert.Null(PdfOps.ParsePages(text, 5));
        }

        [Fact]
        public void Null_text_means_all_pages()
        {
            Assert.Equal(new[] { 1, 2 }, PdfOps.ParsePages(null, 2));
        }
    }
}
