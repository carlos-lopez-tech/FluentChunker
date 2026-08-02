using FluentChunker.Chunkers.Recursive;
using FluentChunker.Contracts;
using FluentChunker.Tokenizers;

namespace FluentChunker.Tests
{
    public class RecursiveChunkerTests
    {
        // Three small paragraphs (~10 tokens each): any two exceed a 15-token budget, each alone fits it.
        // Themed on the testfile.txt archive/retrieval corpus.
        private const string ParagraphOne = "The team reviewed the reports before the meeting started.";
        private const string ParagraphTwo = "The group compared results from the two search systems.";
        private const string ParagraphThree = "The notes described how context changed across the chunks.";
        private const string ThreeParagraphs =
            ParagraphOne + "\n\n" + ParagraphTwo + "\n\n" + ParagraphThree;

        // One paragraph of three lines (~11 tokens each, ~33 total): oversized for a 15-token budget, while each line alone fits it and no two lines fit together.
        private const string LineOne = "The first note mentioned retrieval quality and chunk overlap.";
        private const string LineTwo = "The second note compared embedding models and search recall.";
        private const string LineThree = "The third note reviewed metadata filters and reranking stages.";
        private const string MultiLineParagraph = LineOne + "\n" + LineTwo + "\n" + LineThree;

        // One line of three sentences (~25 tokens): oversized for a 15-token budget, while each sentence alone fits it.
        private const string ThreeSentenceLine =
            "The team compared several embedding models. Retrieval quality improved with token overlap. " +
            "Smaller chunks sharpened ranking precision.";

        // A single sentence (~24 tokens) with no internal structure at all: forces the token-window floor for any budget below its size.
        private const string RunOnSentence =
            "The archive team spent the entire morning comparing retrieval pipelines and embedding " +
            "models while debating how overlapping chunks preserved context across long documents.";

        // A fourth small paragraph for overlap scenarios (~10 tokens, same shape as the others).
        private const string ParagraphFour = "The engineers measured overlap between the indexed chunks.";

        private MicrosoftMLTokenizerAdapter _microsoftMLTokenizer;

        [SetUp]
        public void SetUp()
        {
            _microsoftMLTokenizer = new MicrosoftMLTokenizerAdapter("cl100k_base");
        }

        [Test]
        public async Task Chunk_EmptyText_ReturnsEmptyList()
        {
            // Arrange
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 10, overlap: 0);

            // Act
            var result = await chunker.ChunkAsync("").ToListAsync();

            // Assert
            Assert.That(result, Is.Empty);
        }

        [Test]
        public async Task Chunk_WhitespaceOnlyText_ReturnsEmptyList()
        {
            // Arrange
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 10, overlap: 0);

            // Act
            var result = await chunker.ChunkAsync("  \n \t \n  ").ToListAsync();

