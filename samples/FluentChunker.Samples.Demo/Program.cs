namespace FluentChunker.Samples.Demo
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var content = File.ReadAllText("Testfiles\\testfile.md");

            var pipeline = new ChunkingPipelineBuilder()
                .UseTokenizer(new SharpTokenizer("o200k_base"))
                .ByFixedSize(maxTokens: 100, overlap: 20)
                .WithMetadata(meta =>
                {
                    meta.Set("Source", "testfile.md");
                    meta.Set("Author", "Carlos Lopez Vaguez");
                })
                .Build();

            var chunks = pipeline.Execute(content);

            foreach (var chunk in chunks)
            {
                Console.WriteLine($"[{chunk.Index}] ({chunk.TokenCount} tokens)");
                Console.WriteLine($"  Source: {chunk.Metadata.GetValueOrDefault("Source", "N/A")}");
                Console.WriteLine($"  Author: {chunk.Metadata.GetValueOrDefault("Author", "N/A")}");
                Console.WriteLine($"  Text:  {chunk.Text[..Math.Min(80, chunk.Text.Length)]}...");
                Console.WriteLine();
            }

            Console.ReadKey();
        }
    }
}
