using System.Collections.Generic;
using System.Drawing;
using Xunit;

namespace PdfTool.Tests
{
    public class EditHistoryTests
    {
        private static PageEdit Text(string text) => new PageEdit { Text = text };

        [Fact]
        public void New_history_has_nothing_to_undo_or_redo()
        {
            var h = new EditHistory();
            Assert.False(h.CanUndo);
            Assert.False(h.CanRedo);
        }

        [Fact]
        public void Undo_then_redo_restores_each_state()
        {
            var h = new EditHistory();
            var edits = new List<PageEdit>();

            h.Record(edits); edits.Add(Text("one"));
            h.Record(edits); edits.Add(Text("two"));

            edits = h.Undo(edits);
            Assert.Single(edits);
            edits = h.Undo(edits);
            Assert.Empty(edits);
            Assert.False(h.CanUndo);

            edits = h.Redo(edits);
            edits = h.Redo(edits);
            Assert.Equal(new[] { "one", "two" }, edits.ConvertAll(e => e.Text));
            Assert.False(h.CanRedo);
        }

        [Fact]
        public void A_new_change_clears_redo()
        {
            var h = new EditHistory();
            var edits = new List<PageEdit>();
            h.Record(edits); edits.Add(Text("one"));
            edits = h.Undo(edits);
            Assert.True(h.CanRedo);

            h.Record(edits); edits.Add(Text("other"));

            Assert.False(h.CanRedo);
        }

        [Fact]
        public void Snapshots_are_copies_not_shared_objects()
        {
            var h = new EditHistory();
            var edits = new List<PageEdit> { Text("before") };

            h.Record(edits);
            edits[0].Text = "after"; // change the live object after recording

            edits = h.Undo(edits);
            Assert.Equal("before", edits[0].Text);
        }

        [Fact]
        public void Clone_copies_every_field()
        {
            var e = new PageEdit
            {
                Page = 2, Box = new RectangleF(1, 2, 3, 4), Text = "hi", FontFamily = "Georgia", FontSize = 18,
                Bold = true, Italic = true, Underline = true, Color = Color.Teal
            };
            var copy = e.Clone();
            Assert.NotSame(e, copy);
            Assert.Equal(e.Fingerprint(), copy.Fingerprint());
        }

        [Fact]
        public void Lines_splits_on_any_newline_style()
        {
            Assert.Equal(new[] { "a", "b", "c" }, Text("a\r\nb\nc").Lines);
        }
    }
}
