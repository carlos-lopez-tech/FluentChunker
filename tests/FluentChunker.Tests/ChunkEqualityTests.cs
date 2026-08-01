using FluentChunker.Contracts;
using FluentChunker.Tokenizers;

namespace FluentChunker.Tests
{
    public class ChunkEqualityTests
    {
        private const string SampleText = "Chunk equality is a promise the record keyword makes.";

        private static Chunk CreateChunk() => new()
        {
            Index = 0,
            Text = SampleText,
            TokenCount = 11,
            OverlapCount = 0,
            SourceStart = 0,
            SourceLength = SampleText.Length
        };

        [Test]
        public void Equals_IdenticalContentAndNoMetadata_AreEqual()
        {
            // Arrange
            var a = CreateChunk();
            var b = CreateChunk();

            // Act & Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(a, Is.EqualTo(b));
                Assert.That(a == b, Is.True);
                Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            }
        }

        [Test]
        public void Distinct_IdenticalChunks_CollapsesToOne()
        {
            // Arrange
            var chunks = new[] { CreateChunk(), CreateChunk() };

            // Act & Assert
            Assert.That(chunks.Distinct().Count(), Is.EqualTo(1));
        }

        [Test]
        public void HashSet_EqualButDistinctInstance_IsFound()
        {
            // Arrange
            var set = new HashSet<Chunk> { CreateChunk() };

            // Act & Assert
            Assert.That(set, Does.Contain(CreateChunk()));
        }

        [Test]
        public async Task Chunk_TwoRunsOverTheSameDocument_ProducesEqualChunks()
        {
            // Arrange
            var pipeline = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(new MicrosoftMLTokenizerAdapter("cl100k_base"))
                .UseSplitter(s => s
                    .WithFixedSizeChunking()
                    .WithMaxTokenSize(10))
                .AddMetadata(meta => meta.Set("Source", "equality.md"))
                .Build();

            const string document = "A pragmatic developer chooses the least complicated implementation that reliably solves the problem.";

            // Act
            var firstRun = await pipeline.ChunkAsync(document).ToListAsync();
            var secondRun = await pipeline.ChunkAsync(document).ToListAsync();

            // Assert
            Assert.That(firstRun, Is.EqualTo(secondRun));
        }

        [Test]
        public void Equals_SeparatelyBuiltIdenticalMetadata_AreEqual()
        {
            // Arrange
            var a = CreateChunk() with { Metadata = ChunkMetadata.Empty.With("Source", "doc.txt") };
            var b = CreateChunk() with { Metadata = ChunkMetadata.Empty.With("Source", "doc.txt") };

            // Act & Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(a, Is.EqualTo(b));
                Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            }
        }

        [Test]
        public void Equals_SameTextButDifferentSourceMetadata_AreDistinguished()
        {
            // Arrange - the dedup escape hatch: tagging chunks with their document keeps chunk 3 of doc A from colliding with chunk 3 of doc B.
            var a = CreateChunk() with { Metadata = ChunkMetadata.Empty.With("Source", "a.txt") };
            var b = CreateChunk() with { Metadata = ChunkMetadata.Empty.With("Source", "b.txt") };

            // Act & Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(a, Is.Not.EqualTo(b));
                Assert.That(a.GetHashCode(), Is.Not.EqualTo(b.GetHashCode()));
            }
        }

        [Test]
        public void Equals_MetadataUnderDifferentKeys_AreNotEqual()
        {
            // Arrange
            var a = CreateChunk() with { Metadata = ChunkMetadata.Empty.With("Source", "doc.txt") };
            var b = CreateChunk() with { Metadata = ChunkMetadata.Empty.With("Origin", "doc.txt") };

            // Act & Assert
            Assert.That(a, Is.Not.EqualTo(b));
        }

        [Test]
        public void Equals_OneChunkCarryingAnExtraMetadataEntry_AreNotEqual()
        {
            // Arrange
            var metadata = ChunkMetadata.Empty.With("Source", "doc.txt");
            var a = CreateChunk() with { Metadata = metadata };
            var b = CreateChunk() with { Metadata = metadata.With("Page", 4) };

            // Act & Assert
            Assert.That(a, Is.Not.EqualTo(b));
        }

        [Test]
        public void Equals_MetadataPresentOnOnlyOneChunk_AreNotEqual()
        {
            // Arrange
            var withMetadata = CreateChunk() with { Metadata = ChunkMetadata.Empty.With("Source", "doc.txt") };
            var without = CreateChunk();

            // Act & Assert
            Assert.That(withMetadata, Is.Not.EqualTo(without));
        }

        [Test]
        public void WithExpression_CopiedChunk_SharesTheImmutableMetadataInstance()
        {
            // Arrange - no copy constructor is needed, because sharing is safe when ChunkMetadata cannot change.
            var chunk = CreateChunk() with { Metadata = ChunkMetadata.Empty.With("Source", "doc.txt") };

            // Act
            var copy = chunk with { Index = 1 };

            // Assert
            Assert.That(copy.Metadata, Is.SameAs(chunk.Metadata));
        }

        [Test]
        public void Metadata_AssignedNull_IsCoercedToEmpty()
        {
            // Arrange & Act
            var chunk = CreateChunk() with { Metadata = null! };

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(chunk.Metadata, Is.SameAs(ChunkMetadata.Empty));
                Assert.That(chunk, Is.EqualTo(CreateChunk()));
            }
        }
    }
}
