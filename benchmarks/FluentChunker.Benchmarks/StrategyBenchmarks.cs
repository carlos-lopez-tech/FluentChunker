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
        private byte[] _documentBytes = null!;
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
            _documentBytes = System.Text.Encoding.UTF8.GetBytes(_document);

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

        // The reader overloads are the only path in the library that does real I/O, and they allocate differently: a pooled 262,144-char buffer plus one bounded string per segment.
        // Fed from a MemoryStream over the corpus bytes rather than a file, so the number measures chunking rather than the disk.
        // Constructing the stream inside the benchmark is deliberate, since a consumed reader cannot be replayed, and a MemoryStream over an existing array does not copy it.
        // Structure-aware has no reader overload by design, so the pairing stops at three strategies.

        [Benchmark]
        public async Task<List<Chunk>> FixedSizeStreaming()
        {
            using var reader = new StreamReader(new MemoryStream(_documentBytes));
            return await _fixedSize.ChunkAsync(reader).ToListAsync();
        }

        [Benchmark]
        public async Task<List<Chunk>> SentenceAwareStreaming()
        {
            using var reader = new StreamReader(new MemoryStream(_documentBytes));
            return await _sentenceAware.ChunkAsync(reader).ToListAsync();
        }

        [Benchmark]
        public async Task<List<Chunk>> RecursiveStreaming()
        {
            using var reader = new StreamReader(new MemoryStream(_documentBytes));
            return await _recursive.ChunkAsync(reader).ToListAsync();
        }
    }
}
