using FluentChunker.Contracts;

namespace FluentChunker.Samples.Demo
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var content = File.ReadAllText("Testfiles\\testfile.md");
            const string EncodingName = "o200k_base";

        // Example 1: Create a tokenizer using the SharpTokenizerAdapter
        var sharpTokenTokenizer = new SharpTokenizerAdapter(EncodingName); 

            var pipeline1 = new ChunkingPipeline.Builder()
                .UseTokenizer(sharpTokenTokenizer)
                .ByFixedSize(500)
                .WithOverlap(50)
                .WithMetadata(meta =>
                {
                    meta.Set("Source", "testfile.md");
                    meta.Set("Author", "Carlos Lopez Vaguez");
                })
                .Build();

            var chunks1 = pipeline1.Execute(content);

            Console.WriteLine("Example 1: SharpTokenizerAdapter\n");
            PrintChunks(chunks1);


            // Example 2: Create a tokenizer using the MicrosoftMLTokenizerAdapter
            var microsoftMLTokenizer = new MicrosoftMLTokenizerAdapter(EncodingName);

            var pipeline2 = new ChunkingPipeline.Builder()
                .UseTokenizer(microsoftMLTokenizer)
                .ByFixedSize(500)
                .WithOverlap(50)
                .WithMetadata(meta =>
                {
                    meta.Set("Source", "testfile.md");
                    meta.Set("Author", "Carlos Lopez Vaguez");
                })
                .Build();

            var chunks2 = pipeline2.Execute(content);

            Console.WriteLine("Example 2: MicrosoftMLTokenizerAdapter\n");
            PrintChunks(chunks2);

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
