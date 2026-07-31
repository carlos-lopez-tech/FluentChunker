using FluentChunker.Chunkers.StructureAware;

namespace FluentChunker.Tests
{
    public class MarkdownSectionParserTests
    {
        // Two flat sections with one paragraph each, themed on the archive/retrieval corpus.
        private const string TwoSectionDocument =
            "## Retrieval Notes\n\n" +
            "The team reviewed the reports before the meeting started.\n\n" +
            "## Ranking Results\n\n" +
            "The group compared results from the two search systems.";

        // Heading level jumps down two levels (h1 -> h3) and then back up to h2: the h3 entry must be popped from the path when the h2 arrives.
        private const string NestedHeadingsDocument =
            "# Archive Study\n\n" +
            "### Method Details\n\n" +
            "The historian described tone changes inside long paragraphs.\n\n" +
            "## Findings\n\n" +
            "Smaller segments improved ranking precision during similarity searches.";

        [Test]
        public void Parse_EmptyText_ReturnsNoSections()
        {
            // Arrange & Act
            var result = MarkdownSectionParser.Parse("");

            // Assert
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void Parse_PlainTextWithoutMarkdown_ReturnsSinglePreambleSection()
        {
            // Arrange
            var text = "The research group arrived at the archive before sunrise.\n\n" +
                       "Two engineers compared embedding models near the eastern window.";

            // Act
            var result = MarkdownSectionParser.Parse(text);

            // Assert
            Assert.That(result, Has.Count.EqualTo(1));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].HeadingPath, Is.Empty);
                Assert.That(result[0].Blocks, Has.Count.EqualTo(2));
            }
        }

        [Test]
        public void Parse_PlainTextParagraphs_AreSplittableBlocks()
        {
            // Arrange
            var text = "The research group arrived at the archive before sunrise.\n\n" +
                       "Two engineers compared embedding models near the eastern window.";

            // Act
            var result = MarkdownSectionParser.Parse(text);

            // Assert
            foreach (var block in result[0].Blocks)
            {
                Assert.That(block.IsAtomic, Is.False);
            }
        }

        [Test]
        public void Parse_TwoSections_ReturnsOneSectionPerHeading()
        {
            // Arrange & Act
            var result = MarkdownSectionParser.Parse(TwoSectionDocument);

            // Assert
            Assert.That(result, Has.Count.EqualTo(2));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].HeadingPath, Is.EqualTo(new[] { "Retrieval Notes" }));
                Assert.That(result[1].HeadingPath, Is.EqualTo(new[] { "Ranking Results" }));
            }
        }

        [Test]
        public void Parse_HeadingBlock_IsFirstAtomicBlockOfItsSection()
        {
            // Arrange & Act
            var result = MarkdownSectionParser.Parse(TwoSectionDocument);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].Blocks[0].IsAtomic, Is.True);
                Assert.That(GetBlockText(TwoSectionDocument, result[0], 0), Is.EqualTo("## Retrieval Notes"));
            }
        }

        [Test]
        public void Parse_NestedHeadings_BuildsAndPopsHeadingPaths()
        {
            // Arrange & Act
            var result = MarkdownSectionParser.Parse(NestedHeadingsDocument);

            // Assert - h1 section (heading only), then h1 > h3, then h1 > h2 after the pop
            Assert.That(result, Has.Count.EqualTo(3));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].HeadingPath, Is.EqualTo(new[] { "Archive Study" }));
                Assert.That(result[1].HeadingPath, Is.EqualTo(new[] { "Archive Study", "Method Details" }));
                Assert.That(result[2].HeadingPath, Is.EqualTo(new[] { "Archive Study", "Findings" }));
            }
        }

        [Test]
        public void Parse_ContentBeforeFirstHeading_BecomesPreambleWithEmptyPath()
        {
            // Arrange
            var text = "The archive opened early for the chunking experiments.\n\n" +
                       "# Archive Study\n\n" +
                       "The engineers loaded the benchmark corpus.";

            // Act
            var result = MarkdownSectionParser.Parse(text);

            // Assert
            Assert.That(result, Has.Count.EqualTo(2));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].HeadingPath, Is.Empty);
                Assert.That(result[1].HeadingPath, Is.EqualTo(new[] { "Archive Study" }));
            }
        }

        [Test]
        public void Parse_SetextHeading_StartsANewSection()
        {
            // Arrange - Setext style: the title is underlined with equals signs
            var text = "Archive Study\n" +
                       "=============\n\n" +
                       "The research group prepared the retrieval benchmark.";

            // Act
            var result = MarkdownSectionParser.Parse(text);

            // Assert
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].HeadingPath, Is.EqualTo(new[] { "Archive Study" }));
        }

        [Test]
        public void Parse_HeadingWithInlineCode_ExtractsPlainBreadcrumbText()
        {
            // Arrange
            var text = "## The `SentenceSplitter` Component\n\n" +
                       "The splitter located sentence boundaries inside each paragraph.";

            // Act
            var result = MarkdownSectionParser.Parse(text);

            // Assert - markup markers are dropped from the breadcrumb, text is kept
            Assert.That(result[0].HeadingPath, Is.EqualTo(new[] { "The SentenceSplitter Component" }));
        }

        [Test]
        public void Parse_BlockSpans_AreVerbatimSlicesOfSource()
        {
            // Arrange & Act
            var result = MarkdownSectionParser.Parse(TwoSectionDocument);

            // Assert - every block span slices real content out of the original string
            foreach (var section in result)
            {
                foreach (var block in section.Blocks)
                {
                    var slice = TwoSectionDocument.Substring(block.Span.Start, block.Span.Length);
                    Assert.That(slice.Trim(), Is.EqualTo(slice), $"Span not content-trimmed: '{slice}'");
                }
            }
        }

        [Test]
        public void Parse_CrlfLineEndings_ProducesSameSectionsAndCleanSpans()
        {
            // Arrange
            var text = "## Retrieval Notes\r\n\r\n" +
                       "The team reviewed the reports before the meeting started.\r\n";

            // Act
            var result = MarkdownSectionParser.Parse(text);

            // Assert
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Blocks, Has.Count.EqualTo(2));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].HeadingPath, Is.EqualTo(new[] { "Retrieval Notes" }));
                Assert.That(GetBlockText(text, result[0], 1),
                    Is.EqualTo("The team reviewed the reports before the meeting started."));
            }
        }

        [Test]
        public void Parse_FencedCodeWithBlankLines_IsSingleAtomicBlock()
        {
            // Arrange - the blank line inside the fence would split a paragraph-based splitter (like BlockSplitter); the Markdown parser must keep the fence whole.
            var text = "## Benchmark Configuration\n\n" +
                       "```text\n" +
                       "maxTokens: 128\n" +
                       "\n" +
                       "overlap: 16\n" +
                       "```";

            // Act
            var result = MarkdownSectionParser.Parse(text);

            // Assert - exactly two blocks: the heading and one whole fence
            Assert.That(result[0].Blocks, Has.Count.EqualTo(2));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].Blocks[1].IsAtomic, Is.True);
                Assert.That(GetBlockText(text, result[0], 1), Does.StartWith("```text"));
                Assert.That(GetBlockText(text, result[0], 1), Does.EndWith("```"));
                Assert.That(GetBlockText(text, result[0], 1), Does.Contain("overlap: 16"));
            }
        }

        [Test]
        public void Parse_PipeTable_IsSingleAtomicBlock()
        {
            // Arrange
            var text = "## Retrieval Scores\n\n" +
                       "| Strategy | Recall |\n" +
                       "| --- | --- |\n" +
                       "| fixed-size | 0.71 |\n" +
                       "| recursive | 0.83 |";

            // Act
            var result = MarkdownSectionParser.Parse(text);

            // Assert - heading plus one table block, not one paragraph per row
            Assert.That(result[0].Blocks, Has.Count.EqualTo(2));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].Blocks[1].IsAtomic, Is.True);
                Assert.That(GetBlockText(text, result[0], 1), Does.StartWith("| Strategy"));
                Assert.That(GetBlockText(text, result[0], 1), Does.EndWith("| recursive | 0.83 |"));
            }
        }

        [Test]
        public void Parse_BulletList_IsSingleAtomicBlock()
        {
            // Arrange
            var text = "## Follow-Up Actions\n\n" +
                       "- Re-run the benchmark with a larger corpus.\n" +
                       "- Compare reranking latency across configurations.\n" +
                       "- Archive the retrieval logs for the next session.";

            // Act
            var result = MarkdownSectionParser.Parse(text);

            // Assert
            Assert.That(result[0].Blocks, Has.Count.EqualTo(2));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].Blocks[1].IsAtomic, Is.True);
                Assert.That(GetBlockText(text, result[0], 1), Does.StartWith("- Re-run"));
                Assert.That(GetBlockText(text, result[0], 1), Does.EndWith("next session."));
            }
        }

        [Test]
        public void Parse_QuoteBlock_IsSingleAtomicBlock()
        {
            // Arrange
            var text = "## Historian Remarks\n\n" +
                       "> Archival letters frequently changed tone halfway through a paragraph,\n" +
                       "> making naive sentence splitting unreliable for historical material.";

            // Act
            var result = MarkdownSectionParser.Parse(text);

            // Assert
            Assert.That(result[0].Blocks, Has.Count.EqualTo(2));
            Assert.That(result[0].Blocks[1].IsAtomic, Is.True);
        }

        [Test]
        public void Parse_MixedSection_KeepsBlockOrderAndClassification()
        {
            // Arrange - paragraph (splittable), list (atomic), paragraph (splittable)
            var text = "## Experiment Notes\n\n" +
                       "The engineers logged every retrieval run in order.\n\n" +
                       "- Index the corpus.\n" +
                       "- Execute the query set.\n\n" +
                       "The scores were archived after each run.";

            // Act
            var result = MarkdownSectionParser.Parse(text);

            // Assert
            Assert.That(result[0].Blocks, Has.Count.EqualTo(4));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].Blocks[0].IsAtomic, Is.True, "heading");
                Assert.That(result[0].Blocks[1].IsAtomic, Is.False, "first paragraph");
                Assert.That(result[0].Blocks[2].IsAtomic, Is.True, "list");
                Assert.That(result[0].Blocks[3].IsAtomic, Is.False, "second paragraph");
            }
        }

        [Test]
        public void Parse_HeadingInsideQuote_DoesNotStartANewSection()
        {
            // Arrange - a quoted heading is content, not document structure
            var text = "## Historian Remarks\n\n" +
                       "> # Not A Real Section\n" +
                       "> The quoted line only cites the report title.";

            // Act
            var result = MarkdownSectionParser.Parse(text);

            // Assert - still one section; the quote stays a single atomic block
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].HeadingPath, Is.EqualTo(new[] { "Historian Remarks" }));
        }

        private static string GetBlockText(string source, Section section, int blockIndex)
        {
            var span = section.Blocks[blockIndex].Span;
            return source.Substring(span.Start, span.Length);
        }
    }
}
