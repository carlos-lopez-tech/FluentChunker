using FluentChunker.Chunkers.Recursive;
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

        // One paragraph of three lines (~11 tokens each, ~33 total):
        // oversized for a 15-token budget, while each line alone fits it and no two lines fit together.
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
        public void Chunk_EmptyText_ReturnsEmptyList()
        {
            // Arrange
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 10, overlap: 0);

            // Act
            var result = chunker.Chunk("");

            // Assert
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void Chunk_WhitespaceOnlyText_ReturnsEmptyList()
        {
            // Arrange
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 10, overlap: 0);

            // Act
            var result = chunker.Chunk("  \n \t \n  ");

            // Assert
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void Chunk_SentenceCheaperWithItsPrecedingSpace_StaysWithinBudget()
        {
            // Arrange - " Demonstrated words follow after it." costs 7 tokens, but a chunk starting at that sentence drops the space and costs 8
            const string text = "The opening sentence stands here. Demonstrated words follow after it.";
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 7, overlap: 0);

            // Act
            var result = chunker.Chunk(text);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result, Is.Not.Empty);
                Assert.That(result.Select(c => _microsoftMLTokenizer.CountTokens(c.Text)),
                    Has.All.LessThanOrEqualTo(7));
            }
        }

        [Test]
        public void Chunk_TextWithinBudget_ReturnsSingleChunk()
        {
            // Arrange
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 100, overlap: 0);

            // Act
            var result = chunker.Chunk(ParagraphOne);

            // Assert
            Assert.That(result, Has.Count.EqualTo(1));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].Index, Is.EqualTo(0));
                Assert.That(result[0].Text, Is.EqualTo(ParagraphOne));
            }
        }

        [Test]
        public void Chunk_SmallParagraphs_PacksThemIntoOneChunk()
        {
            // Arrange - generous budget: all three paragraphs must land in a single chunk
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 100, overlap: 0);

            // Act
            var result = chunker.Chunk(ThreeParagraphs);

            // Assert - one chunk spanning all paragraphs, blank lines preserved inside the slice
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Text, Is.EqualTo(ThreeParagraphs));
        }

        [Test]
        public void Chunk_ParagraphsExceedingBudget_SplitAtParagraphBoundary()
        {
            // Arrange - 15 tokens fit any single paragraph but never two
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 15, overlap: 0);

            // Act
            var result = chunker.Chunk(ThreeParagraphs);

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
        public void Chunk_ChunkText_IsVerbatimSliceOfSource()
        {
            // Arrange
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 15, overlap: 0);

            // Act
            var result = chunker.Chunk(ThreeParagraphs);

            // Assert
            foreach (var chunk in result)
            {
                Assert.That(ThreeParagraphs.Contains(chunk.Text, StringComparison.Ordinal), Is.True,
                    $"Chunk {chunk.Index} is not a verbatim slice of the source: '{chunk.Text}'");
            }
        }

        [Test]
        public void Chunk_MultipleChunks_HasSequentialIndices()
        {
            // Arrange
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 15, overlap: 0);

            // Act
            var result = chunker.Chunk(ThreeParagraphs);

            // Assert
            for (var i = 0; i < result.Count; i++)
            {
                Assert.That(result[i].Index, Is.EqualTo(i));
            }
        }

        [Test]
        public void Chunk_OversizedParagraph_DescendsToLines()
        {
            // Arrange - the whole input is ONE paragraph (single '\n' separators) over budget
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 15, overlap: 0);

            // Act
            var result = chunker.Chunk(MultiLineParagraph);

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
        public void Chunk_OversizedSingleLineParagraph_DescendsToSentences()
        {
            // Arrange - no line breaks anywhere: the line tier returns the unit unchanged and descent continues to the sentence tier
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 15, overlap: 0);

            // Act
            var result = chunker.Chunk(ThreeSentenceLine);

            // Assert - chunks align with sentence boundaries
            Assert.That(result, Has.Count.GreaterThan(1));
            foreach (var chunk in result)
            {
                Assert.That(chunk.Text, Does.EndWith("."));
            }
        }

        [Test]
        public void Chunk_AbbreviationInsideOversizedLine_DoesNotSplitAtAbbreviation()
        {
            // Arrange - "Vgl" is NOT in the built-in abbreviation set; without the custom entry the sentence tier would falsely split after "Vgl." (entries are stored dot-trimmed - the builder normalizes user input the same way in Step 6)
            var abbreviations = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Vgl" };
            var text = "Vgl. Abschnitt 3 im Buch. Weiter geht es hier. Noch ein Satz folgt am Ende.";
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 12, overlap: 0,
                additionalAbbreviations: abbreviations);

            // Act
            var result = chunker.Chunk(text);

            // Assert - the guarded abbreviation keeps its sentence intact
            Assert.That(result[0].Text, Does.StartWith("Vgl. Abschnitt"));
            foreach (var chunk in result)
            {
                Assert.That(chunk.Text, Is.Not.EqualTo("Vgl."));
            }
        }

        [Test]
        public void Chunk_OversizedSentence_FallsBackToBalancedTokenWindows()
        {
            // Arrange
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 15, overlap: 0);

            // Act
            var result = chunker.Chunk(RunOnSentence);

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
        public void Chunk_SmallUnitBeforeOversizedUnit_NotMergedAcrossSeam()
        {
            // Arrange - "A brief opening note." (~4 tokens) would fit together with the first line of the oversized paragraph (4 + 11 ≤ 15); strict seams must keep it separate
            var text = "A brief opening note.\n\n" + MultiLineParagraph;
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 15, overlap: 0);

            // Act
            var result = chunker.Chunk(text);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].Text, Is.EqualTo("A brief opening note."));
                Assert.That(result[1].Text, Is.EqualTo(LineOne));
            }
        }

        [Test]
        public void Chunk_SmallUnitAfterOversizedUnit_NotMergedAcrossSeam()
        {
            // Arrange - mirror case: the trailing small paragraph must not absorb the last line of the oversized paragraph
            var text = MultiLineParagraph + "\n\nA brief closing note.";
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 15, overlap: 0);

            // Act
            var result = chunker.Chunk(text);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[^1].Text, Is.EqualTo("A brief closing note."));
                Assert.That(result[^2].Text, Is.EqualTo(LineThree));
            }
        }

        [Test]
        public void Chunk_CrLfDocument_ChunksStayWithinBudget()
        {
            // Arrange - same structure as MultiLineParagraph but with \r\n line endings
            var text = LineOne + "\r\n" + LineTwo + "\r\n" + LineThree;
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 15, overlap: 0);

            // Act
            var result = chunker.Chunk(text);

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
        public void Chunk_MixedStructureDocument_EveryChunkStaysWithinTokenBudget(int maxTokens)
        {
            // Arrange - small paragraphs, an oversized multi-line paragraph, and a run-on sentence: exercises every tier of the ladder in one document
            var text = ThreeParagraphs + "\n\n" + MultiLineParagraph + "\n\n" + RunOnSentence;
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens, overlap: 0);

            // Act
            var result = chunker.Chunk(text);

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
        public void Chunk_WithOverlap_CarriesWholeTrailingUnits()
        {
            // Arrange - budget fits two paragraphs plus a carried one; overlap fits exactly one whole paragraph (~10-13 tokens gap-inclusive)
            var text = ThreeParagraphs + "\n\n" + ParagraphFour;
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 28, overlap: 14);

            // Act
            var result = chunker.Chunk(text);

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
        public void Chunk_WithOverlap_FirstChunkHasZeroOverlapCount()
        {
            // Arrange
            var text = ThreeParagraphs + "\n\n" + ParagraphFour;
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 28, overlap: 14);

            // Act
            var result = chunker.Chunk(text);

            // Assert
            Assert.That(result[0].OverlapCount, Is.Zero);
        }

        [Test]
        public void Chunk_OverlapAtDescentSeam_ResetsToZero()
        {
            // Arrange - small paragraph, oversized multi-line paragraph, small paragraph.
            // Both neighbors of the oversized unit would fit the 14-token overlap budget,
            // so a zero OverlapCount can only come from the seam reset itself.
            var text = "A brief opening note.\n\n" + MultiLineParagraph + "\n\nA brief closing note.";
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 28, overlap: 14);

            // Act
            var result = chunker.Chunk(text);

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
        public void Chunk_OverlapWithinDescendedRun_CarriesTrailingLine()
        {
            // Arrange - same document: INSIDE the descended line run, overlap must still work
            var text = "A brief opening note.\n\n" + MultiLineParagraph + "\n\nA brief closing note.";
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 28, overlap: 14);

            // Act
            var result = chunker.Chunk(text);

            // Assert - exactly one chunk carries overlap: the line chunk seeded with the previous line ([L1 L2] flushes, [L2 L3] receives L2)
            var seededChunk = result.Single(c => c.OverlapCount > 0);
            Assert.That(seededChunk.Text, Does.StartWith(LineTwo));
        }

        [Test]
        public void Chunk_OverlapAfterFallbackWindows_ResetsToZero()
        {
            // Arrange - a run-on sentence forces token windows; the paragraph after them must start without carried tokens
            var text = RunOnSentence + "\n\nA brief closing note.";
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 15, overlap: 8);

            // Act
            var result = chunker.Chunk(text);

            // Assert - windows never carry overlap, and neither does the chunk after them
            Assert.That(result[^1].Text, Is.EqualTo("A brief closing note."));
            foreach (var chunk in result)
            {
                Assert.That(chunk.OverlapCount, Is.Zero);
            }
        }

        [Test]
        public void Chunk_SeedLeavesNoRoomForIncomingUnit_TrimsSeed()
        {
            // Arrange - a generous overlap budget (14) admits a whole trailing paragraph,
            // but seed (~10) + incoming (~10) would blow the 15-token chunk budget, so the seed must be dropped again: the budget invariant always wins over overlap
            var chunker = new RecursiveChunker(_microsoftMLTokenizer, maxTokens: 15, overlap: 14);

            // Act
            var result = chunker.Chunk(ThreeParagraphs);

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
    }
}
