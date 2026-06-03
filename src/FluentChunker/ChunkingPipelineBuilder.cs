namespace FluentChunker
{
    public class ChunkingPipelineBuilder
    {
        private SharpTokenizer? _tokenizer;
        private int _maxTokens;
        private int _overlap;
        private Action<MetadataBuilder>? _metadataConfig;

        public ChunkingPipelineBuilder UseTokenizer(SharpTokenizer tokenizer)
        {
            _tokenizer = tokenizer;
            return this;
        }

        public ChunkingPipelineBuilder ByFixedSize(int maxTokens, int overlap = 0)
        {
            _maxTokens = maxTokens;
            _overlap = overlap;
            return this;
        }

        public ChunkingPipelineBuilder WithMetadata(Action<MetadataBuilder> configure)
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
