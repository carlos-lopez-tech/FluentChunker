using SharpToken;

namespace FluentChunker
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var content = File.ReadAllText("Testfiles\\testfile.md");
            Console.WriteLine(content);

            var tokenizer = GptEncoding.GetEncoding("o200k_base");
            var tokenCount = tokenizer.CountTokens(content);
            Console.WriteLine($"Token count: {tokenCount}");

            var encoded = tokenizer.Encode(content);
            foreach (var token in encoded)
            {
                Console.WriteLine(token);
            }

            Console.WriteLine("---");

            var decoded = tokenizer.Decode(encoded);
            Console.WriteLine(decoded);

            var pipeline = new ChunkingPipelineBuilder()
                .UseTokenizer("o200k_base")
                .BySentence(maxTokens: 512, overlap: 50)
                //.EnrichFromContent(enricher =>
                //{
                //    enricher.InheritMarkdownHeadings();
                //})
                //.WithMetadata(meta =>
                //{
                //    meta.Set("Source", "testfile.md");
                //    meta.Set("Author", "John Doe");
                //})
                .Build();

            Console.WriteLine(pipeline);

            var chunks = pipeline.Execute(content);

            foreach (var chunk in chunks)
            {
                Console.WriteLine($"[{chunk.Index}] ({chunk.TokenCount} tokens)");
                Console.WriteLine($"  Heading: {chunk.Metadata.GetValueOrDefault("Heading", "N/A")}");
                Console.WriteLine($"  Source:  {chunk.Metadata.GetValueOrDefault("Source", "N/A")}");
                Console.WriteLine($"  Text:    {chunk.Text[..Math.Min(80, chunk.Text.Length)]}...");
                Console.WriteLine();
            }

            Console.ReadKey();
        }
    }

    public class ContentEnricher
    {
        public void InheritMarkdownHeadings()
        {
            // TODO: Extract and inherit markdown headings into chunk metadata
        }
    }

    public class MetadataBuilder
    {
        private readonly Dictionary<string, string> _metadata = new();

        public void Set(string key, string value)
        {
            _metadata[key] = value;
        }

        internal Dictionary<string, string> Build() => new(_metadata);
    }

    public class ChunkingPipelineBuilder
    {
        private string? _tokenizerName;
        private Action<ContentEnricher>? _contentEnricher;
        private Action<MetadataBuilder>? _metadataConfig;

        public ChunkingPipelineBuilder UseTokenizer(string tokenizerName)
        {
            _tokenizerName = tokenizerName;
            return this;
        }

        public ChunkingPipelineBuilder ByFixedSize(int maxTokens, int overlap = 0)
        {
            // TODO: Configure fixed-size chunking strategy
            return this;
        }

        public ChunkingPipelineBuilder BySentence(int maxTokens, int overlap = 0)
        {
            Console.WriteLine("asdfg");
            return this;
        }

        public ChunkingPipelineBuilder ByParagraph(int maxTokens, int overlap = 0)
        {
            // TODO: Configure paragraph-based chunking strategy
            return this;
        }

        public ChunkingPipelineBuilder EnrichFromContent(Action<ContentEnricher> configure)
        {
            _contentEnricher = configure;
            return this;
        }

        public ChunkingPipelineBuilder WithMetadata(Action<MetadataBuilder> configure)
        {
            _metadataConfig = configure;
            return this;
        }

        public ChunkingPipeline Build()
        {
            // TODO: Assemble and return the configured pipeline
            return new ChunkingPipeline();
        }
    }

    public class Chunk
    {
        public int Index { get; set; }
        public string Text { get; set; } = string.Empty;
        public int TokenCount { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    public class ChunkingPipeline
    {
        public List<Chunk> Execute(string document)
        {
            // TODO: Execute the pipeline against the document
            return new List<Chunk>();
        }
    }
}
