using System.Reflection;

namespace FluentChunker.Tests
{
    // Read once and cached, because every sweep case runs over the same 1.2 MB document and re-reading it per case would dominate the fixture.
    internal static class SweepCorpus
    {
        private static readonly Lazy<string> EnglishProse = new(() => Load("english-prose.md"));

        internal static string Prose => EnglishProse.Value;

        private static string Load(string fileName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resource = assembly.GetManifestResourceNames().Single(name => name.EndsWith(fileName, StringComparison.Ordinal));

            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);

            // Normalise to LF so a CRLF checkout cannot change the character offsets the sweeps assert against.
            return reader.ReadToEnd().Replace("\r\n", "\n");
        }
    }
}
