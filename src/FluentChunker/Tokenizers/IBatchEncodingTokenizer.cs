namespace FluentChunker.Tokenizers
{
    // Encoding a whole document is one opaque ITokenizer call, and on a multi-megabyte document it is the longest stretch of chunking no cancellation token can reach.
    // A tokenizer that can stop after a set number of tokens and say how far it got turns that stretch into a loop, which is the only way cooperative cancellation can observe it.
    // Kept internal because it is a performance detail of the shipped adapter rather than a contract custom tokenizers have to meet: a tokenizer that does not implement it is encoded whole, exactly as before.
    internal interface IBatchEncodingTokenizer
    {
        /// <summary>
        /// Encodes at most <paramref name="maxTokens"/> tokens from the start of <paramref name="text"/>.
        /// </summary>
        /// <param name="text">The text to encode from, whose start is where encoding resumes.</param>
        /// <param name="maxTokens">The most tokens this call may produce.</param>
        /// <param name="charsConsumed">How many characters the returned tokens cover, so the caller resumes there; zero when no token fits.</param>
        /// <returns>The token IDs, which must equal the same run of a whole-document encode so that batching cannot move chunk boundaries.</returns>
        IReadOnlyList<int> EncodeUpTo(ReadOnlySpan<char> text, int maxTokens, out int charsConsumed);
    }
}
