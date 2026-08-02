using FluentChunker.Contracts;
using FluentChunker.Tokenizers;

namespace FluentChunker.Chunkers.SentenceAware
{
    /// <summary>
    /// Splits a document along sentence boundaries, greedily packing whole sentences into chunks of at most <c>maxTokens</c> tokens so no chunk ever cuts a sentence in half.
    /// When <c>overlap</c> is set, each new chunk begins with the last whole sentences of the previous one, as many as fit the overlap budget.
    /// A single sentence that exceeds the budget on its own cannot be kept whole and falls back to fixed token windows.
    /// </summary>
    /// <param name="tokenizer">The tokenizer used to encode and decode the document.</param>
    /// <param name="maxTokens">Upper token bound per chunk; must be positive.</param>
    /// <param name="overlap">Tokens shared between consecutive chunks; must be smaller than <paramref name="maxTokens"/>.</param>
    /// <param name="additionalAbbreviations">Extra abbreviations recognised alongside the built-in list.</param>
    /// <param name="segmentCapacity">Sliding window capacity for the streamed path.</param>
    /// <param name="streamBasePosition">Sliding window starting position for the streamed path.</param>
    internal class SentenceAwareChunker(
        ITokenizer tokenizer,
        int maxTokens,
        int overlap,
        IReadOnlySet<string>? additionalAbbreviations = null,
        int segmentCapacity = SlidingTextWindow.DefaultCapacity,
        long streamBasePosition = 0) : IStreamingChunker
    {
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

        // The shared loop's per-segment handler: complete sentences form the piece, and the last span of a non-final segment is carried whole into the next one.
        // A segment with a single span is treated as unconfirmed whenever more input can still follow, because a sentence split is only confirmable with the following sentence's start in view, so this also degrades a segment that happens to hold exactly one complete, terminated sentence.
        // A whitespace-only segment (no spans at all) is consumed without emitting, matching the in-memory core, which emits no chunk for it either.
        private (int Consumed, IEnumerable<Chunk> Chunks) HandleSegment(
            string segment, bool endOfStream, long basePosition, ChunkCounter counter, CancellationToken cancellationToken)
        {
            var sentences = SentenceSplitter.Split(segment, _additionalAbbreviations);

            if (sentences.Count == 0)
                return (segment.Length, []);

            if (!endOfStream && sentences.Count == 1)
                return (segment.Length, EmitCapacityStretch(segment, counter, basePosition));

            var consumed = endOfStream ? segment.Length : sentences[^1].Start;
            var piece = consumed == segment.Length ? segment : segment[..consumed];

            return (consumed, StreamingChunkLoop.RemapPiece(ChunkCore(piece, cancellationToken), counter, basePosition));
        }

        // The degradation for the single-span segment HandleSegment could not confirm: each buffered stretch is laid out with Strided windows, because Balanced is reserved for units whose total token count is known.
        // Overlap is 0 throughout: a capacity cut is a seam, and seams reset overlap.
        private IEnumerable<Chunk> EmitCapacityStretch(string segment, ChunkCounter counter, long basePosition)
        {
            var tokens = _tokenizer.Encode(segment);

            if (tokens.Count <= _maxTokens)
            {
                yield return new Chunk
                {
                    Index = counter.Next(),
                    Text = segment,
                    TokenCount = tokens.Count,
                    OverlapCount = 0,
                    SourceStart = basePosition,
                    SourceLength = segment.Length
                };
                yield break;
            }

            var boundary = new TokenBoundary(_tokenizer, tokens);

            foreach (var window in TokenWindows.Strided(tokens.Count, segment.Length, _maxTokens, 0, boundary))
            {
                yield return new Chunk
                {
                    Index = counter.Next(),
                    Text = segment.Substring(window.CharStart, window.CharLength),
                    TokenCount = window.Length,
                    OverlapCount = 0,
                    SourceStart = basePosition + window.CharStart,
                    SourceLength = window.CharLength
                };
            }
        }

        // Sizing every sentence runs before the first chunk can be cut and is the bulk of that wait, so the token goes into it rather than only guarding the chunks that come out.
        // Splitting the document into sentences stays uninterruptible, a floor far smaller than the counting it precedes.
        private IEnumerable<Chunk> ChunkCore(string text, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(text))
                yield break;

            var sentences = SentenceSplitter.Split(text, _additionalAbbreviations);
            if (sentences.Count == 0)
                yield break;

            var tokenCounts = CountSentenceTokens(text, sentences, cancellationToken);

            var chunkSentences = new List<int>();
            var chunkTokens = 0;
            var chunkOverlap = 0;
            var counter = new ChunkCounter();

            // Walk the sentences left to right, appending each to the current chunk until the next one would push it over the token budget, then start a new chunk.
            for (var i = 0; i < sentences.Count; i++)
            {
                if (tokenCounts[i] > _maxTokens)
                {
                    // This sentence is over budget on its own.
                    // Flush whatever is buffered, split just this sentence into token windows,
                    // then continue with an empty buffer so the sentences around it never merge into those windows and no overlap is carried across the gap.
                    if (chunkSentences.Count > 0)
                        yield return BuildChunk(text, sentences, chunkSentences, chunkOverlap, counter.Next());
                    chunkSentences.Clear();
                    chunkTokens = 0;
                    chunkOverlap = 0;

                    foreach (var window in EmitOversizedSentence(text, sentences[i], counter))
                        yield return window;
                    continue;
                }

                if (chunkSentences.Count > 0 && chunkTokens + tokenCounts[i] > _maxTokens)
                {
                    // Adding this sentence would overflow the budget.
                    // Flush the current chunk, then seed the next one with the last whole sentences of the flushed chunk (the overlap) before adding the incoming sentence below.
                    yield return BuildChunk(text, sentences, chunkSentences, chunkOverlap, counter.Next());

                    var (seed, seedTokens) = SeedNextChunk(chunkSentences, tokenCounts);

                    chunkSentences.Clear();
                    chunkSentences.AddRange(seed);
                    chunkTokens = seedTokens;
                    chunkOverlap = seedTokens;

                    TrimSeedToFit(chunkSentences, ref chunkTokens, ref chunkOverlap, tokenCounts[i], tokenCounts);
                }

                chunkSentences.Add(i);
                chunkTokens += tokenCounts[i];
            }

            if (chunkSentences.Count > 0)
                yield return BuildChunk(text, sentences, chunkSentences, chunkOverlap, counter.Next());
        }

