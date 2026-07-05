using FluentChunker.Dtos;

namespace FluentChunker
{
    public record ChunkingPipeline
    {
        private readonly FixedSizeChunker _chunker;
        private readonly Dictionary<string, string>? _metadata;

        internal ChunkingPipeline(FixedSizeChunker chunker, Dictionary<string, string>? metadata)
        {
            _chunker = chunker;
            _metadata = metadata;
        }

        public IReadOnlyList<ChunkDto> Execute(string document)
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
            private Action<MetadataBuilder>? _metadataConfig;

            public Builder UseTokenizer(ITokenizer tokenizer)
            {
                _tokenizer = tokenizer;
                return this;
            }

            public Builder ByFixedSize(int maxTokens, int overlap = 0)
            {
                _maxTokens = maxTokens;
                _overlap = overlap;
                return this;
            }

            public Builder WithMetadata(Action<MetadataBuilder> configure)
            {
                _metadataConfig = configure;
                return this;
            }

            public ChunkingPipeline Build()
            {
                ArgumentNullException.ThrowIfNull(_tokenizer, nameof(_tokenizer));

                var chunker = new FixedSizeChunker(_tokenizer, _maxTokens, _overlap);

                Dictionary<string, string>? metadata = null;
                if (_metadataConfig is not null)
                {
                    var builder = new MetadataBuilder();
                    _metadataConfig(builder);
                    metadata = builder.Build();
                }

                return new ChunkingPipeline(chunker, metadata);
            }
        }
    }
}
