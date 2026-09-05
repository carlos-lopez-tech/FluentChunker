using BenchmarkDotNet.Attributes;
using FluentChunker.Tokenizers;

namespace FluentChunker.Benchmarks
{
    /// <summary>
    /// The same volume of text delivered as many small documents rather than one large one, which is the shape of a real indexing run.
    /// </summary>
    /// <remarks>
    /// <see cref="StrategyBenchmarks"/> chunks a single large document, which amortises per-call setup away to almost nothing.
    /// Holding the total character count fixed while varying the document size leaves per-call cost as the only thing separating the rows, which is what a caller indexing a directory of small files actually pays.
    /// </remarks>
    [MemoryDiagnoser]
    public class SmallDocumentBenchmarks
    {
        // Fixed so every row chunks the same volume of text, which is what makes the rows comparable to each other.
        private const int TotalChars = 800_000;
        private const int MaxTokens = 512;

        private IReadOnlyList<string> _documents = null!;
        private ChunkingPipeline _fixedSize = null!;
        private ChunkingPipeline _sentenceAware = null!;
        private ChunkingPipeline _recursive = null!;
        private ChunkingPipeline _structureAware = null!;

        [Params(2_000, 8_000, 32_000)]
        public int DocumentChars { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _documents = CorpusLoader.LoadDocuments("english-prose.md", DocumentChars, TotalChars);

            var tokenizer = new MicrosoftMLTokenizerAdapter("cl100k_base");

            _fixedSize = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(tokenizer)
                .UseSplitter(s => s.WithFixedSizeChunking().WithMaxTokenSize(MaxTokens))
                .Build();

            _sentenceAware = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(tokenizer)
                .UseSplitter(s => s.WithSentenceAwareChunking().WithMaxTokenSize(MaxTokens))
                .Build();

            _recursive = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(tokenizer)
                .UseSplitter(s => s.WithRecursiveChunking().WithMaxTokenSize(MaxTokens))
                .Build();

            _structureAware = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(tokenizer)
                .UseSplitter(s => s.WithStructureAwareChunking().WithMaxTokenSize(MaxTokens))
                .Build();
        }

        [Benchmark(Baseline = true)]
        public async Task<int> FixedSize() => await SumTokensAsync(_fixedSize);

        [Benchmark]
        public async Task<int> SentenceAware() => await SumTokensAsync(_sentenceAware);

        [Benchmark]
        public async Task<int> Recursive() => await SumTokensAsync(_recursive);

        [Benchmark]
        public async Task<int> StructureAware() => await SumTokensAsync(_structureAware);

        // Summing rather than collecting keeps the measurement on the chunking instead of on list growth, while still consuming every chunk so none of the work can be optimised away.
        private async Task<int> SumTokensAsync(ChunkingPipeline pipeline)
        {
            var total = 0;

            foreach (var document in _documents)
            {
                await foreach (var chunk in pipeline.ChunkAsync(document))
                {
                    total += chunk.TokenCount;
                }
            }

            return total;
        }
    }
}
