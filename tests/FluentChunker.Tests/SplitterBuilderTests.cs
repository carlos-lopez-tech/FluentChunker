using FluentChunker.Chunkers.Recursive;
using FluentChunker.Chunkers.SentenceAware;
using FluentChunker.Chunkers.StructureAware;
using FluentChunker.Tokenizers;

namespace FluentChunker.Tests
{
    public class SplitterBuilderTests
    {
        private const string EncodingName = "cl100k_base";
        private MicrosoftMLTokenizerAdapter _microsoftMLTokenizer;

        [SetUp]
        public void Setup()
        {
            _microsoftMLTokenizer = new MicrosoftMLTokenizerAdapter(EncodingName);
        }

        [Test]
        public void UseSplitter_EmptyConfiguration_ThrowsInvalidOperationException()
        {
            // Arrange
            var splitterStep = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_microsoftMLTokenizer);

            // Act & Assert - the error must name every available strategy
            var ex = Assert.Throws<InvalidOperationException>(() => splitterStep.UseSplitter(s => { }));
            Assert.That(ex!.Message, Does.Contain("WithFixedSizeChunking")
                .And.Contain("WithSentenceAwareChunking")
                .And.Contain("WithRecursiveChunking")
                .And.Contain("WithStructureAwareChunking"));
        }

