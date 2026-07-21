using FluentChunker.Chunkers.SentenceAware;
using FluentChunker.Contracts;
using FluentChunker.Tokenizers;

namespace FluentChunker.Chunkers.Recursive
{
    /// <summary>
    /// Splits a document by structure, trying the largest pieces first: paragraphs, then
    /// lines, then sentences. Adjacent pieces are packed together into chunks of at most
    /// <c>maxTokens</c> tokens; a piece that exceeds that budget on its own is split into
    /// smaller pieces instead. A sentence that is still too big falls back to fixed token
    /// windows.
    /// </summary>
    public class RecursiveChunker(ITokenizer tokenizer, int maxTokens, int overlap,
        IReadOnlySet<string>? additionalAbbreviations = null) : IChunker
    {
        // The three structural levels the chunker splits on, from largest to smallest. It
        // tries the largest first; a piece still too big for the token budget is split again
        // one level smaller. A sentence that is still too big has no smaller level left, so it
        // falls back to raw token windows (see EmitTokenWindows), which can always be made to fit.
        private enum Tier { Paragraphs, Lines, Sentences }

        private readonly ITokenizer _tokenizer = tokenizer;
        private readonly int _maxTokens = maxTokens;
        private readonly int _overlap = overlap;
        private readonly IReadOnlySet<string>? _additionalAbbreviations = additionalAbbreviations;

        public IReadOnlyList<Chunk> Chunk(string text)
        {
            if (string.IsNullOrEmpty(text))
                return [];

            var paragraphs = BlockSplitter.SplitParagraphs(text, 0, text.Length);
            if (paragraphs.Count == 0)
                return [];

            var chunks = new List<Chunk>();
            var index = 0;
            PackRun(text, paragraphs, Tier.Paragraphs, chunks, ref index);
            return chunks;
        }

        // Packs one list of same-level pieces (all paragraphs, or all lines, or all sentences) into chunks. 
        // Pieces are appended to the current chunk until the next one would push it over the token budget, then a new chunk starts.
        // A single piece that is already over budget on its own is split one level smaller instead (see Descend): 
        // the current chunk is flushed first and a fresh one is started afterward, so pieces on opposite sides of
        // that split never end up in the same chunk, and no overlap is carried across it.
        private void PackRun(string text, IReadOnlyList<SentenceSpan> units, Tier tier,
            List<Chunk> chunks, ref int index)
        {
            var tokenCounts = CountUnitTokens(text, units);
            var buffer = new List<int>();
            var bufferTokens = 0;
            var bufferOverlap = 0;

            for (var i = 0; i < units.Count; i++)
            {
                if (tokenCounts[i] > _maxTokens)
                {
                    // This piece is over budget by itself. 
                    // Flush the current chunk, split just this piece one level smaller, 
                    // then continue with an empty buffer so nothing before or after it is merged into the pieces the split produces.
                    FlushChunk(text, units, buffer, bufferOverlap, chunks, ref index);
                    buffer.Clear();
                    bufferTokens = 0;
                    bufferOverlap = 0;

                    Descend(text, units[i], tier, chunks, ref index);
                    continue;
                }

                if (buffer.Count > 0 && bufferTokens + tokenCounts[i] > _maxTokens)
                {
                    FlushChunk(text, units, buffer, bufferOverlap, chunks, ref index);

                    var (seed, seedTokens) = SeedNextChunk(buffer, tokenCounts);
                    buffer.Clear();
                    buffer.AddRange(seed);
                    bufferTokens = seedTokens;
                    bufferOverlap = seedTokens;

                    TrimSeedToFit(buffer, ref bufferTokens, ref bufferOverlap,
                        tokenCounts[i], tokenCounts);
                }

                buffer.Add(i);
                bufferTokens += tokenCounts[i];
            }

            FlushChunk(text, units, buffer, bufferOverlap, chunks, ref index);
        }

        // Builds the overlap the next chunk will start with: 
        // the last few whole pieces of the chunk just flushed, taken from the end backward, as many as fit in the overlap budget.
        private (List<int> Seed, int SeedTokens) SeedNextChunk(List<int> flushedUnits, int[] tokenCounts)
        {
            var seed = new List<int>();
            var seedTokens = 0;

            for (var j = flushedUnits.Count - 1; j >= 0; j--)
            {
                var candidate = flushedUnits[j];
                if (seedTokens + tokenCounts[candidate] > _overlap)
                    break;
                seed.Insert(0, candidate);
                seedTokens += tokenCounts[candidate];
            }

            return (seed, seedTokens);
        }

        // The overlap pieces plus the incoming piece may not fit the chunk budget together.
        // If they don't, drop overlap pieces from the front until they do. Staying within the budget always takes priority over keeping overlap.
        private void TrimSeedToFit(List<int> seed, ref int bufferTokens, ref int bufferOverlap,
            int incomingTokenCount, int[] tokenCounts)
        {
            while (seed.Count > 0 && bufferTokens + incomingTokenCount > _maxTokens)
            {
                bufferTokens -= tokenCounts[seed[0]];
                bufferOverlap -= tokenCounts[seed[0]];
                seed.RemoveAt(0);
            }
        }

        private void Descend(string text, SentenceSpan unit, Tier tier,
            List<Chunk> chunks, ref int index)
        {
            switch (tier)
            {
                case Tier.Paragraphs:
                    PackRun(text, BlockSplitter.SplitLines(text, unit.Start, unit.Length),
                        Tier.Lines, chunks, ref index);
                    break;
                case Tier.Lines:
                    PackRun(text, SplitSentences(text, unit), Tier.Sentences, chunks, ref index);
                    break;
                case Tier.Sentences:
                    EmitTokenWindows(text, unit, chunks, ref index);
                    break;
            }
        }

        // SentenceSplitter works on a standalone string whose positions start at 0, but this piece sits at unit.
        // Start inside the document. Split a copy of just this piece, then shift every returned position by unit.
        // Start so the spans point back into the original document rather than into the copy.
        private IReadOnlyList<SentenceSpan> SplitSentences(string text, SentenceSpan unit)
        {
            var slice = text.Substring(unit.Start, unit.Length);
            var local = SentenceSplitter.Split(slice, _additionalAbbreviations);

            var mapped = new SentenceSpan[local.Count];
            for (var i = 0; i < local.Count; i++)
            {
                mapped[i] = new SentenceSpan(unit.Start + local[i].Start, local[i].Length);
            }

            return mapped;
        }

        // A single sentence over budget has no structural boundary left to split on, so cut it into token windows instead.
        // The windows are sized as evenly as possible (rather than filling each to the max and leaving a tiny last one) and do not overlap.
        // Unlike every other chunk, this text is rebuilt from tokens instead of sliced from the source, 
        // so it may gain a leading space that the tokenizer attaches to the first word.
        private void EmitTokenWindows(string text, SentenceSpan sentence,
            List<Chunk> chunks, ref int index)
        {
            var tokens = _tokenizer.Encode(text.Substring(sentence.Start, sentence.Length));

            foreach (var (start, length) in TokenWindows.Balanced(tokens.Count, _maxTokens))
            {
                var window = TokenWindows.Slice(tokens, start, length);

                chunks.Add(new Chunk
                {
                    Index = index++,
                    Text = _tokenizer.Decode(window),
                    TokenCount = length,
                    OverlapCount = 0
                });
            }
        }

        // Counts the tokens of each piece. From the second piece onward the count includes the whitespace between it and the previous piece,
        // because a chunk is later sliced as one continuous span and that in-between whitespace ends up inside the slice. 
        // Counting it here keeps the packing decision honest about the chunk's real size.
        private int[] CountUnitTokens(string text, IReadOnlyList<SentenceSpan> units)
        {
            var tokenCounts = new int[units.Count];

            for (var i = 0; i < units.Count; i++)
            {
                var start = i == 0 ? units[i].Start : units[i - 1].End;
                tokenCounts[i] = _tokenizer.CountTokens(text[start..units[i].End]);
            }

            return tokenCounts;
        }

        private void FlushChunk(string text, IReadOnlyList<SentenceSpan> units,
            List<int> bufferedUnits, int overlapCount, List<Chunk> chunks, ref int index)
        {
            if (bufferedUnits.Count == 0)
                return;

            var start = units[bufferedUnits[0]].Start;
            var end = units[bufferedUnits[^1]].End;
            var chunkText = text[start..end];

            chunks.Add(new Chunk
            {
                Index = index++,
                Text = chunkText,
                TokenCount = _tokenizer.CountTokens(chunkText),
                OverlapCount = overlapCount
            });
        }
    }
}
