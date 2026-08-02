using BenchmarkDotNet.Attributes;
using FluentChunker.Contracts;
using FluentChunker.Tokenizers;

namespace FluentChunker.Benchmarks
{
    /// <summary>
    /// The four strategies end to end over one committed corpus, which is the number a caller pays per document.
    /// </summary>
    [MemoryDiagnoser]
    public class StrategyBenchmarks
    {
        private string _document = null!;
        private ChunkingPipeline _fixedSize = null!;
        private ChunkingPipeline _sentenceAware = null!;
        private ChunkingPipeline _recursive = null!;
        private ChunkingPipeline _structureAware = null!;

        [Params(128, 512, 2048)]
        public int MaxTokens { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _document = CorpusLoader.Load("english-prose.md");

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
        public async Task<List<Chunk>> FixedSize() => await _fixedSize.ChunkAsync(_document).ToListAsync();

        [Benchmark]
        public async Task<List<Chunk>> SentenceAware() => await _sentenceAware.ChunkAsync(_document).ToListAsync();

        [Benchmark]
        public async Task<List<Chunk>> Recursive() => await _recursive.ChunkAsync(_document).ToListAsync();

        [Benchmark]
        public async Task<List<Chunk>> StructureAware() => await _structureAware.ChunkAsync(_document).ToListAsync();
    }
}
