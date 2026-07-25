namespace FluentChunker.Contracts
{
    /// <summary>
    /// Splits a document into chunks.
    /// Implementations are selected through the fluent splitter builder rather than constructed directly.
    /// </summary>
    public interface IChunker
    {
        /// <summary>
        /// Splits <paramref name="text"/> into chunks, in document order.
        /// </summary>
        /// <param name="text">The document to split.</param>
        /// <returns>The chunks, or an empty list if <paramref name="text"/> holds no content to split.</returns>
        IReadOnlyList<Chunk> Chunk(string text);
    }
}
