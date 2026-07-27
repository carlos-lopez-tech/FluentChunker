using FluentChunker.Contracts;
using FluentChunker.Tokenizers;
using System.ComponentModel;
using System.Text.Json;

namespace FluentChunker
{
    /// <summary>
    /// An immutable, reusable chunking configuration.
    /// Build one with <see cref="CreateBuilder"/>, then run it against as many documents as needed.
    /// </summary>
    public sealed class ChunkingPipeline
    {
        private readonly IChunker _chunker;

        // Prebuilt once: with no document metadata, every stamped chunk shares this instance.
        private readonly ChunkMetadata _pipelineMetadata;

        // Takes ownership of the builder's dictionary, which it hands over and never touches again.
        internal ChunkingPipeline(IChunker chunker, Dictionary<string, JsonElement> metadata)
        {
            _chunker = chunker;
            _pipelineMetadata = metadata.Count == 0 ? ChunkMetadata.Empty : new ChunkMetadata(metadata);
        }

        /// <summary>
        /// Starts building a pipeline.
        /// The returned step interfaces expose only the calls valid at each stage, in order: tokenizer, then splitter, then optional metadata.
        /// A builder can be built more than once, and each result is independent.
        /// </summary>
        /// <returns>The first step, which requires a tokenizer.</returns>
        public static ITokenizerStep CreateBuilder() => new Builder();

        /// <summary>
        /// Splits a document, stamping every chunk with the pipeline-level metadata.
        /// </summary>
        /// <param name="document">The document to split.</param>
        /// <returns>The chunks, in document order.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
        public IReadOnlyList<Chunk> Chunk(string document) => Chunk(document, null);

        /// <summary>
        /// Splits a document, adding metadata that applies only to this call.
        /// Document values overwrite pipeline values on key collisions and never leak into later calls.
        /// </summary>
        /// <param name="document">The document to split.</param>
        /// <param name="configureMetadata">Populates the document-level metadata, or null for none.</param>
        /// <returns>The chunks, in document order.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
        public IReadOnlyList<Chunk> Chunk(string document, Action<MetadataBuilder>? configureMetadata)
        {
            ArgumentNullException.ThrowIfNull(document);

            var chunks = _chunker.Chunk(document);

            // Built once per call and shared by every chunk that carries no chunker metadata, so stamping allocates one container instead of one dictionary per chunk.
            var effectiveMetadata = BuildEffectiveMetadata(configureMetadata);

            if (effectiveMetadata.Count == 0)
                return chunks;

            var stamped = new Chunk[chunks.Count];
            for (var i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                stamped[i] = chunk with
                {
                    Metadata = chunk.Metadata.Count == 0 ? effectiveMetadata : Merge(chunk.Metadata, effectiveMetadata)
                };
            }

            return stamped;
        }

        // Document values overwrite pipeline values, and both overwrite chunker values such as headingPath,
        // the same precedence stamping had when it wrote into the chunk's dictionary in place.
        private static ChunkMetadata Merge(ChunkMetadata chunkerMetadata, ChunkMetadata effectiveMetadata)
        {
            var merged = new Dictionary<string, JsonElement>(chunkerMetadata.Count + effectiveMetadata.Count, StringComparer.Ordinal);
            foreach (var kvp in chunkerMetadata)
            {
                merged[kvp.Key] = kvp.Value;
            }
            foreach (var kvp in effectiveMetadata)
            {
                merged[kvp.Key] = kvp.Value;
            }

            return new ChunkMetadata(merged);
        }

        private ChunkMetadata BuildEffectiveMetadata(Action<MetadataBuilder>? configureMetadata)
        {
            if (configureMetadata is null)
                return _pipelineMetadata;

            var metadataBuilder = new MetadataBuilder();
            configureMetadata(metadataBuilder);
            var documentMetadata = metadataBuilder.Build();

            // Copied rather than mutated, so the pipeline stays immutable and reusable across documents
            var merged = new Dictionary<string, JsonElement>(_pipelineMetadata, StringComparer.Ordinal);
            foreach (var kvp in documentMetadata)
            {
                merged[kvp.Key] = kvp.Value;
            }

            return new ChunkMetadata(merged);
        }

