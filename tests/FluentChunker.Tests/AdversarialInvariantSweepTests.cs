using System.Text;
using FluentChunker.Tokenizers;

namespace FluentChunker.Tests
{
    /// <summary>
    /// Asserts the per-chunk invariants over the inputs prose cannot reach: text with no whitespace to split on, scripts whose characters span several tokens, and emoji built from surrogate pairs and zero-width joiners.
    /// </summary>
    /// <remarks>
    /// English prose exercises the happy path every strategy was written for, so it cannot separate a chunker that respects character boundaries from one that got lucky.
    /// </remarks>
    [TestFixture]
    [Category("Sweep")]
    public class AdversarialInvariantSweepTests
    {
        private const int Budget = 64;

        private ITokenizer _tokenizer = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp() => _tokenizer = new MicrosoftMLTokenizerAdapter("cl100k_base");

        // The repeat counts differ per pattern so every case lands in the same few-thousand-character range despite the patterns having very different lengths.
        [TestCase("no whitespace at all", "abcdefghij", 500)]
        [TestCase("CJK, no spaces", "这是一个测试文本", 400)]
        [TestCase("emoji, surrogate pairs and joiners", "🙂🎉👨‍👩‍👧‍👦", 300)]
        [TestCase("mixed scripts", "hello مرحبا שלום नमस्ते สวัสดี ", 200)]
        public async Task ChunkAsync_AdversarialInput_HoldsEveryChunkInvariant(string scenario, string pattern, int repeats)
        {
            // Arrange
            var text = string.Concat(Enumerable.Repeat(pattern, repeats));
            var pipeline = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_tokenizer)
                .UseSplitter(s => s.WithRecursiveChunking().WithMaxTokenSize(Budget))
                .Build();

            // Act
            var chunks = await pipeline.ChunkAsync(text).ToListAsync();

            // Assert
            Assert.That(chunks, Is.Not.Empty, scenario);

            var overBudget = new InvariantViolations($"{scenario}: no chunk exceeds its budget");
            var splitCharacters = new InvariantViolations($"{scenario}: no chunk splits a character");

            for (var i = 0; i < chunks.Count; i++)
            {
                var measured = _tokenizer.CountTokens(chunks[i].Text);

                if (measured > Budget)
                    overBudget.Add($"#{i} measured {measured} > {Budget}");

                var lone = CountLoneSurrogates(chunks[i].Text);

                if (lone > 0)
                    splitCharacters.Add($"#{i} carries {lone} lone surrogates");
            }

            var rebuilt = string.Concat(chunks.Select(c => c.SliceOf(text)));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(overBudget.Count, Is.Zero, overBudget.Message);
                Assert.That(splitCharacters.Count, Is.Zero, splitCharacters.Message);

                // Compared on non-whitespace content because the packing strategies deliberately drop whitespace between structural units.
                Assert.That(NonWhitespaceOf(rebuilt), Is.EqualTo(NonWhitespaceOf(text)), scenario);
            }
        }

        // Counted directly rather than by looking for U+FFFD, which only appears once something has already encoded the broken text and so cannot detect a split pair in the chunk itself.
        private static int CountLoneSurrogates(string text)
        {
            var lone = 0;

            for (var i = 0; i < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i]))
                {
                    // A high surrogate is only whole when its low partner follows it in the same chunk.
                    if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    {
                        i++;
                        continue;
                    }

                    lone++;
                }
                else if (char.IsLowSurrogate(text[i]))
                {
                    // Reached without consuming a high surrogate first, so this half was cut from its pair.
                    lone++;
                }
            }

            return lone;
        }

        private static string NonWhitespaceOf(string text)
        {
            var builder = new StringBuilder(text.Length);

            foreach (var c in text)
            {
                if (!char.IsWhiteSpace(c))
                    builder.Append(c);
            }

            return builder.ToString();
        }
    }
}
