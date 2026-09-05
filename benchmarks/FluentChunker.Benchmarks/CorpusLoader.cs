using System.Reflection;

namespace FluentChunker.Benchmarks
{
    // Read through the manifest rather than from disk, because BenchmarkDotNet's generated child processes do not run in this project's output directory.
    internal static class CorpusLoader
    {
        internal static string Load(string fileName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resource = assembly.GetManifestResourceNames().Single(name => name.EndsWith(fileName, StringComparison.Ordinal));

            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);

            // Normalise to LF because the golden boundary file records absolute character offsets, and a CRLF checkout shifts every one of them by a character per preceding line.
            return reader.ReadToEnd().Replace("\r\n", "\n");
        }

        /// <summary>
        /// Slices the corpus into documents of roughly <paramref name="approximateChars"/> each, stopping once <paramref name="totalChars"/> have been handed out.
        /// </summary>
        /// <remarks>
        /// Each cut runs on to the next line break, so no document ends mid-word and the sentence and structure strategies still see realistic input.
        /// </remarks>
        internal static IReadOnlyList<string> LoadDocuments(string fileName, int approximateChars, int totalChars)
        {
            var corpus = Load(fileName);
            var documents = new List<string>();
            var position = 0;

            while (position < corpus.Length && position < totalChars)
            {
                var target = position + approximateChars;

                if (target >= corpus.Length)
                {
                    documents.Add(corpus[position..]);
                    break;
                }

                var lineBreak = corpus.IndexOf('\n', target);
                var end = lineBreak < 0 ? corpus.Length : lineBreak + 1;

                documents.Add(corpus[position..end]);
                position = end;
            }

            return documents;
        }
    }
}
