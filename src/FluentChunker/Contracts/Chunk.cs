using System.Text.Json;

namespace FluentChunker.Contracts
{
    /// <summary>
    /// One unit of output produced by a <see cref="ChunkingPipeline"/>.
    /// </summary>
    public record Chunk
    {
        /// <summary>
        /// Zero-based position of this chunk within the sequence returned for a single document.
        /// </summary>
        public required int Index { get; init; }

        /// <summary>
        /// The chunk text, including any overlap carried in from the previous chunk.
        /// </summary>
        public required string Text { get; init; }

        /// <summary>
        /// Number of tokens in <see cref="Text"/> as measured by the pipeline's tokenizer.
        /// Never exceeds the configured max token size.
        /// </summary>
        public required int TokenCount { get; init; }

        /// <summary>
        /// How many of this chunk's tokens were carried in from the previous chunk.
        /// Counts tokens received, not emitted, so it is zero on the first chunk and on every chunk produced by a strategy configured without overlap.
        /// </summary>
        public int OverlapCount { get; init; }

        /// <summary>
        /// Pipeline-level and document-level metadata, merged, with document values winning.
        /// Structure-aware chunking reserves the key <c>headingPath</c> for its heading breadcrumb.
        /// </summary>
        public Dictionary<string, JsonElement> Metadata { get; init; } = [];
    }
}
