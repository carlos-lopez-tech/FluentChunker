using FluentChunker.Chunkers.SentenceAware;

namespace FluentChunker.Chunkers.StructureAware
{
    internal sealed record Section
    {
        public required IReadOnlyList<string> HeadingPath { get; init; }
        public required IReadOnlyList<SectionBlock> Blocks { get; init; }
    }

    internal readonly record struct SectionBlock(SentenceSpan Span, bool IsAtomic);
}
