namespace FluentChunker.Contracts
{
    /// <summary>
    /// A chunking strategy that can also read its document from a <see cref="TextReader"/> with bounded memory.
    /// The defining promise is that memory stays flat regardless of input size: the strategy reads through a bounded window and never materialises the whole stream.
    /// Fixed-size, sentence-aware and recursive chunking implement it; structure-aware chunking does not, because Markdown structure needs the whole document in memory.
    /// </summary>
    public interface IStreamingChunker : IChunker
    {
        /// <summary>
        /// Splits the text read from <paramref name="reader"/> into chunks, produced lazily and in stream order as the sequence is enumerated.
        /// </summary>
        /// <param name="reader">The document source. The caller owns it: the library never disposes it, and it is read forward exactly once.</param>
        /// <param name="cancellationToken">Observed before each chunk is produced and by every read: a token cancelled up front makes the enumeration throw <see cref="OperationCanceledException"/> before the first chunk, and one cancelled mid-enumeration stops it before the next.</param>
        /// <returns>The chunks, or an empty sequence if the stream holds no content to split.</returns>
        /// <remarks>
        /// <see cref="Chunk.SourceStart"/> values are absolute positions in the stream and can exceed <see cref="int.MaxValue"/>; there is no in-memory string to slice, so verification means seeking and re-reading the source.
        /// A chunk never spans a buffer seam and overlap resets at every seam, so boundary-free stretches longer than the internal buffer degrade to hard cuts rather than unbounded buffering.
        /// </remarks>
        IAsyncEnumerable<Chunk> ChunkAsync(TextReader reader, CancellationToken cancellationToken = default);
    }
}
