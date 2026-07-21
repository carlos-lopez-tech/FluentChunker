using FluentChunker.Contracts;
using FluentChunker.Tokenizers;

namespace FluentChunker.Chunkers.SentenceAware
{
    /// <summary>
    /// Splits a document along sentence boundaries, greedily packing whole sentences into
    /// chunks of at most <c>maxTokens</c> tokens so no chunk ever cuts a sentence in half.
    /// When <c>overlap</c> is set, each new chunk begins with the last whole sentences of the
    /// previous one, as many as fit the overlap budget. A single sentence that exceeds the
    /// budget on its own cannot be kept whole and falls back to fixed token windows.
    /// </summary>
    public class SentenceAwareChunker(
        ITokenizer tokenizer, 
        int maxTokens, 
        int overlap,
        IReadOnlySet<string>? additionalAbbreviations = null) : IChunker
    {
        private readonly ITokenizer _tokenizer = tokenizer;
        private readonly int _maxTokens = maxTokens;
        private readonly int _overlap = overlap;
        private readonly IReadOnlySet<string>? _additionalAbbreviations = additionalAbbreviations;

        public IReadOnlyList<Chunk> Chunk(string text)
        {
            if (string.IsNullOrEmpty(text))
                return [];

            var sentences = SentenceSplitter.Split(text, _additionalAbbreviations);
            if (sentences.Count == 0)
                return [];

            var tokenCounts = CountSentenceTokens(text, sentences);

            var chunks = new List<Chunk>();
            var chunkSentences = new List<int>();
            var chunkTokens = 0;
            var chunkOverlap = 0;
            var index = 0;

            // Walk the sentences left to right, appending each to the current chunk until the
            // next one would push it over the token budget, then start a new chunk.
            for (var i = 0; i < sentences.Count; i++)
            {
                if (tokenCounts[i] > _maxTokens)
                {
                    // This sentence is over budget on its own. 
                    // Flush whatever is buffered, split just this sentence into token windows, 
                    // then continue with an empty buffer so the sentences around it never merge into those windows and
                    // no overlap is carried across the gap.
                    FlushChunk(text, sentences, chunkSentences, chunkOverlap, chunks, ref index);
                    chunkSentences.Clear();
                    chunkTokens = 0;
                    chunkOverlap = 0;

                    EmitOversizedSentence(text, sentences[i], chunks, ref index);
                    continue;
                }

                if (chunkSentences.Count > 0 && chunkTokens + tokenCounts[i] > _maxTokens)
                {
                    // Adding this sentence would overflow the budget. 
                    // Flush the current chunk, then seed the next one with the last whole sentences of the flushed chunk 
                    // (the overlap) before adding the incoming sentence below.
                    FlushChunk(text, sentences, chunkSentences, chunkOverlap, chunks, ref index);

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

            FlushChunk(text, sentences, chunkSentences, chunkOverlap, chunks, ref index);
            return chunks;
        }

        // Builds the overlap the next chunk will start with: the last whole sentences of the
        // chunk just flushed, taken from the end backward, as many as fit the overlap budget.
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

        // Counts the tokens of each sentence. 
        // From the second sentence onward the count includes the whitespace between it and the previous sentence, 
        // because a chunk is later sliced as one continuous span and that in-between whitespace ends up inside the slice. 
        // Counting it here keeps the packing decision honest about the chunk's real size.
        private int[] CountSentenceTokens(string text, IReadOnlyList<SentenceSpan> sentences)
        {
            var tokenCounts = new int[sentences.Count];

            for (var i = 0; i < sentences.Count; i++)
            {
                var start = i == 0 ? sentences[i].Start : sentences[i - 1].End;
                tokenCounts[i] = _tokenizer.CountTokens(text[start..sentences[i].End]);
            }

            return tokenCounts;
        }

        // A sentence that alone exceeds the budget has no smaller boundary to split on, so cut it into token windows instead. 
        // The windows are sized as evenly as possible (rather than filling each to the max and leaving a tiny last one, such as a lone "." chunk) and do not overlap. 
        // Unlike every other chunk, this text is rebuilt from tokens instead of sliced from the source, 
        // so it may gain a leading space that the tokenizer attaches to the first word.
        private void EmitOversizedSentence(string text, SentenceSpan sentence, List<Chunk> chunks, ref int index)
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

        private void FlushChunk(string text, IReadOnlyList<SentenceSpan> sentences,
            List<int> chunkSentences, int overlapCount, List<Chunk> chunks, ref int index)
        {
            if (chunkSentences.Count == 0)
                return;

            var start = sentences[chunkSentences[0]].Start;
            var end = sentences[chunkSentences[^1]].End;
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
