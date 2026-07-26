using Microsoft.ML.Tokenizers;

namespace FluentChunker.Tokenizers
{
    /// <summary>
    /// An <see cref="ITokenizer"/> backed by Microsoft.ML.Tokenizers, giving token counts that match OpenAI models.
    /// </summary>
    /// <param name="encodingName">
    /// The tiktoken encoding name.
    /// Only <c>cl100k_base</c>, used by the current OpenAI embedding models, resolves out of the box; it is the one data package FluentChunker ships.
    /// Every other encoding requires a reference to its own <c>Microsoft.ML.Tokenizers.Data.*</c> package, and the exception names the one to add.
    /// Match this to the model that will consume the chunks.
    /// A different encoding counts the same text roughly 10% higher or lower, so a mismatch silently produces chunks that miss the model's real token budget.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The data package for <paramref name="encodingName"/> is not referenced.
    /// </exception>
    public sealed class MicrosoftMLTokenizerAdapter(string encodingName) : ITokenizer
    {
        private readonly Tokenizer _tokenizer = TiktokenTokenizer.CreateForEncoding(encodingName);

        /// <inheritdoc/>
        public IReadOnlyList<int> Encode(string text) => _tokenizer.EncodeToIds(text);

        /// <inheritdoc/>
        public string Decode(IReadOnlyList<int> tokens) => _tokenizer.Decode(tokens);

        /// <inheritdoc/>
        public int CountTokens(string text) => _tokenizer.CountTokens(text);
    }
}
