using FluentChunker.Tokenizers;

namespace FluentChunker.Tests
{
    public class ChunkingPipelineMetadataTests
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
        public async Task Chunk_PipelineMetadataConfigured_AttachesMetadataToAllChunks()
        {
            // Arrange
            var pipeline = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithFixedSizeChunking()
                    .WithMaxTokenSize(10))
                .AddMetadata(meta =>
                {
                    meta.Set("Source", "test.md");
                    meta.Set("Author", "Carlos Lopez Vaguez");
                    meta.Set("Date", DateTime.UtcNow.ToString("o"));
                    meta.Set("Numeric", 1);
                })
                .Build();

            // Act
            var chunks = await pipeline.ChunkAsync(InputText).ToListAsync();

            // Assert
            Assert.That(chunks, Has.Count.EqualTo(2));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(chunks[0].Text, Is.Not.Empty);
                Assert.That(chunks[0].TokenCount, Is.EqualTo(10));
                Assert.That(chunks[0].Metadata["Source"].GetString(), Is.EqualTo("test.md"));
                Assert.That(chunks[0].Metadata["Author"].GetString(), Is.EqualTo("Carlos Lopez Vaguez"));
                Assert.That(chunks[0].Metadata["Date"].GetString(), Is.Not.Empty);
                Assert.That(chunks[0].Metadata["Numeric"].GetInt32(), Is.EqualTo(1));
            }
        }

        [Test]
        public async Task Chunk_WithoutMetadata_ReturnsChunksWithEmptyMetadata()
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
            Assert.That(chunks, Has.Count.GreaterThan(1));
            foreach (var chunk in chunks)
            {
                Assert.That(chunk.Metadata, Is.Empty);
            }
        }

        [Test]
        public async Task Chunk_WithOverlapAndMetadata_AttachesMetadataToAllChunks()
        {
            // Arrange
            var pipeline = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithFixedSizeChunking()
                    .WithMaxTokenSize(10)
                    .WithOverlap(1))
                .AddMetadata(meta => meta.Set("Source", "overlap-test"))
                .Build();

            // Act
            var chunks = await pipeline.ChunkAsync(InputText).ToListAsync();

            // Assert - first chunk receives nothing; every following chunk receives the overlap
            Assert.That(chunks, Has.Count.GreaterThan(1));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(chunks[0].OverlapCount, Is.Zero);
                Assert.That(chunks[1].OverlapCount, Is.EqualTo(1));
            }
            foreach (var chunk in chunks)
            {
                Assert.That(chunk.Metadata["Source"].GetString(), Is.EqualTo("overlap-test"));
            }
        }

        [Test]
        public async Task AddMetadata_CalledMultipleTimes_MergesAllEntries()
        {
            // Arrange
            var pipeline = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithFixedSizeChunking()
                    .WithMaxTokenSize(10))
                .AddMetadata(meta => meta.Set("Source", "first.md"))
                .AddMetadata(meta => meta.Set("Author", "Carlos Lopez Vaguez"))
                .Build();

            // Act
            var chunks = await pipeline.ChunkAsync(InputText).ToListAsync();

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(chunks[0].Metadata["Source"].GetString(), Is.EqualTo("first.md"));
                Assert.That(chunks[0].Metadata["Author"].GetString(), Is.EqualTo("Carlos Lopez Vaguez"));
            }
        }

        [Test]
        public async Task Chunk_WithDocumentMetadata_AttachesToAllChunks()
        {
            // Arrange
            var pipeline = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithFixedSizeChunking()
                    .WithMaxTokenSize(10))
                .Build();

            // Act
            var chunks = await pipeline.ChunkAsync(InputText, meta =>
            {
                meta.Set("Url", "https://carloslopez.tech/doc-1");
                meta.Set("Title", "Document One");
            }).ToListAsync();

            // Assert
            Assert.That(chunks, Has.Count.GreaterThan(1));
            foreach (var chunk in chunks)
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(chunk.Metadata["Url"].GetString(), Is.EqualTo("https://carloslopez.tech/doc-1"));
                    Assert.That(chunk.Metadata["Title"].GetString(), Is.EqualTo("Document One"));
                }
            }
        }

        [Test]
        public async Task Chunk_WithPipelineAndDocumentMetadata_MergesBoth()
        {
            // Arrange
            var pipeline = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithFixedSizeChunking()
                    .WithMaxTokenSize(10))
                .AddMetadata(meta => meta.Set("SourceType", "wiki"))
                .Build();

            // Act
            var chunks = await pipeline.ChunkAsync(InputText, meta => meta.Set("Url", "https://carloslopez.tech/doc-1")).ToListAsync();

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(chunks[0].Metadata["SourceType"].GetString(), Is.EqualTo("wiki"));
                Assert.That(chunks[0].Metadata["Url"].GetString(), Is.EqualTo("https://carloslopez.tech/doc-1"));
            }
        }

        [Test]
        public async Task Chunk_DocumentMetadata_OverridesPipelineMetadataOnKeyCollision()
        {
            // Arrange
            var pipeline = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithFixedSizeChunking()
                    .WithMaxTokenSize(10))
                .AddMetadata(meta => meta.Set("Language", "en"))
                .Build();

            // Act
            var chunks = await pipeline.ChunkAsync(InputText, meta => meta.Set("Language", "de")).ToListAsync();

            // Assert
            Assert.That(chunks[0].Metadata["Language"].GetString(), Is.EqualTo("de"));
        }

        [Test]
        public async Task Chunk_ReusedPipelineWithDifferentDocumentMetadata_DoesNotLeakBetweenCalls()
        {
            // Arrange
            var pipeline = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithFixedSizeChunking()
                    .WithMaxTokenSize(10))
                .Build();

            // Act
            var chunksDoc1 = await pipeline.ChunkAsync(InputText, meta => meta.Set("Url", "https://carloslopez.tech/doc-1")).ToListAsync();
            var chunksDoc2 = await pipeline.ChunkAsync(InputText, meta => meta.Set("Url", "https://carloslopez.tech/doc-2")).ToListAsync();
            var chunksDoc3 = await pipeline.ChunkAsync(InputText).ToListAsync();

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(chunksDoc1[0].Metadata["Url"].GetString(), Is.EqualTo("https://carloslopez.tech/doc-1"));
                Assert.That(chunksDoc2[0].Metadata["Url"].GetString(), Is.EqualTo("https://carloslopez.tech/doc-2"));
                Assert.That(chunksDoc3[0].Metadata, Is.Empty);
            }
        }

        [Test]
        public async Task Chunk_PipelineMetadata_IsOneInstanceSharedByAllChunks()
        {
            // Arrange - stamping builds the merged metadata once per call, not once per chunk.
            var pipeline = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithFixedSizeChunking()
                    .WithMaxTokenSize(10))
                .AddMetadata(meta => meta.Set("Source", "shared.md"))
                .Build();

            // Act
            var chunks = await pipeline.ChunkAsync(InputText).ToListAsync();

            // Assert
            Assert.That(chunks, Has.Count.GreaterThan(1));
            Assert.That(chunks[1].Metadata, Is.SameAs(chunks[0].Metadata));
        }

        [Test]
        public async Task Chunk_StructureAwareChunksOfOneSection_ShareTheirSectionMetadata()
        {
            // Arrange
            var pipeline = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithStructureAwareChunking()
                    .WithMaxTokenSize(10))
                .Build();

            const string markdown = "# Section\n\nFirst paragraph with enough words to exceed the small budget alone.\n\n" +
                                    "Second paragraph, also long enough to become its own separate chunk here.";

            // Act
            var chunks = await pipeline.ChunkAsync(markdown).ToListAsync();

            // Assert
            Assert.That(chunks, Has.Count.GreaterThan(1));
            Assert.That(chunks[1].Metadata, Is.SameAs(chunks[0].Metadata));
        }

        [Test]
        public async Task Chunk_PipelineMetadataCollidingWithChunkerKey_OverridesTheChunkerValue()
        {
            // Arrange - headingPath is reserved by structure-aware chunking and colliding with it is discouraged,
            // but the precedence is pinned: pipeline and document values overwrite chunker values, exactly as they did when stamping wrote into the chunk's dictionary.
            var pipeline = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithStructureAwareChunking()
                    .WithMaxTokenSize(50))
                .AddMetadata(meta => meta.Set("headingPath", "override"))
                .Build();

            // Act
            var chunks = await pipeline.ChunkAsync("# Real Heading\n\nBody text.").ToListAsync();

            // Assert
            Assert.That(chunks[0].Metadata["headingPath"].GetString(), Is.EqualTo("override"));
        }
    }
}
