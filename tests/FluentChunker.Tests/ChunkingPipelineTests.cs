namespace FluentChunker.Tests
{
    public class ChunkingPipelineTests
    {
        private const string InputText = "A pragmatic developer chooses the least complicated implementation that reliably solves the problem.";
        private SharpTokenizer _sharpTokenizer;

        [SetUp]
        public void Setup()
        {
            _sharpTokenizer = new SharpTokenizer("o200k_base");
        }

        [Test]
        public void Execute_WithMetadata_AttachesMetadataToAllChunks()
        {
            // Arrange
            var pipeline = new ChunkingPipeline.Builder()
                .UseTokenizer(_sharpTokenizer)
                .ByFixedSize(maxTokens: 10, overlap: 0)
                .WithMetadata(meta =>
                {
                    meta.Set("Source", "test.md");
                    meta.Set("Author", "Carlos Lopez Vaguez");
                })
                .Build();

            // Act
            var chunks = pipeline.Execute(InputText);

            // Assert
            Assert.That(chunks, Has.Count.EqualTo(2));
            Assert.That(chunks[0].Text, Is.Not.Empty);
            Assert.That(chunks[0].TokenCount, Is.EqualTo(10));
            Assert.That(chunks[0].Metadata["Source"], Is.EqualTo("test.md"));
            Assert.That(chunks[0].Metadata["Author"], Is.EqualTo("Carlos Lopez Vaguez"));
        }

        [Test]
        public void Execute_WithoutMetadata_ReturnsChunksWithEmptyMetadata()
        {
            // Arrange
            var pipeline = new ChunkingPipeline.Builder()
                .UseTokenizer(_sharpTokenizer)
                .ByFixedSize(maxTokens: 10, overlap: 0)
                .Build();

            // Act
            var chunks = pipeline.Execute(InputText);

            // Assert
            Assert.That(chunks, Has.Count.GreaterThan(1));
            foreach (var chunk in chunks)
            {
                Assert.That(chunk.Metadata, Is.Empty);
            }
        }

        [Test]
        public void Execute_WithOverlap_AttachesMetadataToAllChunks()
        {
            // Arrange
            var pipeline = new ChunkingPipeline.Builder()
                .UseTokenizer(_sharpTokenizer)
                .ByFixedSize(maxTokens: 10, overlap: 1)
                .WithMetadata(meta => meta.Set("Source", "overlap-test"))
                .Build();

            // Act
            var chunks = pipeline.Execute(InputText);

            // Assert
            Assert.That(chunks, Has.Count.GreaterThan(1));
            foreach (var chunk in chunks)
            {
                Assert.That(chunk.Metadata["Source"], Is.EqualTo("overlap-test"));
            }
        }

        [Test]
        public void Build_WithoutTokenizer_ThrowsArgumentNullException()
        {
            // Arrange
            var builder = new ChunkingPipeline.Builder()
                .ByFixedSize(maxTokens: 10);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => builder.Build());
        }

        [Test]
        public void Build_WithTokenizerButNoChunkingStrategy_ThrowsException()
        {
            // Arrange
            var builder = new ChunkingPipeline.Builder()
                .UseTokenizer(_sharpTokenizer);

            // Act & Assert
            Assert.Throws<ArgumentException>(() => builder.Build());
        }

        [Test]
        public void Builder_MultipleInstances_DontInterfere()
        {
            // Arrange
            var builder1 = new ChunkingPipeline.Builder()
                .UseTokenizer(_sharpTokenizer)
                .ByFixedSize(maxTokens: 10)
                .WithMetadata(meta => meta.Set("Version", "1"));

            var builder2 = new ChunkingPipeline.Builder()
                .UseTokenizer(_sharpTokenizer)
                .ByFixedSize(maxTokens: 20)
                .WithMetadata(meta => meta.Set("Version", "2"));

            // Act
            var pipeline1 = builder1.Build();
            var pipeline2 = builder2.Build();

            var chunks1 = pipeline1.Execute(InputText);
            var chunks2 = pipeline2.Execute(InputText);

            // Assert
            Assert.That(chunks1[0].Metadata["Version"], Is.EqualTo("1"));
            Assert.That(chunks2[0].Metadata["Version"], Is.EqualTo("2"));
            Assert.That(chunks1, Has.Count.GreaterThan(chunks2.Count));
        }

        [Test]
        public void Builder_CanBeReusedForMultiplePipelines()
        {
            // Arrange
            var builder = new ChunkingPipeline.Builder()
                .UseTokenizer(_sharpTokenizer)
                .ByFixedSize(maxTokens: 15);

            // Act
            var pipeline1 = builder.Build();
            var pipeline2 = builder.Build();

            var chunks1 = pipeline1.Execute(InputText);
            var chunks2 = pipeline2.Execute(InputText);

            // Assert
            Assert.That(chunks1, Has.Count.EqualTo(chunks2.Count));
            Assert.That(chunks1.Select(c => c.Text), Is.EqualTo(chunks2.Select(c => c.Text)));
        }
    }
}
