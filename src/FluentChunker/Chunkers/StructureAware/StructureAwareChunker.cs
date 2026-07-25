using System.Text.Json;
using FluentChunker.Chunkers.SentenceAware;
using FluentChunker.Contracts;
using FluentChunker.Tokenizers;

namespace FluentChunker.Chunkers.StructureAware
{
    /// <summary>
    /// Splits Markdown documents along their explicit structure.
    /// Every heading starts a new section and sections are hard boundaries: a chunk never spans two sections.
    /// Blocks inside a section are packed greedily up to the token budget;
    /// atomic blocks (code fences, tables, lists, quotes) are never split while they fit.
    /// An oversized paragraph descends to whole sentences;
    /// an oversized atomic block or single sentence falls back to evenly balanced token windows.
    /// Every chunk carries its heading path in <c>Metadata["headingPath"]</c> (empty for content before the first heading).
    /// This strategy applies no overlap: <c>OverlapCount</c> is always 0.
    /// Plain text without Markdown structure degrades gracefully to paragraph packing.
    /// </summary>
    internal class StructureAwareChunker(ITokenizer tokenizer, int maxTokens, IReadOnlySet<string>? additionalAbbreviations = null) : IChunker
    {
        internal const string HeadingPathMetadataKey = "headingPath";
        private const string HeadingPathSeparator = " > ";

        // The two levels the packer works on inside a section.
        // Blocks descend to sentences (paragraphs only); sentences have no smaller structure and fall back to token windows.
        private enum Tier { Blocks, Sentences }

        private readonly ITokenizer _tokenizer = tokenizer;
        private readonly int _maxTokens = maxTokens;
        private readonly IReadOnlySet<string>? _additionalAbbreviations = additionalAbbreviations;

        public IReadOnlyList<Chunk> Chunk(string text)
        {
            if (string.IsNullOrEmpty(text))
                return [];

            var sections = MarkdownSectionParser.Parse(text);
            var chunks = new List<Chunk>();
            var index = 0;

            foreach (var section in sections)
            {
                var headingPath = string.Join(HeadingPathSeparator, section.HeadingPath);
                PackRun(text, section.Blocks, Tier.Blocks, headingPath, chunks, ref index);
            }

            return chunks;
        }

        // Packs one run of same-level units into chunks.
        // Because every section is packed independently, a chunk can never span two sections: headings are hard seams.
        // A unit that alone exceeds the budget is flushed around and descended into, so units on opposite sides of it never share a chunk.
        private void PackRun(string text, IReadOnlyList<SectionBlock> units, Tier tier, string headingPath, List<Chunk> chunks, ref int index)
        {
            var tokenCounts = CountUnitTokens(text, units);
            var buffer = new List<int>();
            var bufferTokens = 0;

            for (var i = 0; i < units.Count; i++)
            {
                if (tokenCounts[i] > _maxTokens)
                {
                    FlushChunk(text, units, buffer, headingPath, chunks, ref index);
                    buffer.Clear();
                    bufferTokens = 0;

                    Descend(text, units[i], tier, headingPath, chunks, ref index);
                    continue;
                }

                if (buffer.Count > 0 && bufferTokens + tokenCounts[i] > _maxTokens)
                {
                    FlushChunk(text, units, buffer, headingPath, chunks, ref index);
                    buffer.Clear();
                    bufferTokens = 0;
                }

                buffer.Add(i);
                bufferTokens += tokenCounts[i];
            }

            FlushChunk(text, units, buffer, headingPath, chunks, ref index);
        }

        // An oversized atomic unit (heading, fence, table, list, quote) has nothing semantic to descend into; an oversized sentence has no smaller level left.
        // Both go straight to token windows.
        // Only an oversized paragraph descends to its sentences.
        private void Descend(string text, SectionBlock unit, Tier tier, string headingPath, List<Chunk> chunks, ref int index)
        {
            if (tier == Tier.Sentences || unit.IsAtomic)
            {
                EmitTokenWindows(text, unit.Span, headingPath, chunks, ref index);
                return;
            }

            PackRun(text, SplitSentences(text, unit.Span), Tier.Sentences, headingPath,
                chunks, ref index);
        }

        // SentenceSplitter works on a standalone string whose positions start at 0, but this paragraph sits inside the document.
        // Split a copy of just this paragraph, then shift every returned position so the spans point back into the original document.
        private IReadOnlyList<SectionBlock> SplitSentences(string text, SentenceSpan paragraph)
        {
            var slice = text.Substring(paragraph.Start, paragraph.Length);
            var local = SentenceSplitter.Split(slice, _additionalAbbreviations);

            var mapped = new SectionBlock[local.Count];
            for (var i = 0; i < local.Count; i++)
            {
                // A sentence has no smaller structural level, so it is atomic by definition.
                mapped[i] = new SectionBlock(
                    new SentenceSpan(paragraph.Start + local[i].Start, local[i].Length),
                    IsAtomic: true);
            }

            return mapped;
        }

        // Counts the tokens of each unit.
        // From the second unit onward the count includes the whitespace between it and the previous unit,
        // because a chunk is later sliced as one continuous span and that in-between whitespace ends up inside the slice.
        private int[] CountUnitTokens(string text, IReadOnlyList<SectionBlock> units)
        {
            var tokenCounts = new int[units.Count];

            for (var i = 0; i < units.Count; i++)
            {
                var start = i == 0 ? units[i].Span.Start : units[i - 1].Span.End;
                tokenCounts[i] = _tokenizer.CountTokens(text[start..units[i].Span.End]);
            }

            return tokenCounts;
        }

        private void FlushChunk(string text, IReadOnlyList<SectionBlock> units, List<int> bufferedUnits, string headingPath, List<Chunk> chunks, ref int index)
        {
            if (bufferedUnits.Count == 0)
                return;

            var start = units[bufferedUnits[0]].Span.Start;
            var end = units[bufferedUnits[^1]].Span.End;
            var chunkText = text[start..end];

            chunks.Add(CreateChunk(index++, chunkText, _tokenizer.CountTokens(chunkText), headingPath));
        }

        // A unit over budget with no smaller structure available is cut into evenly balanced token windows.
        // Unlike every other chunk, this text is rebuilt from tokens instead of sliced from the source, so it may gain a leading space from the tokenizer.
        private void EmitTokenWindows(string text, SentenceSpan unit, string headingPath, List<Chunk> chunks, ref int index)
        {
            var tokens = _tokenizer.Encode(text.Substring(unit.Start, unit.Length));

            foreach (var (start, length) in TokenWindows.Balanced(tokens.Count, _maxTokens))
            {
                var window = TokenWindows.Slice(tokens, start, length);
                chunks.Add(CreateChunk(index++, _tokenizer.Decode(window), length, headingPath));
            }
        }

        private static Chunk CreateChunk(int index, string text, int tokenCount, string headingPath)
        {
            var chunk = new Chunk
            {
                Index = index,
                Text = text,
                TokenCount = tokenCount,
                OverlapCount = 0
            };
            chunk.Metadata[HeadingPathMetadataKey] = JsonSerializer.SerializeToElement(headingPath);
            return chunk;
        }
    }
}
