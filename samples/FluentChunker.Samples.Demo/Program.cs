using FluentChunker.Dtos;

namespace FluentChunker.Samples.Demo
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var content = File.ReadAllText("Testfiles\\testfile.md");

            // Example 1: Create a tokenizer using the SharpTokenizerAdapter
            var sharpTokenTokenizer = new SharpTokenizerAdapter("o200k_base"); 

            var pipeline1 = new ChunkingPipeline.Builder()
                .UseTokenizer(sharpTokenTokenizer)
                .ByFixedSize(maxTokens: 500, overlap: 50)
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
            var microsoftMLTokenizer = new MicrosoftMLTokenizerAdapter("o200k_base");

            var pipeline2 = new ChunkingPipeline.Builder()
                .UseTokenizer(microsoftMLTokenizer)
                .ByFixedSize(maxTokens: 500, overlap: 50)
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

        private static void PrintChunks(IReadOnlyList<ChunkDto> chunks)
        {
            foreach (var chunk in chunks)
            {
                Console.WriteLine($"[{chunk.Index}] ({chunk.TokenCount} tokens)");
                Console.WriteLine($"  Source: {chunk.Metadata.GetValueOrDefault("Source", "N/A")}");
                Console.WriteLine($"  Author: {chunk.Metadata.GetValueOrDefault("Author", "N/A")}");
                Console.WriteLine($"  Text:  {chunk.Text[..Math.Min(80, chunk.Text.Length)]}...");
                Console.WriteLine(string.Concat(Enumerable.Repeat("- ", 50)));
                Console.WriteLine("\n\n");
            }
        }
    }
}