        private sealed class Builder : ITokenizerStep, ISplitterStep, IOptionalStep
        {
            private ITokenizer? _tokenizer;
            private IChunker? _chunker;
            private Action<MetadataBuilder>? _metadata;

            /// <inheritdoc/>
            public ISplitterStep UseTokenizer(ITokenizer tokenizer)
            {
                ArgumentNullException.ThrowIfNull(tokenizer);
                _tokenizer = tokenizer;
                return this;
            }

            /// <inheritdoc/>
            public IOptionalStep UseSplitter(Action<Splitter.Builder> buildSplitterAction)
            {
                ArgumentNullException.ThrowIfNull(buildSplitterAction);

                var splitterBuilder = new Splitter.Builder(_tokenizer!);
                buildSplitterAction(splitterBuilder);
                _chunker = splitterBuilder.Build();

                return this;
            }

            /// <inheritdoc/>
            public IOptionalStep AddMetadata(Action<MetadataBuilder> configure)
            {
                ArgumentNullException.ThrowIfNull(configure);
                _metadata += configure;
                return this;
            }

            /// <inheritdoc/>
            public ChunkingPipeline Build()
            {
                ArgumentNullException.ThrowIfNull(_chunker);

                Dictionary<string, JsonElement> metadata = new(StringComparer.Ordinal);
                if (_metadata is not null)
                {
                    var metadataBuilder = new MetadataBuilder();
                    _metadata(metadataBuilder);
                    metadata = metadataBuilder.Build();
                }

                return new ChunkingPipeline(_chunker, metadata);
            }
        }
    }

    /// <summary>
    /// Builder stage requiring a tokenizer.
    /// Hidden from IntelliSense: reach it through <see cref="ChunkingPipeline.CreateBuilder"/> rather than naming it.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public interface ITokenizerStep
    {
        /// <summary>
        /// Sets the tokenizer used to count tokens when sizing chunks.
        /// </summary>
        /// <param name="tokenizer">The tokenizer implementation (e.g. <see cref="Tokenizers.MicrosoftMLTokenizerAdapter"/> or a custom <see cref="ITokenizer"/>).</param>
        /// <returns>The next step, which requires a splitter.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="tokenizer"/> is null.</exception>
        ISplitterStep UseTokenizer(ITokenizer tokenizer);
    }

    /// <summary>
    /// Builder stage requiring a splitter, reached once a tokenizer is set.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public interface ISplitterStep
    {
        /// <summary>
        /// Configures how documents are split into chunks: the chunking strategy,
        /// maximum chunk size in tokens, and optional overlap.
        /// </summary>
        /// <param name="buildSplitterAction">Configures the splitter, e.g.
        /// <c>s => s.WithSentenceAwareChunking().WithMaxTokenSize(200).WithOverlap(20)</c>.</param>
        /// <returns>The optional step, from which the pipeline can be built.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="buildSplitterAction"/> is null.</exception>
        IOptionalStep UseSplitter(Action<Splitter.Builder> buildSplitterAction);
    }

    /// <summary>
    /// Final builder stage, reached once a splitter is configured.
    /// Everything here is optional except <see cref="Build"/>.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public interface IOptionalStep
    {
        /// <summary>
        /// Adds pipeline-level metadata that is stamped onto every chunk of every document.
        /// Calling this multiple times composes: all configured entries are applied, with later registrations overwriting earlier ones on key collisions.
        /// </summary>
        /// <param name="configure">Populates the metadata entries.</param>
        /// <returns>The same step, for further optional configuration.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is null.</exception>
        IOptionalStep AddMetadata(Action<MetadataBuilder> configure);

        /// <summary>
        /// Creates the immutable <see cref="ChunkingPipeline"/> from the configured steps.
        /// The pipeline can be reused across documents via <see cref="ChunkingPipeline.Chunk(string)"/>.
        /// </summary>
        ChunkingPipeline Build();
    }
}
