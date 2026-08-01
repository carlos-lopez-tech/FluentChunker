using System.Text;
using FluentChunker.Chunkers.Recursive;
using FluentChunker.Chunkers.SentenceAware;
using FluentChunker.Chunkers.StructureAware;
using FluentChunker.Contracts;
using FluentChunker.Tokenizers;

namespace FluentChunker.Tests
{
    // Every strategy sizes its units before it can cut the first chunk, and on a large document that sizing is the bulk of the wait.
    // These pin that the token is observed inside it rather than only once chunks start coming out.
    public class PrologueCancellationTests
    {
        private const int Units = 500;

        private static string ManySentences()
        {
            var builder = new StringBuilder();

            for (var i = 0; i < Units; i++)
            {
                builder.Append("The archive compared chunking strategies for retrieval quality across models. ");
            }

            return builder.ToString();
        }

        private static string ManyParagraphs()
        {
            var builder = new StringBuilder();

            for (var i = 0; i < Units; i++)
            {
                builder.Append("The archive compared chunking strategies for retrieval quality across models.\n\n");
            }

            return builder.ToString();
        }

        private static string ManyMarkdownBlocks()
        {
            var builder = new StringBuilder();

            for (var i = 0; i < Units; i++)
            {
                builder.Append($"## Section {i}\n\nThe archive compared chunking strategies for retrieval quality.\n\n");
            }

            return builder.ToString();
        }

        // One token per character, cancelling once it has been asked to size a few units.
        // Sizing is the only place a test can reach the middle of a prologue that is otherwise far too fast to interrupt by timing.
        private sealed class CancellingTokenizer(CancellationTokenSource source, int cancelAfterCalls) : ITokenizer
        {
            internal int CountCalls { get; private set; }

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
                    decoded.Append((char)token);
                }

                return decoded.ToString();
            }

            public int CountTokens(string text)
            {
                CountCalls++;

                if (CountCalls >= cancelAfterCalls)
                    source.Cancel();

                return text.Length;
            }
        }

        private static async Task<(Exception? Caught, int Chunks)> ConsumeAsync(IChunker chunker, string document, CancellationToken cancellationToken)
        {
            var chunks = 0;

            try
            {
                await foreach (var _ in chunker.ChunkAsync(document, cancellationToken))
                {
                    chunks++;
                }
            }
            catch (Exception exception)
            {
                return (exception, chunks);
            }

            return (null, chunks);
        }

        [Test]
        public async Task ChunkAsync_TokenCancelledWhileSizingSentences_ThrowsWithoutSizingTheRest()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            var tokenizer = new CancellingTokenizer(cts, cancelAfterCalls: 3);
            var chunker = new SentenceAwareChunker(tokenizer, maxTokens: 64, overlap: 0);

            // Act
            var (caught, chunks) = await ConsumeAsync(chunker, ManySentences(), cts.Token);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(caught, Is.InstanceOf<OperationCanceledException>());
                Assert.That(chunks, Is.Zero);
                Assert.That(tokenizer.CountCalls, Is.LessThan(Units), "sizing ran to completion instead of stopping where the token was cancelled");
            }
        }

        [Test]
        public async Task ChunkAsync_TokenCancelledWhileSizingParagraphs_ThrowsWithoutSizingTheRest()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            var tokenizer = new CancellingTokenizer(cts, cancelAfterCalls: 3);
            var chunker = new RecursiveChunker(tokenizer, maxTokens: 64, overlap: 0);

            // Act
            var (caught, chunks) = await ConsumeAsync(chunker, ManyParagraphs(), cts.Token);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(caught, Is.InstanceOf<OperationCanceledException>());
                Assert.That(chunks, Is.Zero);
                Assert.That(tokenizer.CountCalls, Is.LessThan(Units), "sizing ran to completion instead of stopping where the token was cancelled");
            }
        }

        [Test]
        public async Task ChunkAsync_TokenCancelledWhileSizingMarkdownBlocks_ThrowsWithoutSizingTheRest()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            var tokenizer = new CancellingTokenizer(cts, cancelAfterCalls: 3);
            var chunker = new StructureAwareChunker(tokenizer, maxTokens: 64);

            // Act
            var (caught, chunks) = await ConsumeAsync(chunker, ManyMarkdownBlocks(), cts.Token);

            // Assert - the Markdig parse ahead of this is not interruptible, so only the sizing that follows it can stop early
            using (Assert.EnterMultipleScope())
            {
                Assert.That(caught, Is.InstanceOf<OperationCanceledException>());
                Assert.That(chunks, Is.Zero);
                Assert.That(tokenizer.CountCalls, Is.LessThan(Units), "sizing ran to completion instead of stopping where the token was cancelled");
            }
        }
    }
}
