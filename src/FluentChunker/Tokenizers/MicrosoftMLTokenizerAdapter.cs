using Microsoft.ML.Tokenizers;

namespace FluentChunker.Tokenizers
{
    /// <summary>
    /// An <see cref="ITokenizer"/> backed by Microsoft.ML.Tokenizers, measuring chunks with the same tokenizer the consuming model uses.
    /// </summary>
    public sealed class MicrosoftMLTokenizerAdapter : ITokenizer, IBatchEncodingTokenizer
    {
        private readonly Tokenizer _tokenizer;

        /// <summary>
        /// Creates an adapter over a tiktoken encoding, the OpenAI-compatible path.
        /// </summary>
        /// <param name="encodingName">
        /// The tiktoken encoding name.
        /// Only <c>cl100k_base</c>, used by the current OpenAI embedding models, resolves out of the box; it is the one data package FluentChunker ships.
        /// Every other encoding requires a reference to its own tokenizer data package.
        /// Match this to the model that will consume the chunks.
        /// A different encoding counts the same text roughly 10% higher or lower, so a mismatch silently produces chunks that miss the model's real token budget.
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// The data package for <paramref name="encodingName"/> is not referenced; the message names the package to add.
        /// </exception>
        public MicrosoftMLTokenizerAdapter(string encodingName) => _tokenizer = TiktokenTokenizer.CreateForEncoding(encodingName);

        /// <summary>
        /// Creates an adapter over any Microsoft.ML.Tokenizers tokenizer, which is how non-OpenAI embedding models are sized: BERT and WordPiece for BGE and E5, SentencePiece for Nomic and Qwen, and the Llama family.
        /// </summary>
        /// <param name="tokenizer">
        /// The tokenizer to adapt.
        /// Its <c>Decode</c> must reproduce the encoded text exactly, character for character, because FluentChunker decodes token runs both to find legal cut points and to measure how far a chunk extends in the source.
        /// A tokenizer that normalises text, as BERT does by lowercasing and as SentencePiece and Llama models commonly do by collapsing whitespace, still counts tokens correctly but makes chunk boundaries drift from the source.
        /// Verify the round trip against your own corpus before indexing with it; the README gives the check and explains what the chunking-time guard does and does not catch.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="tokenizer"/> is null.</exception>
        public MicrosoftMLTokenizerAdapter(Tokenizer tokenizer)
        {
            ArgumentNullException.ThrowIfNull(tokenizer);

            _tokenizer = tokenizer;
        }

        /// <inheritdoc/>
        public IReadOnlyList<int> Encode(string text) => _tokenizer.EncodeToIds(text);

        /// <inheritdoc/>
        public string Decode(IReadOnlyList<int> tokens) => _tokenizer.Decode(tokens);

        /// <inheritdoc/>
        public int CountTokens(string text) => _tokenizer.CountTokens(text);

        // Microsoft.ML.Tokenizers stops on a boundary it chose itself and reports how far that reached, which is why resuming there reproduces a whole-document encode exactly rather than approximately.
        // Verified identical for tiktoken across Latin and mixed CJK, Arabic, Devanagari, Greek, Cyrillic and emoji text, at batch sizes from 512 to 32768 tokens.
        IReadOnlyList<int> IBatchEncodingTokenizer.EncodeUpTo(ReadOnlySpan<char> text, int maxTokens, out int charsConsumed) =>
            _tokenizer.EncodeToIds(text, maxTokens, out _, out charsConsumed);
    }
}
