namespace FluentChunker.Chunkers
{
    // CharStart is relative to the text that was encoded, so callers over a substring add their own base offset.
    internal readonly record struct TokenWindow(int Start, int Length, int Overlap, int CharStart, int CharLength);

    // Character extents follow from decoding each window, which is exact only because lengths add across legal cuts.
    // Both layouts terminate on token coverage but accumulate character coverage separately, so a tokenizer whose Decode does not reproduce its input lets the two diverge and silently truncates the tail.
    // expectedCharLength closes that, and is the length of the caller's own substring rather than of the whole document.
    internal static class TokenWindows
    {
        // The overlap is reported per window rather than assumed, because snapping a boundary onto a legal cut can shift how much a window really received.
        internal static IEnumerable<TokenWindow> Strided(
            int totalTokens, 
            int expectedCharLength, 
            int maxTokens, 
            int overlap, 
            TokenBoundary boundary)
        {
            var start = 0;
            var charStart = 0;
            var previousEnd = 0;

            while (start < totalTokens)
            {
                var end = boundary.SnapToSafeCut(Math.Min(start + maxTokens, totalTokens), start);
                var length = end - start;
                var charLength = boundary.CharLengthOf(start, length);

                // Fails here rather than in the caller's Substring, whose ArgumentOutOfRangeException names nothing useful.
                if (charStart + charLength > expectedCharLength)
                    throw RoundTripOverrun(charStart + charLength, expectedCharLength);

                // A short final window can be smaller than the overlap it was seeded with, leaving it carried content entirely.
                yield return new TokenWindow(start, length, Math.Min(previousEnd - start, length), charStart, charLength);

                if (end >= totalTokens)
                {
                    // Checked here rather than after the loop, where charStart has already been rewound by the overlap and is no longer the coverage end.
                    if (charStart + charLength != expectedCharLength)
                        throw RoundTripShortfall(charStart + charLength, expectedCharLength);

                    yield break;
                }

                previousEnd = end;
                var previousCharEnd = charStart + charLength;

                // The floor keeps the start strictly advancing, which is what guarantees this loop terminates.
                start = boundary.SnapToSafeCut(Math.Max(start + 1, end - overlap), start);

                // Walked back over the shared tokens rather than forward from the old start, so it stays anchored to the boundary just emitted.
                charStart = previousCharEnd - boundary.CharLengthOf(start, previousEnd - start);
            }
        }

        // The target is recomputed from the tokens still left rather than from a split planned up front, because snapping pulls boundaries backward and a plan made before that has gone stale.
        // Spreading each shortfall over the windows that follow is what keeps the tail from needing a window of its own.
        internal static IEnumerable<TokenWindow> Balanced(int totalTokens, int expectedCharLength, int maxTokens, TokenBoundary boundary)
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

                // See Strided: an extent past the end has to fail before a caller slices with it.
                if (charStart + charLength > expectedCharLength)
                    throw RoundTripOverrun(charStart + charLength, expectedCharLength);

                yield return new TokenWindow(start, end - start, Overlap: 0, charStart, charLength);

                start = end;
                charStart += charLength;
            }

            // This layout never rewinds, so charStart is the coverage end, and an empty sequence leaves both sides at zero without needing its own branch.
            if (charStart != expectedCharLength)
                throw RoundTripShortfall(charStart, expectedCharLength);
        }

        // Reported as a partial count rather than a total, because the sequence is abandoned mid-layout and the full decoded length is never measured.
        private static InvalidOperationException RoundTripOverrun(int actual, int expected) =>
            RoundTripFailure($"decoding its tokens reached {actual} characters, past the {expected} the encoded text contains");

        private static InvalidOperationException RoundTripShortfall(int actual, int expected) =>
            RoundTripFailure($"decoding the full token sequence produced {actual} characters where the encoded text has {expected}");

        // A normalisation that preserves length, such as lowercasing ASCII, keeps every extent exact, so this deliberately checks length rather than the full round-trip the tokenizer owes.
        // Do not strengthen it into a real round-trip comparison: that costs a decode of the whole text on every call, which is what TokenBoundary memoises to avoid.
        private static InvalidOperationException RoundTripFailure(string detail) =>
            new($"The tokenizer failed to round-trip the text it encoded: {detail}. " +
                "FluentChunker derives chunk positions from decoded token runs, so this drift would misplace chunk boundaries and either drop content or read past the end of the document. " +
                "Common causes are out-of-vocabulary words decoding as a fixed placeholder such as [UNK], whitespace being collapsed, and separators being inserted, as BERT does to CJK text unless IndividuallyTokenizeCjk is turned off. " +
                "Verify that Decode(Encode(text)) equals text over a representative sample of your corpus before chunking with this tokenizer.");

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
