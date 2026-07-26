using FluentChunker.Tokenizers;

namespace FluentChunker.Tests
{
    public class MultiByteChunkingTests
    {
        // Each document reaches a token-window fallback: the emoji and CJK runs have no sentence boundary
        // the splitter recognises, so they arrive as a single oversized unit.
        private const string EmojiDocument = "Emoji run: 😀😃😄😁😆😅🤣😂🙂🙃 and more 🎉🎊🎈 all in one very long stretch without any break at all";
        private const string ChineseDocument = "这是第一句话。这是第二句话。这是第三句话。这是第四句话。这是第五句话。这是第六句话。";
        private const string MixedDocument = "混合 text 日本語のテキスト العربية עברית ไทย देवनागरी mixed together in one long run with no break";

        private MicrosoftMLTokenizerAdapter _tokenizer;

        [SetUp]
        public void SetUp()
        {
            _tokenizer = new MicrosoftMLTokenizerAdapter("cl100k_base");
        }

        private static readonly string[] Documents = [EmojiDocument, ChineseDocument, MixedDocument];

        private IReadOnlyList<ChunkingPipeline> PipelinesFor(int maxTokens) =>
        [
            ChunkingPipeline.CreateBuilder()
            .UseTokenizer(_tokenizer)
            .UseSplitter(s => s
                .WithFixedSizeChunking()
                .WithMaxTokenSize(maxTokens))
            .Build(),

            ChunkingPipeline.CreateBuilder()
            .UseTokenizer(_tokenizer)
            .UseSplitter(s => s
                .WithSentenceAwareChunking()
                .WithMaxTokenSize(maxTokens))
            .Build(),

            ChunkingPipeline.CreateBuilder()
            .UseTokenizer(_tokenizer)
            .UseSplitter(s => s
                .WithRecursiveChunking()
                .WithMaxTokenSize(maxTokens))
            .Build(),

            ChunkingPipeline.CreateBuilder()
            .UseTokenizer(_tokenizer)
            .UseSplitter(s => s
                .WithStructureAwareChunking()
                .WithMaxTokenSize(maxTokens))
            .Build(),
        ];

        [TestCase(8)]
        [TestCase(12)]
        [TestCase(20)]
        [TestCase(40)]
        public void Chunk_MultiByteDocumentsAcrossEveryStrategy_ProducesNoReplacementCharacters(int maxTokens)
        {
            // Arrange
            var pipelines = PipelinesFor(maxTokens);

            // Act
            var corrupted = new List<string>();
            foreach (var pipeline in pipelines)
            {
                foreach (var document in Documents)
                {
                    corrupted.AddRange(pipeline.Chunk(document).Where(c => c.Text.Contains('�')).Select(c => c.Text));
                }
            }

            // Assert
            Assert.That(corrupted, Is.Empty);
        }

        [TestCase(8)]
        [TestCase(12)]
        [TestCase(20)]
        [TestCase(40)]
        public void Chunk_MultiByteDocumentsAcrossEveryStrategy_KeepsEveryChunkWithinBudget(int maxTokens)
        {
            // Arrange
            var pipelines = PipelinesFor(maxTokens);

            // Act
            var overBudget = new List<int>();
            foreach (var pipeline in pipelines)
            {
                foreach (var document in Documents)
                {
                    overBudget.AddRange(pipeline.Chunk(document)
                        .Select(c => _tokenizer.CountTokens(c.Text))
                        .Where(count => count > maxTokens));
                }
            }

            // Assert
            Assert.That(overBudget, Is.Empty);
        }

        [TestCase(8)]
        [TestCase(12)]
        [TestCase(20)]
        [TestCase(40)]
        public void Chunk_MultiByteDocumentsAcrossEveryStrategy_ReportsAccurateTokenCounts(int maxTokens)
        {
            // Arrange
            var pipelines = PipelinesFor(maxTokens);

            // Act
            var mismatched = new List<string>();
            foreach (var pipeline in pipelines)
            {
                foreach (var document in Documents)
                {
                    mismatched.AddRange(pipeline.Chunk(document)
                        .Where(c => _tokenizer.CountTokens(c.Text) != c.TokenCount)
                        .Select(c => c.Text));
                }
            }

            // Assert
            Assert.That(mismatched, Is.Empty);
        }

