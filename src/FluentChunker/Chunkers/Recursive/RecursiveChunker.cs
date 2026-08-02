using FluentChunker.Chunkers.SentenceAware;
using FluentChunker.Contracts;
using FluentChunker.Tokenizers;

namespace FluentChunker.Chunkers.Recursive
{
    /// <summary>
    /// Splits a document by structure, trying the largest pieces first: paragraphs, then lines, then sentences.
    /// Adjacent pieces are packed together into chunks of at most <c>maxTokens</c> tokens,
    /// a piece that exceeds that budget on its own is split into smaller pieces instead.
    /// A sentence that is still too big falls back to fixed token windows.
    /// </summary>
    /// <param name="tokenizer">The tokenizer used to encode and decode the document.</param>
    /// <param name="maxTokens">Upper token bound per chunk; must be positive.</param>
    /// <param name="overlap">Tokens shared between consecutive chunks; must be smaller than <paramref name="maxTokens"/>.</param>
    /// <param name="additionalAbbreviations">Extra abbreviations recognised alongside the built-in list.</param>
    /// <param name="segmentCapacity">Sliding window capacity for the streamed path.</param>
    /// <param name="streamBasePosition">Sliding window starting position for the streamed path.</param>
    internal class RecursiveChunker(
        ITokenizer tokenizer,
        int maxTokens,
        int overlap,
        IReadOnlySet<string>? additionalAbbreviations = null,
        int segmentCapacity = SlidingTextWindow.DefaultCapacity,
        long streamBasePosition = 0) : IStreamingChunker
    {
        // The three structural levels the chunker splits on, from largest to smallest.
        // It tries the largest first, a piece still too big for the token budget is split again one level smaller.
        // A sentence that is still too big has no smaller level left,
        // so it falls back to raw token windows (see EmitTokenWindows), which can always be made to fit.
        private enum Tier { Paragraphs, Lines, Sentences }

        private readonly ITokenizer _tokenizer = tokenizer;
        private readonly int _maxTokens = maxTokens;
        private readonly int _overlap = overlap;
        private readonly IReadOnlySet<string>? _additionalAbbreviations = additionalAbbreviations;

        // The last two parameters exist for tests only: tiny capacities force segment seams deterministically, and a seeded base proves the long offset arithmetic without streaming gigabytes.
        // The builder never sets either, so production always streams with the default capacity from position zero.
        private readonly int _segmentCapacity = segmentCapacity;
        private readonly long _streamBasePosition = streamBasePosition;

        /// <inheritdoc/>
        public IAsyncEnumerable<Chunk> ChunkAsync(string text, CancellationToken cancellationToken = default) =>
            ChunkStream.Over(token => ChunkCore(text, token), cancellationToken);

        /// <inheritdoc/>
        public IAsyncEnumerable<Chunk> ChunkAsync(TextReader reader, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(reader);
            return StreamingChunkLoop.Run(reader, _segmentCapacity, _streamBasePosition, HandleSegment, cancellationToken);
        }

        // The shared loop's per-segment handler: complete paragraphs form the piece, and the last span of a non-final segment is carried whole into the next one, because a paragraph is only closed by a blank line or by end of input.
        // A single paragraph that fills the whole buffer forces the descent: the buffer becomes the piece, and ChunkCore treats it as one oversized paragraph and descends the ladder exactly as it would in memory, with the piece's end acting as the descent seam.
        // A whitespace-only segment (no spans at all) is consumed without emitting, matching the in-memory core, which emits no chunk for it either.
        private (int Consumed, IEnumerable<Chunk> Chunks) HandleSegment(
            string segment, bool endOfStream, long basePosition, ChunkCounter counter, CancellationToken cancellationToken)
        {
            var paragraphs = BlockSplitter.SplitParagraphs(segment, 0, segment.Length);

            if (paragraphs.Count == 0)
                return (segment.Length, []);

            var consumed = (endOfStream || paragraphs.Count == 1) ? segment.Length : paragraphs[^1].Start;
            var piece = consumed == segment.Length ? segment : segment[..consumed];

            return (consumed, StreamingChunkLoop.RemapPiece(ChunkCore(piece, cancellationToken), counter, basePosition));
        }

        // Sizing every unit of a tier runs before that tier can emit its first chunk and is the bulk of the wait, so the token is threaded down to it.
        // Splitting the document into paragraphs stays uninterruptible, a negligible floor against the counting it precedes.
        private IEnumerable<Chunk> ChunkCore(string text, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(text))
                yield break;

            var paragraphs = BlockSplitter.SplitParagraphs(text, 0, text.Length);
            if (paragraphs.Count == 0)
                yield break;

            var counter = new ChunkCounter();

            foreach (var chunk in PackRun(text, paragraphs, Tier.Paragraphs, counter, cancellationToken))
                yield return chunk;
        }

