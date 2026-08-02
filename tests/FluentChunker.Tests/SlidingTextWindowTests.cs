using System.Text;
using FluentChunker.Chunkers;

namespace FluentChunker.Tests
{
    public class SlidingTextWindowTests
    {
        // Returns one char per Read call, so a fill only reaches capacity if something loops over the partial reads, which ReadBlockAsync does inside the BCL.
        // Overriding Read(char[], int, int) is enough: the TextReader base class routes ReadBlockAsync(Memory<char>, ...) through it.
        private sealed class OneCharAtATimeReader(string text) : TextReader
        {
            private int _position;

            public override int Read(char[] buffer, int index, int count)
            {
                if (_position >= text.Length)
                    return 0;

                buffer[index] = text[_position++];
                return 1;
            }
        }

        [Test]
        public void Constructor_NullReader_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => _ = new SlidingTextWindow(null!, capacity: 8));
        }

        [Test]
        public void Constructor_CapacityBelowTwo_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            using var reader = new StringReader("archive");

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SlidingTextWindow(reader, capacity: 1));
        }

        [Test]
        public void Constructor_NegativeBasePosition_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            using var reader = new StringReader("archive");

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SlidingTextWindow(reader, capacity: 8, basePosition: -1));
        }

        [Test]
        public async Task FillAsync_ReaderReturningOneCharPerRead_LoopsUntilFull()
        {
            // Arrange
            using var reader = new OneCharAtATimeReader("retrieval pipeline");
            using var window = new SlidingTextWindow(reader, capacity: 8);

            // Act
            await window.FillAsync(CancellationToken.None);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(window.PeekSegment(), Is.EqualTo("retrieva"));
                Assert.That(window.EndOfStream, Is.False);
            }
        }

        [Test]
        public async Task FillAsync_InputShorterThanCapacity_ReadsAllAndSetsEndOfStream()
        {
            // Arrange
            using var reader = new StringReader("corpus");
            using var window = new SlidingTextWindow(reader, capacity: 8);

            // Act
            await window.FillAsync(CancellationToken.None);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(window.PeekSegment(), Is.EqualTo("corpus"));
                Assert.That(window.EndOfStream, Is.True);
            }
        }

        [Test]
        public async Task Consume_PartOfSegment_CompactsTailAndAdvancesBasePosition()
        {
            // Arrange
            using var reader = new StringReader("retrieval pipeline");
            using var window = new SlidingTextWindow(reader, capacity: 8);
            await window.FillAsync(CancellationToken.None);

            // Act: consume "retri", keep "eva", then refill on top of the compacted tail
            window.Consume(5);
            var basePositionAfterConsume = window.BasePosition;
            await window.FillAsync(CancellationToken.None);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(basePositionAfterConsume, Is.EqualTo(5));
                Assert.That(window.PeekSegment(), Is.EqualTo("eval pip"));
            }
        }

        [Test]
        public async Task BasePosition_SeededNearIntMaxValue_StaysExactAcrossConsumeCycles()
        {
            // Arrange: seeding is a test-only seam; production always starts at 0
            using var reader = new StringReader("archive index store");
            using var window = new SlidingTextWindow(reader, capacity: 8, basePosition: 2_147_483_642);

            // Act
            await window.FillAsync(CancellationToken.None);
            window.Consume(8);
            var afterFirstCycle = window.BasePosition;
            await window.FillAsync(CancellationToken.None);
            window.Consume(8);

            // Assert: 2,147,483,642 + 8 crosses int.MaxValue (2,147,483,647) exactly as long arithmetic
            using (Assert.EnterMultipleScope())
            {
                Assert.That(afterFirstCycle, Is.EqualTo(2_147_483_650));
                Assert.That(window.BasePosition, Is.EqualTo(2_147_483_658));
            }
        }

        [Test]
        public void FillAsync_PreCancelledToken_ThrowsOperationCanceledException()
        {
            // Arrange
            using var reader = new StringReader("archive");
            using var window = new SlidingTextWindow(reader, capacity: 8);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            Assert.ThrowsAsync<OperationCanceledException>(async () => await window.FillAsync(cts.Token));
        }

        [Test]
        public async Task PeekSegment_HighSurrogateAtCapacityEdge_HoldsItBack()
        {
            // Arrange: "archive" is 7 chars, so the 8th buffer slot lands on the emoji's high surrogate
            using var reader = new StringReader("archive😀 log");
            using var window = new SlidingTextWindow(reader, capacity: 8);

            // Act
            await window.FillAsync(CancellationToken.None);
            var firstSegment = window.PeekSegment();
            window.Consume(firstSegment.Length);
            await window.FillAsync(CancellationToken.None);
            var secondSegment = window.PeekSegment();

            // Assert: the pair is never split, held back first, reunited in the next segment
            using (Assert.EnterMultipleScope())
            {
                Assert.That(firstSegment, Is.EqualTo("archive"));
                Assert.That(secondSegment, Does.StartWith("😀"));
            }
        }

        [Test]
        public async Task PeekSegment_CarriageReturnAtCapacityEdge_HoldsItBack()
        {
            // Arrange: "archive" is 7 chars, so the 8th buffer slot lands on the '\r' of "\r\n"
            using var reader = new StringReader("archive\r\nindex");
            using var window = new SlidingTextWindow(reader, capacity: 8);

            // Act
            await window.FillAsync(CancellationToken.None);
            var firstSegment = window.PeekSegment();
            window.Consume(firstSegment.Length);
            await window.FillAsync(CancellationToken.None);
            var secondSegment = window.PeekSegment();

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(firstSegment, Is.EqualTo("archive"));
                Assert.That(secondSegment, Is.EqualTo("\r\nindex"));
            }
        }

        [Test]
        public async Task PeekSegment_DanglingCarriageReturnAtEndOfStream_IsExposed()
        {
            // Arrange: at EOF a trailing '\r' is document content, not a pair opener
            using var reader = new StringReader("archive\r");
            using var window = new SlidingTextWindow(reader, capacity: 16);

            // Act
            await window.FillAsync(CancellationToken.None);

            // Assert
            Assert.That(window.PeekSegment(), Is.EqualTo("archive\r"));
        }

        [Test]
        public async Task PeekSegment_LoneHighSurrogateAtEndOfStream_IsExposed()
        {
            // Arrange: malformed input ending on a lone high surrogate must still drain
            using var reader = new StringReader("index\uD83D");
            using var window = new SlidingTextWindow(reader, capacity: 16);

            // Act
            await window.FillAsync(CancellationToken.None);

            // Assert
            Assert.That(window.PeekSegment(), Is.EqualTo("index\uD83D"));
        }

        [Test]
        public async Task PeekSegment_TinyCapacityCycle_ReassemblesTheStreamExactly()
        {
            // Arrange: hold-backs may only defer chars, never lose or duplicate them, whatever the alignment
            const string Input = "archive log\r\nretrieval 😀 embeddings\r\n";
            using var reader = new StringReader(Input);
            using var window = new SlidingTextWindow(reader, capacity: 8);
            var reassembled = new StringBuilder();

            // Act: consume whole segments until the stream is drained
            while (true)
            {
                await window.FillAsync(CancellationToken.None);
                var segment = window.PeekSegment();
                if (segment.Length == 0)
                    break;
                reassembled.Append(segment);
                window.Consume(segment.Length);
            }

            // Assert
            Assert.That(reassembled.ToString(), Is.EqualTo(Input));
        }
    }
}