        [Test]
        public void Chunk_OversizedMultiByteUnitWithoutOverlap_ChunksReconstructTheUnitExactly()
        {
            // Arrange - no overlap, so the chunks partition the document and must concatenate back to it.
            var pipeline = ChunkingPipeline.CreateBuilder().UseTokenizer(_tokenizer)
                .UseSplitter(s => s.WithSentenceAwareChunking().WithMaxTokenSize(12)).Build();

            // Act
            var result = pipeline.Chunk(ChineseDocument);

            // Assert
            Assert.That(result, Has.Count.GreaterThan(1));
            Assert.That(string.Concat(result.Select(c => c.Text)), Is.EqualTo(ChineseDocument));
        }

        // The worst case behind the four-token floor on the max token size.
        //
        // A character is never split across chunks, because splitting one is what produced U+FFFD in the first place,
        // so a budget smaller than the widest single character could not be honoured and is rejected instead.
        // Four is the exact threshold: encoding all 1,112,032 Unicode characters under cl100k_base yields a maximum of four tokens for one character, at U+10000,
        // and the longest indivisible run stays four whether the character stands alone or follows a space or a letter.
        //
        // These three characters are that worst case, so they pin the boundary rather than merely sampling near it.
        // An emoji fixture cannot do this job: the widest emoji run is three tokens, so it would fit a budget of three and hide the real threshold.
        private const string FourTokenCharacterDocument = "𐀀𐌰𒀀 ancient scripts in one unbroken run with no sentence boundary at all";

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void WithMaxTokenSize_BudgetNarrowerThanTheWidestCharacter_ThrowsArgumentOutOfRangeException(int maxTokens)
        {
            // Arrange
            var splitterStep = ChunkingPipeline.CreateBuilder().UseTokenizer(_tokenizer);

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                splitterStep.UseSplitter(s => s
                    .WithFixedSizeChunking()
                    .WithMaxTokenSize(maxTokens)));
        }

        [TestCase(4)]
        [TestCase(5)]
        [TestCase(8)]
        [TestCase(16)]
        [TestCase(64)]
        [TestCase(512)]
        public void Chunk_MaxTokenSizeOfFourOrAbove_NeverExceedsTheBudget(int maxTokens)
        {
            // Arrange - the worst-case document alongside the others, since the guarantee is claimed for any input.
            var pipelines = PipelinesFor(maxTokens);
            string[] documents = [FourTokenCharacterDocument, EmojiDocument, ChineseDocument, MixedDocument];

            // Act
            var overBudget = pipelines
                .SelectMany(p => documents.SelectMany(p.Chunk))
                .Where(c => c.TokenCount > maxTokens)
                .Select(c => $"{c.TokenCount} > {maxTokens}: {c.Text}")
                .ToList();

            // Assert
            Assert.That(overBudget, Is.Empty);
        }

        [TestCase(4)]
        [TestCase(5)]
        [TestCase(16)]
        public void Chunk_FourTokenCharactersWithoutOverlap_ChunksReconstructTheDocumentExactly(int maxTokens)
        {
            // Arrange - fixed-size chunking partitions the whole document, so the chunks must concatenate back to it, starting at the narrowest budget the builder allows.
            var pipeline = ChunkingPipeline.CreateBuilder().UseTokenizer(_tokenizer)
                .UseSplitter(s => s.WithFixedSizeChunking().WithMaxTokenSize(maxTokens)).Build();

            // Act
            var result = pipeline.Chunk(FourTokenCharacterDocument);

            // Assert
            Assert.That(string.Concat(result.Select(c => c.Text)), Is.EqualTo(FourTokenCharacterDocument));
        }
    }
}
