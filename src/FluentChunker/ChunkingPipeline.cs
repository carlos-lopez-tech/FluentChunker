using FluentChunker.Contracts;
using FluentChunker.Tokenizers;
using System.ComponentModel;
using System.Text.Json;

namespace FluentChunker
{
    public record ChunkingPipeline
    {
        private readonly IChunker _chunker;
        private readonly Dictionary<string, JsonElement> _metadata;

        internal ChunkingPipeline(IChunker chunker, Dictionary<string, JsonElement> metadata)
        {
            _chunker = chunker;
            _metadata = metadata;
        }

        public IReadOnlyList<Chunk> Execute(string document) => Execute(document, null);

        public IReadOnlyList<Chunk> Execute(string document, Action<MetadataBuilder>? configureMetadata)
        {
            ArgumentNullException.ThrowIfNull(document);

            var chunks = _chunker.Chunk(document);
            var metadata = BuildEffectiveMetadata(configureMetadata);

            if (metadata is { Count: > 0 })
            {
                foreach (var chunk in chunks)
                {
                    foreach (var kvp in metadata)
                    {
                        chunk.Metadata[kvp.Key] = kvp.Value;
                    }
                }
            }

            return chunks;
        }

        private Dictionary<string, JsonElement> BuildEffectiveMetadata(Action<MetadataBuilder>? configureMetadata)
        {
            if (configureMetadata is null)
                return _metadata;

            var metadataBuilder = new MetadataBuilder();
            configureMetadata(metadataBuilder);
            var documentMetadata = metadataBuilder.Build();

            // merged locally so the pipeline stays immutable - document values must not leak into later Execute calls
            var merged = new Dictionary<string, JsonElement>(_metadata);
            foreach (var kvp in documentMetadata)
            {
                merged[kvp.Key] = kvp.Value;
            }

            return merged;
        }

        public class Builder : ITokenizerStep, ISplitterStep, IOptionalStep
        {
            private ITokenizer? _tokenizer;
            private IChunker? _chunker;
            private Action<MetadataBuilder>? _metadata;

            private Builder()
            {

            }

            /// <summary>
            /// Starts building a <see cref="ChunkingPipeline"/>. The builder guides you through the
            /// required steps in order: tokenizer, then splitter, then optional metadata.
            /// </summary>
            /// <returns>The first step, which requires a tokenizer.</returns>
            public static ITokenizerStep Initialize() => new Builder();

            public ISplitterStep UseTokenizer(ITokenizer tokenizer)
            {
                ArgumentNullException.ThrowIfNull(tokenizer);
                _tokenizer = tokenizer;
                return this;
            }

            public IOptionalStep UseSplitter(Action<Splitter.Builder> buildSplitterAction)
            {
                ArgumentNullException.ThrowIfNull(buildSplitterAction);

                var splitterBuilder = new Splitter.Builder(_tokenizer!);
                buildSplitterAction(splitterBuilder);
                _chunker = splitterBuilder.Build();

                return this;
            }

            public IOptionalStep AddMetadata(Action<MetadataBuilder> configure)
            {
                ArgumentNullException.ThrowIfNull(configure);
                _metadata += configure;
                return this;
            }

            public ChunkingPipeline Build()
            {
                ArgumentNullException.ThrowIfNull(_chunker);

                Dictionary<string, JsonElement> metadata = [];
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

    [EditorBrowsable(EditorBrowsableState.Never)]
    public interface ITokenizerStep
    {
        /// <summary>
        /// Sets the tokenizer used to count tokens when sizing chunks.
        /// </summary>
        /// <param name="tokenizer">The tokenizer implementation (e.g. a SharpToken or Microsoft.ML.Tokenizers adapter).</param>
        /// <returns>The next step, which requires a splitter.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="tokenizer"/> is null.</exception>
        ISplitterStep UseTokenizer(ITokenizer tokenizer);
    }

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

    [EditorBrowsable(EditorBrowsableState.Never)]
    public interface IOptionalStep
    {
        /// <summary>
        /// Adds pipeline-level metadata that is stamped onto every chunk of every document.
        /// Calling this multiple times composes: all configured entries are applied, with
        /// later registrations overwriting earlier ones on key collisions.
        /// </summary>
        /// <param name="configure">Populates the metadata entries.</param>
        /// <returns>The same step, for further optional configuration.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is null.</exception>
        IOptionalStep AddMetadata(Action<MetadataBuilder> configure);

        //IOptionalStep UseTransform(Action<Transform.Builder> buildTransformAction);
        //IOptionalStep UseEnrichment(Action<Enrichment.Builder> buildEnrichmentAction);

        /// <summary>
        /// Creates the immutable <see cref="ChunkingPipeline"/> from the configured steps.
        /// The pipeline can be reused across documents via <see cref="ChunkingPipeline.Execute(string)"/>.
        /// </summary>
        ChunkingPipeline Build();
    }
}
