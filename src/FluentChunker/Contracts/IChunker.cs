namespace FluentChunker.Contracts
{
    public interface IChunker
    {
        IReadOnlyList<Chunk> Chunk(string text);
    }
}
