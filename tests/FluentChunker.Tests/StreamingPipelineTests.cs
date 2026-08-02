using System.Text.Json;
using FluentChunker.Chunkers;
using FluentChunker.Contracts;
using FluentChunker.Tokenizers;

namespace FluentChunker.Tests
{
    public class StreamingPipelineTests
    {
        // Fixed chunks for both entry points, so these tests pin the pipeline's routing and stamping rather than any strategy's cutting.
        private sealed class FakeStreamingChunker : IStreamingChunker
        {
            internal static readonly Chunk[] Chunks =
            [
                new() { Index = 0, Text = "The archive ingests documents.", TokenCount = 5, SourceStart = 0, SourceLength = 30 },
                new() { Index = 1, Text = "The index embeds every chunk.", TokenCount = 5, SourceStart = 31, SourceLength = 29 }
            ];

            public IAsyncEnumerable<Chunk> ChunkAsync(string text, CancellationToken cancellationToken = default) =>
                ChunkStream.Over(Chunks, cancellationToken);

            public IAsyncEnumerable<Chunk> ChunkAsync(TextReader reader, CancellationToken cancellationToken = default) =>
                ChunkStream.Over(Chunks, cancellationToken);
        }

        private static ChunkingPipeline FakePipeline(Action<MetadataBuilder>? pipelineMetadata = null)
        {
            var metadataBuilder = new MetadataBuilder();
            pipelineMetadata?.Invoke(metadataBuilder);
            return new ChunkingPipeline(new FakeStreamingChunker(), metadataBuilder.Build());
        }

        [Test]
        public void ChunkAsync_NullReader_ThrowsArgumentNullExceptionAtCallTime()
        {
            // Arrange
            var pipeline = FakePipeline();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => pipeline.ChunkAsync((TextReader)null!));
        }

        [Test]
        public void ChunkAsync_NullReaderWithMetadata_ThrowsArgumentNullExceptionAtCallTime()
        {
            // Arrange
            var pipeline = FakePipeline();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => pipeline.ChunkAsync((TextReader)null!, meta => meta.Set("Source", "stream")));
        }

        [Test]
        public void ChunkAsync_ReaderOnNonStreamingStrategy_ThrowsActionableExceptionAtCallTime()
        {
            // Arrange - structure-aware is the strategy that deliberately cannot stream
            var pipeline = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(new MicrosoftMLTokenizerAdapter("cl100k_base"))
                .UseSplitter(s => s.WithStructureAwareChunking().WithMaxTokenSize(512))
                .Build();
            using var reader = new StringReader("# Archive\n\nIndexing notes.");

            // Act & Assert - thrown from the call, not from the first MoveNextAsync
            var exception = Assert.Throws<InvalidOperationException>(() => pipeline.ChunkAsync(reader));
            Assert.That(exception!.Message, Does.Contain("does not support streaming input"));
        }

        [Test]
        public async Task ChunkAsync_ReaderWithMetadata_StampsPipelineAndDocumentValues()
        {
            // Arrange
            var pipeline = FakePipeline(meta => meta.Set("Corpus", "archive"));
            using var reader = new StringReader("unused by the fake");

            // Act
            var result = await pipeline.ChunkAsync(reader, meta => meta.Set("Source", "stream")).ToListAsync();

            // Assert
            Assert.That(result, Has.Count.EqualTo(2));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].Metadata["Corpus"].GetString(), Is.EqualTo("archive"));
                Assert.That(result[0].Metadata["Source"].GetString(), Is.EqualTo("stream"));
                Assert.That(result[1].Metadata["Corpus"].GetString(), Is.EqualTo("archive"));
                Assert.That(result[1].Metadata["Source"].GetString(), Is.EqualTo("stream"));
            }
        }

        [Test]
        public async Task ChunkAsync_ReaderWithoutMetadata_PassesChunksThroughInOrder()
        {
            // Arrange
            var pipeline = FakePipeline();
            using var reader = new StringReader("unused by the fake");

            // Act
            var result = await pipeline.ChunkAsync(reader).ToListAsync();

            // Assert - zero-metadata pass-through: value-equal to the fake's chunks, in order
            Assert.That(result, Is.EqualTo(FakeStreamingChunker.Chunks));
        }

        [Test]
        public void ChunkAsync_ReaderPreCancelledToken_ThrowsBeforeFirstChunk()
        {
            // Arrange
            var pipeline = FakePipeline();
            using var reader = new StringReader("unused by the fake");
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var consumed = new List<Chunk>();

            // Act
            async Task Consume()
            {
                await foreach (var chunk in pipeline.ChunkAsync(reader, cts.Token))
                {
                    consumed.Add(chunk);
                }
            }

            // Assert - the token reaches the producer through the factory, not just the stamping loop
            Assert.ThrowsAsync<OperationCanceledException>(Consume);
            Assert.That(consumed, Is.Empty);
        }
    }
}
