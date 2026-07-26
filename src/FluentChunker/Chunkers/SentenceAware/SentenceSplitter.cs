namespace FluentChunker.Chunkers.SentenceAware
{
    internal static class SentenceSplitter
    {
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
                var isNonSpacing = IsNonSpacingTerminator(text[i]);

                if (isNonSpacing || IsSpacingTerminator(text[i]))
                {
                    var end = i + 1;
                    while (end < text.Length && Closers.Contains(text[end]))
                        end++;

                    if (isNonSpacing || IsSentenceBoundary(text, i, end, sentenceStart, additionalAbbreviations))
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

        private static bool IsSentenceBoundary(string text, int terminatorIndex, int end, int sentenceStart, IReadOnlySet<string>? additionalAbbreviations)
        {
            if (end < text.Length && !char.IsWhiteSpace(text[end]))
                return false;

            var next = SkipWhitespace(text, end);
            if (next < text.Length && !CanStartSentence(text[next]))
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

        // Both terminator sets are tested for every character in the document, so they are comparisons rather than a scan over a constant string.
        // The string form cost about a third of the throughput of sentence-aware chunking on a one megabyte document.
        // U+061F Arabic question mark, U+06D4 Urdu full stop, U+0964 Devanagari danda, U+0965 Devanagari double danda.
        // Escapes rather than the characters themselves, which are invisible or indistinguishable from their neighbours in most editors and diffs.
        private static bool IsSpacingTerminator(char value) 
            => value is '.' or '!' or '?' or '\u061F' or '\u06D4' or '\u0964' or '\u0965';

        // Scripts written without spaces between sentences, so requiring whitespace after one of these would reject every real boundary.
        // None of them ever ends an abbreviation, so the caller skips the abbreviation guard for them as well.
        // U+3002 ideographic full stop, U+FF01 fullwidth exclamation mark, U+FF1F fullwidth question mark, U+FF0E fullwidth full stop.
        private static bool IsNonSpacingTerminator(char value) 
            => value is '\u3002' or '\uFF01' or '\uFF1F' or '\uFF0E';

        // A caseless letter counts as a sentence start because most of the world's scripts have no case at all,
        // and requiring an uppercase character rejected every boundary in Chinese, Japanese, Arabic, Hebrew, Thai and Devanagari text.
        // Latin lowercase is excluded by IsLower, so "He did it. and then left." still reads as one sentence.
        private static bool CanStartSentence(char value) =>
            char.IsUpper(value)
            || char.IsDigit(value)
            || Openers.Contains(value)
            || IsCaselessLetter(value);

        private static bool IsCaselessLetter(char value) =>
            char.IsLetter(value) && !char.IsUpper(value) && !char.IsLower(value);

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