        // Packs one list of same-level pieces (all paragraphs, or all lines, or all sentences) into chunks.
        // Pieces are appended to the current chunk until the next one would push it over the token budget, then a new chunk starts.
        // A single piece that is already over budget on its own is split one level smaller instead (see Descend): the current chunk is flushed first and a fresh one is started afterward,
        // so pieces on opposite sides of that split never end up in the same chunk, and no overlap is carried across it.
        private IEnumerable<Chunk> PackRun(string text, IReadOnlyList<SentenceSpan> units, Tier tier, ChunkCounter counter, CancellationToken cancellationToken)
        {
            var tokenCounts = CountUnitTokens(text, units, cancellationToken);
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
                    if (buffer.Count > 0)
                        yield return BuildChunk(text, units, buffer, bufferOverlap, counter.Next());
                    buffer.Clear();
                    bufferTokens = 0;
                    bufferOverlap = 0;

                    foreach (var chunk in Descend(text, units[i], tier, counter, cancellationToken))
                        yield return chunk;
                    continue;
                }

                if (buffer.Count > 0 && bufferTokens + tokenCounts[i] > _maxTokens)
                {
                    yield return BuildChunk(text, units, buffer, bufferOverlap, counter.Next());

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

            if (buffer.Count > 0)
                yield return BuildChunk(text, units, buffer, bufferOverlap, counter.Next());
        }

        // Builds the overlap the next chunk will start with: the last few whole pieces of the chunk just flushed, taken from the end backward, as many as fit in the overlap budget.
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

        // See TrimSeedToFit in SentenceAwareChunker: overlap pieces are dropped from the front until the seed and the incoming piece fit together, because staying within the budget outranks keeping overlap.
        private void TrimSeedToFit(
            List<int> seed, 
            ref int bufferTokens, 
            ref int bufferOverlap,
            int incomingTokenCount, 
            int[] tokenCounts)
        {
            while (seed.Count > 0 && bufferTokens + incomingTokenCount > _maxTokens)
            {
                bufferTokens -= tokenCounts[seed[0]];
                bufferOverlap -= tokenCounts[seed[0]];
                seed.RemoveAt(0);
            }
        }

        // Descend only dispatches to the next tier, so it returns that tier's stream directly instead of becoming an iterator of its own.
        // The discard arm is the token-window floor, which also keeps the switch expression exhaustive over the enum for the warnaserror gate.
        private IEnumerable<Chunk> Descend(string text, SentenceSpan unit, Tier tier, ChunkCounter counter, CancellationToken cancellationToken) =>
            tier switch
            {
                Tier.Paragraphs => PackRun(text, BlockSplitter.SplitLines(text, unit.Start, unit.Length), Tier.Lines, counter, cancellationToken),
                Tier.Lines => PackRun(text, SplitSentences(text, unit), Tier.Sentences, counter, cancellationToken),
                _ => EmitTokenWindows(text, unit, counter),
            };

        // SentenceSplitter works on a standalone string whose positions start at 0, but this piece sits at an offset inside the document.
        // Split a copy of just this piece, then shift every returned position by that offset so the spans point back into the original document.
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

        // See EmitOversizedSentence in SentenceAwareChunker: a sentence with no structural boundary left is cut into evenly balanced, non-overlapping windows that all land on legal cuts, with extents shifted by its start to point back into the document.
        private IEnumerable<Chunk> EmitTokenWindows(string text, SentenceSpan sentence, ChunkCounter counter)
        {
            var tokens = _tokenizer.Encode(text.Substring(sentence.Start, sentence.Length));
            var boundary = new TokenBoundary(_tokenizer, tokens);

            foreach (var window in TokenWindows.Balanced(tokens.Count, sentence.Length, _maxTokens, boundary))
            {
                var sourceStart = sentence.Start + window.CharStart;

                yield return new Chunk
                {
                    Index = counter.Next(),
                    Text = text.Substring(sourceStart, window.CharLength),
                    TokenCount = window.Length,
                    OverlapCount = 0,
                    SourceStart = sourceStart,
                    SourceLength = window.CharLength
                };
            }
        }

        // See CountSentenceTokens in SentenceAwareChunker: each piece is budgeted against the worse of the two positions it can occupy, because dropping the whitespace before it can raise its token count rather than lower it.
        private int[] CountUnitTokens(string text, IReadOnlyList<SentenceSpan> units, CancellationToken cancellationToken)
        {
            var tokenCounts = new int[units.Count];

            for (var i = 0; i < units.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var unit = units[i];
                var asChunkStart = _tokenizer.CountTokens(text.Substring(unit.Start, unit.Length));

                // The first piece has no preceding whitespace to lose, so it costs the same in either position.
                if (i == 0)
                {
                    tokenCounts[i] = asChunkStart;
                    continue;
                }

                var previousEnd = units[i - 1].End;
                var insideChunk = _tokenizer.CountTokens(text.Substring(previousEnd, unit.End - previousEnd));

                tokenCounts[i] = Math.Max(asChunkStart, insideChunk);
            }

            return tokenCounts;
        }

        private Chunk BuildChunk(
            string text,
            IReadOnlyList<SentenceSpan> units,
            List<int> bufferedUnits,
            int overlapCount,
            int index)
        {
            var start = units[bufferedUnits[0]].Start;
            var end = units[bufferedUnits[^1]].End;
            var chunkText = text[start..end];

            return new Chunk
            {
                Index = index,
                Text = chunkText,
                TokenCount = _tokenizer.CountTokens(chunkText),
                OverlapCount = overlapCount,
                SourceStart = start,
                SourceLength = end - start
            };
        }
    }
}
