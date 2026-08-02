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

            return reader.ReadToEnd();
        }
    }
}
