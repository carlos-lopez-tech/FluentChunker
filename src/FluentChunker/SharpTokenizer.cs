using SharpToken;

namespace FluentChunker
{
    public sealed class SharpTokenizer : ITokenizer
    {
        private readonly GptEncoding _encoding;

        public SharpTokenizer(string encodingName)
        {
            _encoding = GptEncoding.GetEncoding(encodingName);
        }

        public int CountTokens(string text)
        {
            return _encoding.CountTokens(text);
        }

        public IReadOnlyList<int> Encode(string text)
        {
            return _encoding.Encode(text);
        }

        public string Decode(IReadOnlyList<int> tokens)
        {
            return _encoding.Decode(tokens);
        }
    }
}
