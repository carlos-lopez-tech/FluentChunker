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
        /// <summary>
        /// The <c>maxTokensPerLine</c> settings <see cref="TextChunker"/> is timed at.
        /// </summary>
        /// <remarks>
        /// SplitPlainTextParagraphs re-splits its input at maxTokensPerParagraph minus the overlap, and its fallback halves an over-long line at the character midpoint.
        /// Pinning maxTokensPerLine to the paragraph budget therefore leaves the packer roughly 250-token pieces it can never recombine, so it emits 1,024 chunks where FluentChunker emits 700 and the two sides are no longer doing the same amount of work.
        /// Sweeping the setting is what makes the timing comparable; 512 is kept so the corrected figures can be read against the pinned ones they replace.
        /// This is an ArgumentsSource rather than a Params so it applies to the one benchmark it means something to, instead of re-running the FluentChunker methods once per value.
        /// </remarks>
        public static IEnumerable<int> LineBudgets => [512, 128, 64, 16];

        private string _document = null!;
        private Dictionary<int, List<string>> _lines = null!;
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
            // One split per swept budget, prepared here for the same reason: what is being timed is the packing, not the preparation.
            _lines = LineBudgets.ToDictionary(budget => budget, budget => TextChunker.SplitPlainTextLines(_document, budget, CountTokens));
        }

        private int CountTokens(string text) => _tokenizer.CountTokens(text);

        [Benchmark(Baseline = true)]
        public async Task<List<Chunk>> FluentChunker_Recursive() =>
            await _fluentRecursive.ChunkAsync(_document).ToListAsync();

        [Benchmark]
        public async Task<List<Chunk>> FluentChunker_SentenceAware() =>
            await _fluentSentenceAware.ChunkAsync(_document).ToListAsync();

        [Benchmark]
        [ArgumentsSource(nameof(LineBudgets))]
        public List<string> SemanticKernel_TextChunker(int maxTokensPerLine) =>
            TextChunker.SplitPlainTextParagraphs(_lines[maxTokensPerLine], MaxTokens, Overlap, tokenCounter: CountTokens);
    }
}
