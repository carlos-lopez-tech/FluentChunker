using Microsoft.ML.Tokenizers;

namespace FluentChunker.Tokenizers
{
    public sealed class MicrosoftMLTokenizerAdapter(string encodingName) : ITokenizer
    {
        private readonly Tokenizer _tokenizer = TiktokenTokenizer.CreateForEncoding(encodingName);

        public IReadOnlyList<int> Encode(string text) => _tokenizer.EncodeToIds(text);

        public string Decode(IReadOnlyList<int> tokens) => _tokenizer.Decode(tokens);

        public int CountTokens(string text) => _tokenizer.CountTokens(text);
    }
}
