namespace FluentChunker.Chunkers.SentenceAware
{
    internal static class SentenceSplitter
    {
        private const string Terminators = ".!?";
        private const string Closers = "\"'»«)]";
        private const string Openers = "¿¡«»„\"'([";

        // Matched case-insensitively against the word before a '.', without its trailing dot.
        private static readonly HashSet<string> DefaultAbbreviations = new(StringComparer.OrdinalIgnoreCase)
        {
            // EN
            "Dr", "Mr", "Mrs", "Ms", "Prof", "St", "vs", "etc", "e.g", "i.e",
            "Fig", "No", "approx", "Inc", "Jr", "Sr",
            // DE
            "Nr", "ca", "bzw", "usw", "z.B", "u.a", "Str", "Abs", "Hr", "Fr",
            // FR
            "M", "Mme", "Mlle", "p.ex", "c.-à-d", "réf",
            // ES
            "Sra", "Srta", "Ud", "Uds", "pág", "núm"
        };

        internal static IReadOnlyList<SentenceSpan> Split(string text, IReadOnlySet<string>? additionalAbbreviations = null)
        {
            if (string.IsNullOrWhiteSpace(text))
                return [];

            var spans = new List<SentenceSpan>();
            var sentenceStart = SkipWhitespace(text, 0);
            var i = sentenceStart;

            while (i < text.Length)
            {
                if (Terminators.Contains(text[i]))
                {
                    var end = i + 1;
                    while (end < text.Length && Closers.Contains(text[end]))
                        end++;

                    if (IsSentenceBoundary(text, i, end, sentenceStart, additionalAbbreviations))
                    {
                        spans.Add(new SentenceSpan(sentenceStart, end - sentenceStart));
                        sentenceStart = SkipWhitespace(text, end);
                        i = sentenceStart;
                        continue;
                    }
                }

                i++;
            }

            AddTrailingSentence(text, sentenceStart, spans);
            return spans;
        }

        private static bool IsSentenceBoundary(string text, int terminatorIndex, int end, int sentenceStart,
            IReadOnlySet<string>? additionalAbbreviations)
        {
            if (end < text.Length && !char.IsWhiteSpace(text[end]))
                return false;

            var next = SkipWhitespace(text, end);
            if (next < text.Length
                && !char.IsUpper(text[next])
                && !char.IsDigit(text[next])
                && !Openers.Contains(text[next]))
                return false;

            if (text[terminatorIndex] == '.')
            {
                var word = PrecedingWord(text, terminatorIndex, sentenceStart);
                if (word.Length > 0 &&
                    (DefaultAbbreviations.Contains(word) || ContainsIgnoreCase(additionalAbbreviations, word)))
                    return false;
            }

            return true;
        }

        private static bool ContainsIgnoreCase(IReadOnlySet<string>? set, string word)
        {
            if (set is null)
                return false;

            foreach (var entry in set)
            {
                if (string.Equals(entry, word, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string PrecedingWord(string text, int terminatorIndex, int sentenceStart)
        {
            var start = terminatorIndex;
            while (start > sentenceStart
                   && !char.IsWhiteSpace(text[start - 1])
                   && !Closers.Contains(text[start - 1])
                   && !Openers.Contains(text[start - 1]))
                start--;
            return text[start..terminatorIndex];
        }

        private static void AddTrailingSentence(string text, int sentenceStart, List<SentenceSpan> spans)
        {
            if (sentenceStart >= text.Length)
                return;

            var last = text.Length;
            while (last > sentenceStart && char.IsWhiteSpace(text[last - 1]))
                last--;

            if (last > sentenceStart)
                spans.Add(new SentenceSpan(sentenceStart, last - sentenceStart));
        }

        private static int SkipWhitespace(string text, int index)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
                index++;
            return index;
        }
    }
}
