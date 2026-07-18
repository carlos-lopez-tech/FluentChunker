using FluentChunker.Contracts;

namespace FluentChunker.Samples.Demo
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var content = File.ReadAllText("Testfiles\\testfile.txt");
            const string EncodingName = "o200k_base";

            // Example 1: Create Pipeline by using a tokenizer with MicrosoftMLTokenizerAdapter
            var microsoftMLTokenizer = new MicrosoftMLTokenizerAdapter(EncodingName);

            var pipeline = ChunkingPipeline.Builder.Initialize()
                .UseTokenizer(microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithFixedSizeChunking()
                    .WithMaxTokenSize(500)
                    .WithOverlap(50))
                .WithMetadata(meta => meta.Set("SourceType", "sample-files"))
                .Build();

            var chunks1 = pipeline.Execute(content, meta =>
            {
                meta.Set("Source", "testfile.txt");
                meta.Set("Title", "Demo Test File");
                meta.Set("CreatedAt", DateTime.UtcNow);
            });

            Console.WriteLine("Example 1: MicrosoftMLTokenizerAdapter\n");
            PrintChunks(chunks1);
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
