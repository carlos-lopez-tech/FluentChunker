using FluentChunker.Chunkers.Recursive;
using FluentChunker.Chunkers.SentenceAware;
using FluentChunker.Tokenizers;

namespace FluentChunker.Tests
{
    public class SplitterBuilderTests
    {
        private const string EncodingName = "o200k_base";
        private SharpTokenizerAdapter _sharpTokenizer;

        [SetUp]
        public void Setup()
        {
            _sharpTokenizer = new SharpTokenizerAdapter(EncodingName);
        }

        [Test]
        public void UseSplitter_EmptyConfiguration_ThrowsInvalidOperationException()
        {
            // Arrange
            var splitterStep = ChunkingPipeline.Builder.Initialize()
                .UseTokenizer(_sharpTokenizer);

            // Act & Assert — the error must name every available strategy
            var ex = Assert.Throws<InvalidOperationException>(() => splitterStep.UseSplitter(s => { }));
            Assert.That(ex!.Message, Does.Contain("WithFixedSizeChunking")
                .And.Contain("WithSentenceAwareChunking")
                .And.Contain("WithRecursiveChunking"));
        }

        [Test]
        public void UseSplitter_StrategyWithoutMaxTokenSize_ThrowsInvalidOperationException()
        {
            // Arrange
            var splitterStep = ChunkingPipeline.Builder.Initialize()
                .UseTokenizer(_sharpTokenizer);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                splitterStep.UseSplitter(s => s.WithFixedSizeChunking()));
        }

        [Test]
        public void WithMaxTokenSize_NonPositiveValue_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            var splitterStep = ChunkingPipeline.Builder.Initialize()
                .UseTokenizer(_sharpTokenizer);

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                splitterStep.UseSplitter(s => s
                    .WithFixedSizeChunking()
                    .WithMaxTokenSize(0)));
        }

        [Test]
        public void WithOverlap_NegativeValue_ThrowsArgumentOutOfRangeException()
        {
            // Arrange
            var splitterStep = ChunkingPipeline.Builder.Initialize()
                .UseTokenizer(_sharpTokenizer);

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
            var splitterStep = ChunkingPipeline.Builder.Initialize()
                .UseTokenizer(_sharpTokenizer);

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
            var builder = new Splitter.Builder(_sharpTokenizer);
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
            var splitterStep = ChunkingPipeline.Builder.Initialize()
                .UseTokenizer(_sharpTokenizer);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                splitterStep.UseSplitter(s => s.WithSentenceAwareChunking()));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void WithAbbreviations_NullOrWhitespaceEntry_ThrowsArgumentException(string? entry)
        {
            // Arrange
            var splitterStep = ChunkingPipeline.Builder.Initialize()
                .UseTokenizer(_sharpTokenizer);

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                splitterStep.UseSplitter(s => s
                    .WithSentenceAwareChunking()
                    .WithMaxTokenSize(20)
                    .WithAbbreviations(entry!)));
        }

        // At MaxTokenSize(6), "Vgl." (3 tokens) and " Abschnitt 3 im Buch." (6 tokens, gap-inclusive)
        // don't fit together (3+6 > 6), so without the abbreviation guard the splitter treats
        // "Vgl." as its own sentence and the first chunk is exactly "Vgl.".
        //  Registering "Vgl" as an abbreviation suppresses that false break, merging it into "Vgl. Abschnitt 3 im Buch."
        // (9 tokens) which then no longer fits the 6-token budget as a single unit and falls back to fixed-size windowing
        // - proving the guard changed the chunker's actual segmentation.
        [Test]
        public void UseSplitter_CustomAbbreviationConfigured_SuppressesMatchingSentenceBreak()
        {
            // Arrange
            var text = "Vgl. Abschnitt 3 im Buch. Weiter geht es.";
            var unguardedPipeline = ChunkingPipeline.Builder.Initialize()
                .UseTokenizer(_sharpTokenizer)
                .UseSplitter(s => s
                    .WithSentenceAwareChunking()
                    .WithMaxTokenSize(6))
                .Build();
            var guardedPipeline = ChunkingPipeline.Builder.Initialize()
                .UseTokenizer(_sharpTokenizer)
                .UseSplitter(s => s
                    .WithSentenceAwareChunking()
                    .WithMaxTokenSize(6)
                    .WithAbbreviations("Vgl"))
                .Build();

            // Act
            var unguardedResult = unguardedPipeline.Execute(text);
            var guardedResult = guardedPipeline.Execute(text);

            using (Assert.EnterMultipleScope())
            {
                // Assert
                Assert.That(unguardedResult[0].Text, Is.EqualTo("Vgl."));
                Assert.That(guardedResult[0].Text, Is.Not.EqualTo("Vgl."));
            }
            Assert.That(guardedResult[0].Text, Does.Contain("Abschnitt"));
        }

        [Test]
        public void UseSplitter_CustomAbbreviationWithTrailingDot_IsNormalizedAndSuppressesMatchingSentenceBreak()
        {
            // Arrange
            var text = "Vgl. Abschnitt 3 im Buch. Weiter geht es.";
            var pipeline = ChunkingPipeline.Builder.Initialize()
                .UseTokenizer(_sharpTokenizer)
                .UseSplitter(s => s
                    .WithSentenceAwareChunking()
                    .WithMaxTokenSize(6)
                    .WithAbbreviations("Vgl.")) // trailing dot supplied on purpose
                .Build();

            // Act
            var result = pipeline.Execute(text);

            // Assert - same suppressed break as the no-dot case, proving trailing-dot normalization
            Assert.That(result[0].Text, Is.Not.EqualTo("Vgl."));
            Assert.That(result[0].Text, Does.Contain("Abschnitt"));
        }

        [Test]
        public void UseSplitter_SentenceAwarePipeline_ProducesSentenceAlignedChunks()
        {
            // Arrange
            var pipeline = ChunkingPipeline.Builder.Initialize()
                .UseTokenizer(_sharpTokenizer)
                .UseSplitter(s => s
                    .WithSentenceAwareChunking()
                    .WithMaxTokenSize(15))
                .Build();
            var text = "The quick brown fox jumps over the fence. A lazy dog sleeps in the sun. " +
                       "Cats watch quietly from the roof.";

            // Act
            var result = pipeline.Execute(text);

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
            var builder = new Splitter.Builder(_sharpTokenizer);
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
            var splitterStep = ChunkingPipeline.Builder.Initialize()
                .UseTokenizer(_sharpTokenizer);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                splitterStep.UseSplitter(s => s.WithRecursiveChunking()));
        }

        [Test]
        public void UseSplitter_RecursivePipeline_ProducesStructureAlignedChunks()
        {
            // Arrange — two paragraphs of two sentences each; a 15-token budget forces each paragraph to descend to its sentences
            var pipeline = ChunkingPipeline.Builder.Initialize()
                .UseTokenizer(_sharpTokenizer)
                .UseSplitter(s => s
                    .WithRecursiveChunking()
                    .WithMaxTokenSize(15))
                .Build();
            var text = "The quick brown fox jumps over the fence. A lazy dog sleeps in the sun.\n\n" +
                       "Cats watch quietly from the roof. Birds sing their morning songs.";

            // Act
            var result = pipeline.Execute(text);

            // Assert — chunks align with structural boundaries and respect the budget
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
    }
}
