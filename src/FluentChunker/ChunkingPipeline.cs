using FluentChunker.Contracts;
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

            public IOptionalStep WithMetadata(Action<MetadataBuilder> configure)
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
        ISplitterStep UseTokenizer(ITokenizer tokenizer);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public interface ISplitterStep
    {
        IOptionalStep UseSplitter(Action<Splitter.Builder> buildSplitterAction);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public interface IOptionalStep
    {
        IOptionalStep WithMetadata(Action<MetadataBuilder> configure);

        //IOptionalStep UseTransform(Action<Transform.Builder> buildTransformAction);
        //IOptionalStep UseEnrichment(Action<Enrichment.Builder> buildEnrichmentAction);

        ChunkingPipeline Build();
    }
}
