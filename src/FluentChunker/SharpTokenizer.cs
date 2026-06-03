using SharpToken;

namespace FluentChunker
{
    public class SharpTokenizer
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

        public List<int> Encode(string text)
        {
            return _encoding.Encode(text);
        }

        public string Decode(List<int> tokens)
        {
            return _encoding.Decode(tokens);
        }
    }
}
