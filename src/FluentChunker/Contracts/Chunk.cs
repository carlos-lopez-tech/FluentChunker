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
        /// Index in the source document where <see cref="Text"/> begins, or -1 when the chunker does not track it.
        /// </summary>
        /// <remarks>
        /// Every built-in strategy reports a real value, and <c>document.Substring(SourceStart, SourceLength)</c> returns <see cref="Text"/> exactly.
        /// With overlap configured, consecutive chunks deliberately cover overlapping ranges.
        /// </remarks>
        public int SourceStart { get; init; } = -1;

        /// <summary>
        /// Length of <see cref="Text"/> in the source document, or -1 when the chunker does not track it.
        /// </summary>
        /// <remarks>
        /// Always equal to <c>Text.Length</c> for the built-in strategies, since every chunk they produce is a verbatim slice of the document.
        /// </remarks>
        public int SourceLength { get; init; } = -1;

        /// <summary>
        /// Pipeline-level and document-level metadata, merged, with document values winning.
        /// Structure-aware chunking reserves the key <c>headingPath</c> for its heading breadcrumb.
        /// </summary>
        /// <remarks>
        /// Immutable.
        /// Add an entry by copying the chunk: <c>chunk with { Metadata = chunk.Metadata.With("key", value) }</c>.
        /// Never null: assigning null stores <see cref="ChunkMetadata.Empty"/>.
        /// </remarks>
        public ChunkMetadata Metadata
        {
            get => _metadata;
            init => _metadata = value ?? ChunkMetadata.Empty;
        }

        private readonly ChunkMetadata _metadata = ChunkMetadata.Empty;
    }
}
