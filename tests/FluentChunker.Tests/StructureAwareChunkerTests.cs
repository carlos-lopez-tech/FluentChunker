using FluentChunker.Chunkers.StructureAware;
using FluentChunker.Tokenizers;

namespace FluentChunker.Tests
{
    public class StructureAwareChunkerTests
    {
        // Three small paragraphs of about 10 tokens each, themed on the archive/retrieval corpus.
        private const string ParagraphOne = "The team reviewed the reports before the meeting started.";
        private const string ParagraphTwo = "The group compared results from the two search systems.";
        private const string ParagraphThree = "The notes described how context changed across the chunks.";

        // Two flat sections that easily fit a 100-token budget individually AND combined: only a hard seam at the second heading explains getting two chunks out of them.
        private const string TwoSectionDocument =
            "## Retrieval Notes\n\n" + ParagraphOne + "\n\n" +
            "## Ranking Results\n\n" + ParagraphTwo;

        // One section whose heading is deliberately long (about 8 tokens) so that at a 15-token budget the heading never packs together with a paragraph (8 + 12 > 15),
        // and no two paragraphs pack together either (10 + 12 > 15).
        private const string OversizedSection =
            "## Retrieval Notes From The Archive Session\n\n" +
            ParagraphOne + "\n\n" + ParagraphTwo + "\n\n" + ParagraphThree;

        private MicrosoftMLTokenizerAdapter _microsoftMLTokenizer;

        [SetUp]
        public void SetUp()
        {
            _microsoftMLTokenizer = new MicrosoftMLTokenizerAdapter("cl100k_base");
        }

        [Test]
        public void Chunk_SentenceCheaperWithItsPrecedingSpace_StaysWithinBudget()
        {
            // Arrange - " Demonstrated words follow after it." costs 7 tokens, but a chunk starting at that sentence drops the space and costs 8
            const string text = "The opening sentence stands here. Demonstrated words follow after it.";
            var chunker = new StructureAwareChunker(_microsoftMLTokenizer, maxTokens: 7);

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
        public void Chunk_EmptyText_ReturnsEmptyList()
        {
            // Arrange
            var chunker = new StructureAwareChunker(_microsoftMLTokenizer, maxTokens: 100);

            // Act
            var result = chunker.Chunk("");

            // Assert
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void Chunk_WhitespaceOnlyText_ReturnsEmptyList()
        {
            // Arrange
            var chunker = new StructureAwareChunker(_microsoftMLTokenizer, maxTokens: 100);

            // Act
            var result = chunker.Chunk("  \n \t \n  ");

            // Assert
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void Chunk_TwoSmallSections_ProducesOneChunkPerSection()
        {
            // Arrange - both sections together are far below the budget; only the hard seam at the second heading forces two chunks
            var chunker = new StructureAwareChunker(_microsoftMLTokenizer, maxTokens: 100);

            // Act
            var result = chunker.Chunk(TwoSectionDocument);

            // Assert
            Assert.That(result, Has.Count.EqualTo(2));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].Text, Is.EqualTo("## Retrieval Notes\n\n" + ParagraphOne));
                Assert.That(result[1].Text, Is.EqualTo("## Ranking Results\n\n" + ParagraphTwo));
            }
        }

