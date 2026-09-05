using System.Text;
using FluentChunker.Contracts;
using FluentChunker.Tokenizers;

namespace FluentChunker.Tests
{
    // Public because it appears in the signature of a public test method, which is how [TestCase] names each strategy at the call site.
    public enum SweepStrategy
    {
        FixedSize,
        SentenceAware,
        Recursive,
        StructureAware,
    }

    /// <summary>
    /// Asserts the per-chunk invariants over a 1.2 million character document, where budget arithmetic and offset tracking have room to drift apart.
    /// </summary>
    /// <remarks>
    /// The rest of the suite proves these same properties on inputs small enough to reason about by hand, which is what makes a failure there diagnosable.
    /// This fixture exists for the failures that only appear at length: a rounding error that costs one character per thousand chunks is invisible on a paragraph.
    /// It is categorised so the fast inner loop can exclude it with --filter TestCategory!=Sweep.
    /// </remarks>
    [TestFixture]
    [Category("Sweep")]
    public class CorpusInvariantSweepTests
    {
        private ITokenizer _tokenizer = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp() => _tokenizer = new MicrosoftMLTokenizerAdapter("cl100k_base");

        [TestCase(SweepStrategy.FixedSize, 128, 0)]
        [TestCase(SweepStrategy.FixedSize, 128, 64)]
        [TestCase(SweepStrategy.FixedSize, 512, 0)]
        [TestCase(SweepStrategy.FixedSize, 512, 64)]
        [TestCase(SweepStrategy.SentenceAware, 128, 0)]
        [TestCase(SweepStrategy.SentenceAware, 128, 64)]
        [TestCase(SweepStrategy.SentenceAware, 512, 0)]
        [TestCase(SweepStrategy.SentenceAware, 512, 64)]
        [TestCase(SweepStrategy.Recursive, 128, 0)]
        [TestCase(SweepStrategy.Recursive, 128, 64)]
        [TestCase(SweepStrategy.Recursive, 512, 0)]
        [TestCase(SweepStrategy.Recursive, 512, 64)]
        // Structure-aware applies no overlap by design, so a second overlap row would repeat the first.
        [TestCase(SweepStrategy.StructureAware, 128, 0)]
        [TestCase(SweepStrategy.StructureAware, 512, 0)]
        public async Task ChunkAsync_FullCorpus_HoldsEveryChunkInvariant(SweepStrategy strategy, int budget, int overlap)
        {
            // Arrange
            var document = SweepCorpus.Prose;
            var pipeline = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(_tokenizer)
                .UseSplitter(Configure(strategy, budget, overlap))
                .Build();

            // Act
            var chunks = await pipeline.ChunkAsync(document).ToListAsync();

            // Assert
            Assert.That(chunks, Is.Not.Empty);

            var overBudget = new InvariantViolations("no chunk exceeds its budget");
            var undercounted = new InvariantViolations("TokenCount bounds the chunk text");
            var notVerbatim = new InvariantViolations("every chunk slices back verbatim");

            for (var i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];

                // Re-measure rather than trust the reported count, because the claim under test is that the text itself fits the budget.
                var measured = _tokenizer.CountTokens(chunk.Text);

                if (measured > budget)
                    overBudget.Add($"#{i} measured {measured} > {budget}");

                // An upper bound rather than equality: BPE merges differently at a chunk's edges than mid-document, so a chunk sliced out of the document encoding can cost one token less on its own.
                // The bound is the safe direction, and a reported count below the real one would mean a chunk that overruns the budget of whatever consumes it.
                if (chunk.TokenCount < measured)
                    undercounted.Add($"#{i} reported {chunk.TokenCount} < measured {measured}");

                if (chunk.SliceOf(document) != chunk.Text)
                    notVerbatim.Add($"#{i} at {chunk.SourceStart}+{chunk.SourceLength}");
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(overBudget.Count, Is.Zero, overBudget.Message);
                Assert.That(undercounted.Count, Is.Zero, undercounted.Message);
                Assert.That(notVerbatim.Count, Is.Zero, notVerbatim.Message);
                Assert.That(NonWhitespaceOf(Rebuild(chunks, document)), Is.EqualTo(NonWhitespaceOf(document)));
            }
        }

        private static Action<Splitter.Builder> Configure(SweepStrategy strategy, int budget, int overlap) => strategy switch
        {
            SweepStrategy.FixedSize => s => s.WithFixedSizeChunking().WithMaxTokenSize(budget).WithOverlap(overlap),
            SweepStrategy.SentenceAware => s => s.WithSentenceAwareChunking().WithMaxTokenSize(budget).WithOverlap(overlap),
            SweepStrategy.Recursive => s => s.WithRecursiveChunking().WithMaxTokenSize(budget).WithOverlap(overlap),
            SweepStrategy.StructureAware => s => s.WithStructureAwareChunking().WithMaxTokenSize(budget),
            _ => throw new ArgumentOutOfRangeException(nameof(strategy)),
        };

        // Walks the chunks by their offsets and appends only the part of each that was not already covered, which is what makes the result comparable to the document under overlap.
        private static string Rebuild(IReadOnlyList<Chunk> chunks, string document)
        {
            var rebuilt = new StringBuilder(document.Length);
            var covered = 0L;

            foreach (var chunk in chunks)
            {
                if (chunk.SourceStart >= covered)
                {
                    rebuilt.Append(document, (int)chunk.SourceStart, chunk.SourceLength);
                    covered = chunk.SourceStart + chunk.SourceLength;
                    continue;
                }

                var fresh = chunk.SourceStart + chunk.SourceLength - covered;

                if (fresh <= 0)
                    continue;

                rebuilt.Append(document, (int)covered, (int)fresh);
                covered += fresh;
            }

            return rebuilt.ToString();
        }

        // The packing strategies deliberately drop the whitespace between structural units, so coverage is a claim about content rather than about every character.
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
