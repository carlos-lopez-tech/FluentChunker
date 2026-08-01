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
        public IAsyncEnumerable<Chunk> ChunkAsync(string text, CancellationToken cancellationToken = default) =>
            ChunkStream.Over(token => ChunkCore(text, token), cancellationToken);

        // Unlike the other strategies, which encode one structural unit at a time, this one needs the whole document tokenized before it can place a single window.
        // That made the encode the longest uninterruptible stretch in the library, so the token goes into it rather than only guarding the chunks that come out.
        private IEnumerable<Chunk> ChunkCore(string text, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(text))
                yield break;

            var allTokens = DocumentEncoding.Encode(_tokenizer, text, cancellationToken);

            if (allTokens.Count <= _maxTokens)
            {
                yield return new Chunk
                {
                    Index = 0,
                    Text = text,
                    TokenCount = allTokens.Count,
                    SourceStart = 0,
                    SourceLength = text.Length
                };
                yield break;
            }

            var index = 0;
            var boundary = new TokenBoundary(_tokenizer, allTokens);

            // OverlapCount is set from the window rather than _overlap,
            // so callers relying on it to reconstruct shared context see what the chunk actually contains.
            // The window's character extent selects the same text the tokens decode to, which lets the chunk be a verbatim source slice.
            foreach (var window in TokenWindows.Strided(allTokens.Count, text.Length, _maxTokens, _overlap, boundary))
            {
                yield return new Chunk
                {
                    Index = index++,
                    Text = text.Substring(window.CharStart, window.CharLength),
                    TokenCount = window.Length,
                    OverlapCount = window.Overlap,
                    SourceStart = window.CharStart,
                    SourceLength = window.CharLength
                };
            }
        }
    }
}
