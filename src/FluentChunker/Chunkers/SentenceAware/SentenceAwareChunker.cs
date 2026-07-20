using FluentChunker.Contracts;
using FluentChunker.Tokenizers;

namespace FluentChunker.Chunkers.SentenceAware
{
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
            var index = 0;

            var chunkOverlap = 0;

            for (var i = 0; i < sentences.Count; i++)
            {
                if (tokenCounts[i] > _maxTokens)
                {
                    FlushChunk(text, sentences, chunkSentences, chunkOverlap, chunks, ref index);
                    chunkSentences.Clear();
                    chunkTokens = 0;
                    chunkOverlap = 0;

                    EmitOversizedSentence(text, sentences[i], chunks, ref index);
                    continue;
                }

                if (chunkSentences.Count > 0 && chunkTokens + tokenCounts[i] > _maxTokens)
                {
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

        // Seed the next chunk with whole trailing sentences from the just-flushed chunk,
        // walking backward within the overlap budget.
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

        // If the seed leaves no room for the incoming sentence, drop from the front until it fits —
        // the budget invariant always wins over overlap.
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

        // Each sentence is counted together with the whitespace gap that precedes it,
        // because that gap ends up inside the chunk slice (see plan note on gap-inclusive counts).
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

        // A sentence that alone exceeds the budget cannot be kept whole: split it into
        // evenly-sized token windows (no overlap between them). Balanced sizing avoids
        // a tiny remainder window — e.g. a lone "." chunk — that a fixed stride would leave.
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
