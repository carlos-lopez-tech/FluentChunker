namespace FluentChunker.Chunkers.SentenceAware
{
    internal readonly record struct SentenceSpan(int Start, int Length)
    {
        public int End => Start + Length;
    }
}
