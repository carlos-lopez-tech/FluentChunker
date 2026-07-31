using FluentChunker.Chunkers.SentenceAware;

namespace FluentChunker.Chunkers.Recursive
{
    // Splits text into paragraphs (separated by blank lines) or into single lines.
    // RecursiveChunker uses these two levels: it breaks a document into paragraphs, and when a paragraph is too large it breaks that paragraph into lines.
    //
    // Both methods take a (start, length) window into the original string and return the positions of each block as SentenceSpan(Start, Length) values that point straight back into that same string.
    // Nothing is copied or substring'd, and the returned positions are in the original string's coordinates, so the caller can slice the block out of the original string using a span's start and end and get the exact original bytes back.
    //
    // Each returned span is trimmed so it begins and ends on real content: leading and trailing whitespace (blank lines between paragraphs, a trailing '\r' before a '\n', indentation) is left outside the span.
    // That way a chunk built from these spans never starts or ends on stray separator whitespace.
    internal static class BlockSplitter
    {
        internal static IReadOnlyList<SentenceSpan> SplitParagraphs(string text, int start, int length)
        {
            var spans = new List<SentenceSpan>();
            var end = start + length;
            var paragraphStart = -1;   // first content char of the current paragraph, -1 = none open
            var paragraphEnd = -1;     // one past the last content char seen in the current paragraph
            var lineStart = start;

            // A whitespace-only line ends the current paragraph.
            // Iterating line by line makes \r\n handling automatic ('\r' is whitespace and is trimmed like any other).
            for (var i = start; i <= end; i++)
            {
                if (i != end && text[i] != '\n')
                    continue;

                var (contentStart, contentEnd) = TrimRange(text, lineStart, i);

                if (contentStart < contentEnd)
                {
                    if (paragraphStart < 0)
                        paragraphStart = contentStart;
                    paragraphEnd = contentEnd;
                }
                else if (paragraphStart >= 0)
                {
                    spans.Add(new SentenceSpan(paragraphStart, paragraphEnd - paragraphStart));
                    paragraphStart = -1;
                }

                lineStart = i + 1;
            }

            if (paragraphStart >= 0)
                spans.Add(new SentenceSpan(paragraphStart, paragraphEnd - paragraphStart));

            return spans;
        }

        internal static IReadOnlyList<SentenceSpan> SplitLines(string text, int start, int length)
        {
            var spans = new List<SentenceSpan>();
            var end = start + length;
            var lineStart = start;

            for (var i = start; i <= end; i++)
            {
                if (i != end && text[i] != '\n')
                    continue;

                var (contentStart, contentEnd) = TrimRange(text, lineStart, i);

                if (contentStart < contentEnd)
                    spans.Add(new SentenceSpan(contentStart, contentEnd - contentStart));

                lineStart = i + 1;
            }

            return spans;
        }

        private static (int Start, int End) TrimRange(string text, int start, int end)
        {
            while (start < end && char.IsWhiteSpace(text[start]))
                start++;
            while (end > start && char.IsWhiteSpace(text[end - 1]))
                end--;
            return (start, end);
        }
    }
}
