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
        public void Execute_FixedSizeSplitterWithMaxTokenSize_ReturnsFullChunks()
        {
            // Arrange
            var pipeline = ChunkingPipeline.Builder.Initialize()
                .UseTokenizer(_microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithFixedSizeChunking()
                    .WithMaxTokenSize(10))
                .Build();

            // Act
            var chunks = pipeline.Execute(InputText);

            // Assert
            Assert.That(chunks, Has.Count.EqualTo(2));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(chunks[0].Text, Is.Not.Empty);
                Assert.That(chunks[0].TokenCount, Is.EqualTo(10));
            }
        }

        [Test]
        public void Execute_FixedSizeSplitterWithOverlap_MarksOverlapOnReceivingChunks()
        {
            // Arrange
            var pipeline = ChunkingPipeline.Builder.Initialize()
                .UseTokenizer(_microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithFixedSizeChunking()
                    .WithMaxTokenSize(10)
                    .WithOverlap(2))
                .Build();

            // Act
            var chunks = pipeline.Execute(InputText);

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
        public void Execute_NullDocument_ThrowsArgumentNullException()
        {
            // Arrange
            var pipeline = ChunkingPipeline.Builder.Initialize()
                .UseTokenizer(_microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithFixedSizeChunking()
                    .WithMaxTokenSize(10))
                .Build();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => pipeline.Execute(null!));
        }

        [Test]
        public void Builder_MultipleInstancesWithDifferentConfigurations_DontInterfere()
        {
            // Arrange
            var builder1 = ChunkingPipeline.Builder.Initialize()
                .UseTokenizer(_microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithFixedSizeChunking()
                    .WithMaxTokenSize(10))
                .AddMetadata(meta => meta.Set("Version", "1"));

            var builder2 = ChunkingPipeline.Builder.Initialize()
                .UseTokenizer(_microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithFixedSizeChunking()
                    .WithMaxTokenSize(20))
                .AddMetadata(meta => meta.Set("Version", "2"));

            // Act
            var pipeline1 = builder1.Build();
            var pipeline2 = builder2.Build();

            var chunks1 = pipeline1.Execute(InputText);
            var chunks2 = pipeline2.Execute(InputText);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(chunks1[0].Metadata["Version"].GetString(), Is.EqualTo("1"));
                Assert.That(chunks2[0].Metadata["Version"].GetString(), Is.EqualTo("2"));
                Assert.That(chunks1, Has.Count.GreaterThan(chunks2.Count));
            }   
        }

        [Test]
        public void Builder_CanBeReusedForMultiplePipelines()
        {
            // Arrange
            var builder = ChunkingPipeline.Builder.Initialize()
                .UseTokenizer(_microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithFixedSizeChunking()
                    .WithMaxTokenSize(15));

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
