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
    }
}
