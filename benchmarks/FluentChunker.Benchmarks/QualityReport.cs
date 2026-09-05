using System.Diagnostics;
using System.Text;
using FluentChunker.Contracts;
using FluentChunker.Tokenizers;
using Microsoft.SemanticKernel.Text;

namespace FluentChunker.Benchmarks
{
    /// <summary>
    /// Measures the output properties a timing run cannot see: how tightly each library fills the token budget it was given, and how long the first chunk takes to arrive.
    /// </summary>
    /// <remarks>
    /// A chunker that overruns the budget is faster for a reason, so speed is only comparable alongside what each side did with the contract it claims.
    /// Nothing here gates: the invariants this used to enforce live in FluentChunker.Tests, where a failure names the strategy, the budget and the chunk instead of adding to a total.
    /// </remarks>
    internal static class QualityReport
    {
        private const int MaxTokens = 512;
        private const int Overlap = 64;

        internal static async Task RunAsync()
        {
            var document = CorpusLoader.Load("english-prose.md");
            var tokenizer = new MicrosoftMLTokenizerAdapter("cl100k_base");

            Console.WriteLine($"Corpus: {document.Length:N0} chars, {tokenizer.CountTokens(document):N0} cl100k tokens");
            Console.WriteLine($"Budget: {MaxTokens} tokens, overlap {Overlap}");
            Console.WriteLine();

            await ReportFluentAsync("FluentChunker fixed-size", document, tokenizer,
                s => s.WithFixedSizeChunking().WithMaxTokenSize(MaxTokens).WithOverlap(Overlap));

            await ReportFluentAsync("FluentChunker sentence-aware", document, tokenizer,
                s => s.WithSentenceAwareChunking().WithMaxTokenSize(MaxTokens).WithOverlap(Overlap));

            await ReportFluentAsync("FluentChunker recursive", document, tokenizer,
                s => s.WithRecursiveChunking().WithMaxTokenSize(MaxTokens).WithOverlap(Overlap));

            await ReportFluentAsync("FluentChunker structure-aware", document, tokenizer,
                s => s.WithStructureAwareChunking().WithMaxTokenSize(MaxTokens));

            ReportSemanticKernel(document, tokenizer);

            await ReportStreamingLatencyAsync(document, tokenizer);

            await ReportAdversarialAsync(tokenizer);
        }

        // Prose exercises the happy path both libraries were written for, so it cannot separate them.
        // These inputs can: text with no whitespace to split on, scripts whose characters span several tokens, and emoji whose surrogate pairs a naive character cut would break.
        private static async Task ReportAdversarialAsync(ITokenizer tokenizer)
        {
            const int budget = 64;

            (string Name, string Text)[] cases =
            [
                ("no whitespace at all", string.Concat(Enumerable.Repeat("abcdefghij", 500))),
                ("CJK, no spaces", string.Concat(Enumerable.Repeat("这是一个测试文本", 400))),
                ("emoji, surrogate pairs", string.Concat(Enumerable.Repeat("🙂🎉👨‍👩‍👧‍👦", 300))),
                ("mixed scripts", string.Concat(Enumerable.Repeat("hello مرحبا שלום नमस्ते สวัสดี ", 200))),
            ];

            Console.WriteLine($"Adversarial inputs, budget {budget} tokens");
            Console.WriteLine();

            foreach (var (name, text) in cases)
            {
                var pipeline = ChunkingPipeline.CreateBuilder()
                    .UseTokenizer(tokenizer)
                    .UseSplitter(s => s.WithRecursiveChunking().WithMaxTokenSize(budget))
                    .Build();

                var chunks = await pipeline.ChunkAsync(text).ToListAsync();

                var fluentMax = chunks.Count == 0 ? 0 : chunks.Max(c => tokenizer.CountTokens(c.Text));
                var fluentOverruns = chunks.Count(c => tokenizer.CountTokens(c.Text) > budget);

                var concatenated = string.Concat(chunks.Select(c => c.SliceOf(text)));
                var rebuilt = concatenated == text
                    ? "exact"
                    : Strip(concatenated) != Strip(text)
                        ? "CONTENT LOST"
                        : $"content complete, {text.Length - concatenated.Length} whitespace chars dropped between units";

                var skLines = TextChunker.SplitPlainTextLines(text, budget, tokenizer.CountTokens);
                var skChunks = TextChunker.SplitPlainTextParagraphs(skLines, budget, tokenCounter: tokenizer.CountTokens);
                var skMax = skChunks.Count == 0 ? 0 : skChunks.Max(tokenizer.CountTokens);
                var skOverruns = skChunks.Count(c => tokenizer.CountTokens(c) > budget);

                Console.WriteLine($"  {name}");
                Console.WriteLine($"    FluentChunker    {chunks.Count,4} chunks, max {fluentMax,4} tokens, {fluentOverruns} overruns, rebuild: {rebuilt}");
                Console.WriteLine($"    SemanticKernel   {skChunks.Count,4} chunks, max {skMax,4} tokens, {skOverruns} overruns");
                Console.WriteLine();
            }
        }

