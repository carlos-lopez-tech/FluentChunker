using FluentChunker.Chunkers.SentenceAware;
using FluentChunker.Contracts;
using FluentChunker.Tokenizers;

namespace FluentChunker.Tests
{
    public class SentenceAwareChunkerTests
    {
        private const string InputText =
            "The quick brown fox jumps over the fence. A lazy dog sleeps in the sun. " +
            "Cats watch quietly from the roof. Birds sing their morning songs.";

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
            var chunker = new SentenceAwareChunker(_microsoftMLTokenizer, maxTokens: 100, overlap: 0);

            // Act
            var result = await chunker.ChunkAsync(InputText).ToListAsync();

            // Assert
            Assert.That(result, Has.Count.EqualTo(1));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].Index, Is.Zero);
                Assert.That(result[0].Text, Is.EqualTo(InputText));
            }
        }

        [Test]
        public async Task Chunk_TextExceedsMaxTokens_ChunksStayWithinBudget()
        {
            // Arrange
            var chunker = new SentenceAwareChunker(_microsoftMLTokenizer, maxTokens: 15, overlap: 0);

            // Act
            var result = await chunker.ChunkAsync(InputText).ToListAsync();

            // Assert
            Assert.That(result, Has.Count.GreaterThan(1));
            foreach (var chunk in result)
            {
                Assert.That(chunk.TokenCount, Is.LessThanOrEqualTo(15));
            }
        }

        [Test]
        public async Task Chunk_SentenceCheaperWithItsPrecedingSpace_StaysWithinBudget()
        {
            // Arrange - " Demonstrated words follow after it." costs 7 tokens, but a chunk starting at that sentence drops the space and costs 8
            const string text = "The opening sentence stands here. Demonstrated words follow after it.";
            var chunker = new SentenceAwareChunker(_microsoftMLTokenizer, maxTokens: 7, overlap: 0);

            // Act
            var result = await chunker.ChunkAsync(text).ToListAsync();

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result, Is.Not.Empty);
                Assert.That(result.Select(c => _microsoftMLTokenizer.CountTokens(c.Text)),
                    Has.All.LessThanOrEqualTo(7));
            }
        }

        [Test]
        public async Task Chunk_MultipleSentences_NeverCutsMidSentence()
        {
            // Arrange
            var chunker = new SentenceAwareChunker(_microsoftMLTokenizer, maxTokens: 15, overlap: 0);

            // Act
            var result = await chunker.ChunkAsync(InputText).ToListAsync();

            // Assert - every chunk ends exactly where a sentence ends
            foreach (var chunk in result)
            {
                Assert.That(chunk.Text, Does.EndWith("."));
            }
        }

        [Test]
        public async Task Chunk_ChunkText_IsVerbatimSliceOfSource()
        {
            // Arrange
            var textWithParagraphs =
                "First paragraph sentence one. First paragraph sentence two.\n\n" +
                "Second paragraph sentence one. Second paragraph sentence two.";
            var chunker = new SentenceAwareChunker(_microsoftMLTokenizer, maxTokens: 20, overlap: 0);

            // Act
            var result = await chunker.ChunkAsync(textWithParagraphs).ToListAsync();

            // Assert - chunk text is copied verbatim from the source, whitespace included
            foreach (var chunk in result)
            {
                Assert.That(textWithParagraphs, Does.Contain(chunk.Text));
            }
        }

        [Test]
        public async Task Chunk_EmptyText_ReturnsEmptyList()
        {
            // Arrange
            var chunker = new SentenceAwareChunker(_microsoftMLTokenizer, maxTokens: 10, overlap: 0);

            // Act
            var result = await chunker.ChunkAsync("").ToListAsync();

            // Assert
            Assert.That(result, Is.Empty);
        }

        [Test]
        public async Task Chunk_MultipleChunks_HasSequentialIndices()
        {
            // Arrange
            var chunker = new SentenceAwareChunker(_microsoftMLTokenizer, maxTokens: 15, overlap: 0);

            // Act
            var result = await chunker.ChunkAsync(InputText).ToListAsync();

            // Assert
            for (var i = 0; i < result.Count; i++)
            {
                Assert.That(result[i].Index, Is.EqualTo(i));
            }
        }

        [Test]
        public async Task Chunk_WithOverlap_CarriesWholeTrailingSentences()
        {
            // Arrange - real gap-inclusive token counts for InputText's 4 sentences are 9, 8, 7, 6 (verified against MicrosoftMLTokenizerAdapter/cl100k_base); maxTokens 20 packs 2 sentences per chunk and overlap 12 always carries exactly the last sentence (any 2 sentences sum >= 13)
            var chunker = new SentenceAwareChunker(_microsoftMLTokenizer, maxTokens: 20, overlap: 12);

            // Act
            var result = await chunker.ChunkAsync(InputText).ToListAsync();

            // Assert - every non-first chunk starts with a sentence repeated from the previous chunk
            Assert.That(result, Has.Count.GreaterThan(1));
            for (var i = 1; i < result.Count; i++)
            {
                Assert.That(result[i].OverlapCount, Is.GreaterThan(0));

                var firstSentenceOfCurrent = result[i].Text.Split('.')[0] + ".";
                Assert.That(result[i - 1].Text, Does.Contain(firstSentenceOfCurrent));
            }
        }

        [Test]
        public async Task Chunk_OverlapBudgetSmallerThanLastSentence_CarriesNothing()
        {
            // Arrange - every sentence in InputText is 6-9 gap-inclusive tokens; a budget of 2 fits none
            var chunker = new SentenceAwareChunker(_microsoftMLTokenizer, maxTokens: 20, overlap: 2);

            // Act
            var result = await chunker.ChunkAsync(InputText).ToListAsync();

            // Assert
            Assert.That(result, Has.Count.GreaterThan(1));
            foreach (var chunk in result)
            {
                Assert.That(chunk.OverlapCount, Is.Zero);
            }
        }

        [Test]
        public async Task Chunk_WithOverlap_AllChunksStayWithinBudget()
        {
            // Arrange - overlap close to maxTokens stresses the seed-dropping edge case
            var chunker = new SentenceAwareChunker(_microsoftMLTokenizer, maxTokens: 15, overlap: 14);

            // Act
            var result = await chunker.ChunkAsync(InputText).ToListAsync();

            // Assert - the budget invariant always wins over overlap
            foreach (var chunk in result)
            {
                Assert.That(chunk.TokenCount, Is.LessThanOrEqualTo(15));
            }
        }

        [Test]
        public async Task Chunk_FirstChunk_HasZeroOverlapCount()
        {
            // Arrange - same parameters as Chunk_WithOverlap_CarriesWholeTrailingSentences, which produces multiple chunks; the first one must never receive carried-over sentences
            var chunker = new SentenceAwareChunker(_microsoftMLTokenizer, maxTokens: 20, overlap: 12);

            // Act
            var result = await chunker.ChunkAsync(InputText).ToListAsync();

            // Assert
            Assert.That(result[0].OverlapCount, Is.Zero);
        }

        [Test]
        public async Task Chunk_OversizedSentence_FallsBackToTokenWindows()
        {
            // Arrange - one giant "sentence" with no terminator, far beyond the budget
            var giantSentence = string.Join(" ", Enumerable.Repeat("word", 100));
            var chunker = new SentenceAwareChunker(_microsoftMLTokenizer, maxTokens: 10, overlap: 0);

            // Act
            var result = await chunker.ChunkAsync(giantSentence).ToListAsync();

            // Assert
            Assert.That(result, Has.Count.GreaterThan(1));
            foreach (var chunk in result)
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(chunk.TokenCount, Is.LessThanOrEqualTo(10));
                    Assert.That(chunk.OverlapCount, Is.Zero);
                }
            }
        }

        [Test]
        public async Task Chunk_OversizedSentenceBetweenNormalSentences_ResumesPacking()
        {
            // Arrange - the giant sentence starts uppercase so the boundary before it is detected
            var giantSentence = string.Join(" ", Enumerable.Repeat("Word", 100)) + ".";
            var text = $"Short opener here. {giantSentence} Short closer here.";
            var chunker = new SentenceAwareChunker(_microsoftMLTokenizer, maxTokens: 10, overlap: 0);

            // Act
            var result = await chunker.ChunkAsync(text).ToListAsync();

            // Assert - first and last chunks are the intact short sentences
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].Text, Is.EqualTo("Short opener here."));
                Assert.That(result[^1].Text, Is.EqualTo("Short closer here."));
                foreach (var chunk in result)
                {
                    Assert.That(chunk.TokenCount, Is.LessThanOrEqualTo(10));
                }
            }
        }

        [Test]
        public async Task Chunk_OversizedSentenceWithOverlapConfigured_ResetsOverlapAcrossFallback()
        {
            // Arrange - overlap > 0 configured; an oversized sentence in the middle must reset overlap state, not leak stale carryover across the fallback boundary
            var giantSentence = string.Join(" ", Enumerable.Repeat("Giant", 100)) + ".";
            var text = "Short opener here. Another short one follows. " +
                $"{giantSentence} " +
                "Final short sentence here. One more to close.";
            var chunker = new SentenceAwareChunker(_microsoftMLTokenizer, maxTokens: 10, overlap: 8);

            // Act
            var resultList = await chunker.ChunkAsync(text).ToListAsync();

            // Assert - every fallback window (from the giant sentence) carries zero overlap
            var fallbackIndices = new List<int>();
            for (var i = 0; i < resultList.Count; i++)
            {
                if (resultList[i].Text.Contains("Giant"))
                    fallbackIndices.Add(i);
            }

            Assert.That(fallbackIndices, Is.Not.Empty);
            foreach (var i in fallbackIndices)
            {
                Assert.That(resultList[i].OverlapCount, Is.Zero);
            }

            // ...and the first normal chunk resumed after the fallback also starts with zero overlap, since there's nothing valid to carry across a fallback break
            var lastFallbackIndex = fallbackIndices[^1];
            using (Assert.EnterMultipleScope())
            {
                Assert.That(lastFallbackIndex, Is.LessThan(resultList.Count - 1));
                Assert.That(resultList[lastFallbackIndex + 1].OverlapCount, Is.Zero);
            }
        }

        [Test]
        public async Task Chunk_TwoConsecutiveOversizedSentences_EmitsWindowsForBoth()
        {
            // Arrange - two back-to-back oversized sentences; each must get its own independent set of fallback windows with no cross-contamination between them
            var giantSentence1 = string.Join(" ", Enumerable.Repeat("Alpha", 100)) + ".";
            var giantSentence2 = string.Join(" ", Enumerable.Repeat("Beta", 100)) + ".";
            var text = $"{giantSentence1} {giantSentence2}";
            var chunker = new SentenceAwareChunker(_microsoftMLTokenizer, maxTokens: 10, overlap: 0);

            // Act
            var result = await chunker.ChunkAsync(text).ToListAsync();

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result, Has.Count.GreaterThan(2));
                foreach (var chunk in result)
                {
                    Assert.That(chunk.TokenCount, Is.LessThanOrEqualTo(10));
                    Assert.That(chunk.OverlapCount, Is.Zero);
                    Assert.That(chunk.Text.Contains("Alpha") && chunk.Text.Contains("Beta"), Is.False);
                }
            }
        }

        [Test]
        public async Task Chunk_OversizedSentence_EmitsBalancedWindowsWithoutTinyRemainder()
        {
            // Arrange - 61 tokens at a budget of 10 would leave a useless 1-token "." chunk with naive fixed-stride windowing; balanced windows spread the tokens evenly
            var giantSentence = string.Join(" ", Enumerable.Repeat("Word", 60)) + ".";
            var chunker = new SentenceAwareChunker(_microsoftMLTokenizer, maxTokens: 10, overlap: 0);

            // Act
            var result = await chunker.ChunkAsync(giantSentence).ToListAsync();

            // Assert - every window stays within budget and none is a tiny remainder
            Assert.That(result, Has.Count.GreaterThan(1));
            foreach (var chunk in result)
            {
                Assert.That(chunk.TokenCount, Is.LessThanOrEqualTo(10));
                Assert.That(chunk.TokenCount, Is.GreaterThanOrEqualTo(5));
            }
        }

        // One token per character with an exact round trip, so packing math is predictable and the round-trip guard stays quiet.
        private sealed class CharTokenizer : FluentChunker.Tokenizers.ITokenizer
        {
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
                var decoded = new System.Text.StringBuilder();

                foreach (var token in tokens)
                {
                    decoded.Append((char)token);
                }

                return decoded.ToString();
            }

            public int CountTokens(string text) => text.Length;
        }

        // Three sentences whose gap-inclusive char counts are 13, 15 and 17, so a 17-token budget packs exactly one sentence per chunk.
        private const string ThreeSentenceText = "Index a file. Rank the hits. Cite the source.";

        [Test]
        public async Task ChunkAsync_EmptyText_YieldsNothing()
        {
            // Arrange
            var chunker = new SentenceAwareChunker(new CharTokenizer(), maxTokens: 17, overlap: 0);

            // Act
            var result = await chunker.ChunkAsync(string.Empty).ToListAsync();

            // Assert
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void ChunkAsync_PreCancelledToken_ThrowsBeforeFirstChunk()
        {
            // Arrange
            var chunker = new SentenceAwareChunker(new CharTokenizer(), maxTokens: 17, overlap: 0);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var consumed = new List<Chunk>();

            // Act
            async Task Consume()
            {
                await foreach (var chunk in chunker.ChunkAsync(ThreeSentenceText, cts.Token))
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
            var chunker = new SentenceAwareChunker(new CharTokenizer(), maxTokens: 17, overlap: 0);
            using var cts = new CancellationTokenSource();
            var consumed = new List<Chunk>();

            // Act
            async Task Consume()
            {
                await foreach (var chunk in chunker.ChunkAsync(ThreeSentenceText, cts.Token))
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
        public void ChunkAsync_NullReader_ThrowsArgumentNullExceptionAtCallTime()
        {
            // Arrange
            var chunker = new SentenceAwareChunker(new CharTokenizer(), maxTokens: 17, overlap: 0);

            // Act & Assert - the wrapper must throw before the first MoveNextAsync, not lazily inside the iterator
            Assert.Throws<ArgumentNullException>(() => chunker.ChunkAsync((TextReader)null!));
        }

        // Single-segment fixtures for streamed-vs-in-memory equivalence, sized well below the default capacity.
        private const string ThreePackedSentences = "The archive stores embeddings. Retrieval ranks the chunks. Reranking refines the answers.";
        private const string OversizedMiddleSentence = "Chunks flow. The archive pipeline tokenizes, embeds, indexes, ranks, rescores and finally cites every retrieved passage across the corpus. Done.";

        [TestCase(0)]
        [TestCase(8)]
        public async Task ChunkAsync_ReaderOverSingleSegmentDocument_MatchesInMemoryFieldForField(int overlap)
        {
            // Arrange - measured with cl100k_base: gap-inclusive per-sentence counts are 5, 7, 8 (19 total), so maxTokens 16 packs the first two sentences (12 tokens) into one chunk and forces a second.
            // Overlap 8 then carries sentence 1 (7 tokens) into that second chunk, so the overlap 8 case actually exercises OverlapCount, unlike a budget loose enough to fit all three in one.
            var chunker = new SentenceAwareChunker(new MicrosoftMLTokenizerAdapter("cl100k_base"), maxTokens: 16, overlap: overlap);
            using var reader = new StringReader(ThreePackedSentences);

            // Act
            var inMemory = await chunker.ChunkAsync(ThreePackedSentences).ToListAsync();
            var streamed = await chunker.ChunkAsync(reader).ToListAsync();

            // Assert - value-equal records compare every field of every chunk, overlap stamps included.
            // The count and overlap assertions below guard the fixture itself,
            // so a future tokenizer or fixture change cannot silently collapse this back to a single-chunk, zero-overlap comparison without failing.
            using (Assert.EnterMultipleScope())
            {
                Assert.That(streamed, Is.EqualTo(inMemory));
                Assert.That(streamed, Has.Count.GreaterThan(1));
                if (overlap == 8)
                    Assert.That(streamed.Select(c => c.OverlapCount), Has.Some.GreaterThan(0));
            }
        }

        [Test]
        public async Task ChunkAsync_ReaderOverOversizedSentenceInOneSegment_MatchesInMemoryBalancedFallback()
        {
            // Arrange - the middle sentence exceeds the budget but fits the buffer, so the streamed path must produce the same Balanced windows as the in-memory one
            var chunker = new SentenceAwareChunker(new MicrosoftMLTokenizerAdapter("cl100k_base"), maxTokens: 16, overlap: 0);
            using var reader = new StringReader(OversizedMiddleSentence);

            // Act
            var inMemory = await chunker.ChunkAsync(OversizedMiddleSentence).ToListAsync();
            var streamed = await chunker.ChunkAsync(reader).ToListAsync();

            // Assert
            Assert.That(streamed, Is.EqualTo(inMemory));
        }

        [Test]
        public async Task ChunkAsync_ReaderWithSentenceStraddlingTheCapacityEdge_CarriesItWholeIntoTheNextSegment()
        {
            // Arrange - capacity 24 buffers "Index the file. Rank hit", so the second sentence must be carried, never emitted partial
            var chunker = new SentenceAwareChunker(new CharTokenizer(), maxTokens: 16, overlap: 0, segmentCapacity: 24);
            using var reader = new StringReader("Index the file. Rank hits.");

            // Act
            var result = await chunker.ChunkAsync(reader).ToListAsync();

            // Assert
            Assert.That(result, Has.Count.EqualTo(2));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].Text, Is.EqualTo("Index the file."));
                Assert.That(result[1].Text, Is.EqualTo("Rank hits."));
                Assert.That(result[0].SourceStart, Is.EqualTo(0));
                Assert.That(result[1].SourceStart, Is.EqualTo(16));
                Assert.That(result[1].Index, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task ChunkAsync_ReaderWithTerminatorExactlyAtTheCapacityEdge_TreatsTheSegmentAsUnconfirmed()
        {
            // Arrange - "Index the file. " fills capacity 16 exactly; without the next sentence's start in view the split is unconfirmable,
            // so the segment goes through the capacity path whole, trailing space included, a documented seam divergence
            var chunker = new SentenceAwareChunker(new CharTokenizer(), maxTokens: 16, overlap: 0, segmentCapacity: 16);
            using var reader = new StringReader("Index the file. Rank.");

            // Act
            var result = await chunker.ChunkAsync(reader).ToListAsync();

            // Assert
            Assert.That(result, Has.Count.EqualTo(2));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].Text, Is.EqualTo("Index the file. "));
                Assert.That(result[1].Text, Is.EqualTo("Rank."));
                Assert.That(result[0].OverlapCount, Is.Zero);
                Assert.That(result[1].OverlapCount, Is.Zero);
                Assert.That(result[1].SourceStart, Is.EqualTo(16));
            }
        }

        [Test]
        public void ChunkAsync_ReaderPreCancelledToken_ThrowsBeforeFirstChunk()
        {
            // Arrange
            var chunker = new SentenceAwareChunker(new CharTokenizer(), maxTokens: 17, overlap: 0);
            using var reader = new StringReader(ThreeSentenceText);
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
            var chunker = new SentenceAwareChunker(new CharTokenizer(), maxTokens: 17, overlap: 0);
            using var reader = new StringReader(ThreeSentenceText);
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
        public async Task ChunkAsync_ReaderWithSentenceOutgrowingTheCapacity_HardCutsIntoBudgetSizedWindows()
        {
            // Arrange - capacity 17 buffers "archive embedding" as one terminator-less span, 17 tokens against maxTokens 8.
            // 17 does not divide evenly by 8, so Strided (0,8)(8,8)(16,1) and Balanced (0,6)(6,6)(12,5) genuinely diverge here, unlike a capacity of 16, where both layouts collapse to the same 8+8 cut and a Strided-to-Balanced swap in EmitCapacityStretch would pass unnoticed.
            // The asserted texts below are what Strided produces; Balanced would instead yield "archiv", "e embe", "dding".
            var chunker = new SentenceAwareChunker(new CharTokenizer(), maxTokens: 8, overlap: 0, segmentCapacity: 17);
            using var reader = new StringReader("archive embedding rank.");

            // Act
            var result = await chunker.ChunkAsync(reader).ToListAsync();

            // Assert
            Assert.That(result, Has.Count.EqualTo(4));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Select(c => c.Text), Is.EqualTo(new[] { "archive ", "embeddin", "g", "rank." }));
                Assert.That(result.Select(c => c.TokenCount), Is.EqualTo(new[] { 8, 8, 1, 5 }));
                Assert.That(result.Select(c => c.OverlapCount), Is.All.Zero);
                Assert.That(result.Select(c => c.SourceStart), Is.EqualTo(new long[] { 0, 8, 16, 18 }));
                Assert.That(result[3].Index, Is.EqualTo(3));
            }
        }

        [Test]
        public async Task ChunkAsync_ReaderWithTerminatorLessStream_StaysBoundedAndKeepsTheBudget()
        {
            // Arrange - the documented CJK degradation shape: no sentence terminator ever arrives
            // A non-progressing regression here would busy-loop with no I/O, so bound the run and let it time out instead of hang.
            var reader = new MeteredRepeatingReader("archive embeddings ranking corpus ", totalChars: 10_000);
            var chunker = new SentenceAwareChunker(new CharTokenizer(), maxTokens: 8, overlap: 0, segmentCapacity: 64);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // Act & Assert - the window fills at most one buffer ahead of what it has handed out, so reading leads the yielded chunks by less than one capacity.
            // The +128 bound (2x capacity) leaves slack rather than pinning that exactly, while still catching a full-stream materialisation by orders of magnitude, and no chunk ever exceeds the budget.
            await foreach (var chunk in chunker.ChunkAsync(reader, cts.Token))
            {
                Assert.That(reader.CharsProvided, Is.LessThanOrEqualTo(chunk.SourceStart + chunk.SourceLength + 128));
                Assert.That(chunk.TokenCount, Is.LessThanOrEqualTo(8));
            }
        }

        [Test]
        public async Task ChunkAsync_ReaderWithAbbreviationInTheCarriedSpan_KeepsGuardProtectionAcrossTheSeam()
        {
            // Arrange - capacity 24 buffers "Send the file. See Dr. V": the carried span holds "Dr." and must not be falsely split after the seam
            var chunker = new SentenceAwareChunker(new CharTokenizer(), maxTokens: 16, overlap: 0, segmentCapacity: 24);
            using var reader = new StringReader("Send the file. See Dr. Vale.");

            // Act
            var result = await chunker.ChunkAsync(reader).ToListAsync();

            // Assert
            Assert.That(result, Has.Count.EqualTo(2));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].Text, Is.EqualTo("Send the file."));
                Assert.That(result[1].Text, Is.EqualTo("See Dr. Vale."));
                Assert.That(result[1].SourceStart, Is.EqualTo(15));
            }
        }

        [Test]
        public async Task ChunkAsync_ReaderWithWhitespaceOnlyLeadingSegment_EmitsNothingForIt()
        {
            // Arrange - a full buffer of whitespace splits into no sentences and must leave the buffer without a chunk, matching the in-memory path
            var chunker = new SentenceAwareChunker(new CharTokenizer(), maxTokens: 16, overlap: 0, segmentCapacity: 16);
            using var reader = new StringReader(new string(' ', 16) + "Rank on.");

            // Act
            var result = await chunker.ChunkAsync(reader).ToListAsync();

            // Assert
            Assert.That(result, Has.Count.EqualTo(1));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].Text, Is.EqualTo("Rank on."));
                Assert.That(result[0].SourceStart, Is.EqualTo(16));
                Assert.That(result[0].Index, Is.Zero);
            }
        }
    }
}
