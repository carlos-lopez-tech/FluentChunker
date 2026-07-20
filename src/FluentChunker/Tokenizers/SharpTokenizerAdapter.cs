using SharpToken;

namespace FluentChunker.Tokenizers
{
    public sealed class SharpTokenizerAdapter(string encodingName) : ITokenizer
    {
        private readonly GptEncoding _tokenizer = GptEncoding.GetEncoding(encodingName);

        public int CountTokens(string text) => _tokenizer.CountTokens(text);

        public IReadOnlyList<int> Encode(string text) => _tokenizer.Encode(text);

        public string Decode(IReadOnlyList<int> tokens) => _tokenizer.Decode(tokens);
    }
}
