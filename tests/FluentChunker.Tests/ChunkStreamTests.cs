using FluentChunker.Chunkers;
using FluentChunker.Contracts;

namespace FluentChunker.Tests
{
    public class ChunkStreamTests
    {
        // The facade never inspects chunk content, so minimal themed chunks keep the fixtures honest without a tokenizer.
        private static Chunk MakeChunk(int index, string text) =>
            new() { Index = index, Text = text, TokenCount = 1 };

        private static List<Chunk> ThreeChunks() =>
        [
            MakeChunk(0, "The archive ingests documents."),
            MakeChunk(1, "The splitter cuts them into chunks."),
            MakeChunk(2, "The index embeds every chunk.")
        ];

        [Test]
        public async Task Over_SourceWithChunks_YieldsAllInOrder()
        {
            // Arrange
            var source = ThreeChunks();

            // Act
            var result = await ChunkStream.Over(source, CancellationToken.None).ToListAsync();

            // Assert
            Assert.That(result, Has.Count.EqualTo(3));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0], Is.EqualTo(source[0]));
                Assert.That(result[1], Is.EqualTo(source[1]));
                Assert.That(result[2], Is.EqualTo(source[2]));
            }
        }

        [Test]
        public async Task Over_EmptySource_YieldsNothing()
        {
            // Arrange & Act
            var result = await ChunkStream.Over([], CancellationToken.None).ToListAsync();

            // Assert
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void Over_PreCancelledToken_ThrowsBeforeFirstChunk()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var consumed = new List<Chunk>();

            // Act
            async Task Consume()
            {
                await foreach (var chunk in ChunkStream.Over(ThreeChunks(), cts.Token))
                {
                    consumed.Add(chunk);
                }
            }

            // Assert
            Assert.ThrowsAsync<OperationCanceledException>(Consume);
            Assert.That(consumed, Is.Empty);
        }

        [Test]
        public void Over_PreCancelledTokenOverEmptySource_StillThrows()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                await foreach (var _ in ChunkStream.Over([], cts.Token))
                {
                }
            });
        }

        [Test]
        public void Over_TokenCancelledMidEnumeration_StopsBeforeNextChunk()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            var consumed = new List<Chunk>();

            // Act
            async Task Consume()
            {
                await foreach (var chunk in ChunkStream.Over(ThreeChunks(), cts.Token))
                {
                    consumed.Add(chunk);
                    cts.Cancel();
                }
            }

            // Assert
            Assert.ThrowsAsync<OperationCanceledException>(Consume);
            Assert.That(consumed, Has.Count.EqualTo(1));
        }

        [Test]
        public void Over_TokenSuppliedThroughWithCancellation_ThrowsBeforeFirstChunk()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var consumed = new List<Chunk>();

            // Act
            async Task Consume()
            {
                await foreach (var chunk in ChunkStream.Over(ThreeChunks(), CancellationToken.None).WithCancellation(cts.Token))
                {
                    consumed.Add(chunk);
                }
            }

            // Assert - EnumeratorCancellation substitutes the consumer's token when the chunker itself was given none
            Assert.ThrowsAsync<OperationCanceledException>(Consume);
            Assert.That(consumed, Is.Empty);
        }

        [Test]
        public void Over_CreationTokenCancelledWhileConsumerTokenLive_StillThrows()
        {
            // Arrange
            using var creation = new CancellationTokenSource();
            using var consumer = new CancellationTokenSource();
            creation.Cancel();
            var consumed = new List<Chunk>();

            // Act
            async Task Consume()
            {
                await foreach (var chunk in ChunkStream.Over(ThreeChunks(), creation.Token).WithCancellation(consumer.Token))
                {
                    consumed.Add(chunk);
                }
            }

            // Assert - the two tokens merge, so WithCancellation never masks the one the stream was created with
            Assert.ThrowsAsync<OperationCanceledException>(Consume);
            Assert.That(consumed, Is.Empty);
        }
    }
}
