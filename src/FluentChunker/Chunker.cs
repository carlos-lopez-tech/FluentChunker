namespace FluentChunker
{
    public class Chunker
    {
        public static Chunker Create() => new();

        public IEnumerable<string> Chunk(string text)
        {
            return text.Split('\n');
        }
    }
}
