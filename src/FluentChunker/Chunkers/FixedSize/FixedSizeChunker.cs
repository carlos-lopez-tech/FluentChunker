using FluentChunker.Contracts;
using FluentChunker.Tokenizers;

namespace FluentChunker.Chunkers.FixedSize
{
    /// <summary>
    /// Splits a document into fixed-size token windows of at most <c>maxTokens</c> tokens,
    /// optionally overlapping by <c>overlap</c> tokens.
    /// </summary>
    /// <param name="tokenizer">The tokenizer used to encode and decode the document.</param>
    /// <param name="maxTokens">Upper token bound per chunk; must be positive.</param>
    /// <param name="overlap">Tokens shared between consecutive chunks; must be smaller than <paramref name="maxTokens"/>.</param>
    internal class FixedSizeChunker(ITokenizer tokenizer, int maxTokens, int overlap) : IChunker
    {
        private readonly ITokenizer _tokenizer = tokenizer;
        private readonly int _maxTokens = maxTokens;
        private readonly int _overlap = overlap;

        /// <inheritdoc/>
        public IReadOnlyList<Chunk> Chunk(string text)
        {
            if (string.IsNullOrEmpty(text))
                return [];

            var allTokens = _tokenizer.Encode(text);

            if (allTokens.Count <= _maxTokens)
                return [new Chunk { Index = 0, Text = text, TokenCount = allTokens.Count }];

            var chunks = new List<Chunk>();
            var index = 0;
            var boundary = new TokenBoundary(_tokenizer, allTokens);

            // OverlapCount is set from windowOverlap rather than _overlap,
            // so callers relying on it to reconstruct shared context see what the chunk actually contains.
            foreach (var (start, length, windowOverlap) in TokenWindows.Strided(allTokens.Count, _maxTokens, _overlap, boundary))
            {
                var window = TokenWindows.Slice(allTokens, start, length);

                chunks.Add(new Chunk
                {
                    Index = index++,
                    Text = _tokenizer.Decode(window),
                    TokenCount = length,
                    OverlapCount = windowOverlap
                });
            }

            return chunks;
        }
    }
}
