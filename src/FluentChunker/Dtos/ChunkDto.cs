namespace FluentChunker.Dtos
{
    public record ChunkDto
    {
        public required int Index { get; init; }
        public required string Text { get; init; }
        public required int TokenCount { get; init; }
        public Dictionary<string, string> Metadata { get; init; } = [];
    }
}
