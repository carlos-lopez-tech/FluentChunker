using FluentChunker.Contracts;
using FluentChunker.Tokenizers;

namespace FluentChunker.Samples.Demo
{
    internal static class Program
    {
        static void Main()
        {
            var content = File.ReadAllText(SampleFile("testfile.txt"));

            // The encoding FluentChunker ships, used by the OpenAI embedding models.
            // Chunks feeding a chat model want o200k_base, which needs its own Microsoft.ML.Tokenizers.Data package.
            const string EncodingName = "cl100k_base";

            var microsoftMLTokenizer = new MicrosoftMLTokenizerAdapter(EncodingName);

            // Example 1: Create Pipeline by using Fixed-Size Chunking.
            var pipeline1 = ChunkingPipeline.Builder.Initialize()
                .UseTokenizer(microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithFixedSizeChunking()
                    .WithMaxTokenSize(512)
                    .WithOverlap(50))
                .AddMetadata(meta => meta.Set("SourceType", "sample-files"))
                .Build();

            var chunks1 = pipeline1.Execute(content, meta =>
            {
                meta.Set("Source", "testfile.txt");
                meta.Set("Title", "Demo Test File");
                meta.Set("CreatedAt", DateTime.UtcNow);
            });

            Console.WriteLine(">>> Example 1: Fixed-Size Chunking <<<\n");
            PrintChunks(chunks1);


            // Example 2: Create Pipeline by using Sentence-Aware Chunking.
            var pipeline2 = ChunkingPipeline.Builder.Initialize()
                .UseTokenizer(microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithSentenceAwareChunking()
                    .WithMaxTokenSize(512)
                    .WithOverlap(50))
                .AddMetadata(meta => meta.Set("SourceType", "sample-files"))
                .Build();

            var chunks2 = pipeline2.Execute(content, meta =>
            {
                meta.Set("Source", "testfile.txt");
                meta.Set("Title", "Demo Test File");
                meta.Set("CreatedAt", DateTime.UtcNow);
            });

            Console.WriteLine(">>> Example 2: Sentence-Aware Chunking <<<\n");
            PrintChunks(chunks2);


            // Example 3: Create Pipeline by using Sentence-Aware Chunking with custom abbreviations.
            // "Fig.", "No." and "approx." are not in the built-in EN/DE/FR/ES set,
            // so without AddAbbreviations the splitter would falsely end a sentence after each of them.
            // Trailing dots are optional and matching is case-insensitive.
            var pipeline3 = ChunkingPipeline.Builder.Initialize()
                .UseTokenizer(microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithSentenceAwareChunking()
                    .WithMaxTokenSize(32)
                    .WithOverlap(8)
                    .AddAbbreviations("Fig.", "No.", "approx."))
                .AddMetadata(meta => meta.Set("SourceType", "inline-sample"))
                .Build();

            var abbreviationText =
                "The results are shown in Fig. 4 of the report. " +
                "Order No. 1234 took approx. three weeks to arrive. " +
                "A follow-up shipment is expected next month.";

            var chunks3 = pipeline3.Execute(abbreviationText, meta =>
            {
                meta.Set("Source", "inline");
                meta.Set("CreatedAt", DateTime.UtcNow);
            });

            Console.WriteLine(">>> Example 3: Sentence-Aware Chunking with custom abbreviations <<<\n");
            PrintChunks(chunks3);


            // Example 4: Create Pipeline by using Recursive Chunking.
            // Splits along paragraphs -> lines -> sentences -> token windows,
            // descending only for pieces that exceed the budget,
            // chunks never merge across a structural seam and overlap resets to zero there.
            var pipeline4 = ChunkingPipeline.Builder.Initialize()
                .UseTokenizer(microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithRecursiveChunking()
                    .WithMaxTokenSize(512)
                    .WithOverlap(50))
                .AddMetadata(meta => meta.Set("SourceType", "sample-files"))
                .Build();

            var chunks4 = pipeline4.Execute(content, meta =>
            {
                meta.Set("Source", "testfile.txt");
                meta.Set("Title", "Demo Test File");
                meta.Set("CreatedAt", DateTime.UtcNow);
            });

            Console.WriteLine(">>> Example 4: Recursive Chunking <<<\n");
            PrintChunks(chunks4);


            // Example 5: Recursive Chunking descending into an oversized paragraph.
            // A tight 30-token budget over the same archive/retrieval corpus:
            // the short title paragraph fits and stays its own chunk (strict seam - never merged with its oversized neighbor),
            // while the following paragraph alone blows the budget,
            // so the chunker descends paragraph -> lines -> sentences and packs whole sentences up to the budget (each sentence exceeds the 10-token overlap,
            // so no whole sentence is carried and OverlapCount stays 0).
            var pipeline5 = ChunkingPipeline.Builder.Initialize()
                .UseTokenizer(microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithRecursiveChunking()
                    .WithMaxTokenSize(30)
                    .WithOverlap(10))
                .AddMetadata(meta => meta.Set("SourceType", "inline-sample"))
                .Build();

            var descentText =
                "Chunking Test Document\n\n" +
                "Another participant demonstrated a visualization showing how overlapping chunks " +
                "sometimes preserved context more effectively than fixed windows without overlap. " +
                "Several researchers compared retrieval results generated from synthetic benchmark " +
                "documents against outputs produced from real company reports and customer support transcripts.";

            var chunks5 = pipeline5.Execute(descentText, meta => meta.Set("Source", "inline"));

            Console.WriteLine(">>> Example 5: Recursive Chunking (descent into an oversized paragraph) <<<\n");
            PrintChunks(chunks5);


            // Example 6: Structure-Aware Chunking over a Markdown document.
            // Headings are hard boundaries: a chunk never spans two sections,
            // and every chunk carries its heading path in Metadata["headingPath"].
            // Code fences, tables and lists stay whole while they fit, only an oversized paragraph descends to sentences.
            // This strategy has no overlap by design.
            var markdownContent = File.ReadAllText(SampleFile("testfile.md"));

            var pipeline6 = ChunkingPipeline.Builder.Initialize()
                .UseTokenizer(microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithStructureAwareChunking()
                    .WithMaxTokenSize(512))
                .AddMetadata(meta => meta.Set("SourceType", "sample-files"))
                .Build();

            var chunks6 = pipeline6.Execute(markdownContent, meta =>
            {
                meta.Set("Source", "testfile.md");
                meta.Set("Title", "Demo Test File (Markdown)");
                meta.Set("CreatedAt", DateTime.UtcNow);
            });

            Console.WriteLine(">>> Example 6: Structure-Aware Chunking (Markdown sections) <<<\n");
            PrintChunks(chunks6);

            // Keeps the window open when launched from a file explorer, but ReadKey throws whenever input is redirected, so skip it when piped or run in CI
            if (!Console.IsInputRedirected)
            {
                Console.ReadKey();
            }
        }

        // Path.Combine keeps the separator correct off Windows, and the folder name matches the on-disk casing, which matters on case-sensitive filesystems
        private static string SampleFile(string fileName) =>
            Path.Combine(AppContext.BaseDirectory, "testfiles", fileName);

        private static void PrintChunks(IReadOnlyList<Chunk> chunks)
        {
            foreach (var chunk in chunks)
            {
                Console.WriteLine($"Index: [{chunk.Index}] ({chunk.TokenCount} tokens) ({chunk.OverlapCount} overlap)");
                foreach (var kvp in chunk.Metadata)
                {
                    Console.WriteLine($"  {kvp.Key}: {kvp.Value}");
                }
                Console.WriteLine($"  Text:  {chunk.Text}");

                Console.WriteLine(string.Concat(Enumerable.Repeat("- ", 50)));
                Console.WriteLine("\n");
            }
            Console.WriteLine("\n");
        }
    }
}
