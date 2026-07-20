using FluentChunker.Chunkers.FixedSize;
using FluentChunker.Tokenizers;

namespace FluentChunker.Tests
{
    public class FixedSizeChunkerTests
    {
        private const string InputText = "A pragmatic developer chooses the least complicated implementation that reliably solves the problem.";
        private MicrosoftMLTokenizerAdapter _microsoftMLTokenizer;

        [SetUp]
        public void SetUp()
        {
            _microsoftMLTokenizer = new MicrosoftMLTokenizerAdapter("o200k_base");
        }

        [Test]
        public void Chunk_TextShorterThanMaxTokens_ReturnsSingleChunk()
        {
            // Arrange
            var chunker = new FixedSizeChunker(_microsoftMLTokenizer, maxTokens: 20, overlap: 0);

            // Act
            var result = chunker.Chunk(InputText);

            // Assert
            Assert.That(result, Has.Count.EqualTo(1));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].Index, Is.Zero);
                Assert.That(result[0].Text, Is.EqualTo(InputText));
                Assert.That(result[0].TokenCount, Is.LessThanOrEqualTo(20));
            }
        }

        [Test]
        public void Chunk_TextExceedsMaxTokens_ReturnsMultipleChunks()
        {
            // Arrange
            var longText = string.Join(" ", Enumerable.Repeat(InputText, 10));
            var chunker = new FixedSizeChunker(_microsoftMLTokenizer, maxTokens: 10, overlap: 0);

            // Act
            var result = chunker.Chunk(longText);

            // Assert
            Assert.That(result, Has.Count.GreaterThan(1));
            foreach (var chunk in result)
            {
                Assert.That(chunk.TokenCount, Is.LessThanOrEqualTo(10));
            }
        }

        [Test]
        public void Chunk_MultipleChunks_HasSequentialIndices()
        {
            // Arrange
            var longText = string.Join(" ", Enumerable.Repeat("word", 100));
            var chunker = new FixedSizeChunker(_microsoftMLTokenizer, maxTokens: 10, overlap: 0);

            // Act
            var result = chunker.Chunk(longText);

            // Assert
            for (var i = 0; i < result.Count; i++)
            {
                Assert.That(result[i].Index, Is.EqualTo(i));
            }
        }

        [Test]
        public void Chunk_WithOverlap_ProducesMoreChunksThanWithout()
        {
            // Arrange
            var longText = string.Join(" ", Enumerable.Repeat(InputText, 10));
            var withoutOverlap = new FixedSizeChunker(_microsoftMLTokenizer, maxTokens: 20, overlap: 0);
            var withOverlap = new FixedSizeChunker(_microsoftMLTokenizer, maxTokens: 20, overlap: 5);            

            // Act
            var resultNoOverlap = withoutOverlap.Chunk(longText);
            var resultWithOverlap = withOverlap.Chunk(longText);

            // Assert
            Assert.That(resultWithOverlap, Has.Count.GreaterThan(resultNoOverlap.Count));
        }

        [Test]
        public void Chunk_WithOverlap_StampsCarriedTokensOnReceivingChunk()
        {
            // Arrange
            var longText = string.Join(" ", Enumerable.Repeat(InputText, 10));
            var chunker = new FixedSizeChunker(_microsoftMLTokenizer, maxTokens: 20, overlap: 5);

            // Act
            var result = chunker.Chunk(longText);

            // Assert — OverlapCount reports tokens carried INTO a chunk (aligned with SentenceAwareChunker)
            Assert.That(result, Has.Count.GreaterThan(1));
            Assert.That(result[0].OverlapCount, Is.Zero);
            for (var i = 1; i < result.Count; i++)
            {
                Assert.That(result[i].OverlapCount, Is.EqualTo(5));
            }
        }

        [Test]
        public void Chunk_EmptyText_ReturnsEmptyList()
        {
            // Arrange
            var chunker = new FixedSizeChunker(_microsoftMLTokenizer, maxTokens: 10, overlap: 0);

            // Act
            var result = chunker.Chunk("");

            // Assert
            Assert.That(result, Is.Empty);
        }
    }
}
