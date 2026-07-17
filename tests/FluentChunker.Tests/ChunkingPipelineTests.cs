namespace FluentChunker.Tests
{
    public class ChunkingPipelineTests
    {
        private const string InputText = "A pragmatic developer chooses the least complicated implementation that reliably solves the problem.";
        private const string EncodingName = "o200k_base";
        private SharpTokenizerAdapter _sharpTokenizer;
        private MicrosoftMLTokenizerAdapter _microsoftMLTokenizer;

        [SetUp]
        public void Setup()
        {
            _sharpTokenizer = new SharpTokenizerAdapter(EncodingName);
            _microsoftMLTokenizer = new MicrosoftMLTokenizerAdapter(EncodingName);
        }

        [Test]
        public void Execute_WithMetadata_AttachesMetadataToAllChunks()
        {
            // Arrange
            var pipeline = new ChunkingPipeline.Builder()
                .UseTokenizer(_sharpTokenizer)
                .ByFixedSize(maxTokens: 10)
                .WithMetadata(meta =>
                {
                    meta.Set("Source", "test.md");
                    meta.Set("Author", "Carlos Lopez Vaguez");
                    meta.Set("Date", DateTime.UtcNow.ToString("o"));
                    meta.Set("Numeric", 1);
                })
                .Build();

            // Act
            var chunks = pipeline.Execute(InputText);   

            // Assert
            Assert.That(chunks, Has.Count.EqualTo(2));
            Assert.That(chunks[0].Text, Is.Not.Empty);
            Assert.That(chunks[0].TokenCount, Is.EqualTo(10));
            Assert.That(chunks[0].Metadata["Source"].GetString(), Is.EqualTo("test.md"));
            Assert.That(chunks[0].Metadata["Author"].GetString(), Is.EqualTo("Carlos Lopez Vaguez"));
            Assert.That(chunks[0].Metadata["Date"].GetString(), Is.Not.Empty);
            Assert.That(chunks[0].Metadata["Numeric"].GetInt32(), Is.EqualTo(1));
        }

        [Test]
        public void Execute_WithoutMetadata_ReturnsChunksWithEmptyMetadata()
        {
            // Arrange
            var pipeline = new ChunkingPipeline.Builder()
                .UseTokenizer(_sharpTokenizer)
                .ByFixedSize(10)
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
                .ByFixedSize(10)
                //.WithOverlap(1)
                .WithMetadata(meta => meta.Set("Source", "overlap-test"))
                .Build();

            // Act
            var chunks = pipeline.Execute(InputText);

            // Assert
            Assert.That(chunks, Has.Count.GreaterThan(1));
            Assert.That(chunks[0].OverlapCount, Is.EqualTo(1));
            foreach (var chunk in chunks)
            {
                Assert.That(chunk.Metadata["Source"].GetString(), Is.EqualTo("overlap-test"));
            }
        }

        [Test]
        public void Build_WithoutTokenizer_ThrowsArgumentNullException()
        {
            // Arrange
            var builder = new ChunkingPipeline.Builder()
                .ByFixedSize(10);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => builder.Build());
        }

        [Test]
        public void Builder_MultipleInstancesWithDifferentTokenizers_DontInterfere()
        {
            // Arrange
            var builder1 = new ChunkingPipeline.Builder()
                .UseTokenizer(_sharpTokenizer)
                .ByFixedSize(10)
                .WithMetadata(meta => meta.Set("Version", "1"));

            var builder2 = new ChunkingPipeline.Builder()
                .UseTokenizer(_microsoftMLTokenizer)
                .ByFixedSize(20)
                .WithMetadata(meta => meta.Set("Version", "2"));

            // Act
            var pipeline1 = builder1.Build();
            var pipeline2 = builder2.Build();

            var chunks1 = pipeline1.Execute(InputText);
            var chunks2 = pipeline2.Execute(InputText);

            // Assert
            Assert.That(chunks1[0].Metadata["Version"].GetString(), Is.EqualTo("1"));
            Assert.That(chunks2[0].Metadata["Version"].GetString(), Is.EqualTo("2"));
            Assert.That(chunks1, Has.Count.GreaterThan(chunks2.Count));
        }

        [Test]
        public void Builder_CanBeReusedForMultiplePipelines()
        {
            // Arrange
            var builder = new ChunkingPipeline.Builder()
                .UseTokenizer(_sharpTokenizer)
                .ByFixedSize(15);

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
