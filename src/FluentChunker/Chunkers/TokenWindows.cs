namespace FluentChunker.Chunkers
{
    // Window layouts over a token sequence, shared by FixedSizeChunker and the oversized-unit fallbacks.
    // Every boundary is placed through TokenBoundary, so a window never begins or ends inside a character.
    // Callers slice and decode the windows themselves.
    internal static class TokenWindows
    {
        // Fixed stride: windows of at most maxTokens, each starting `overlap` tokens before the previous one ended,
        // so consecutive windows share that many tokens.
        // The overlap is reported per window rather than assumed, because snapping a boundary onto a legal cut can shift how much a window really received.
        internal static IEnumerable<(int Start, int Length, int Overlap)> Strided(
            int totalTokens, int maxTokens, int overlapTokens, TokenBoundary boundary)
        {
            var start = 0;
            var previousEnd = 0;

            while (start < totalTokens)
            {
                var end = boundary.SnapToSafeCut(Math.Min(start + maxTokens, totalTokens), start);
                var length = end - start;

                // A short final window can be smaller than the overlap it was seeded with, in which case it is carried content entirely.
                yield return (start, length, Math.Min(previousEnd - start, length));

                if (end >= totalTokens)
                    yield break;

                previousEnd = end;

                // Step back by the overlap, then snap that start onto a legal cut too, so the shared region never begins inside a character.
                // The floor keeps the start strictly advancing, which is what guarantees this loop terminates.
                start = boundary.SnapToSafeCut(Math.Max(start + 1, end - overlapTokens), start);
            }
        }

        // Even split: windows sized as evenly as possible rather than filling each to the max and leaving a tiny remainder at the end, and never overlapping.
        // The target is recomputed from the tokens still left rather than from a split planned up front, because snapping pulls boundaries backward and a plan made before that has already gone stale.
        // Spreading each shortfall over the windows that follow is what keeps the tail from needing a window of its own.
        internal static IEnumerable<(int Start, int Length)> Balanced(int totalTokens, int maxTokens, TokenBoundary boundary)
        {
            var start = 0;

            while (start < totalTokens)
            {
                var remaining = totalTokens - start;
                var remainingWindows = (remaining + maxTokens - 1) / maxTokens;
                var idealLength = (remaining + remainingWindows - 1) / remainingWindows;
                var end = boundary.SnapToSafeCut(Math.Max(start + 1, start + idealLength), start);

                yield return (start, end - start);
                start = end;
            }
        }

        internal static int[] Slice(IReadOnlyList<int> tokens, int start, int length)
        {
            var window = new int[length];

            for (var i = 0; i < length; i++)
            {
                window[i] = tokens[start + i];
            }

            return window;
        }
    }
}
