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

            // Slide a window of maxTokens across the token list,
            // stepping forward by (maxTokens - overlap) so each window shares exactly overlap tokens with the previous one.
            foreach (var (start, length) in TokenWindows.Strided(allTokens.Count, _maxTokens, _maxTokens - _overlap))
            {
                var window = TokenWindows.Slice(allTokens, start, length);

                chunks.Add(new Chunk
                {
                    Index = index++,
                    Text = _tokenizer.Decode(window),
                    TokenCount = length,
                    // Tokens carried into this chunk from the previous one.
                    // The final window can be shorter than the overlap, in which case it is carried content entirely.
                    OverlapCount = index == 1 ? 0 : Math.Min(_overlap, length)
                });
            }

            return chunks;
        }
    }
}