        // Builds the overlap the next chunk will start with: the last whole sentences of the chunk just flushed, taken from the end backward, as many as fit the overlap budget.
        private (List<int> Seed, int SeedTokens) SeedNextChunk(List<int> flushedSentences, int[] tokenCounts)
        {
            var seed = new List<int>();
            var seedTokens = 0;

            for (var j = flushedSentences.Count - 1; j >= 0; j--)
            {
                var candidate = flushedSentences[j];
                if (seedTokens + tokenCounts[candidate] > _overlap)
                    break;
                seed.Insert(0, candidate);
                seedTokens += tokenCounts[candidate];
            }

            return (seed, seedTokens);
        }

        // The overlap sentences plus the incoming sentence may not fit the chunk budget together.
        // If they don't, drop overlap sentences from the front until they do.
        // Staying within the budget always takes priority over keeping overlap.
        private void TrimSeedToFit(List<int> seed, ref int chunkTokens, ref int chunkOverlap,
            int incomingTokenCount, int[] tokenCounts)
        {
            while (seed.Count > 0 && chunkTokens + incomingTokenCount > _maxTokens)
            {
                chunkTokens -= tokenCounts[seed[0]];
                chunkOverlap -= tokenCounts[seed[0]];
                seed.RemoveAt(0);
            }
        }

        // Counts the tokens of each sentence, budgeting for the worse of the two positions it can occupy.
        // A sentence in the middle of a chunk is sliced together with the whitespace before it, but a sentence that begins a chunk is sliced from its own start and loses that whitespace.
        // Dropping a leading space can raise the count instead of lowering it, because BPE encodes a space-prefixed word more cheaply than a bare one, so only the larger of the two counts keeps a packed chunk inside the budget.
        private int[] CountSentenceTokens(string text, IReadOnlyList<SentenceSpan> sentences, CancellationToken cancellationToken)
        {
            var tokenCounts = new int[sentences.Count];

            for (var i = 0; i < sentences.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var sentence = sentences[i];
                var asChunkStart = _tokenizer.CountTokens(text.Substring(sentence.Start, sentence.Length));

                // The first sentence has no preceding whitespace to lose, so it costs the same in either position.
                if (i == 0)
                {
                    tokenCounts[i] = asChunkStart;
                    continue;
                }

                var previousEnd = sentences[i - 1].End;
                var insideChunk = _tokenizer.CountTokens(text.Substring(previousEnd, sentence.End - previousEnd));

                tokenCounts[i] = Math.Max(asChunkStart, insideChunk);
            }

            return tokenCounts;
        }

        // A sentence that alone exceeds the budget has no smaller boundary to split on, so cut it into token windows instead.
        // The windows are sized as evenly as possible rather than filling each to the max and leaving a tiny last one, such as a lone "." chunk, and they do not overlap.
        // Every boundary lands on a legal cut, so the windows concatenate back to the sentence exactly.
        // Extents are relative to the sentence, so they are shifted by its start to point back into the document.
        private IEnumerable<Chunk> EmitOversizedSentence(string text, SentenceSpan sentence, ChunkCounter counter)
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

        private Chunk BuildChunk(
            string text,
            IReadOnlyList<SentenceSpan> sentences,
            List<int> chunkSentences,
            int overlapCount,
            int index)
        {
            var start = sentences[chunkSentences[0]].Start;
            var end = sentences[chunkSentences[^1]].End;
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
