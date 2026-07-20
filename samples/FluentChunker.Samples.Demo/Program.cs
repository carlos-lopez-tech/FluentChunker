using FluentChunker.Contracts;
using FluentChunker.Tokenizers;

namespace FluentChunker.Samples.Demo
{
    internal static class Program
    {
        static void Main()
        {
            var content = File.ReadAllText("Testfiles\\testfile.txt");
            const string EncodingName = "o200k_base";

            // Create an instance of MicrosoftMLTokenizerAdapter with the specified encoding
            var microsoftMLTokenizer = new MicrosoftMLTokenizerAdapter(EncodingName);

            // Example 1: Create Pipeline by using Fixed-Size Chunking
            var pipeline1 = ChunkingPipeline.Builder.Initialize()
                .UseTokenizer(microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithFixedSizeChunking()
                    .WithMaxTokenSize(512)
                    .WithOverlap(50))
                .WithMetadata(meta => meta.Set("SourceType", "sample-files"))
                .Build();

            var chunks1 = pipeline1.Execute(content, meta =>
            {
                meta.Set("Source", "testfile.txt");
                meta.Set("Title", "Demo Test File");
                meta.Set("CreatedAt", DateTime.UtcNow);
            });

            Console.WriteLine("Example 1: Fixed-Size Chunking\n");
            PrintChunks(chunks1);


            // Example 2: Create Pipeline by using Sentence-Aware Chunking
            var pipeline2 = ChunkingPipeline.Builder.Initialize()
                .UseTokenizer(microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithSentenceAwareChunking()
                    .WithMaxTokenSize(512)
                    .WithOverlap(50))
                .WithMetadata(meta => meta.Set("SourceType", "sample-files"))
                .Build();

            var chunks2 = pipeline2.Execute(content, meta =>
            {
                meta.Set("Source", "testfile.txt");
                meta.Set("Title", "Demo Test File");
                meta.Set("CreatedAt", DateTime.UtcNow);
            });

            Console.WriteLine("Example 2: Sentence-Aware Chunking\n");
            PrintChunks(chunks2);


            // Example 3: Sentence-Aware Chunking with custom abbreviations
            // "Fig.", "No." and "approx." are not in the built-in EN/DE/FR/ES set,
            // so without WithAbbreviations the splitter would falsely end a sentence
            // after each of them. Trailing dots are optional and matching is case-insensitive.
            var pipeline3 = ChunkingPipeline.Builder.Initialize()
                .UseTokenizer(microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithSentenceAwareChunking()
                    .WithMaxTokenSize(32)
                    .WithOverlap(8)
                    .WithAbbreviations("Fig.", "No.", "approx."))
                .WithMetadata(meta => meta.Set("SourceType", "inline-sample"))
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

            Console.WriteLine("Example 3: Sentence-Aware Chunking with custom abbreviations\n");
            PrintChunks(chunks3);
            Console.ReadKey();
        }

        private static void PrintChunks(IReadOnlyList<Chunk> chunks)
        {
            foreach (var chunk in chunks)
            {
                Console.WriteLine($"Index: [{chunk.Index}] ({chunk.TokenCount} tokens) ({chunk.OverlapCount} overlap)");
                foreach (var kvp in chunk.Metadata)
                {
                    Console.WriteLine($"  {kvp.Key}: {kvp.Value}");
                }
                Console.WriteLine($"  Text:  {chunk.Text[..Math.Min(80, chunk.Text.Length)]}...");

                Console.WriteLine(string.Concat(Enumerable.Repeat("- ", 50)));
                Console.WriteLine("\n");
            }
            Console.WriteLine("\n");
        }
    }
}
