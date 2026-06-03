using FluentChunker.Dtos;

namespace FluentChunker
{
    public class FixedSizeChunker
    {
        private readonly SharpTokenizer _tokenizer;
        private readonly int _maxTokens;
        private readonly int _overlap;

        public FixedSizeChunker(SharpTokenizer tokenizer, int maxTokens, int overlap = 0)
        {
            _tokenizer = tokenizer;
            _maxTokens = maxTokens;
            _overlap = overlap;
        }

        public List<ChunkDto> Chunk(string text)
        {
            if (string.IsNullOrEmpty(text))
                return [];

            var allTokens = _tokenizer.Encode(text);

            if (allTokens.Count <= _maxTokens)
                return [new ChunkDto { Index = 0, Text = text, TokenCount = allTokens.Count }];

            var chunks = new List<ChunkDto>();
            var step = _maxTokens - _overlap;
            var index = 0;

            for (var start = 0; start < allTokens.Count; start += step)
            {
                var end = Math.Min(start + _maxTokens, allTokens.Count);
                var window = allTokens.GetRange(start, end - start);
                var decoded = _tokenizer.Decode(window);

                chunks.Add(new ChunkDto
                {
                    Index = index++,
                    Text = decoded,
                    TokenCount = window.Count
                });

                if (end == allTokens.Count)
                    break;
            }

            return chunks;
        }
    }
}
