using FluentChunker.Contracts;

namespace FluentChunker
{
    public interface IChunker
    {
        IReadOnlyList<Chunk> Chunk(string text);
    }
}
