using BenchmarkDotNet.Attributes;
using FluentChunker.Contracts;
using FluentChunker.Tokenizers;
using Microsoft.SemanticKernel.Text;

namespace FluentChunker.Benchmarks
{
    /// <summary>
    /// FluentChunker against Semantic Kernel's <see cref="TextChunker"/>, the chunker a .NET RAG project reaches for today.
    /// </summary>
    /// <remarks>
    /// Both sides are given the same corpus, the same budget and the same tiktoken counter, so the only variable left is the implementation.
    /// The comparison is speed only: <see cref="TextChunker"/> returns bare strings, so it has no offsets, token counts or metadata to measure against.
    /// </remarks>
    [MemoryDiagnoser]
    public class CompetitorBenchmarks
    {
        private string _document = null!;
        private List<string> _lines = null!;
        private MicrosoftMLTokenizerAdapter _tokenizer = null!;
        private ChunkingPipeline _fluentRecursive = null!;
        private ChunkingPipeline _fluentSentenceAware = null!;

        [Params(512)]
        public int MaxTokens { get; set; }

        [Params(0, 64)]
        public int Overlap { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _document = CorpusLoader.Load("english-prose.md");
            _tokenizer = new MicrosoftMLTokenizerAdapter("cl100k_base");

            _fluentRecursive = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_tokenizer)
                .UseSplitter(s => s.WithRecursiveChunking().WithMaxTokenSize(MaxTokens).WithOverlap(Overlap))
                .Build();

            _fluentSentenceAware = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_tokenizer)
                .UseSplitter(s => s.WithSentenceAwareChunking().WithMaxTokenSize(MaxTokens).WithOverlap(Overlap))
                .Build();

            // The line split is Semantic Kernel's own prerequisite step and is hoisted out of the measurement, so its paragraph packing is timed on equal terms with a FluentChunker call that receives the raw document.
            _lines = TextChunker.SplitPlainTextLines(_document, MaxTokens, CountTokens);
        }

        private int CountTokens(string text) => _tokenizer.CountTokens(text);

        [Benchmark(Baseline = true)]
        public async Task<List<Chunk>> FluentChunker_Recursive() =>
            await _fluentRecursive.ChunkAsync(_document).ToListAsync();

        [Benchmark]
        public async Task<List<Chunk>> FluentChunker_SentenceAware() =>
            await _fluentSentenceAware.ChunkAsync(_document).ToListAsync();

        [Benchmark]
        public List<string> SemanticKernel_TextChunker() =>
            TextChunker.SplitPlainTextParagraphs(_lines, MaxTokens, Overlap, tokenCounter: CountTokens);
    }
}
