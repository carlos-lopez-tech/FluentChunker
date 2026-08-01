using System.Runtime.CompilerServices;
using FluentChunker.Contracts;

namespace FluentChunker.Chunkers
{
    // The async facade every chunker shares.
    // Chunking is CPU-bound and the cores are synchronous iterators, so MoveNextAsync completing synchronously is the designed behaviour, never work to push onto the thread pool.
    internal static class ChunkStream
    {
        // For a core that produces chunks without observing the token itself, which is every core whose work between chunks is too small to interrupt.
        internal static IAsyncEnumerable<Chunk> Over(IEnumerable<Chunk> chunks, CancellationToken cancellationToken) =>
            Over(_ => chunks, cancellationToken);

        // EnumeratorCancellation merges the token the chunker received with one supplied later through WithCancellation, so both ways of cancelling compose.
        // The core is taken as a factory rather than a sequence because that merged token only exists once enumeration starts, and a core that polls internally has to be handed it rather than the one captured at call time.
        // The leading check makes a pre-cancelled token throw before the first chunk is cut, even when the core would produce nothing, and the in-loop check stops a cancelled enumeration before the next chunk.
        internal static async IAsyncEnumerable<Chunk> Over(Func<CancellationToken, IEnumerable<Chunk>> core, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var chunk in core(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return chunk;
            }
        }
    }
}