            // Assert
            Assert.That(result, Is.Empty);
        }

        [Test]
        public async Task Chunk_SentenceCheaperWithItsPrecedingSpace_StaysWithinBudget()
        {
            // Arrange - " Demonstrated words follow after it." costs 7 tokens, but a chunk starting at that sentence drops the space and costs 8
            const string text = "The opening sentence stands here. Demonstrated words follow after it.";
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 7, overlap: 0);

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
        public async Task Chunk_TextWithinBudget_ReturnsSingleChunk()
        {
            // Arrange
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 100, overlap: 0);

            // Act
            var result = await chunker.ChunkAsync(ParagraphOne).ToListAsync();

            // Assert
            Assert.That(result, Has.Count.EqualTo(1));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].Index, Is.EqualTo(0));
                Assert.That(result[0].Text, Is.EqualTo(ParagraphOne));
            }
        }

        [Test]
        public async Task Chunk_SmallParagraphs_PacksThemIntoOneChunk()
        {
            // Arrange - generous budget: all three paragraphs must land in a single chunk
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 100, overlap: 0);

            // Act
            var result = await chunker.ChunkAsync(ThreeParagraphs).ToListAsync();

            // Assert - one chunk spanning all paragraphs, blank lines preserved inside the slice
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Text, Is.EqualTo(ThreeParagraphs));
        }

        [Test]
        public async Task Chunk_ParagraphsExceedingBudget_SplitAtParagraphBoundary()
        {
            // Arrange - 15 tokens fit any single paragraph but never two
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 15, overlap: 0);

            // Act
            var result = await chunker.ChunkAsync(ThreeParagraphs).ToListAsync();

            // Assert - one chunk per paragraph, each an exact paragraph slice
            Assert.That(result, Has.Count.EqualTo(3));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].Text, Is.EqualTo(ParagraphOne));
                Assert.That(result[1].Text, Is.EqualTo(ParagraphTwo));
                Assert.That(result[2].Text, Is.EqualTo(ParagraphThree));
            }
        }

        [Test]
        public async Task Chunk_ChunkText_IsVerbatimSliceOfSource()
        {
            // Arrange
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 15, overlap: 0);

            // Act
            var result = await chunker.ChunkAsync(ThreeParagraphs).ToListAsync();

            // Assert
            foreach (var chunk in result)
            {
                Assert.That(ThreeParagraphs.Contains(chunk.Text, StringComparison.Ordinal), Is.True,
                    $"Chunk {chunk.Index} is not a verbatim slice of the source: '{chunk.Text}'");
            }
        }

        [Test]
        public async Task Chunk_MultipleChunks_HasSequentialIndices()
        {
            // Arrange
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 15, overlap: 0);

            // Act
            var result = await chunker.ChunkAsync(ThreeParagraphs).ToListAsync();

            // Assert
            for (var i = 0; i < result.Count; i++)
            {
                Assert.That(result[i].Index, Is.EqualTo(i));
            }
        }

        [Test]
        public async Task Chunk_OversizedParagraph_DescendsToLines()
        {
            // Arrange - the whole input is ONE paragraph (single '\n' separators) over budget
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 15, overlap: 0);

            // Act
            var result = await chunker.ChunkAsync(MultiLineParagraph).ToListAsync();

            // Assert - one chunk per line, each an exact line slice
            Assert.That(result, Has.Count.EqualTo(3));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].Text, Is.EqualTo(LineOne));
                Assert.That(result[1].Text, Is.EqualTo(LineTwo));
                Assert.That(result[2].Text, Is.EqualTo(LineThree));
            }
        }

        [Test]
        public async Task Chunk_OversizedSingleLineParagraph_DescendsToSentences()
        {
            // Arrange - no line breaks anywhere: the line tier returns the unit unchanged and descent continues to the sentence tier
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 15, overlap: 0);

            // Act
            var result = await chunker.ChunkAsync(ThreeSentenceLine).ToListAsync();

            // Assert - chunks align with sentence boundaries
            Assert.That(result, Has.Count.GreaterThan(1));
            foreach (var chunk in result)
            {
                Assert.That(chunk.Text, Does.EndWith("."));
            }
        }

        [Test]
        public async Task Chunk_AbbreviationInsideOversizedLine_DoesNotSplitAtAbbreviation()
        {
            // Arrange - "Vgl" is NOT in the built-in abbreviation set; without the custom entry the sentence tier would falsely split after "Vgl." (entries are stored dot-trimmed - the builder normalizes user input the same way in Step 6)
            var abbreviations = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Vgl" };
            var text = "Vgl. Abschnitt 3 im Buch. Weiter geht es hier. Noch ein Satz folgt am Ende.";
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 12, overlap: 0,
                additionalAbbreviations: abbreviations);

            // Act
            var result = await chunker.ChunkAsync(text).ToListAsync();

            // Assert - the guarded abbreviation keeps its sentence intact
            Assert.That(result[0].Text, Does.StartWith("Vgl. Abschnitt"));
            foreach (var chunk in result)
            {
                Assert.That(chunk.Text, Is.Not.EqualTo("Vgl."));
            }
        }

        [Test]
        public async Task Chunk_OversizedSentence_FallsBackToBalancedTokenWindows()
        {
            // Arrange
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 15, overlap: 0);

            // Act
            var result = await chunker.ChunkAsync(RunOnSentence).ToListAsync();

            // Assert - multiple windows, all within budget, evenly balanced (sizes differ by at most one token), no overlap between them
            Assert.That(result, Has.Count.GreaterThan(1));
            var min = result.Min(c => c.TokenCount);
            var max = result.Max(c => c.TokenCount);
            Assert.That(max - min, Is.LessThanOrEqualTo(1));
            foreach (var chunk in result)
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(chunk.TokenCount, Is.LessThanOrEqualTo(15));
                    Assert.That(chunk.OverlapCount, Is.Zero);
                }
            }
        }

        [Test]
        public async Task Chunk_SmallUnitBeforeOversizedUnit_NotMergedAcrossSeam()
        {
            // Arrange - "A brief opening note." (~4 tokens) would fit together with the first line of the oversized paragraph (4 + 11 ≤ 15); strict seams must keep it separate
            var text = "A brief opening note.\n\n" + MultiLineParagraph;
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 15, overlap: 0);

            // Act
            var result = await chunker.ChunkAsync(text).ToListAsync();

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].Text, Is.EqualTo("A brief opening note."));
                Assert.That(result[1].Text, Is.EqualTo(LineOne));
            }
        }

        [Test]
        public async Task Chunk_SmallUnitAfterOversizedUnit_NotMergedAcrossSeam()
        {
            // Arrange - mirror case: the trailing small paragraph must not absorb the last line of the oversized paragraph
            var text = MultiLineParagraph + "\n\nA brief closing note.";
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 15, overlap: 0);

            // Act
            var result = await chunker.ChunkAsync(text).ToListAsync();

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[^1].Text, Is.EqualTo("A brief closing note."));
                Assert.That(result[^2].Text, Is.EqualTo(LineThree));
            }
        }

        [Test]
        public async Task Chunk_CrLfDocument_ChunksStayWithinBudget()
        {
            // Arrange - same structure as MultiLineParagraph but with \r\n line endings
            var text = LineOne + "\r\n" + LineTwo + "\r\n" + LineThree;
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 15, overlap: 0);

            // Act
            var result = await chunker.ChunkAsync(text).ToListAsync();

            // Assert - budget holds and no chunk starts or ends on separator whitespace
            Assert.That(result, Has.Count.EqualTo(3));
            foreach (var chunk in result)
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(chunk.TokenCount, Is.LessThanOrEqualTo(15));
                    Assert.That(chunk.Text, Is.EqualTo(chunk.Text.Trim()));
                }
            }
        }

        [TestCase(10)]
        [TestCase(15)]
        [TestCase(25)]
        public async Task Chunk_MixedStructureDocument_EveryChunkStaysWithinTokenBudget(int maxTokens)
        {
            // Arrange - small paragraphs, an oversized multi-line paragraph, and a run-on sentence: exercises every tier of the ladder in one document
            var text = ThreeParagraphs + "\n\n" + MultiLineParagraph + "\n\n" + RunOnSentence;
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens, overlap: 0);

            // Act
            var result = await chunker.ChunkAsync(text).ToListAsync();

            // Assert - the budget invariant holds on every chunk, indices stay sequential
            Assert.That(result, Has.Count.GreaterThan(1));
            for (var i = 0; i < result.Count; i++)
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(result[i].TokenCount, Is.LessThanOrEqualTo(maxTokens));
                    Assert.That(result[i].Index, Is.EqualTo(i));
                }
            }
        }

        [Test]
        public async Task Chunk_WithOverlap_CarriesWholeTrailingUnits()
        {
            // Arrange - budget fits two paragraphs plus a carried one; overlap fits exactly one whole paragraph (~10-13 tokens gap-inclusive)
            var text = ThreeParagraphs + "\n\n" + ParagraphFour;
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 28, overlap: 14);

            // Act
            var result = await chunker.ChunkAsync(text).ToListAsync();

            // Assert - the second chunk received carried tokens, and the carried text is exactly the trailing paragraph of the first chunk (whole-unit carry)
            Assert.That(result, Has.Count.GreaterThanOrEqualTo(2));
            var carried = result[1].Text[..result[1].Text.IndexOf('\n')];
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[1].OverlapCount, Is.GreaterThan(0));
                Assert.That(result[0].Text, Does.EndWith(carried));
            }
        }

        [Test]
        public async Task Chunk_WithOverlap_FirstChunkHasZeroOverlapCount()
        {
            // Arrange
            var text = ThreeParagraphs + "\n\n" + ParagraphFour;
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 28, overlap: 14);

            // Act
            var result = await chunker.ChunkAsync(text).ToListAsync();

            // Assert
            Assert.That(result[0].OverlapCount, Is.Zero);
        }

        [Test]
        public async Task Chunk_OverlapAtDescentSeam_ResetsToZero()
        {
            // Arrange - small paragraph, oversized multi-line paragraph, small paragraph.
            // Both neighbors of the oversized unit would fit the 14-token overlap budget,
            // so a zero OverlapCount can only come from the seam reset itself.
            var text = "A brief opening note.\n\n" + MultiLineParagraph + "\n\nA brief closing note.";
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 28, overlap: 14);

            // Act
            var result = await chunker.ChunkAsync(text).ToListAsync();

            // Assert - no carry INTO the descent (first line chunk) nor OUT of it (outro)
            var firstDescendedChunk = result.First(c => c.Text.StartsWith("The first note"));
            var outroChunk = result.Single(c => c.Text == "A brief closing note.");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(firstDescendedChunk.OverlapCount, Is.Zero);
                Assert.That(outroChunk.OverlapCount, Is.Zero);
            }
        }

        [Test]
        public async Task Chunk_OverlapWithinDescendedRun_CarriesTrailingLine()
        {
            // Arrange - same document: INSIDE the descended line run, overlap must still work
            var text = "A brief opening note.\n\n" + MultiLineParagraph + "\n\nA brief closing note.";
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 28, overlap: 14);

            // Act
            var result = await chunker.ChunkAsync(text).ToListAsync();

            // Assert - exactly one chunk carries overlap: the line chunk seeded with the previous line ([L1 L2] flushes, [L2 L3] receives L2)
            var seededChunk = result.Single(c => c.OverlapCount > 0);
            Assert.That(seededChunk.Text, Does.StartWith(LineTwo));
        }

        [Test]
        public async Task Chunk_OverlapAfterFallbackWindows_ResetsToZero()
        {
            // Arrange - a run-on sentence forces token windows; the paragraph after them must start without carried tokens
            var text = RunOnSentence + "\n\nA brief closing note.";
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 15, overlap: 8);

            // Act
            var result = await chunker.ChunkAsync(text).ToListAsync();

            // Assert - windows never carry overlap, and neither does the chunk after them
            Assert.That(result[^1].Text, Is.EqualTo("A brief closing note."));
            foreach (var chunk in result)
            {
                Assert.That(chunk.OverlapCount, Is.Zero);
            }
        }

        [Test]
        public async Task Chunk_SeedLeavesNoRoomForIncomingUnit_TrimsSeed()
        {
            // Arrange - a generous overlap budget (14) admits a whole trailing paragraph,
            // but seed (~10) + incoming (~10) would blow the 15-token chunk budget, so the seed must be dropped again: the budget invariant always wins over overlap
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 15, overlap: 14);

            // Act
            var result = await chunker.ChunkAsync(ThreeParagraphs).ToListAsync();

            // Assert - one paragraph per chunk, nothing carried anywhere
            Assert.That(result, Has.Count.EqualTo(3));
            foreach (var chunk in result)
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(chunk.OverlapCount, Is.Zero);
                    Assert.That(chunk.TokenCount, Is.LessThanOrEqualTo(15));
                }
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

        // Three paragraphs whose gap-inclusive char counts are 19, 22 and 21, so a 22-token budget packs exactly one paragraph per chunk with no descent.
        private const string ThreeParagraphText = "First archive note.\n\nSecond archive note.\n\nThird archive note.";

        [Test]
        public async Task ChunkAsync_EmptyText_YieldsNothing()
        {
            // Arrange
            var chunker = new RecursiveChunker(new CharTokenizer(), maxTokens: 22, overlap: 0);

            // Act
            var result = await chunker.ChunkAsync(string.Empty).ToListAsync();

            // Assert
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void ChunkAsync_PreCancelledToken_ThrowsBeforeFirstChunk()
        {
            // Arrange
            var chunker = new RecursiveChunker(new CharTokenizer(), maxTokens: 22, overlap: 0);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var consumed = new List<Chunk>();

            // Act
            async Task Consume()
            {
                await foreach (var chunk in chunker.ChunkAsync(ThreeParagraphText, cts.Token))
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
            var chunker = new RecursiveChunker(new CharTokenizer(), maxTokens: 22, overlap: 0);
            using var cts = new CancellationTokenSource();
            var consumed = new List<Chunk>();

            // Act
            async Task Consume()
            {
                await foreach (var chunk in chunker.ChunkAsync(ThreeParagraphText, cts.Token))
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
            var chunker = new RecursiveChunker(new CharTokenizer(), maxTokens: 22, overlap: 0);

            // Act & Assert - the wrapper must throw before the first MoveNextAsync, not lazily inside the iterator
            Assert.Throws<ArgumentNullException>(() => chunker.ChunkAsync((TextReader)null!));
        }

        // Single-segment fixtures for streamed-vs-in-memory equivalence, sized well below the default capacity.
        private const string ThreeParagraphDoc = "The archive ingests new documents daily.\n\nEach document is split into token bounded chunks.\n\nEmbeddings are stored in the vector index.";
        private const string FullLadderDoc = "Chunking Notes\n\nThe research group compared retrieval quality across overlapping and non overlapping chunk configurations while measuring reranking impact on synthetic and real corpora.";

        [Test]
        public async Task ChunkAsync_ReaderOverSingleSegmentParagraphs_MatchesInMemoryFieldForField()
        {
            // Arrange
            var chunker = new RecursiveChunker(new MicrosoftMLTokenizerAdapter("cl100k_base"), maxTokens: 24, overlap: 0);
            using var reader = new StringReader(ThreeParagraphDoc);

            // Act
            var inMemory = await chunker.ChunkAsync(ThreeParagraphDoc).ToListAsync();
            var streamed = await chunker.ChunkAsync(reader).ToListAsync();

            // Assert - value-equal records compare every field of every chunk
            Assert.That(streamed, Is.EqualTo(inMemory));
        }

        [Test]
        public async Task ChunkAsync_ReaderOverSingleSegmentLadderDocument_MatchesInMemoryFieldForField()
        {
            // Arrange - the second paragraph alone exceeds the budget, so the piece exercises descent through lines and sentences
            var chunker = new RecursiveChunker(new MicrosoftMLTokenizerAdapter("cl100k_base"), maxTokens: 16, overlap: 0);
            using var reader = new StringReader(FullLadderDoc);

            // Act
            var inMemory = await chunker.ChunkAsync(FullLadderDoc).ToListAsync();
            var streamed = await chunker.ChunkAsync(reader).ToListAsync();

            // Assert
            Assert.That(streamed, Is.EqualTo(inMemory));
        }

        [Test]
        public async Task ChunkAsync_ReaderWithParagraphStraddlingTheCapacityEdge_CarriesItWholeIntoTheNextSegment()
        {
            // Arrange - capacity 24 buffers up to the "S" of the second paragraph, which must be carried, never emitted partial; the blank lines are \r\n pairs
            var chunker = new RecursiveChunker(new CharTokenizer(), maxTokens: 22, overlap: 0, segmentCapacity: 24);
            using var reader = new StringReader("First archive note.\r\n\r\nSecond note.");

            // Act
            var result = await chunker.ChunkAsync(reader).ToListAsync();

            // Assert
            Assert.That(result, Has.Count.EqualTo(2));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].Text, Is.EqualTo("First archive note."));
                Assert.That(result[1].Text, Is.EqualTo("Second note."));
                Assert.That(result[0].SourceStart, Is.EqualTo(0));
                Assert.That(result[1].SourceStart, Is.EqualTo(23));
                Assert.That(result[1].Index, Is.EqualTo(1));
            }
        }

        [Test]
        public void ChunkAsync_ReaderPreCancelledToken_ThrowsBeforeFirstChunk()
        {
            // Arrange
            var chunker = new RecursiveChunker(new CharTokenizer(), maxTokens: 22, overlap: 0);
            using var reader = new StringReader(ThreeParagraphText);
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
            var chunker = new RecursiveChunker(new CharTokenizer(), maxTokens: 22, overlap: 0);
            using var reader = new StringReader(ThreeParagraphText);
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
        public async Task ChunkAsync_ReaderWithNoBlankLinesEver_StaysBoundedAndKeepsTheBudget()
        {
            // Arrange - the failure forced descent exists to prevent: a paragraph that never closes must not buffer forever.
            // A regression here busy-loops with no I/O, so bound the run: a reintroduced defect then times out instead of hanging CI.
            var reader = new MeteredRepeatingReader("the archive group compared retrieval strategies ", totalChars: 10_000);
            var chunker = new RecursiveChunker(new CharTokenizer(), maxTokens: 8, overlap: 0, segmentCapacity: 64);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // Act & Assert - the window fills at most one buffer ahead of what it has handed out, so reading leads the yielded chunks by less than one capacity.
            // The +128 bound (2x capacity) leaves slack rather than pinning that exactly, and no chunk ever exceeds the budget.
            await foreach (var chunk in chunker.ChunkAsync(reader, cts.Token))
            {
                Assert.That(reader.CharsProvided, Is.LessThanOrEqualTo(chunk.SourceStart + chunk.SourceLength + 128));
                Assert.That(chunk.TokenCount, Is.LessThanOrEqualTo(8));
            }
        }

        [Test]
        public async Task ChunkAsync_ReaderWithOpenParagraphFillingTheBuffer_DescendsTheBufferedPrefixAndResetsOverlapAtTheSeam()
        {
            // Arrange - capacity 16 over a blank-line-free document: the prefix "archive notes ra" is descended as one synthetic paragraph,
            // the ladder bottoms out in balanced windows, and the chunk after the seam starts with OverlapCount 0 despite overlap being configured.
            // A regression here busy-loops with no I/O, so bound the run: a reintroduced defect then times out instead of hanging CI.
            var chunker = new RecursiveChunker(new CharTokenizer(), maxTokens: 8, overlap: 3, segmentCapacity: 16);
            using var reader = new StringReader("archive notes rank lists");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // Act
            var result = await chunker.ChunkAsync(reader, cts.Token).ToListAsync();

            // Assert - the mid-word cut in "notes ra" is the documented forced-descent divergence: bounded memory outranks unit integrity here.
            Assert.That(result, Has.Count.EqualTo(3));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Select(c => c.Text), Is.EqualTo(new[] { "archive ", "notes ra", "nk lists" }));
                Assert.That(result.Select(c => c.SourceStart), Is.EqualTo(new long[] { 0, 8, 16 }));
                Assert.That(result.Select(c => c.OverlapCount), Is.All.Zero);
                Assert.That(result[2].Index, Is.EqualTo(2));
            }
        }

        [Test]
        public async Task ChunkAsync_ReaderAcrossManySegments_KeepsIndexContinuity()
        {
            // Arrange - bound the run: a reintroduced forced-descent defect busy-loops with no I/O and would otherwise hang instead of failing.
            var reader = new MeteredRepeatingReader("the archive group compared retrieval strategies ", totalChars: 2_000);
            var chunker = new RecursiveChunker(new CharTokenizer(), maxTokens: 8, overlap: 0, segmentCapacity: 64);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // Act
            var result = await chunker.ChunkAsync(reader, cts.Token).ToListAsync();

            // Assert
            Assert.That(result, Has.Count.GreaterThan(10));
            Assert.That(result.Select(c => c.Index), Is.EqualTo(Enumerable.Range(0, result.Count)));
        }
    }
}
