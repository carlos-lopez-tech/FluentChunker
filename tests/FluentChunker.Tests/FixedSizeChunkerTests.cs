using FluentChunker.Chunkers.FixedSize;
using FluentChunker.Contracts;
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
        public async Task Chunk_TextShorterThanMaxTokens_ReturnsSingleChunk()
        {
            // Arrange
            var chunker = new FixedSizeChunker(_microsoftMLTokenizer, maxTokens: 20, overlap: 0);

            // Act
            var result = await chunker.ChunkAsync(InputText).ToListAsync();

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
        public async Task Chunk_TextExceedsMaxTokens_ReturnsMultipleChunks()
        {
            // Arrange
            var longText = string.Join(" ", Enumerable.Repeat(InputText, 10));
            var chunker = new FixedSizeChunker(_microsoftMLTokenizer, maxTokens: 10, overlap: 0);

            // Act
            var result = await chunker.ChunkAsync(longText).ToListAsync();

            // Assert
            Assert.That(result, Has.Count.GreaterThan(1));
            foreach (var chunk in result)
            {
                Assert.That(chunk.TokenCount, Is.LessThanOrEqualTo(10));
            }
        }

        [Test]
        public async Task Chunk_MultipleChunks_HasSequentialIndices()
        {
            // Arrange
            var longText = string.Join(" ", Enumerable.Repeat("word", 100));
            var chunker = new FixedSizeChunker(_microsoftMLTokenizer, maxTokens: 10, overlap: 0);

            // Act
            var result = await chunker.ChunkAsync(longText).ToListAsync();

            // Assert
            for (var i = 0; i < result.Count; i++)
            {
                Assert.That(result[i].Index, Is.EqualTo(i));
            }
        }

        [Test]
        public async Task Chunk_WithOverlap_ProducesMoreChunksThanWithout()
        {
            // Arrange
            var longText = string.Join(" ", Enumerable.Repeat(InputText, 10));
            var withoutOverlap = new FixedSizeChunker(_microsoftMLTokenizer, maxTokens: 20, overlap: 0);
            var withOverlap = new FixedSizeChunker(_microsoftMLTokenizer, maxTokens: 20, overlap: 5);            

            // Act
            var resultNoOverlap = await withoutOverlap.ChunkAsync(longText).ToListAsync();
            var resultWithOverlap = await withOverlap.ChunkAsync(longText).ToListAsync();

            // Assert
            Assert.That(resultWithOverlap, Has.Count.GreaterThan(resultNoOverlap.Count));
        }

        [Test]
        public async Task Chunk_WithOverlap_StampsCarriedTokensOnReceivingChunk()
        {
            // Arrange
            var longText = string.Join(" ", Enumerable.Repeat(InputText, 10));
            var chunker = new FixedSizeChunker(_microsoftMLTokenizer, maxTokens: 20, overlap: 5);

            // Act
            var result = await chunker.ChunkAsync(longText).ToListAsync();

            // Assert - OverlapCount reports tokens carried INTO a chunk (aligned with SentenceAwareChunker)
            Assert.That(result, Has.Count.GreaterThan(1));
            Assert.That(result[0].OverlapCount, Is.Zero);
            for (var i = 1; i < result.Count; i++)
            {
                Assert.That(result[i].OverlapCount, Is.EqualTo(5));
            }
        }

        [Test]
        public async Task Chunk_EmptyText_ReturnsEmptyList()
        {
            // Arrange
            var chunker = new FixedSizeChunker(_microsoftMLTokenizer, maxTokens: 10, overlap: 0);

            // Act
            var result = await chunker.ChunkAsync("").ToListAsync();

            // Assert
            Assert.That(result, Is.Empty);
        }

        [Test]
        public async Task Chunk_EmojiDocumentExceedingMaxTokens_ProducesNoReplacementCharacters()
        {
            // Arrange
            var document = string.Concat(Enumerable.Repeat("Hello 👨‍👩‍👧‍👦 family emoji test. ", 40));
            var chunker = new FixedSizeChunker(_microsoftMLTokenizer, maxTokens: 20, overlap: 0);

            // Act
            var result = await chunker.ChunkAsync(document).ToListAsync();

            // Assert
            Assert.That(result, Has.Count.GreaterThan(1));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Where(c => c.Text.Contains('�')), Is.Empty);
                Assert.That(string.Concat(result.Select(c => c.Text)), Is.EqualTo(document));
            }
        }

        [Test]
        public async Task Chunk_EmojiDocumentWithOverlap_KeepsEveryChunkWithinBudget()
        {
            // Arrange
            var document = string.Concat(Enumerable.Repeat("Hello 👨‍👩‍👧‍👦 family emoji test. ", 40));
            var chunker = new FixedSizeChunker(_microsoftMLTokenizer, maxTokens: 20, overlap: 5);

            // Act
            var result = await chunker.ChunkAsync(document).ToListAsync();

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
        public async Task Chunk_MultiByteDocumentWithOverlap_ReportsOffsetsThatSliceBackToEachChunk()
        {
            // Arrange
            var document = string.Concat(Enumerable.Repeat("Hello 👨‍👩‍👧‍👦 family emoji test. ", 40));
            var chunker = new FixedSizeChunker(_microsoftMLTokenizer, maxTokens: 20, overlap: 5);

            // Act
            var result = await chunker.ChunkAsync(document).ToListAsync();

            // Assert
            Assert.That(result, Has.Count.GreaterThan(1));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].SourceStart, Is.Zero);
                Assert.That(result.Where(c => c.SliceOf(document) != c.Text), Is.Empty);
                Assert.That(result.Where(c => c.SourceLength != c.Text.Length), Is.Empty);
                Assert.That(result[^1].SourceStart + result[^1].SourceLength, Is.EqualTo(document.Length));
            }
        }

        // One token per character with an exact round trip, so window math is predictable and the round-trip guard stays quiet.
        // Decode calls are counted because decode work is what the token-window layouts spend per window, which makes it the observable difference between lazy and materialised chunking.
        private sealed class CountingTokenizer : FluentChunker.Tokenizers.ITokenizer
        {
            public int DecodeCalls { get; private set; }

            public IReadOnlyList<int> Encode(string text)
            {
                var tokens = new int[text.Length];

                for (var i = 0; i < text.Length; i++)
                {
                    tokens[i] = text[i];
                }

                return tokens;
            }

            public string Decode(IReadOnlyList<int> tokens)
            {
                DecodeCalls++;
                var decoded = new System.Text.StringBuilder();

                foreach (var token in tokens)
                {
                    decoded.Append((char)token);
                }

                return decoded.ToString();
            }

            public int CountTokens(string text) => text.Length;
        }

        // 24 characters, so an 8-token budget with no overlap cuts exactly three windows.
        private const string ThreeWindowText = "retrieval ranks archives";

        [Test]
        public async Task ChunkAsync_EmptyText_YieldsNothing()
        {
            // Arrange
            var chunker = new FixedSizeChunker(new CountingTokenizer(), maxTokens: 8, overlap: 0);

            // Act
            var result = await chunker.ChunkAsync(string.Empty).ToListAsync();

            // Assert
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void ChunkAsync_PreCancelledToken_ThrowsBeforeFirstChunk()
        {
            // Arrange
            var chunker = new FixedSizeChunker(new CountingTokenizer(), maxTokens: 8, overlap: 0);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var consumed = new List<Chunk>();

            // Act
            async Task Consume()
            {
                await foreach (var chunk in chunker.ChunkAsync(ThreeWindowText, cts.Token))
                {
                    consumed.Add(chunk);
                }
            }

            // Assert
            Assert.ThrowsAsync<OperationCanceledException>(Consume);
            Assert.That(consumed, Is.Empty);
        }

        [Test]
        public void ChunkAsync_TokenCancelledMidEnumeration_StopsBeforeNextChunk()
        {
            // Arrange
            var chunker = new FixedSizeChunker(new CountingTokenizer(), maxTokens: 8, overlap: 0);
            using var cts = new CancellationTokenSource();
            var consumed = new List<Chunk>();

            // Act
            async Task Consume()
            {
                await foreach (var chunk in chunker.ChunkAsync(ThreeWindowText, cts.Token))
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
        public void ChunkAsync_TokenSuppliedThroughWithCancellation_ThrowsBeforeFirstChunk()
        {
            // Arrange
            var chunker = new FixedSizeChunker(new CountingTokenizer(), maxTokens: 8, overlap: 0);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                await foreach (var _ in chunker.ChunkAsync(ThreeWindowText).WithCancellation(cts.Token))
                {
                }
            });
        }

        [Test]
        public async Task ChunkAsync_OnlyFirstChunkConsumed_DecodesFewerWindowsThanFullEnumeration()
        {
            // Arrange
            var partialTokenizer = new CountingTokenizer();
            var fullTokenizer = new CountingTokenizer();
            var text = "the archive splitter feeds embeddings to the index";

            // Act
            await foreach (var _ in new FixedSizeChunker(partialTokenizer, maxTokens: 8, overlap: 0).ChunkAsync(text))
            {
                break;
            }
            await new FixedSizeChunker(fullTokenizer, maxTokens: 8, overlap: 0).ChunkAsync(text).ToListAsync();

            // Assert
            Assert.That(partialTokenizer.DecodeCalls, Is.GreaterThan(0));
            Assert.That(partialTokenizer.DecodeCalls, Is.LessThan(fullTokenizer.DecodeCalls));
        }

        // Single-segment fixtures for streamed-vs-in-memory equivalence, sized well below the default capacity.
        private const string AsciiStreamDoc = "The archive group compared chunking strategies for retrieval quality. Overlapping chunks preserved context across boundaries. Reranking improved the final answer set.";
        private const string MultilingualStreamDoc = "The archive opens with English notes. 这是关于分块的中文句子。هذه جملة عن الفهرسة. यह एक हिंदी वाक्य है। A final English sentence closes it.";

        [TestCase(0, AsciiStreamDoc)]
        [TestCase(6, AsciiStreamDoc)]
        [TestCase(0, MultilingualStreamDoc)]
        [TestCase(6, MultilingualStreamDoc)]
        public async Task ChunkAsync_ReaderOverSingleSegmentDocument_MatchesInMemoryFieldForField(int overlap, string document)
        {
            // Arrange
            var chunker = new FixedSizeChunker(_microsoftMLTokenizer, maxTokens: 16, overlap: overlap);
            using var reader = new StringReader(document);

            // Act
            var inMemory = await chunker.ChunkAsync(document).ToListAsync();
            var streamed = await chunker.ChunkAsync(reader).ToListAsync();

            // Assert - chunks are value-equal records, so one comparison covers every field of every chunk
            Assert.That(streamed, Is.EqualTo(inMemory));
        }

        [Test]
        public async Task ChunkAsync_ReaderAcrossSegments_CutsAtWhitespaceSoWordsNeverStraddleSeams()
        {
            // Arrange - capacity 16 buffers "archive notes ra", so the seam must retreat to the space after "notes"
            var chunker = new FixedSizeChunker(new CountingTokenizer(), maxTokens: 8, overlap: 0, segmentCapacity: 16);
            using var reader = new StringReader("archive notes rank");

            // Act
            var result = await chunker.ChunkAsync(reader).ToListAsync();

            // Assert
            Assert.That(result, Has.Count.EqualTo(3));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].Text, Is.EqualTo("archive "));
                Assert.That(result[1].Text, Is.EqualTo("notes "));
                Assert.That(result[2].Text, Is.EqualTo("rank"));
                Assert.That(result[0].SourceStart, Is.EqualTo(0));
                Assert.That(result[1].SourceStart, Is.EqualTo(8));
                Assert.That(result[2].SourceStart, Is.EqualTo(14));
                Assert.That(result[2].Index, Is.EqualTo(2));
            }
        }

        [Test]
        public async Task ChunkAsync_ReaderAcrossSegments_ResetsOverlapAtTheSeamAndKeepsIndexContinuity()
        {
            // Arrange - overlap 3 flows between windows inside the first segment and must be 0 on the chunk that opens the second
            var chunker = new FixedSizeChunker(new CountingTokenizer(), maxTokens: 8, overlap: 3, segmentCapacity: 16);
            using var reader = new StringReader("archive notes rank");

            // Act
            var result = await chunker.ChunkAsync(reader).ToListAsync();

            // Assert - segment one is "archive notes " (14 tokens): windows at 0, 5 and 10 with overlaps 0, 3, 3; segment two is "rank"
            Assert.That(result, Has.Count.EqualTo(4));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[1].OverlapCount, Is.EqualTo(3));
                Assert.That(result[2].OverlapCount, Is.EqualTo(3));
                Assert.That(result[3].OverlapCount, Is.Zero);
                Assert.That(result[3].Index, Is.EqualTo(3));
            }
        }

        [Test]
        public async Task ChunkAsync_ReaderWithNoWhitespaceInABufferFull_HardCutsAtCapacity()
        {
            // Arrange - bounded memory outranks word integrity when a full buffer holds no whitespace at all
            var chunker = new FixedSizeChunker(new CountingTokenizer(), maxTokens: 8, overlap: 0, segmentCapacity: 16);
            using var reader = new StringReader("archiveindexstore");

            // Act
            var result = await chunker.ChunkAsync(reader).ToListAsync();

            // Assert
            Assert.That(result, Has.Count.EqualTo(3));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].Text, Is.EqualTo("archivei"));
                Assert.That(result[1].Text, Is.EqualTo("ndexstor"));
                Assert.That(result[2].Text, Is.EqualTo("e"));
                Assert.That(result[2].SourceStart, Is.EqualTo(16));
            }
        }

        [Test]
        public void ChunkAsync_ReaderPreCancelledToken_ThrowsBeforeFirstChunk()
        {
            // Arrange
            var chunker = new FixedSizeChunker(new CountingTokenizer(), maxTokens: 8, overlap: 0);
            using var reader = new StringReader(ThreeWindowText);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var consumed = new List<Chunk>();

            // Act
            async Task Consume()
            {
                await foreach (var chunk in chunker.ChunkAsync(reader, cts.Token))
                {
                    consumed.Add(chunk);
                }
            }

            // Assert
            Assert.ThrowsAsync<OperationCanceledException>(Consume);
            Assert.That(consumed, Is.Empty);
        }

        [Test]
        public void ChunkAsync_ReaderTokenCancelledMidEnumeration_StopsBeforeNextChunk()
        {
            // Arrange
            var chunker = new FixedSizeChunker(new CountingTokenizer(), maxTokens: 8, overlap: 0);
            using var reader = new StringReader(ThreeWindowText);
            using var cts = new CancellationTokenSource();
            var consumed = new List<Chunk>();

            // Act
            async Task Consume()
            {
                await foreach (var chunk in chunker.ChunkAsync(reader, cts.Token))
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
        public async Task ChunkAsync_ReaderOverLongStream_NeverReadsFarAheadOfTheYieldedChunks()
        {
            // Arrange: the tripwire against any future ReadToEnd shortcut.
            // A non-progressing regression here would busy-loop with no I/O, so bound the run and let it time out instead of hang.
            var reader = new MeteredRepeatingReader("the archive indexes chunks ", totalChars: 10_000);
            var chunker = new FixedSizeChunker(new CountingTokenizer(), maxTokens: 8, overlap: 0, segmentCapacity: 64);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // Act & Assert - the window fills at most one buffer ahead of what it has handed out, so reading leads the yielded chunks by less than one capacity.
            // The +128 bound (2x capacity) leaves slack rather than pinning that exactly, checked at every yield, so a violation names the first chunk that broke the bound.
            await foreach (var chunk in chunker.ChunkAsync(reader, cts.Token))
            {
                Assert.That(reader.CharsProvided, Is.LessThanOrEqualTo(chunk.SourceStart + chunk.SourceLength + 128));
            }
        }

        [Test]
        public async Task ChunkAsync_ReaderOnlyFirstChunkConsumed_ReadsAtMostTwoBuffers()
        {
            // Arrange - bound the run: a non-progressing regression here would busy-loop with no I/O and would otherwise hang instead of failing.
            var reader = new MeteredRepeatingReader("the archive indexes chunks ", totalChars: 10_000);
            var chunker = new FixedSizeChunker(new CountingTokenizer(), maxTokens: 8, overlap: 0, segmentCapacity: 64);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // Act
            await foreach (var _ in chunker.ChunkAsync(reader, cts.Token))
            {
                break;
            }

            // Assert
            Assert.That(reader.CharsProvided, Is.LessThanOrEqualTo(128));
        }

        [Test]
        public async Task ChunkAsync_ReaderWithSeededBasePosition_EmitsExactOffsetsBeyondIntMaxValue()
        {
            // Arrange: the seeded start position lets CI prove the long arithmetic without streaming gigabytes
            var chunker = new FixedSizeChunker(new CountingTokenizer(), maxTokens: 8, overlap: 0, streamBasePosition: 2_147_483_648);
            using var reader = new StringReader(ThreeWindowText);

            // Act
            var result = await chunker.ChunkAsync(reader).ToListAsync();

            // Assert
            Assert.That(result, Has.Count.EqualTo(3));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].SourceStart, Is.EqualTo(2_147_483_648));
                Assert.That(result[1].SourceStart, Is.EqualTo(2_147_483_656));
                Assert.That(result[2].SourceStart, Is.EqualTo(2_147_483_664));
                Assert.That(result[2].Index, Is.EqualTo(2));
            }
        }
    }
}
