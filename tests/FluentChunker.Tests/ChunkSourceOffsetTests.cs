using FluentChunker.Contracts;
using FluentChunker.Tokenizers;

namespace FluentChunker.Tests
{
    public class ChunkSourceOffsetTests
    {
        // Each document targets a different path: ASCII prose packs structurally, the Markdown one exercises sections and atomic blocks,
        // and the two multi-byte runs have no boundary the splitter recognises so they reach the token-window fallbacks.
        private const string Prose = "First sentence here. Second one follows. Third wraps it up.\n\nNew paragraph starts. And it ends here after a while.";
        private const string Markdown = "# Guide\n\nIntro text goes here.\n\n## Setup\n\nInstall it first.\n\n```bash\nnpm install\n```\n\n## Usage\n\nRun it, then read the output carefully.";
        private const string ChineseRun = "这是第一句话。这是第二句话。这是第三句话。这是第四句话。这是第五句话。";
        private const string EmojiRun = "Emoji run: 😀😃😄😁😆😅🤣😂🙂🙃 and more 🎉🎊🎈 all in one stretch with no break";

        private static readonly string[] Documents = [Prose, Markdown, ChineseRun, EmojiRun];

        private MicrosoftMLTokenizerAdapter _tokenizer;

        [SetUp]
        public void SetUp()
        {
            _tokenizer = new MicrosoftMLTokenizerAdapter("cl100k_base");
        }

        private IReadOnlyList<ChunkingPipeline> PipelinesFor(int maxTokens, int overlap) =>
        [
            ChunkingPipeline.CreateBuilder().UseTokenizer(_tokenizer)
                .UseSplitter(s => s.WithFixedSizeChunking().WithMaxTokenSize(maxTokens).WithOverlap(overlap)).Build(),
            ChunkingPipeline.CreateBuilder().UseTokenizer(_tokenizer)
                .UseSplitter(s => s.WithSentenceAwareChunking().WithMaxTokenSize(maxTokens).WithOverlap(overlap)).Build(),
            ChunkingPipeline.CreateBuilder().UseTokenizer(_tokenizer)
                .UseSplitter(s => s.WithRecursiveChunking().WithMaxTokenSize(maxTokens).WithOverlap(overlap)).Build(),
            ChunkingPipeline.CreateBuilder().UseTokenizer(_tokenizer)
                .UseSplitter(s => s.WithStructureAwareChunking().WithMaxTokenSize(maxTokens)).Build(),
        ];

        [Test]
        public void SourceOffsets_ChunkConstructedWithoutThem_ReportNotTracked()
        {
            // Arrange & Act - an IChunker written outside this library sets only the required members.
            var chunk = new Chunk { Index = 0, Text = "text", TokenCount = 1 };

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(chunk.SourceStart, Is.EqualTo(-1));
                Assert.That(chunk.SourceLength, Is.EqualTo(-1));
            }
        }

        [Test]
        public void SourceOffsets_SetOnConstruction_AreReportedBack()
        {
            // Arrange & Act
            var chunk = new Chunk { Index = 0, Text = "text", TokenCount = 1, SourceStart = 12, SourceLength = 4 };

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(chunk.SourceStart, Is.EqualTo(12));
                Assert.That(chunk.SourceLength, Is.EqualTo(4));
            }
        }

        [TestCase(12, 0)]
        [TestCase(12, 4)]
        [TestCase(30, 0)]
        [TestCase(30, 8)]
        [TestCase(64, 16)]
        public void Chunk_EveryStrategyAndDocument_OffsetsSliceTheSourceBackToTheChunkText(int maxTokens, int overlap)
        {
            // Arrange
            var pipelines = PipelinesFor(maxTokens, overlap);

            // Act
            var violations = new List<string>();
            foreach (var pipeline in pipelines)
            {
                foreach (var document in Documents)
                {
                    foreach (var chunk in pipeline.Chunk(document))
                    {
                        if (chunk.SourceStart < 0 || chunk.SourceLength < 0
                            || chunk.SourceStart + chunk.SourceLength > document.Length
                            || document.Substring(chunk.SourceStart, chunk.SourceLength) != chunk.Text)
                        {
                            violations.Add($"[{chunk.SourceStart},{chunk.SourceLength}] != {chunk.Text}");
                        }
                    }
                }
            }

            // Assert
            Assert.That(violations, Is.Empty);
        }

        [TestCase(12)]
        [TestCase(30)]
        [TestCase(64)]
        public void Chunk_EveryStrategyWithoutOverlap_OffsetsAdvanceMonotonically(int maxTokens)
        {
            // Arrange - without overlap a later chunk can never start before an earlier one ends.
            var pipelines = PipelinesFor(maxTokens, overlap: 0);

            // Act
            var violations = new List<string>();
            foreach (var pipeline in pipelines)
            {
                foreach (var document in Documents)
                {
                    var chunks = pipeline.Chunk(document);
                    for (var i = 1; i < chunks.Count; i++)
                    {
                        if (chunks[i].SourceStart < chunks[i - 1].SourceStart + chunks[i - 1].SourceLength)
                            violations.Add($"chunk {i} starts at {chunks[i].SourceStart}, before {chunks[i - 1].SourceStart + chunks[i - 1].SourceLength}");
                    }
                }
            }

            // Assert
            Assert.That(violations, Is.Empty);
        }

        [Test]
        public void Chunk_WithOverlap_ConsecutiveChunksReportOverlappingRanges()
        {
            // Arrange
            var pipeline = ChunkingPipeline.CreateBuilder().UseTokenizer(_tokenizer)
                .UseSplitter(s => s.WithFixedSizeChunking().WithMaxTokenSize(20).WithOverlap(5)).Build();
            var document = string.Join(" ", Enumerable.Repeat("A pragmatic developer chooses the simplest thing that works.", 10));

            // Act
            var result = pipeline.Chunk(document);

            // Assert
            Assert.That(result, Has.Count.GreaterThan(1));
            Assert.That(result[1].SourceStart, Is.LessThan(result[0].SourceStart + result[0].SourceLength));
        }
    }
}