        private static async Task ReportFluentAsync(
            string label,
            string document,
            ITokenizer tokenizer,
            Action<Splitter.Builder> splitter)
        {
            var pipeline = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(tokenizer)
                .UseSplitter(splitter)
                .Build();

            var chunks = await pipeline.ChunkAsync(document).ToListAsync();

            var overruns = 0;
            var maxObserved = 0;

            foreach (var chunk in chunks)
            {
                // Re-measure rather than trust the reported count, because what is comparable against another library is the size of the text itself.
                var measured = tokenizer.CountTokens(chunk.Text);

                if (measured > MaxTokens)
                    overruns++;

                maxObserved = Math.Max(maxObserved, measured);
            }

            Console.WriteLine(label);
            Console.WriteLine($"  chunks                 {chunks.Count:N0}");
            Console.WriteLine($"  max tokens observed    {maxObserved} (budget {MaxTokens})");
            Console.WriteLine($"  budget overruns        {overruns}");
            Console.WriteLine($"  coverage               {Coverage(chunks, document)}");
            Console.WriteLine();
        }

        // Concatenating the chunks with their overlap removed rebuilds the document, which is how much of it a strategy actually hands back.
        private static string Coverage(IReadOnlyList<Chunk> chunks, string document)
        {
            var rebuilt = new StringBuilder();
            var covered = 0L;

            foreach (var chunk in chunks)
            {
                if (chunk.SourceStart < 0)
                    return "not tracked";

                if (chunk.SourceStart >= covered)
                {
                    rebuilt.Append(document, (int)chunk.SourceStart, chunk.SourceLength);
                    covered = chunk.SourceStart + chunk.SourceLength;
                    continue;
                }

                var fresh = chunk.SourceStart + chunk.SourceLength - covered;
                if (fresh <= 0)
                    continue;

                rebuilt.Append(document, (int)covered, (int)fresh);
                covered += fresh;
            }

            var text = rebuilt.ToString();

            // Whitespace between structural units is deliberately dropped by the packing strategies, so compare on non-whitespace content.
            return Strip(text) == Strip(document)
                ? "complete (all non-whitespace content preserved)"
                : $"INCOMPLETE ({Strip(text).Length:N0} of {Strip(document).Length:N0} non-whitespace chars)";
        }

        private static string Strip(string text)
        {
            var builder = new StringBuilder(text.Length);

            foreach (var c in text)
            {
                if (!char.IsWhiteSpace(c))
                    builder.Append(c);
            }

            return builder.ToString();
        }

        private static void ReportSemanticKernel(string document, ITokenizer tokenizer)
        {
            var lines = TextChunker.SplitPlainTextLines(document, MaxTokens, tokenizer.CountTokens);
            var chunks = TextChunker.SplitPlainTextParagraphs(lines, MaxTokens, Overlap, tokenCounter: tokenizer.CountTokens);

            var overruns = 0;
            var maxObserved = 0;

            foreach (var chunk in chunks)
            {
                var measured = tokenizer.CountTokens(chunk);

                if (measured > MaxTokens)
                    overruns++;

                maxObserved = Math.Max(maxObserved, measured);
            }

            Console.WriteLine("Semantic Kernel TextChunker");
            Console.WriteLine($"  chunks                 {chunks.Count:N0}");
            Console.WriteLine($"  max tokens observed    {maxObserved} (budget {MaxTokens})");
            Console.WriteLine($"  budget overruns        {overruns}");
            Console.WriteLine("  coverage               n/a (returns bare strings, no offsets)");
            Console.WriteLine();
        }

        // The streaming claim is about when the first chunk arrives, not how long the whole document takes, so it needs its own measurement.
        private static async Task ReportStreamingLatencyAsync(string document, ITokenizer tokenizer)
        {
            var pipeline = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(tokenizer)
                .UseSplitter(s => s.WithRecursiveChunking().WithMaxTokenSize(MaxTokens).WithOverlap(Overlap))
                .Build();

            var clock = Stopwatch.StartNew();
            var first = TimeSpan.Zero;
            var count = 0;

            await foreach (var chunk in pipeline.ChunkAsync(document))
            {
                if (count++ == 0)
                    first = clock.Elapsed;

                _ = chunk;
            }

            clock.Stop();

            Console.WriteLine("Streaming latency, recursive, in-memory document");
            Console.WriteLine($"  time to first chunk    {first.TotalMilliseconds:N1} ms");
            Console.WriteLine($"  time to all {count:N0}      {clock.Elapsed.TotalMilliseconds:N1} ms");
            Console.WriteLine();

            using var reader = new StringReader(document);

            clock.Restart();
            var streamedFirst = TimeSpan.Zero;
            var streamedCount = 0;

            await foreach (var chunk in pipeline.ChunkAsync(reader))
            {
                if (streamedCount++ == 0)
                    streamedFirst = clock.Elapsed;

                _ = chunk;
            }

            clock.Stop();

            Console.WriteLine("Streaming latency, recursive, TextReader (bounded memory)");
            Console.WriteLine($"  time to first chunk    {streamedFirst.TotalMilliseconds:N1} ms");
            Console.WriteLine($"  time to all {streamedCount:N0}      {clock.Elapsed.TotalMilliseconds:N1} ms");
            Console.WriteLine();
        }
    }
}
