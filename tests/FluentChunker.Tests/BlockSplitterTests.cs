using FluentChunker.Chunkers.Recursive;

namespace FluentChunker.Tests
{
    public class BlockSplitterTests
    {
        [Test]
        public void SplitParagraphs_BlankLineSeparated_ReturnsParagraphSpans()
        {
            // Arrange
            var text = "First paragraph here.\n\nSecond paragraph there.";

            // Act
            var spans = BlockSplitter.SplitParagraphs(text, 0, text.Length);

            // Assert
            Assert.That(spans, Has.Count.EqualTo(2));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(text.Substring(spans[0].Start, spans[0].Length), Is.EqualTo("First paragraph here."));
                Assert.That(text.Substring(spans[1].Start, spans[1].Length), Is.EqualTo("Second paragraph there."));
            }
        }

        [Test]
        public void SplitParagraphs_BlankLineContainingSpaces_StillSeparates()
        {
            // Arrange - the "blank" line holds spaces and a tab, which must not join the paragraphs
            var text = "First paragraph.\n  \t \nSecond paragraph.";

            // Act
            var spans = BlockSplitter.SplitParagraphs(text, 0, text.Length);

            // Assert
            Assert.That(spans, Has.Count.EqualTo(2));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(text.Substring(spans[0].Start, spans[0].Length), Is.EqualTo("First paragraph."));
                Assert.That(text.Substring(spans[1].Start, spans[1].Length), Is.EqualTo("Second paragraph."));
            }
        }

        [Test]
        public void SplitParagraphs_ConsecutiveBlankLines_ProducesNoEmptySpans()
        {
            // Arrange
            var text = "First paragraph.\n\n\n\nSecond paragraph.";

            // Act
            var spans = BlockSplitter.SplitParagraphs(text, 0, text.Length);

            // Assert - exactly two spans, none of them empty
            Assert.That(spans, Has.Count.EqualTo(2));
            foreach (var span in spans)
            {
                Assert.That(span.Length, Is.GreaterThan(0));
            }
        }

        [Test]
        public void SplitParagraphs_CrLfDocument_TreatsCrLfAsSingleBreak()
        {
            // Arrange
            var text = "First paragraph.\r\n\r\nSecond paragraph.";

            // Act
            var spans = BlockSplitter.SplitParagraphs(text, 0, text.Length);

            // Assert - two paragraphs, and no span carries a trailing '\r'
            Assert.That(spans, Has.Count.EqualTo(2));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(text.Substring(spans[0].Start, spans[0].Length), Is.EqualTo("First paragraph."));
                Assert.That(text.Substring(spans[1].Start, spans[1].Length), Is.EqualTo("Second paragraph."));
            }
        }

        [Test]
        public void SplitParagraphs_NoBlankLines_ReturnsSingleSpan()
        {
            // Arrange - single line breaks do NOT separate paragraphs
            var text = "First line.\nSecond line.";

            // Act
            var spans = BlockSplitter.SplitParagraphs(text, 0, text.Length);

            // Assert - one paragraph covering both lines, interior '\n' included
            Assert.That(spans, Has.Count.EqualTo(1));
            Assert.That(text.Substring(spans[0].Start, spans[0].Length), Is.EqualTo("First line.\nSecond line."));
        }

        [Test]
        public void SplitParagraphs_WhitespaceOnlyText_ReturnsEmptyList()
        {
            // Arrange
            var text = "  \n \t \n  ";

            // Act
            var spans = BlockSplitter.SplitParagraphs(text, 0, text.Length);

            // Assert
            Assert.That(spans, Is.Empty);
        }

        [Test]
        public void SplitParagraphs_Spans_SliceBackToOriginalText()
        {
            // Arrange - leading/trailing whitespace around the document must stay outside every span
            var text = "\n\n  First paragraph.  \n\nSecond paragraph.\n\n";

            // Act
            var spans = BlockSplitter.SplitParagraphs(text, 0, text.Length);

            // Assert - spans are trimmed to content on both ends
            Assert.That(spans, Has.Count.EqualTo(2));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(text.Substring(spans[0].Start, spans[0].Length), Is.EqualTo("First paragraph."));
                Assert.That(text.Substring(spans[1].Start, spans[1].Length), Is.EqualTo("Second paragraph."));
            }
        }

        [Test]
        public void SplitLines_MultiLineText_ReturnsLineSpans()
        {
            // Arrange
            var text = "Line one.\nLine two.\nLine three.";

            // Act
            var spans = BlockSplitter.SplitLines(text, 0, text.Length);

            // Assert
            Assert.That(spans, Has.Count.EqualTo(3));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(text.Substring(spans[0].Start, spans[0].Length), Is.EqualTo("Line one."));
                Assert.That(text.Substring(spans[1].Start, spans[1].Length), Is.EqualTo("Line two."));
                Assert.That(text.Substring(spans[2].Start, spans[2].Length), Is.EqualTo("Line three."));
            }
        }

        [Test]
        public void SplitLines_CrLfLineEndings_SpansExcludeTrailingCr()
        {
            // Arrange
            var text = "Line one.\r\nLine two.\r\nLine three.";

            // Act
            var spans = BlockSplitter.SplitLines(text, 0, text.Length);

            // Assert - no span may end on the '\r' before the line break
            Assert.That(spans, Has.Count.EqualTo(3));
            foreach (var span in spans)
            {
                Assert.That(text.Substring(span.Start, span.Length), Does.Not.Contain('\r'));
            }
        }

        [Test]
        public void SplitLines_BlankLines_ProduceNoSpans()
        {
            // Arrange
            var text = "Line one.\n\nLine two.";

            // Act
            var spans = BlockSplitter.SplitLines(text, 0, text.Length);

            // Assert - the blank middle line yields nothing
            Assert.That(spans, Has.Count.EqualTo(2));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(text.Substring(spans[0].Start, spans[0].Length), Is.EqualTo("Line one."));
                Assert.That(text.Substring(spans[1].Start, spans[1].Length), Is.EqualTo("Line two."));
            }
        }

        [Test]
        public void SplitLines_SingleLineNoBreaks_ReturnsSingleSpan()
        {
            // Arrange
            var text = "Just one line without any breaks.";

            // Act
            var spans = BlockSplitter.SplitLines(text, 0, text.Length);

            // Assert
            Assert.That(spans, Has.Count.EqualTo(1));
            Assert.That(text.Substring(spans[0].Start, spans[0].Length), Is.EqualTo(text));
        }

        [Test]
        public void SplitLines_SubRangeOfDocument_ReturnsOriginalTextCoordinates()
        {
            // Arrange - only the middle region is split; spans must still index into the full text
            var text = "HEADER\nAlpha beta.\nGamma delta.\nFOOTER";
            var start = text.IndexOf("Alpha", StringComparison.Ordinal);
            var length = text.IndexOf("\nFOOTER", StringComparison.Ordinal) - start;

            // Act
            var spans = BlockSplitter.SplitLines(text, start, length);

            // Assert
            Assert.That(spans, Has.Count.EqualTo(2));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(text.Substring(spans[0].Start, spans[0].Length), Is.EqualTo("Alpha beta."));
                Assert.That(text.Substring(spans[1].Start, spans[1].Length), Is.EqualTo("Gamma delta."));
            }
        }
    }
}
