namespace FluentChunker.Tokenizers
{
    /// <summary>
    /// Converts between text and token IDs so chunk sizes can be measured in tokens.
    /// Implement this to use a tokenizer other than the built-in <see cref="MicrosoftMLTokenizerAdapter"/>.
    /// </summary>
    public interface ITokenizer
    {
        /// <summary>
        /// Converts text to token IDs.
        /// </summary>
        /// <param name="text">The text to tokenize.</param>
        /// <returns>The token IDs, in order.</returns>
        public IReadOnlyList<int> Encode(string text);

        /// <summary>
        /// Converts token IDs back to text.
        /// Chunkers slice the output of <see cref="Encode"/> and decode the slices, so this must round-trip any contiguous run of encoded tokens.
        /// </summary>
        /// <param name="tokens">The token IDs to convert.</param>
        /// <returns>The reconstructed text.</returns>
        public string Decode(IReadOnlyList<int> tokens);

        /// <summary>
        /// Counts the tokens in <paramref name="text"/>.
        /// Must agree with the length of <see cref="Encode"/>'s result.
        /// Called once per candidate chunk, so implementations should avoid materializing the token list.
        /// </summary>
        /// <param name="text">The text to measure.</param>
        /// <returns>The number of tokens.</returns>
        public int CountTokens(string text);
    }
}
