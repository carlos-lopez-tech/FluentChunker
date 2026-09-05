using System.Runtime.CompilerServices;
using System.Text;
using FluentChunker.Contracts;
using FluentChunker.Tokenizers;

namespace FluentChunker.Benchmarks
{
    /// <summary>
    /// Records every chunk boundary the shipped strategies produce over the committed corpus, and fails when a later build moves one.
    /// </summary>
    /// <remarks>
    /// A chunk boundary is part of the public contract even though no compiler enforces it: a caller who upgrades and gets different chunk text has to re-embed and re-index their corpus.
    /// That makes boundary movement indistinguishable from a breaking change to the person paying for it, and invisible in an API diff, which is what makes it dangerous.
    /// This is the mechanical check that a performance change did not move one, so it is the gate an optimization has to pass before it can ship in a minor version.
    /// </remarks>
    internal static class BoundaryDrift
    {
        // One canonical configuration rather than a sweep: the golden file has to stay small enough that a real diff is readable, and 512/64 is the configuration every other published number uses.
        // Adding a row here is all it takes to cover another budget, at the cost of a larger file to re-record.
        private static readonly (string Name, Action<Splitter.Builder> Configure)[] Configurations =
        [
            ("fixed-size 512/64", s => s.WithFixedSizeChunking().WithMaxTokenSize(512).WithOverlap(64)),
            ("sentence-aware 512/64", s => s.WithSentenceAwareChunking().WithMaxTokenSize(512).WithOverlap(64)),
            ("recursive 512/64", s => s.WithRecursiveChunking().WithMaxTokenSize(512).WithOverlap(64)),
            ("structure-aware 512", s => s.WithStructureAwareChunking().WithMaxTokenSize(512)),
        ];

        private const string CorpusName = "english-prose.md";

        /// <returns>The process exit code: non-zero when a boundary moved, so a CI step can gate on it.</returns>
        internal static async Task<int> RunAsync(bool record)
        {
            var document = CorpusLoader.Load(CorpusName);
            var tokenizer = new MicrosoftMLTokenizerAdapter("cl100k_base");

            var current = new Dictionary<string, List<Chunk>>();

            foreach (var (name, configure) in Configurations)
            {
                var pipeline = ChunkingPipeline.CreateBuilder()
                    .UseTokenizer(tokenizer)
                    .UseSplitter(configure)
                    .Build();

                current[name] = await pipeline.ChunkAsync(document).ToListAsync();
            }

            return record ? Record(current, document) : Verify(current, document);
        }

        private static int Record(Dictionary<string, List<Chunk>> current, string document)
        {
            var builder = new StringBuilder();

            builder.AppendLine("# FluentChunker chunk-boundary golden output.");
            builder.AppendLine("# Every line is one chunk: sourceStart,sourceLength,tokenCount.");
            builder.AppendLine("# A diff here is a breaking change for adopters, whatever the API diff says.");
            builder.AppendLine($"# Corpus: {CorpusName}, {document.Length} chars.");
            builder.AppendLine("# Re-record with: dotnet run --project benchmarks/FluentChunker.Benchmarks -c Release -- drift --record");

            foreach (var (name, _) in Configurations)
            {
                builder.AppendLine();
                builder.AppendLine($"[{name}]");

                foreach (var chunk in current[name])
                {
                    builder.AppendLine($"{chunk.SourceStart},{chunk.SourceLength},{chunk.TokenCount}");
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(GoldenPath())!);
            File.WriteAllText(GoldenPath(), builder.ToString());

            Console.WriteLine($"Recorded {current.Sum(entry => entry.Value.Count):N0} chunk boundaries to {GoldenPath()}");
            Console.WriteLine("Commit this file. A future run that differs from it is reporting a breaking change.");

            return 0;
        }

        private static int Verify(Dictionary<string, List<Chunk>> current, string document)
        {
            var path = GoldenPath();

            if (!File.Exists(path))
            {
                Console.WriteLine($"No golden file at {path}. Record one first with: drift --record");
                return 1;
            }

            var golden = Parse(File.ReadAllLines(path));
            var drifted = false;

            Console.WriteLine($"Corpus: {CorpusName}, {document.Length:N0} chars");
            Console.WriteLine();

            foreach (var (name, _) in Configurations)
            {
                if (!golden.TryGetValue(name, out var expected))
                {
                    Console.WriteLine($"  {name,-24} NOT IN GOLDEN FILE (re-record to add it)");
                    drifted = true;
                    continue;
                }

                var actual = current[name];
                var aligned = Math.Min(expected.Count, actual.Count);
                var moved = 0;
                var firstDifference = -1;

                for (var i = 0; i < aligned; i++)
                {
                    if (expected[i] == (actual[i].SourceStart, actual[i].SourceLength, actual[i].TokenCount))
                        continue;

                    moved++;
                    if (firstDifference < 0)
                        firstDifference = i;
                }

                if (moved == 0 && expected.Count == actual.Count)
                {
                    Console.WriteLine($"  {name,-24} unchanged ({actual.Count:N0} chunks)");
                    continue;
                }

                drifted = true;

                Console.WriteLine($"  {name,-24} DRIFTED");
                Console.WriteLine($"      chunks           {expected.Count:N0} -> {actual.Count:N0}");
                Console.WriteLine($"      chunks differing {moved:N0} of {aligned:N0} aligned positions");

                if (firstDifference >= 0)
                {
                    var (start, length, tokens) = expected[firstDifference];
                    var now = actual[firstDifference];

                    Console.WriteLine($"      first at index {firstDifference:N0}: "
                        + $"start {start} -> {now.SourceStart}, length {length} -> {now.SourceLength}, tokens {tokens} -> {now.TokenCount}");
                }
            }

            Console.WriteLine();

            if (!drifted)
            {
                Console.WriteLine("No boundary moved. This change is safe for a minor version.");
                return 0;
            }

            Console.WriteLine("Boundaries moved. Adopters would have to re-embed and re-index, so this belongs in a major version.");
            Console.WriteLine("If the move is intended, re-record the golden file in the same commit so the diff is reviewable.");

            return 1;
        }

        private static Dictionary<string, List<(long Start, int Length, int Tokens)>> Parse(string[] lines)
        {
            var result = new Dictionary<string, List<(long, int, int)>>();
            List<(long, int, int)>? section = null;

            foreach (var line in lines)
            {
                if (line.Length == 0 || line[0] == '#')
                    continue;

                if (line[0] == '[')
                {
                    section = [];
                    result[line[1..^1]] = section;
                    continue;
                }

                var parts = line.Split(',');
                section!.Add((long.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2])));
            }

            return result;
        }

        // Resolved from this file's compile-time path rather than the working directory, because `dotnet run` executes from the build output and the golden file belongs next to the source that produces it.
        // Unlike the corpus, this file is written as well as read, so it cannot be an embedded resource.
        private static string GoldenPath([CallerFilePath] string sourcePath = "") =>
            Path.Combine(Path.GetDirectoryName(sourcePath)!, "Golden", "boundaries.txt");
    }
}
