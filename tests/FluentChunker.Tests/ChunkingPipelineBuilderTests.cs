namespace FluentChunker.Tests
{
    public class ChunkingPipelineBuilderTests
    {
        private const string InputText = "A pragmatic developer chooses the least complicated implementation that reliably solves the problem.";

        [Test]
        public void Execute_WithMetadata_AttachesMetadataToAllChunks()
        {
            // Arrange
            var pipeline = new ChunkingPipelineBuilder()
                .UseTokenizer(new SharpTokenizer("o200k_base"))
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
            Assert.That(chunks[0].Text, Is.EqualTo(InputText));
            Assert.That(chunks[0].TokenCount, Is.GreaterThan(0));
            Assert.That(chunks[0].Metadata["Source"], Is.EqualTo("test.md"));
            Assert.That(chunks[0].Metadata["Author"], Is.EqualTo("Carlos Lopez Vaguez"));
        }

        [Test]
        public void Execute_WithoutMetadata_ReturnsChunksWithEmptyMetadata()
        {
            // Arrange
            var pipeline = new ChunkingPipelineBuilder()
                .UseTokenizer(new SharpTokenizer("o200k_base"))
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
            var pipeline = new ChunkingPipelineBuilder()
                .UseTokenizer(new SharpTokenizer("o200k_base"))
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
    }
}
