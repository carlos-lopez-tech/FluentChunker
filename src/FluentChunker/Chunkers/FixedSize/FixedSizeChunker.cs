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
                return [new Chunk
                {
                    Index = 0,
                    Text = text,
                    TokenCount = allTokens.Count,
                    SourceStart = 0,
                    SourceLength = text.Length
                }];

            var chunks = new List<Chunk>();
            var index = 0;
            var boundary = new TokenBoundary(_tokenizer, allTokens);

            // OverlapCount is set from the window rather than _overlap,
            // so callers relying on it to reconstruct shared context see what the chunk actually contains.
            // The window's character extent selects the same text the tokens decode to, which lets the chunk be a verbatim source slice.
            foreach (var window in TokenWindows.Strided(allTokens.Count, text.Length, _maxTokens, _overlap, boundary))
            {
                chunks.Add(new Chunk
                {
                    Index = index++,
                    Text = text.Substring(window.CharStart, window.CharLength),
                    TokenCount = window.Length,
                    OverlapCount = window.Overlap,
                    SourceStart = window.CharStart,
                    SourceLength = window.CharLength
                });
            }

            return chunks;
        }
    }
}
