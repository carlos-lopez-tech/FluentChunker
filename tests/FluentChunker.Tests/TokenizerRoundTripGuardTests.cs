using System.Text;
using FluentChunker.Chunkers.FixedSize;
using FluentChunker.Tokenizers;

namespace FluentChunker.Tests
{
    public class TokenizerRoundTripGuardTests
    {
        // Every document below is a single sentence with no terminator, so each strategy runs out of structural boundaries and reaches the token-window layouts where the guard lives.
        // The drift document carries four 'z' characters, which is what the shortening double deletes on decode, mirroring the four characters the measured BertTokenizer run lost.
        private const string DriftDocument = "the quick brown fox jumps over the lazy dog while a puzzled zebra dozes";
        private const string MixedCaseDocument = "The Quick Brown Fox Jumps Over The Lazy Dog While A Puzzled Zebra Watches";

        // 48 characters, and the char-per-token doubles make that 48 tokens, so a budget of 8 divides it exactly.
        // The neighbouring document is 49, one over, which is the case where the final window is short.
        private const string ExactMultipleDocument = "the quick brown fox jumps over the lazy dog five";
        private const string NotMultipleDocument = "the quick brown fox jumps over the lazy dog again";

        private const string EmojiDocument = "Emoji run: 😀😃😄😁😆😅🤣😂🙂🙃 and more 🎉🎊🎈 all in one very long stretch without any break at all";
        private const string ChineseDocument = "这是第一句话。这是第二句话。这是第三句话。这是第四句话。这是第五句话。这是第六句话。";
        private const string MixedScriptDocument = "混合 text 日本語のテキスト العربية עברית ไทย देवनागरी mixed together in one long run with no break";

        private const string FixedSize = "FixedSize";
        private const string SentenceAware = "SentenceAware";
        private const string Recursive = "Recursive";
        private const string StructureAware = "StructureAware";

        private MicrosoftMLTokenizerAdapter _tokenizer;

        [SetUp]
        public void SetUp()
        {
            _tokenizer = new MicrosoftMLTokenizerAdapter("cl100k_base");
        }

        // One token per character, which needs no vocabulary file and keeps the character arithmetic the guard checks trivially predictable.
        // Only the decode mapping varies between scenarios, because that is the single behaviour the guard reacts to.
        private sealed class CharTokenizer(Func<char, string> decodeChar) : ITokenizer
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
                var decoded = new StringBuilder();

                foreach (var token in tokens)
                {
                    decoded.Append(decodeChar((char)token));
                }

                return decoded.ToString();
            }

