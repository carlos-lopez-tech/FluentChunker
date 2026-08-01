namespace FluentChunker.Chunkers
{
    // Chunk indices are stamped at construction, which happens inside nested lazy iterators, and an iterator method cannot take a ref int parameter.
    // One counter instance per enumeration threads through those iterators instead, so indices stay sequential in document order across recursion and fallback seams.
    internal sealed class ChunkCounter
    {
        private int _next;

        internal int Next() => _next++;
    }
}
