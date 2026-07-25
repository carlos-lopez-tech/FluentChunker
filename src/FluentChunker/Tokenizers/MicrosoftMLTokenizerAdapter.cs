using Microsoft.ML.Tokenizers;

namespace FluentChunker.Tokenizers
{
    /// <summary>
    /// An <see cref="ITokenizer"/> backed by Microsoft.ML.Tokenizers, giving token counts that match OpenAI models.
    /// </summary>
    /// <param name="encodingName">
    /// The tiktoken encoding name.
    /// Only <c>cl100k_base</c>, used by the current OpenAI embedding models, resolves out of the box; it is the one data package FluentChunker ships.
    /// Every other encoding requires a reference to its own <c>Microsoft.ML.Tokenizers.Data.*</c> package, for example <c>Microsoft.ML.Tokenizers.Data.O200kBase</c> for <c>o200k_base</c> (GPT-5, the o-series, GPT-4.1 and GPT-4o).
    /// Match this to the model that will consume the chunks: the same text differs by roughly 10% between encodings, in either direction, so the wrong one silently misreports the budget.
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