            public int CountTokens(string text) => text.Length;
        }

        private static CharTokenizer Conforming() => new(c => c.ToString());

        // Deletes a character on decode, so the decoded text is strictly shorter than the source and the tail would be dropped.
        private static CharTokenizer Shortening() => new(c => c == 'z' ? string.Empty : c.ToString());

        // Doubles a character on decode, so the decoded text runs past the end of the source.
        // This is the direction a real BertTokenizer drifts on CJK text with the default IndividuallyTokenizeCjk, which inserts a separator between characters.
        // Without a bound check inside the layouts it surfaces as ArgumentOutOfRangeException from the caller's Substring, naming nothing the reader can act on.
        private static CharTokenizer Lengthening() => new(c => c == 'z' ? "zz" : c.ToString());

        // Length-preserving normalisation, which the guard must not reject.
        private static CharTokenizer Lowercasing() => new(c => char.ToLowerInvariant(c).ToString());

        private static ChunkingPipeline PipelineFor(string strategy, ITokenizer tokenizer, int maxTokens, int overlap = 0) =>
            strategy switch
            {
                FixedSize => ChunkingPipeline.CreateBuilder()
                    .UseTokenizer(tokenizer)
                    .UseSplitter(s => s.WithFixedSizeChunking().WithMaxTokenSize(maxTokens).WithOverlap(overlap))
                    .Build(),

                SentenceAware => ChunkingPipeline.CreateBuilder()
                    .UseTokenizer(tokenizer)
                    .UseSplitter(s => s.WithSentenceAwareChunking().WithMaxTokenSize(maxTokens).WithOverlap(overlap))
                    .Build(),

                Recursive => ChunkingPipeline.CreateBuilder()
                    .UseTokenizer(tokenizer)
                    .UseSplitter(s => s.WithRecursiveChunking().WithMaxTokenSize(maxTokens).WithOverlap(overlap))
                    .Build(),

                StructureAware => ChunkingPipeline.CreateBuilder()
                    .UseTokenizer(tokenizer)
                    .UseSplitter(s => s.WithStructureAwareChunking().WithMaxTokenSize(maxTokens))
                    .Build(),

                _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "Unknown strategy.")
            };

        [TestCase(FixedSize)]
        [TestCase(SentenceAware)]
        [TestCase(Recursive)]
        [TestCase(StructureAware)]
        public void Chunk_TokenizerDecodesShorterThanItsInput_ThrowsInsteadOfDroppingTheTail(string strategy)
        {
            // Arrange
            var pipeline = PipelineFor(strategy, Shortening(), maxTokens: 8);

            // Act & Assert
            Assert.ThrowsAsync<InvalidOperationException>(async () => await pipeline.ChunkAsync(DriftDocument).ToListAsync());
        }

        [TestCase(FixedSize)]
        [TestCase(SentenceAware)]
        [TestCase(Recursive)]
        public void Chunk_TokenizerDecodesShorterThanItsInputWithOverlap_StillThrows(string strategy)
        {
            // Arrange
            var pipeline = PipelineFor(strategy, Shortening(), maxTokens: 8, overlap: 3);

            // Act & Assert
            Assert.ThrowsAsync<InvalidOperationException>(async () => await pipeline.ChunkAsync(DriftDocument).ToListAsync());
        }

        // The failure mode this guards is a caller slicing its source with an extent that runs past the end.
        // ArgumentOutOfRangeException would mean the bound check inside the layouts is gone and the drift escaped into the caller.
        [TestCase(FixedSize)]
        [TestCase(SentenceAware)]
        [TestCase(Recursive)]
        [TestCase(StructureAware)]
        public void Chunk_TokenizerDecodesLongerThanItsInput_ThrowsNamingTheTokenizerRatherThanOverrunningTheSource(string strategy)
        {
            // Arrange
            var pipeline = PipelineFor(strategy, Lengthening(), maxTokens: 8);

            // Act
            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await pipeline.ChunkAsync(DriftDocument).ToListAsync());

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(exception.Message, Does.Contain("tokenizer failed to round-trip"));
                Assert.That(exception.Message, Does.Contain("Decode(Encode(text))"));
            }
        }

        [TestCase(FixedSize)]
        [TestCase(SentenceAware)]
        [TestCase(Recursive)]
        public void Chunk_TokenizerDecodesLongerThanItsInputWithOverlap_StillThrows(string strategy)
        {
            // Arrange
            var pipeline = PipelineFor(strategy, Lengthening(), maxTokens: 8, overlap: 3);

            // Act & Assert
            Assert.ThrowsAsync<InvalidOperationException>(async () => await pipeline.ChunkAsync(DriftDocument).ToListAsync());
        }

        // Asserted per strategy rather than once, so the exception is pinned to the guard on both layouts instead of merely being an InvalidOperationException from somewhere.
        [TestCase(FixedSize)]
        [TestCase(SentenceAware)]
        [TestCase(Recursive)]
        [TestCase(StructureAware)]
        public void Chunk_TokenizerDecodesShorterThanItsInput_NamesTheTokenizerAndTheCheckToRun(string strategy)
        {
            // Arrange
            var pipeline = PipelineFor(strategy, Shortening(), maxTokens: 8);

            // Act
            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await pipeline.ChunkAsync(DriftDocument).ToListAsync());

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(exception.Message, Does.Contain("tokenizer failed to round-trip"));
                Assert.That(exception.Message, Does.Contain("Decode(Encode(text))"));
                Assert.That(exception.Message, Does.Contain($"{DriftDocument.Length}"), "the expected character count is diagnostic and must appear");
            }
        }

        [TestCase(FixedSize)]
        [TestCase(SentenceAware)]
        [TestCase(Recursive)]
        [TestCase(StructureAware)]
        public async Task Chunk_ConformingTokenizerOverALengthThatDividesTheBudget_DoesNotThrow(string strategy)
        {
            // Arrange
            var pipeline = PipelineFor(strategy, Conforming(), maxTokens: 8);

            // Act
            var chunks = await pipeline.ChunkAsync(ExactMultipleDocument).ToListAsync();

            // Assert
            Assert.That(chunks, Is.Not.Empty);
        }

        [TestCase(FixedSize)]
        [TestCase(SentenceAware)]
        [TestCase(Recursive)]
        [TestCase(StructureAware)]
        public async Task Chunk_ConformingTokenizerOverALengthThatDoesNotDivideTheBudget_DoesNotThrow(string strategy)
        {
            // Arrange
            var pipeline = PipelineFor(strategy, Conforming(), maxTokens: 8);

            // Act
            var chunks = await pipeline.ChunkAsync(NotMultipleDocument).ToListAsync();

            // Assert
            Assert.That(chunks, Is.Not.Empty);
        }

        [TestCase(FixedSize)]
        [TestCase(SentenceAware)]
        [TestCase(Recursive)]
        public async Task Chunk_ConformingTokenizerWithOverlap_DoesNotThrow(string strategy)
        {
            // Arrange
            var pipeline = PipelineFor(strategy, Conforming(), maxTokens: 8, overlap: 3);

            // Act
            var chunks = await pipeline.ChunkAsync(NotMultipleDocument).ToListAsync();

            // Assert
            Assert.That(chunks, Is.Not.Empty);
        }

        [TestCase(FixedSize, 8)]
        [TestCase(FixedSize, 20)]
        [TestCase(SentenceAware, 8)]
        [TestCase(SentenceAware, 20)]
        [TestCase(Recursive, 8)]
        [TestCase(Recursive, 20)]
        [TestCase(StructureAware, 8)]
        [TestCase(StructureAware, 20)]
        public async Task Chunk_ShippedEncodingOverPlainText_DoesNotThrow(string strategy, int maxTokens)
        {
            // Arrange
            var pipeline = PipelineFor(strategy, _tokenizer, maxTokens);

            // Act
            var chunks = await pipeline.ChunkAsync(DriftDocument).ToListAsync();

            // Assert
            Assert.That(chunks, Is.Not.Empty);
        }

        [TestCase(FixedSize)]
        [TestCase(SentenceAware)]
        [TestCase(Recursive)]
        public async Task Chunk_ShippedEncodingWithOverlap_DoesNotThrow(string strategy)
        {
            // Arrange
            var pipeline = PipelineFor(strategy, _tokenizer, maxTokens: 8, overlap: 3);

            // Act
            var chunks = await pipeline.ChunkAsync(DriftDocument).ToListAsync();

            // Assert
            Assert.That(chunks, Is.Not.Empty);
        }

        // The highest-risk false positive: multi-token characters make the character and token counts diverge furthest, which is exactly where a length-based post-condition could misfire.
        [TestCase(FixedSize, EmojiDocument)]
        [TestCase(FixedSize, ChineseDocument)]
        [TestCase(FixedSize, MixedScriptDocument)]
        [TestCase(SentenceAware, EmojiDocument)]
        [TestCase(SentenceAware, ChineseDocument)]
        [TestCase(SentenceAware, MixedScriptDocument)]
        [TestCase(Recursive, EmojiDocument)]
        [TestCase(Recursive, ChineseDocument)]
        [TestCase(Recursive, MixedScriptDocument)]
        [TestCase(StructureAware, EmojiDocument)]
        [TestCase(StructureAware, ChineseDocument)]
        [TestCase(StructureAware, MixedScriptDocument)]
        public async Task Chunk_ShippedEncodingOverNonLatinAndEmojiText_DoesNotThrow(string strategy, string document)
        {
            // Arrange
            var pipeline = PipelineFor(strategy, _tokenizer, maxTokens: 8);

            // Act
            var chunks = await pipeline.ChunkAsync(document).ToListAsync();

            // Assert
            Assert.That(chunks, Is.Not.Empty);
        }

        [TestCase(FixedSize)]
        [TestCase(SentenceAware)]
        [TestCase(Recursive)]
        [TestCase(StructureAware)]
        public async Task Chunk_EmptyDocument_DoesNotThrow(string strategy)
        {
            // Arrange
            var pipeline = PipelineFor(strategy, Conforming(), maxTokens: 8);

            // Act
            var chunks = await pipeline.ChunkAsync(string.Empty).ToListAsync();

            // Assert
            Assert.That(chunks, Is.Empty);
        }

        [TestCase(FixedSize)]
        [TestCase(SentenceAware)]
        [TestCase(Recursive)]
        [TestCase(StructureAware)]
        public async Task Chunk_SingleCharacterDocument_DoesNotThrow(string strategy)
        {
            // Arrange
            var pipeline = PipelineFor(strategy, Conforming(), maxTokens: 8);

            // Act
            var chunks = await pipeline.ChunkAsync("a").ToListAsync();

            // Assert
            Assert.That(chunks, Has.Count.EqualTo(1));
        }

        // Pins the guard's deliberate scope: it asserts length fidelity, not the full round-trip contract.
        // A normalisation that preserves length keeps every extent exact, so throwing here would break a pipeline whose output is entirely correct.
        [TestCase(FixedSize)]
        [TestCase(SentenceAware)]
        [TestCase(Recursive)]
        [TestCase(StructureAware)]
        public async Task Chunk_TokenizerNormalisesWithoutChangingLength_DoesNotThrow(string strategy)
        {
            // Arrange
            var pipeline = PipelineFor(strategy, Lowercasing(), maxTokens: 8);

            // Act
            var chunks = await pipeline.ChunkAsync(MixedCaseDocument).ToListAsync();

            // Assert
            Assert.That(chunks, Is.Not.Empty);
        }

        [Test]
        public async Task Chunk_TokenizerNormalisesWithoutChangingLength_KeepsChunksVerbatimSourceSlices()
        {
            // Arrange
            var pipeline = PipelineFor(FixedSize, Lowercasing(), maxTokens: 8);

            // Act
            var chunks = await pipeline.ChunkAsync(MixedCaseDocument).ToListAsync();

            // Assert
            Assert.That(chunks, Is.Not.Empty);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(chunks.Where(c => c.SliceOf(MixedCaseDocument) != c.Text), Is.Empty);
                Assert.That(string.Concat(chunks.Select(c => c.Text)), Is.EqualTo(MixedCaseDocument), "the normalisation must not reach the emitted text");
                Assert.That(chunks[^1].SourceStart + chunks[^1].SourceLength, Is.EqualTo(MixedCaseDocument.Length));
            }
        }

        [Test]
        public void ChunkAsync_ReaderWithTokenizerDecodingShorterThanItsInput_ThrowsDuringEnumeration()
        {
            // Arrange: the guard must protect the streamed layouts exactly as it protects the in-memory ones
            var chunker = new FixedSizeChunker(Shortening(), maxTokens: 8, overlap: 0);
            using var reader = new StringReader(DriftDocument);

            // Act & Assert
            Assert.ThrowsAsync<InvalidOperationException>(async () => await chunker.ChunkAsync(reader).ToListAsync());
        }
    }
}
