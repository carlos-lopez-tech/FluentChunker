using System.Runtime.CompilerServices;
using FluentChunker.Contracts;

namespace FluentChunker.Chunkers
{
    // The segment-consumption loop every streaming chunker shares: fill, peek, hand the segment to a strategy-specific handler, yield what it returns, consume what it consumed, repeat.
    // What differs per strategy is entirely inside HandleSegment: where a piece is cut, whether a segment degrades (a capacity hard cut, a forced descent) instead of delegating to the in-memory core, and whether a segment is skipped without emitting.
    // None of that is visible here, which is what lets fixed-size, sentence-aware and recursive share this loop without it knowing any strategy's boundary or degradation rules.
    internal static class StreamingChunkLoop
    {
        // One counter instance serves the whole enumeration, so indices stay sequential across segments, and the token passed in is the merged one this loop observes.
        // Consuming fewer characters than the segment holds is how a handler carries an incomplete trailing unit into the next fill.
        internal delegate (int Consumed, IEnumerable<Chunk> Chunks) SegmentHandler(
            string segment, bool endOfStream, long basePosition, ChunkCounter counter, CancellationToken cancellationToken);

        internal static async IAsyncEnumerable<Chunk> Run(
            TextReader reader,
            int segmentCapacity,
            long streamBasePosition,
            SegmentHandler handleSegment,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The using returns the pooled buffer when the enumerator is disposed, which await foreach always does.
            using var window = new SlidingTextWindow(reader, segmentCapacity, streamBasePosition);
            var counter = new ChunkCounter();

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await window.FillAsync(cancellationToken).ConfigureAwait(false);

                var segment = window.PeekSegment();
                if (segment.Length == 0)
                    yield break;

                var (consumed, chunks) = handleSegment(segment, window.EndOfStream, window.BasePosition, counter, cancellationToken);

                foreach (var chunk in chunks)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return chunk;
                }

                window.Consume(consumed);
            }
        }

        // The remap every delegating handler needs: index continues the whole-stream counter, and SourceStart shifts from piece-relative to the window's absolute position.
        // Lazy, so a caller that stops enumerating early still stops the in-memory core early too.
        internal static IEnumerable<Chunk> RemapPiece(IEnumerable<Chunk> chunks, ChunkCounter counter, long basePosition)
        {
            foreach (var chunk in chunks)
                yield return chunk with { Index = counter.Next(), SourceStart = basePosition + chunk.SourceStart };
        }
    }
}