        [Test]
        public void UseSplitter_StrategyWithoutMaxTokenSize_ThrowsInvalidOperationException()
        {
            // Arrange
            var splitterStep = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_microsoftMLTokenizer);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                splitterStep.UseSplitter(s => s.WithFixedSizeChunking()));
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(3)]
        public void WithMaxTokenSize_BelowFourTokens_ThrowsArgumentOutOfRangeException(int maxTokens)
        {
            // Arrange
            var splitterStep = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_microsoftMLTokenizer);

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                splitterStep.UseSplitter(s => s
                    .WithFixedSizeChunking()
                    .WithMaxTokenSize(maxTokens)));
        }

        [Test]
        public async Task WithMaxTokenSize_ExactlyFourTokens_IsAccepted()
        {
            // Arrange - four is the smallest budget that always holds a single character
            var splitterStep = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_microsoftMLTokenizer);

            // Act
            var pipeline = splitterStep
                .UseSplitter(s => s
                    .WithFixedSizeChunking()
                    .WithMaxTokenSize(4))
                .Build();

            // Assert
            Assert.That(await pipeline.ChunkAsync("Alpha beta gamma delta epsilon zeta.").ToListAsync(), Is.Not.Empty);
        }

        [Test]
        public void WithOverlap_NegativeValue_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            var splitterStep = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_microsoftMLTokenizer);

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                splitterStep.UseSplitter(s => s
                    .WithFixedSizeChunking()
                    .WithMaxTokenSize(10)
                    .WithOverlap(-1)));
        }

        [Test]
        public void WithOverlap_OverlapEqualToMaxTokenSize_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            var splitterStep = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_microsoftMLTokenizer);

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                splitterStep.UseSplitter(s => s
                    .WithFixedSizeChunking()
                    .WithMaxTokenSize(10)
                    .WithOverlap(10)));
        }

        [Test]
        public void WithSentenceAwareChunking_ValidConfiguration_BuildsSentenceAwareChunker()
        {
            // Arrange
            var builder = new Splitter.Builder(_microsoftMLTokenizer);
            builder.WithSentenceAwareChunking()
                .WithMaxTokenSize(20)
                .WithOverlap(5);

            // Act
            var chunker = builder.Build();

            // Assert
            Assert.That(chunker, Is.InstanceOf<SentenceAwareChunker>());
        }

        [Test]
        public void WithSentenceAwareChunking_WithoutMaxTokenSize_ThrowsInvalidOperationException()
        {
            // Arrange
            var splitterStep = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_microsoftMLTokenizer);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                splitterStep.UseSplitter(s => s.WithSentenceAwareChunking()));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void AddAbbreviations_NullOrWhitespaceEntry_ThrowsArgumentException(string? entry)
        {
            // Arrange
            var splitterStep = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_microsoftMLTokenizer);

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                splitterStep.UseSplitter(s => s
                    .WithSentenceAwareChunking()
                    .WithMaxTokenSize(20)
                    .AddAbbreviations(entry!)));
        }

        // At MaxTokenSize(6), "Vgl." (3 tokens) and " Abschnitt 3 im Buch." (6 tokens, gap-inclusive) don't fit together (3+6 > 6), so without the abbreviation guard the splitter treats "Vgl." as its own sentence and the first chunk is exactly "Vgl.".
        // Registering "Vgl" as an abbreviation suppresses that false break, merging it into "Vgl. Abschnitt 3 im Buch." (9 tokens) which then no longer fits the 6-token budget as a single unit and falls back to fixed-size windowing, proving the guard changed the chunker's actual segmentation.
        [Test]
        public async Task UseSplitter_CustomAbbreviationConfigured_SuppressesMatchingSentenceBreak()
        {
            // Arrange
            var text = "Vgl. Abschnitt 3 im Buch. Weiter geht es.";
            var unguardedPipeline = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithSentenceAwareChunking()
                    .WithMaxTokenSize(6))
                .Build();
            var guardedPipeline = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithSentenceAwareChunking()
                    .WithMaxTokenSize(6)
                    .AddAbbreviations("Vgl"))
                .Build();

            // Act
            var unguardedResult = await unguardedPipeline.ChunkAsync(text).ToListAsync();
            var guardedResult = await guardedPipeline.ChunkAsync(text).ToListAsync();

            using (Assert.EnterMultipleScope())
            {
                // Assert
                Assert.That(unguardedResult[0].Text, Is.EqualTo("Vgl."));
                Assert.That(guardedResult[0].Text, Is.Not.EqualTo("Vgl."));
            }
            Assert.That(guardedResult[0].Text, Does.Contain("Abschnitt"));
        }

        [Test]
        public async Task UseSplitter_CustomAbbreviationWithTrailingDot_IsNormalizedAndSuppressesMatchingSentenceBreak()
        {
            // Arrange
            var text = "Vgl. Abschnitt 3 im Buch. Weiter geht es.";
            var pipeline = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithSentenceAwareChunking()
                    .WithMaxTokenSize(6)
                    .AddAbbreviations("Vgl.")) // trailing dot supplied on purpose
                .Build();

            // Act
            var result = await pipeline.ChunkAsync(text).ToListAsync();

            // Assert - same suppressed break as the no-dot case, proving trailing-dot normalization
            Assert.That(result[0].Text, Is.Not.EqualTo("Vgl."));
            Assert.That(result[0].Text, Does.Contain("Abschnitt"));
        }

        [Test]
        public async Task UseSplitter_SentenceAwarePipeline_ProducesSentenceAlignedChunks()
        {
            // Arrange
            var pipeline = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithSentenceAwareChunking()
                    .WithMaxTokenSize(15))
                .Build();
            var text = "The quick brown fox jumps over the fence. A lazy dog sleeps in the sun. " +
                       "Cats watch quietly from the roof.";

            // Act
            var result = await pipeline.ChunkAsync(text).ToListAsync();

            // Assert
            Assert.That(result, Has.Count.GreaterThan(1));
            using (Assert.EnterMultipleScope())
            {
                foreach (var chunk in result)
                {
                    Assert.That(chunk.Text, Does.EndWith("."));
                    Assert.That(chunk.TokenCount, Is.LessThanOrEqualTo(15));
                }
            }
        }

        [Test]
        public void WithRecursiveChunking_ValidConfiguration_BuildsRecursiveChunker()
        {
            // Arrange
            var builder = new Splitter.Builder(_microsoftMLTokenizer);
            builder.WithRecursiveChunking()
                .WithMaxTokenSize(20)
                .WithOverlap(5);

            // Act
            var chunker = builder.Build();

            // Assert
            Assert.That(chunker, Is.InstanceOf<RecursiveChunker>());
        }

        [Test]
        public void WithRecursiveChunking_WithoutMaxTokenSize_ThrowsInvalidOperationException()
        {
            // Arrange
            var splitterStep = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_microsoftMLTokenizer);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                splitterStep.UseSplitter(s => s.WithRecursiveChunking()));
        }

        [Test]
        public async Task UseSplitter_RecursivePipeline_ProducesStructureAlignedChunks()
        {
            // Arrange - two paragraphs of two sentences each; a 15-token budget forces each paragraph to descend to its sentences
            var pipeline = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithRecursiveChunking()
                    .WithMaxTokenSize(15))
                .Build();
            var text = "The quick brown fox jumps over the fence. A lazy dog sleeps in the sun.\n\n" +
                       "Cats watch quietly from the roof. Birds sing their morning songs.";

            // Act
            var result = await pipeline.ChunkAsync(text).ToListAsync();

            // Assert - chunks align with structural boundaries and respect the budget
            Assert.That(result, Has.Count.GreaterThan(1));
            using (Assert.EnterMultipleScope())
            {
                foreach (var chunk in result)
                {
                    Assert.That(chunk.Text, Does.EndWith("."));
                    Assert.That(chunk.TokenCount, Is.LessThanOrEqualTo(15));
                }
            }
        }

        [Test]
        public void WithStructureAwareChunking_ValidConfiguration_BuildsStructureAwareChunker()
        {
            // Arrange
            var builder = new Splitter.Builder(_microsoftMLTokenizer);
            builder.WithStructureAwareChunking()
                .WithMaxTokenSize(120);

            // Act
            var chunker = builder.Build();

            // Assert
            Assert.That(chunker, Is.InstanceOf<StructureAwareChunker>());
        }

        [Test]
        public void WithStructureAwareChunking_WithoutMaxTokenSize_ThrowsInvalidOperationException()
        {
            // Arrange
            var splitterStep = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_microsoftMLTokenizer);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                splitterStep.UseSplitter(s => s.WithStructureAwareChunking()));
        }

        [TestCase(0)]
        [TestCase(3)]
        public void WithStructureAwareChunking_MaxTokenSizeBelowFour_ThrowsArgumentOutOfRangeException(int maxTokens)
        {
            // Arrange
            var splitterStep = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_microsoftMLTokenizer);

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                splitterStep.UseSplitter(s => s
                    .WithStructureAwareChunking()
                    .WithMaxTokenSize(maxTokens)));
        }

        [Test]
        public void WithStructureAwareChunking_AllDotsAbbreviation_ThrowsArgumentException()
        {
            // Arrange - an all-dots entry trims down to empty and must fail the whole batch
            var splitterStep = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_microsoftMLTokenizer);

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                splitterStep.UseSplitter(s => s
                    .WithStructureAwareChunking()
                    .WithMaxTokenSize(120)
                    .AddAbbreviations("...")));
        }

        [Test]
        public async Task UseSplitter_StructureAwarePipeline_StampsHeadingPathOnEveryChunk()
        {
            // Arrange
            var pipeline = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithStructureAwareChunking()
                    .WithMaxTokenSize(120))
                .Build();
            var text = "## Retrieval Notes\n\n" +
                       "The team reviewed the reports before the meeting started.\n\n" +
                       "## Ranking Results\n\n" +
                       "The group compared results from the two search systems.";

            // Act
            var result = await pipeline.ChunkAsync(text).ToListAsync();

            // Assert - one chunk per section (hard seams), each carrying its breadcrumb
            Assert.That(result, Has.Count.EqualTo(2));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].Metadata["headingPath"].GetString(), Is.EqualTo("Retrieval Notes"));
                Assert.That(result[1].Metadata["headingPath"].GetString(), Is.EqualTo("Ranking Results"));
            }
        }
    }
}
