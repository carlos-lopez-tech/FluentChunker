namespace FluentChunker.Tokenizers
{
    public interface ITokenizer
    {
        public IReadOnlyList<int> Encode(string text);
        public string Decode(IReadOnlyList<int> tokens);
        public int CountTokens(string text);
    }
}
