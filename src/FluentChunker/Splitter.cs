namespace FluentChunker
{
    public static class Splitter
    {
        public class Builder
        {
            private readonly ITokenizer _tokenizer;
            private Func<IChunker>? _chunkerFactory;
            private int _maxTokens;
            private int _overlap;

            internal Builder(ITokenizer tokenizer) => _tokenizer = tokenizer;

            public ChunkingStrategyStep WithFixedSizeChunking()
            {
                _chunkerFactory = () => new FixedSizeChunker(_tokenizer, _maxTokens, _overlap);
                return new ChunkingStrategyStep(this);
            }

            //public ChunkingStrategyStep WithStructureAwareChunking()
            //{
            //    _chunkerFactory = () => new StructureAwareChunker(_tokenizer, _maxTokens, _overlap);
            //    return new ChunkingStrategyStep(this);
            //}

            internal IChunker Build()
            {
                if (_chunkerFactory is null)
                    throw new InvalidOperationException("A chunking strategy is required — call WithFixedSizeChunking().");
                if (_maxTokens <= 0)
                    throw new InvalidOperationException("A max token size is required — call WithMaxTokenSize().");

                return _chunkerFactory();
            }

            public class ChunkingStrategyStep
            {
                private readonly Builder _builder;

                internal ChunkingStrategyStep(Builder builder) => _builder = builder;

                public MaxTokenSizeStep WithMaxTokenSize(int maxTokens)
                {
                    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTokens);

                    _builder._maxTokens = maxTokens;
                    return new MaxTokenSizeStep(_builder);
                }
            }

            public class MaxTokenSizeStep
            {
                private readonly Builder _builder;

                internal MaxTokenSizeStep(Builder builder) => _builder = builder;

                public MaxTokenSizeStep WithOverlap(int overlap)
                {
                    ArgumentOutOfRangeException.ThrowIfNegative(overlap);
                    // overlap == maxTokens would make FixedSizeChunker's step size zero — an infinite loop
                    ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(overlap, _builder._maxTokens);

                    _builder._overlap = overlap;
                    return this;
                }
            }
        }
    }
}
