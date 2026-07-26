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
            _microsoftMLTokenizer = new MicrosoftMLTokenizerAdapter("cl100k_base");
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

            // Assert - OverlapCount reports tokens carried INTO a chunk (aligned with SentenceAwareChunker)
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

        [Test]
        public void Chunk_EmojiDocumentExceedingMaxTokens_ProducesNoReplacementCharacters()
        {
            // Arrange
            var document = string.Concat(Enumerable.Repeat("Hello 👨‍👩‍👧‍👦 family emoji test. ", 40));
            var chunker = new FixedSizeChunker(_microsoftMLTokenizer, maxTokens: 20, overlap: 0);

            // Act
            var result = chunker.Chunk(document);

            // Assert
            Assert.That(result, Has.Count.GreaterThan(1));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Where(c => c.Text.Contains('�')), Is.Empty);
                Assert.That(string.Concat(result.Select(c => c.Text)), Is.EqualTo(document));
            }
        }

        [Test]
        public void Chunk_EmojiDocumentWithOverlap_KeepsEveryChunkWithinBudget()
        {
            // Arrange
            var document = string.Concat(Enumerable.Repeat("Hello 👨‍👩‍👧‍👦 family emoji test. ", 40));
            var chunker = new FixedSizeChunker(_microsoftMLTokenizer, maxTokens: 20, overlap: 5);

            // Act
            var result = chunker.Chunk(document);

            // Assert
            Assert.That(result, Has.Count.GreaterThan(1));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Select(c => c.TokenCount), Has.All.LessThanOrEqualTo(20));
                Assert.That(result.Where(c => c.Text.Contains('�')), Is.Empty);
                Assert.That(result.Select(c => _microsoftMLTokenizer.CountTokens(c.Text)),
                    Is.EqualTo(result.Select(c => c.TokenCount)));
            }
        }

        [Test]
        public void Chunk_MultiByteDocumentWithOverlap_ReportsOffsetsThatSliceBackToEachChunk()
        {
            // Arrange
            var document = string.Concat(Enumerable.Repeat("Hello 👨‍👩‍👧‍👦 family emoji test. ", 40));
            var chunker = new FixedSizeChunker(_microsoftMLTokenizer, maxTokens: 20, overlap: 5);

            // Act
            var result = chunker.Chunk(document);

            // Assert
            Assert.That(result, Has.Count.GreaterThan(1));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].SourceStart, Is.Zero);
                Assert.That(result.Where(c => document.Substring(c.SourceStart, c.SourceLength) != c.Text), Is.Empty);
                Assert.That(result.Where(c => c.SourceLength != c.Text.Length), Is.Empty);
                Assert.That(result[^1].SourceStart + result[^1].SourceLength, Is.EqualTo(document.Length));
            }
        }
    }
}
