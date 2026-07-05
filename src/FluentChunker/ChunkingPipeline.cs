using FluentChunker.Contracts;
using System.Text.Json;

namespace FluentChunker
{
    public record ChunkingPipeline
    {
        private readonly FixedSizeChunker _chunker;
        private readonly Dictionary<string, JsonElement> _metadata;

        internal ChunkingPipeline(FixedSizeChunker chunker, Dictionary<string, JsonElement> metadata)
        {
            _chunker = chunker;
            _metadata = metadata;
        }

        public IReadOnlyList<Chunk> Execute(string document)
        {
            var chunks = _chunker.Chunk(document);

            if (_metadata is { Count: > 0 })
            {
                foreach (var chunk in chunks)
                {
                    foreach (var kvp in _metadata)
                    {
                        chunk.Metadata[kvp.Key] = kvp.Value;
                    }
                }
            }

            return chunks;
        }

        public class Builder
        {
            private ITokenizer? _tokenizer;
            private int _maxTokens;
            private int _overlap;
            private Action<MetadataBuilder>? _metadata;

            public Builder UseTokenizer(ITokenizer tokenizer)
            {
                _tokenizer = tokenizer;
                return this;
            }

            public Builder ByFixedSize(int maxTokens)
            {
                _maxTokens = maxTokens;
                return this;
            }

            public Builder WithOverlap(int overlap)
            {
                _overlap = overlap;
                return this;
            }

            public Builder WithMetadata(Action<MetadataBuilder> configure)
            {
                _metadata = configure;
                return this;
            }

            public ChunkingPipeline Build()
            {
                ArgumentNullException.ThrowIfNull(_tokenizer);

                var chunker = new FixedSizeChunker(_tokenizer, _maxTokens, _overlap);

                Dictionary<string, JsonElement> metadata = [];
                if (_metadata is not null)
                {
                    var metadataBuilder = new MetadataBuilder();
                    _metadata(metadataBuilder);
                    metadata = metadataBuilder.Build();
                }

                return new ChunkingPipeline(chunker, metadata);
            }
        }
    }
}
