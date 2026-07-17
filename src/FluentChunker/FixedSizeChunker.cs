using FluentChunker.Contracts;

namespace FluentChunker
{
    public class FixedSizeChunker : IChunker
    {
        private readonly ITokenizer _tokenizer;
        private readonly int _maxTokens;
        private readonly int _overlap;

        public FixedSizeChunker(ITokenizer tokenizer, int maxTokens, int overlap)
        {
            _tokenizer = tokenizer;
            _maxTokens = maxTokens;
            _overlap = overlap;
        }

        public IReadOnlyList<Chunk> Chunk(string text)
        {
            if (string.IsNullOrEmpty(text))
                return [];

            var allTokens = _tokenizer.Encode(text);

            if (allTokens.Count <= _maxTokens)
                return [new Chunk { Index = 0, Text = text, TokenCount = allTokens.Count }];

            var chunks = new List<Chunk>();
            var step = _maxTokens - _overlap;
            var index = 0;

            for (var start = 0; start < allTokens.Count; start += step)
            {
                var end = Math.Min(start + _maxTokens, allTokens.Count);
                var length = end - start;

                var window = new int[length];

                for (var i = 0; i < length; i++)
                {
                    window[i] = allTokens[start + i];
                }

                var isLast = end == allTokens.Count;

                chunks.Add(new Chunk
                {
                    Index = index++,
                    Text = _tokenizer.Decode(window),
                    TokenCount = length,
                    OverlapCount = isLast ? 0 : _overlap
                });

                if (isLast)
                    break;
            }

            return chunks;
        }
    }
}
