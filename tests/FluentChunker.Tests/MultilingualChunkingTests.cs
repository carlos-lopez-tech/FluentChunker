using FluentChunker.Tokenizers;

namespace FluentChunker.Tests
{
    public class MultilingualChunkingTests
    {
        // Six clean sentences, 49 tokens under cl100k_base.
        // Before this change the splitter found no boundary, so the whole document arrived at the token-window fallback and was cut on token counts alone.
        private const string ChineseDocument = "这是第一句话。这是第二句话。这是第三句话。这是第四句话。这是第五句话。这是第六句话。";
        private const string HindiDocument = "यह पहला वाक्य है। यह दूसरा वाक्य है। यह तीसरा वाक्य है।";
        private const string MixedDocument = "An English sentence opens the document. 这是中文句子。And English resumes for a while here.";

        private MicrosoftMLTokenizerAdapter _tokenizer;

        [SetUp]
        public void SetUp()
        {
            _tokenizer = new MicrosoftMLTokenizerAdapter("cl100k_base");
        }

        // Sentence spans exclude the whitespace between sentences, so concatenated chunks cannot be compared to the source verbatim.
        // Comparing both sides without whitespace still proves no character was lost, duplicated or reordered.
        private static string WithoutWhitespace(string text)
            => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));

        private IReadOnlyList<ChunkingPipeline> SplitterAwarePipelines(int maxTokens) =>
        [
            ChunkingPipeline.CreateBuilder().UseTokenizer(_tokenizer)
                .UseSplitter(s => s.WithSentenceAwareChunking().WithMaxTokenSize(maxTokens)).Build(),
            ChunkingPipeline.CreateBuilder().UseTokenizer(_tokenizer)
                .UseSplitter(s => s.WithRecursiveChunking().WithMaxTokenSize(maxTokens)).Build(),
            ChunkingPipeline.CreateBuilder().UseTokenizer(_tokenizer)
                .UseSplitter(s => s.WithStructureAwareChunking().WithMaxTokenSize(maxTokens)).Build(),
        ];

        [Test]
        public void Chunk_ChineseDocument_EndsEveryChunkOnASentenceBoundary()
        {
            // Arrange - the fallback cut mid-sentence on token counts; real boundaries end on the full stop.
            var pipeline = ChunkingPipeline.CreateBuilder().UseTokenizer(_tokenizer)
                .UseSplitter(s => s.WithSentenceAwareChunking().WithMaxTokenSize(15)).Build();

            // Act
            var result = pipeline.Chunk(ChineseDocument);

            // Assert
            Assert.That(result, Has.Count.GreaterThan(1));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Select(c => c.Text.TrimEnd()), Has.All.EndsWith("。"));
                Assert.That(result.Select(c => c.TokenCount), Has.All.LessThanOrEqualTo(15));
                Assert.That(string.Concat(result.Select(c => c.Text)), Is.EqualTo(ChineseDocument));
            }
        }

        [Test]
        public void Chunk_HindiDocument_EndsEveryChunkOnADanda()
        {
            // Arrange
            var pipeline = ChunkingPipeline.CreateBuilder().UseTokenizer(_tokenizer)
                .UseSplitter(s => s.WithSentenceAwareChunking().WithMaxTokenSize(30)).Build();

            // Act
            var result = pipeline.Chunk(HindiDocument);

            // Assert
            Assert.That(result, Has.Count.GreaterThan(1));
            Assert.That(result.Select(c => c.Text.TrimEnd()), Has.All.EndsWith("।"));
        }

        [TestCase(15)]
        [TestCase(25)]
        [TestCase(40)]
        public void Chunk_NonLatinDocumentsAcrossSplitterAwareStrategies_KeepEveryChunkWithinBudget(int maxTokens)
        {
            // Arrange
            var pipelines = SplitterAwarePipelines(maxTokens);

            // Act
            var overBudget = new List<int>();
            foreach (var pipeline in pipelines)
            {
                foreach (var document in new[] { ChineseDocument, HindiDocument, MixedDocument })
                {
                    overBudget.AddRange(pipeline.Chunk(document)
                        .Select(c => _tokenizer.CountTokens(c.Text))
                        .Where(count => count > maxTokens));
                }
            }

            // Assert
            Assert.That(overBudget, Is.Empty);
        }

        [Test]
        public void Chunk_MixedScriptDocument_SplitsAtBothLatinAndIdeographicBoundaries()
        {
            // Arrange - a budget that forces a split but leaves each sentence comfortably intact.
            var pipeline = ChunkingPipeline.CreateBuilder().UseTokenizer(_tokenizer)
                .UseSplitter(s => s.WithSentenceAwareChunking().WithMaxTokenSize(12)).Build();

            // Act
            var result = pipeline.Chunk(MixedDocument);

            // Assert
            Assert.That(result, Has.Count.GreaterThan(1));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Select(c => c.Text.TrimEnd()[^1]), Has.All.AnyOf('.', '。'));
                Assert.That(WithoutWhitespace(string.Concat(result.Select(c => c.Text))), Is.EqualTo(WithoutWhitespace(MixedDocument)));
            }
        }
    }
}
