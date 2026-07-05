using SharpToken;

namespace FluentChunker
{
    public sealed class SharpTokenizerAdapter : ITokenizer
    {
        private readonly GptEncoding _tokenizer;

        public SharpTokenizerAdapter(string encodingName)
        {
            _tokenizer = GptEncoding.GetEncoding(encodingName);
        }

        public int CountTokens(string text)
        {
            return _tokenizer.CountTokens(text);
        }

        public IReadOnlyList<int> Encode(string text)
        {
            return _tokenizer.Encode(text);
        }

        public string Decode(IReadOnlyList<int> tokens)
        {
            return _tokenizer.Decode(tokens);
        }
    }
}
