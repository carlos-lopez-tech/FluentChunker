using FluentChunker.Contracts;

namespace FluentChunker
{
    /// <summary>
    /// Splits a document into fixed-size token windows of at most <c>maxTokens</c> tokens,
    /// optionally overlapping by <c>overlap</c> tokens.
    /// </summary>
    /// <remarks>Creates a fixed-size chunker.</remarks>
    /// <param name="tokenizer">The tokenizer used to encode and decode the document.</param>
    /// <param name="maxTokens">Upper token bound per chunk; must be positive.</param>
    /// <param name="overlap">Tokens shared between consecutive chunks; must be smaller than <paramref name="maxTokens"/>.</param>
    public class FixedSizeChunker(ITokenizer tokenizer, int maxTokens, int overlap) : IChunker
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
            var step = _maxTokens - _overlap;
            var index = 0;

            for (var start = 0; start < allTokens.Count; start += step)
            {
                var end = Math.Min(start + _maxTokens, allTokens.Count);
                var length = end - start;

                var window = new int[length];

                for (var i = 0; i < length; i++)
                {
                    window[i] = allTokens[start + i];
                }

                var isLast = end == allTokens.Count;

                chunks.Add(new Chunk
                {
                    Index = index++,
                    Text = _tokenizer.Decode(window),
                    TokenCount = length,
                    OverlapCount = isLast ? 0 : _overlap
                });

                if (isLast)
                    break;
            }

            return chunks;
        }
    }
}
