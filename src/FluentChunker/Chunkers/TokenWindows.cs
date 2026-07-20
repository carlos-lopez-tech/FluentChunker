namespace FluentChunker.Chunkers
{
    // Window layouts over a token sequence, shared by FixedSizeChunker and the SentenceAwareChunker oversized-sentence fallback. 
    // Callers slice and decode the windows themselves.
    internal static class TokenWindows
    {
        // Fixed stride: windows of at most maxTokens, each starting `step` tokens after the previous one (step = maxTokens - overlap), 
        // so consecutive windows share `overlap` tokens.
        internal static IEnumerable<(int Start, int Length)> Strided(int totalTokens, int maxTokens, int step)
        {
            for (var start = 0; start < totalTokens; start += step)
            {
                var length = Math.Min(maxTokens, totalTokens - start);
                yield return (start, length);

                if (start + length == totalTokens)
                    yield break;
            }
        }

        // Even split: as few windows as fit within maxTokens, sized as evenly as possible (lengths differ by at most one) 
        // - avoids a tiny remainder window that a fixed stride would leave at the end.
        internal static IEnumerable<(int Start, int Length)> Balanced(int totalTokens, int maxTokens)
        {
            var windowCount = (totalTokens + maxTokens - 1) / maxTokens;
            var baseLength = totalTokens / windowCount;
            var extra = totalTokens % windowCount;

            var start = 0;
            for (var i = 0; i < windowCount; i++)
            {
                var length = baseLength + (i < extra ? 1 : 0);
                yield return (start, length);
                start += length;
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