        [Test]
        public void Chunk_SectionChunks_CarryHeadingPathMetadata()
        {
            // Arrange
            var chunker = new StructureAwareChunker(_microsoftMLTokenizer, maxTokens: 100);

            // Act
            var result = chunker.Chunk(TwoSectionDocument);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].Metadata["headingPath"].GetString(), Is.EqualTo("Retrieval Notes"));
                Assert.That(result[1].Metadata["headingPath"].GetString(), Is.EqualTo("Ranking Results"));
            }
        }

        [Test]
        public void Chunk_NestedHeadings_JoinHeadingPathWithSeparator()
        {
            // Arrange
            var text = "# Archive Study\n\n" +
                       "## Findings\n\n" + ParagraphOne;
            var chunker = new StructureAwareChunker(_microsoftMLTokenizer, maxTokens: 100);

            // Act
            var result = chunker.Chunk(text);

            // Assert - the h1 section is heading-only, the h2 section carries the full path
            Assert.That(result, Has.Count.EqualTo(2));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].Metadata["headingPath"].GetString(), Is.EqualTo("Archive Study"));
                Assert.That(result[1].Metadata["headingPath"].GetString(), Is.EqualTo("Archive Study > Findings"));
            }
        }

        [Test]
        public void Chunk_PreambleChunk_HasEmptyHeadingPath()
        {
            // Arrange
            var text = ParagraphOne + "\n\n# Archive Study\n\n" + ParagraphTwo;
            var chunker = new StructureAwareChunker(_microsoftMLTokenizer, maxTokens: 100);

            // Act
            var result = chunker.Chunk(text);

            // Assert
            Assert.That(result, Has.Count.EqualTo(2));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].Metadata["headingPath"].GetString(), Is.EqualTo(""));
                Assert.That(result[0].Text, Is.EqualTo(ParagraphOne));
            }
        }

        [Test]
        public void Chunk_SectionFirstChunk_StartsWithHeadingLine()
        {
            // Arrange
            var chunker = new StructureAwareChunker(_microsoftMLTokenizer, maxTokens: 15);

            // Act
            var result = chunker.Chunk(OversizedSection);

            // Assert
            Assert.That(result[0].Text, Is.EqualTo("## Retrieval Notes From The Archive Session"));
        }

        [Test]
        public void Chunk_OversizedSection_SplitsAtBlockBoundaries()
        {
            // Arrange - heading and paragraphs each fit 15 tokens alone, no two fit together
            var chunker = new StructureAwareChunker(_microsoftMLTokenizer, maxTokens: 15);

            // Act
            var result = chunker.Chunk(OversizedSection);

            // Assert - one chunk per block, each an exact verbatim slice
            Assert.That(result, Has.Count.EqualTo(4));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[1].Text, Is.EqualTo(ParagraphOne));
                Assert.That(result[2].Text, Is.EqualTo(ParagraphTwo));
                Assert.That(result[3].Text, Is.EqualTo(ParagraphThree));
            }
        }

        [Test]
        public void Chunk_OversizedSectionChunks_AllCarryTheSameHeadingPath()
        {
            // Arrange
            var chunker = new StructureAwareChunker(_microsoftMLTokenizer, maxTokens: 15);

            // Act
            var result = chunker.Chunk(OversizedSection);

            // Assert
            foreach (var chunk in result)
            {
                Assert.That(chunk.Metadata["headingPath"].GetString(),
                    Is.EqualTo("Retrieval Notes From The Archive Session"));
            }
        }

        [Test]
        public void Chunk_CodeFenceWithBlankLines_StaysWholeWhenItFits()
        {
            // Arrange - the blank line inside the fence would break recursive chunking's paragraph tier; the structure-aware strategy must keep the fence intact
            var text = "## Benchmark Configuration\n\n" +
                       "```text\nmaxTokens: 128\n\noverlap: 16\n```";
            var chunker = new StructureAwareChunker(_microsoftMLTokenizer, maxTokens: 100);

            // Act
            var result = chunker.Chunk(text);

            // Assert
            Assert.That(result, Has.Count.EqualTo(1));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].Text, Does.Contain("maxTokens: 128"));
                Assert.That(result[0].Text, Does.Contain("overlap: 16"));
                Assert.That(result[0].Text, Does.EndWith("```"));
            }
        }

        [Test]
        public void Chunk_PlainTextWithoutMarkdown_PacksParagraphsWithEmptyHeadingPath()
        {
            // Arrange
            var text = ParagraphOne + "\n\n" + ParagraphTwo;
            var chunker = new StructureAwareChunker(_microsoftMLTokenizer, maxTokens: 100);

            // Act
            var result = chunker.Chunk(text);

            // Assert - degrades gracefully: one packed chunk, empty breadcrumb
            Assert.That(result, Has.Count.EqualTo(1));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].Text, Is.EqualTo(text));
                Assert.That(result[0].Metadata["headingPath"].GetString(), Is.EqualTo(""));
            }
        }

        [Test]
        public void Chunk_AllChunks_HaveZeroOverlapCount()
        {
            // Arrange
            var chunker = new StructureAwareChunker(_microsoftMLTokenizer, maxTokens: 15);

            // Act
            var result = chunker.Chunk(OversizedSection);

            // Assert - this strategy has no overlap by design
            foreach (var chunk in result)
            {
                Assert.That(chunk.OverlapCount, Is.Zero);
            }
        }

        [Test]
        public void Chunk_MultipleChunks_HasSequentialIndices()
        {
            // Arrange
            var chunker = new StructureAwareChunker(_microsoftMLTokenizer, maxTokens: 15);

            // Act
            var result = chunker.Chunk(OversizedSection);

            // Assert
            for (var i = 0; i < result.Count; i++)
            {
                Assert.That(result[i].Index, Is.EqualTo(i));
            }
        }

        [Test]
        public void Chunk_PackedChunks_RespectTokenBudget()
        {
            // Arrange
            var chunker = new StructureAwareChunker(_microsoftMLTokenizer, maxTokens: 15);

            // Act
            var result = chunker.Chunk(OversizedSection);

            // Assert
            foreach (var chunk in result)
            {
                Assert.That(chunk.TokenCount, Is.LessThanOrEqualTo(15));
            }
        }

        // One paragraph of three sentences (about 25 tokens): oversized for a 15-token budget, while each sentence alone fits it.
        private const string ThreeSentenceParagraph =
            "The team compared several embedding models. Retrieval quality improved with token overlap. " +
            "Smaller chunks sharpened ranking precision.";

        // A single sentence (about 24 tokens) with no internal structure at all: forces the token-window floor for any budget below its size.
        private const string RunOnSentence =
            "The archive team spent the entire morning comparing retrieval pipelines and embedding " +
            "models while debating how overlapping chunks preserved context across long documents.";

        [Test]
        public void Chunk_OversizedParagraph_DescendsToWholeSentences()
        {
            // Arrange - the paragraph exceeds 15 tokens, its sentences do not
            var text = "## Overlap Findings\n\n" + ThreeSentenceParagraph;
            var chunker = new StructureAwareChunker(_microsoftMLTokenizer, maxTokens: 15);

            // Act
            var result = chunker.Chunk(text);

            // Assert - after the heading chunk, every chunk is sentence-aligned and a verbatim slice of the source (token windows would cut mid-word and decode)
            Assert.That(result, Has.Count.GreaterThan(1));
            using (Assert.EnterMultipleScope())
            {
                foreach (var chunk in result.Skip(1))
                {
                    Assert.That(chunk.Text, Does.EndWith("."));
                    Assert.That(text, Does.Contain(chunk.Text));
                }
            }
        }

        [Test]
        public void Chunk_OversizedSentence_FallsBackToBalancedTokenWindows()
        {
            // Arrange - a single sentence over budget has no structure left to split on
            var text = "## Archive Session\n\n" + RunOnSentence;
            var chunker = new StructureAwareChunker(_microsoftMLTokenizer, maxTokens: 15);

            // Act
            var result = chunker.Chunk(text);

            // Assert - heading chunk plus at least two window chunks, all within budget
            Assert.That(result, Has.Count.GreaterThanOrEqualTo(3));
            using (Assert.EnterMultipleScope())
            {
                foreach (var chunk in result)
                {
                    Assert.That(chunk.TokenCount, Is.LessThanOrEqualTo(15));
                    Assert.That(chunk.Metadata["headingPath"].GetString(), Is.EqualTo("Archive Session"));
                }
            }
        }

        [Test]
        public void Chunk_OversizedCodeFence_FallsBackToTokenWindowsWithHeadingPath()
        {
            // Arrange - an atomic block over budget cannot descend into sentences; it must go straight to balanced token windows, still stamped with its heading path
            var fenceLines = string.Join("\n",
                Enumerable.Range(1, 30).Select(i => $"retrieval-metric-{i}: score"));
            var text = "## Benchmark Configuration\n\n```text\n" + fenceLines + "\n```";
            var chunker = new StructureAwareChunker(_microsoftMLTokenizer, maxTokens: 20);

            // Act
            var result = chunker.Chunk(text);

            // Assert
            Assert.That(result, Has.Count.GreaterThan(2));
            using (Assert.EnterMultipleScope())
            {
                foreach (var chunk in result)
                {
                    Assert.That(chunk.TokenCount, Is.LessThanOrEqualTo(20));
                    Assert.That(chunk.OverlapCount, Is.Zero);
                    Assert.That(chunk.Metadata["headingPath"].GetString(),
                        Is.EqualTo("Benchmark Configuration"));
                }
            }
        }

        [Test]
        public void Chunk_SmallBlocksAroundOversizedParagraph_StayInTheirOwnChunks()
        {
            // Arrange - plain text: small paragraph, oversized run-on paragraph, small paragraph; the small ones must never merge across the descent seam
            var text = ParagraphOne + "\n\n" + RunOnSentence + "\n\n" + ParagraphTwo;
            var chunker = new StructureAwareChunker(_microsoftMLTokenizer, maxTokens: 15);

            // Act
            var result = chunker.Chunk(text);

            // Assert
            Assert.That(result, Has.Count.GreaterThanOrEqualTo(4));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].Text, Is.EqualTo(ParagraphOne));
                Assert.That(result[^1].Text, Is.EqualTo(ParagraphTwo));
            }
        }

        // At maxTokens 6, "Vgl. Abschnitt 3 im Buch." only stays together when "Vgl" is registered as an abbreviation; without the guard the first sentence chunk is exactly "Vgl." (same proven sizing as the sentence-aware builder test).
        [Test]
        public void Chunk_CustomAbbreviation_SuppressesFalseSentenceBreakDuringDescent()
        {
            // Arrange
            var text = "Vgl. Abschnitt 3 im Buch. Weiter geht es.";
            var abbreviations = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Vgl" };
            var unguarded = new StructureAwareChunker(_microsoftMLTokenizer, maxTokens: 6);
            var guarded = new StructureAwareChunker(_microsoftMLTokenizer, maxTokens: 6, abbreviations);

            // Act
            var unguardedResult = unguarded.Chunk(text);
            var guardedResult = guarded.Chunk(text);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(unguardedResult[0].Text, Is.EqualTo("Vgl."));
                Assert.That(guardedResult[0].Text, Is.Not.EqualTo("Vgl."));
            }
        }
    }
}
