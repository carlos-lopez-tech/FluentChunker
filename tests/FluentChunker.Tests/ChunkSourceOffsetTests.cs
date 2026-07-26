using FluentChunker.Contracts;

namespace FluentChunker.Tests
{
    public class ChunkSourceOffsetTests
    {
        [Test]
        public void SourceOffsets_ChunkConstructedWithoutThem_ReportNotTracked()
        {
            // Arrange & Act - an IChunker written outside this library sets only the required members.
            var chunk = new Chunk { Index = 0, Text = "text", TokenCount = 1 };

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(chunk.SourceStart, Is.EqualTo(-1));
                Assert.That(chunk.SourceLength, Is.EqualTo(-1));
            }
        }

        [Test]
        public void SourceOffsets_SetOnConstruction_AreReportedBack()
        {
            // Arrange & Act
            var chunk = new Chunk { Index = 0, Text = "text", TokenCount = 1, SourceStart = 12, SourceLength = 4 };

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(chunk.SourceStart, Is.EqualTo(12));
                Assert.That(chunk.SourceLength, Is.EqualTo(4));
            }
        }
    }
}
