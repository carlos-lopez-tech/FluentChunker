using FluentChunker.Dtos;

namespace FluentChunker
{
    public class ChunkingPipeline
    {
        private readonly FixedSizeChunker _chunker;
        private readonly Dictionary<string, string>? _metadata;

        internal ChunkingPipeline(FixedSizeChunker chunker, Dictionary<string, string>? metadata)
        {
            _chunker = chunker;
            _metadata = metadata;
        }

        public List<ChunkDto> Execute(string document)
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
    }
}
