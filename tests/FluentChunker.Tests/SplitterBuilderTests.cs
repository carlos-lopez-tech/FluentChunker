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

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => splitterStep.UseSplitter(s => { }));
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
    }
}
