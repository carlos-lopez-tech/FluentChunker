namespace FluentChunker.Chunkers
{
    // One window over a token sequence, with the stretch of source text it covers.
    // CharStart is relative to the text that was encoded, so callers over a substring add their own base offset.
    internal readonly record struct TokenWindow(int Start, int Length, int Overlap, int CharStart, int CharLength);

    // Window layouts over a token sequence, shared by FixedSizeChunker and the oversized-unit fallbacks.
    // Every boundary is placed through TokenBoundary, so a window never begins or ends inside a character,
    // and each window's character extent follows from decoding it, which is exact because lengths add across legal cuts.
    internal static class TokenWindows
    {
        // Fixed stride: windows of at most maxTokens, each starting `overlap` tokens before the previous one ended,
        // so consecutive windows share that many tokens and their character extents overlap by the same text.
        // The overlap is reported per window rather than assumed, because snapping a boundary onto a legal cut can shift how much a window really received.
        internal static IEnumerable<TokenWindow> Strided(int totalTokens, int maxTokens, int overlap, TokenBoundary boundary)
        {
            var start = 0;
            var charStart = 0;
            var previousEnd = 0;

            while (start < totalTokens)
            {
                var end = boundary.SnapToSafeCut(Math.Min(start + maxTokens, totalTokens), start);
                var length = end - start;
                var charLength = boundary.CharLengthOf(start, length);

                // A short final window can be smaller than the overlap it was seeded with, in which case it is carried content entirely.
                yield return new TokenWindow(start, length, Math.Min(previousEnd - start, length), charStart, charLength);

                if (end >= totalTokens)
                    yield break;

                previousEnd = end;
                var previousCharEnd = charStart + charLength;

                // Step back by the overlap, then snap that start onto a legal cut too, so the shared region never begins inside a character.
                // The floor keeps the start strictly advancing, which is what guarantees this loop terminates.
                start = boundary.SnapToSafeCut(Math.Max(start + 1, end - overlap), start);

                // Walk the character offset back over the shared tokens rather than forward from the old start, so it stays anchored to the boundary just emitted.
                charStart = previousCharEnd - boundary.CharLengthOf(start, previousEnd - start);
            }
        }

        // Even split: windows sized as evenly as possible rather than filling each to the max and leaving a tiny remainder at the end, and never overlapping.
        // The target is recomputed from the tokens still left rather than from a split planned up front, because snapping pulls boundaries backward and a plan made before that has already gone stale.
        // Spreading each shortfall over the windows that follow is what keeps the tail from needing a window of its own.
        internal static IEnumerable<TokenWindow> Balanced(int totalTokens, int maxTokens, TokenBoundary boundary)
        {
            var start = 0;
            var charStart = 0;

            while (start < totalTokens)
            {
                var remaining = totalTokens - start;
                var remainingWindows = (remaining + maxTokens - 1) / maxTokens;
                var idealLength = (remaining + remainingWindows - 1) / remainingWindows;
                var end = boundary.SnapToSafeCut(Math.Max(start + 1, start + idealLength), start);
                var charLength = boundary.CharLengthOf(start, end - start);

                yield return new TokenWindow(start, end - start, Overlap: 0, charStart, charLength);

                start = end;
                charStart += charLength;
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
