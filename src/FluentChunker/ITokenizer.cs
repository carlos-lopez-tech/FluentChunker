namespace FluentChunker
{
    public interface ITokenizer
    {
        public int CountTokens(string text);
        public IReadOnlyList<int> Encode(string text);
        public string Decode(IReadOnlyList<int> tokens);
    }
}
