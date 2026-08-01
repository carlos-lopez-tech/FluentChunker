namespace FluentChunker.Contracts
{
    /// <summary>
    /// Splits a document into chunks.
    /// Implementations are selected through the fluent splitter builder rather than constructed directly.
    /// </summary>
    public interface IChunker
    {
        /// <summary>
        /// Splits <paramref name="text"/> into chunks, produced lazily and in document order as the sequence is enumerated.
        /// </summary>
        /// <param name="text">The document to split.</param>
        /// <param name="cancellationToken">Observed before each chunk is produced: a token cancelled up front makes the enumeration throw <see cref="OperationCanceledException"/> before the first chunk, and one cancelled mid-enumeration stops it before the next.</param>
        /// <returns>The chunks, or an empty sequence if <paramref name="text"/> holds no content to split.</returns>
        IAsyncEnumerable<Chunk> ChunkAsync(string text, CancellationToken cancellationToken = default);
    }
}
