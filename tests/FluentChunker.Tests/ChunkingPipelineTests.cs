using FluentChunker.Contracts;
using FluentChunker.Tokenizers;

namespace FluentChunker.Tests
{
    public class ChunkingPipelineTests
    {
        private const string InputText = "A pragmatic developer chooses the least complicated implementation that reliably solves the problem.";
        private const string EncodingName = "cl100k_base";
        private MicrosoftMLTokenizerAdapter _microsoftMLTokenizer;

        [SetUp]
        public void Setup()
        {
            _microsoftMLTokenizer = new MicrosoftMLTokenizerAdapter(EncodingName);
        }

        [Test]
        public async Task Chunk_FixedSizeSplitterWithMaxTokenSize_ReturnsFullChunks()
        {
            // Arrange
            var pipeline = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithFixedSizeChunking()
                    .WithMaxTokenSize(10))
                .Build();

            // Act
            var chunks = await pipeline.ChunkAsync(InputText).ToListAsync();

            // Assert
            Assert.That(chunks, Has.Count.EqualTo(2));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(chunks[0].Text, Is.Not.Empty);
                Assert.That(chunks[0].TokenCount, Is.EqualTo(10));
            }
        }

        [Test]
        public async Task Chunk_FixedSizeSplitterWithOverlap_MarksOverlapOnReceivingChunks()
        {
            // Arrange
            var pipeline = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithFixedSizeChunking()
                    .WithMaxTokenSize(10)
                    .WithOverlap(2))
                .Build();

            // Act
            var chunks = await pipeline.ChunkAsync(InputText).ToListAsync();

            // Assert - OverlapCount reports tokens carried INTO a chunk: 0 on the first,
            // the overlap on every following chunk (same semantics as SentenceAwareChunker)
            Assert.That(chunks, Has.Count.GreaterThan(1));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(chunks[0].OverlapCount, Is.Zero);
                Assert.That(chunks[^1].OverlapCount, Is.EqualTo(2));
            }
        }

        [Test]
        public void Chunk_NullDocument_ThrowsArgumentNullException()
        {
            // Arrange
            var pipeline = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithFixedSizeChunking()
                    .WithMaxTokenSize(10))
                .Build();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => pipeline.ChunkAsync((string)null!));
        }

        [Test]
        public async Task Builder_MultipleInstancesWithDifferentConfigurations_DontInterfere()
        {
            // Arrange
            var builder1 = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithFixedSizeChunking()
                    .WithMaxTokenSize(10))
                .AddMetadata(meta => meta.Set("Version", "1"));

            var builder2 = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithFixedSizeChunking()
                    .WithMaxTokenSize(20))
                .AddMetadata(meta => meta.Set("Version", "2"));

            // Act
            var pipeline1 = builder1.Build();
            var pipeline2 = builder2.Build();

            var chunks1 = await pipeline1.ChunkAsync(InputText).ToListAsync();
            var chunks2 = await pipeline2.ChunkAsync(InputText).ToListAsync();

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(chunks1[0].Metadata["Version"].GetString(), Is.EqualTo("1"));
                Assert.That(chunks2[0].Metadata["Version"].GetString(), Is.EqualTo("2"));
                Assert.That(chunks1, Has.Count.GreaterThan(chunks2.Count));
            }   
        }

        [Test]
        public async Task Builder_CanBeReusedForMultiplePipelines()
        {
            // Arrange
            var builder = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithFixedSizeChunking()
                    .WithMaxTokenSize(15));

            // Act
            var pipeline1 = builder.Build();
            var pipeline2 = builder.Build();

            var chunks1 = await pipeline1.ChunkAsync(InputText).ToListAsync();
            var chunks2 = await pipeline2.ChunkAsync(InputText).ToListAsync();

            // Assert
            Assert.That(chunks1, Has.Count.EqualTo(chunks2.Count));
            Assert.That(chunks1, Is.EqualTo(chunks2));
        }

        private static ChunkingPipeline FixedSizePipeline(int maxTokens) =>
            ChunkingPipeline.CreateBuilder()
                .UseTokenizer(new MicrosoftMLTokenizerAdapter("cl100k_base"))
                .UseSplitter(s => s.WithFixedSizeChunking().WithMaxTokenSize(maxTokens).WithOverlap(0))
                .Build();

        [Test]
        public void ChunkAsync_NullDocument_ThrowsAtCallTime()
        {
            // Arrange
            var pipeline = FixedSizePipeline(maxTokens: 8);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => pipeline.ChunkAsync((string)null!));
        }

        [Test]
        public void ChunkAsync_NullDocumentWithMetadata_ThrowsAtCallTime()
        {
            // Arrange
            var pipeline = FixedSizePipeline(maxTokens: 8);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => pipeline.ChunkAsync((string)null!, meta => meta.Set("Source", "archive")));
        }

        [Test]
        public void ChunkAsync_PreCancelledToken_ThrowsBeforeFirstChunk()
        {
            // Arrange
            var pipeline = FixedSizePipeline(maxTokens: 8);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var consumed = new List<Chunk>();

            // Act
            async Task Consume()
            {
                await foreach (var chunk in pipeline.ChunkAsync("The archive group compared chunking strategies for retrieval quality across embedding models.", cts.Token))
                {
                    consumed.Add(chunk);
                }
            }

            // Assert - the token reaches the chunker where chunks are produced, not just a stamping loop around it
            Assert.ThrowsAsync<OperationCanceledException>(Consume);
            Assert.That(consumed, Is.Empty);
        }

        [Test]
        public void ChunkAsync_TokenSuppliedThroughWithCancellation_ThrowsBeforeFirstChunk()
        {
            // Arrange
            var pipeline = FixedSizePipeline(maxTokens: 8);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var consumed = new List<Chunk>();

            // Act
            async Task Consume()
            {
                var stream = pipeline.ChunkAsync("The archive group compared chunking strategies for retrieval quality across embedding models.");
                await foreach (var chunk in stream.WithCancellation(cts.Token))
                {
                    consumed.Add(chunk);
                }
            }

            // Assert - a consumer that only holds the stream still cancels it, because the stamping layer passes its own token down to the chunker
            Assert.ThrowsAsync<OperationCanceledException>(Consume);
            Assert.That(consumed, Is.Empty);
        }

        [Test]
        public async Task ChunkAsync_FirstChunkConsumedLazily_CarriesPipelineMetadata()
        {
            // Arrange
            var pipeline = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(new MicrosoftMLTokenizerAdapter("cl100k_base"))
                .UseSplitter(s => s.WithFixedSizeChunking().WithMaxTokenSize(8).WithOverlap(0))
                .AddMetadata(meta => meta.Set("Corpus", "archive"))
                .Build();
            Chunk? first = null;

            // Act - consume only the first chunk, so stamping is proven to happen in the stream rather than after materialisation
            await foreach (var chunk in pipeline.ChunkAsync("The archive group compared chunking strategies for retrieval quality across embedding models."))
            {
                first = chunk;
                break;
            }

            // Assert
            Assert.That(first, Is.Not.Null);
            Assert.That(first!.Metadata["Corpus"].GetString(), Is.EqualTo("archive"));
        }
    }
}
