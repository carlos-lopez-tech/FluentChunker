using System.Text;
using FluentChunker.Chunkers;
using FluentChunker.Chunkers.FixedSize;
using FluentChunker.Tokenizers;

namespace FluentChunker.Tests
{
    public class DocumentEncodingTests
    {
        // Batching only matters past the internal batch size, so every document here is built long enough to need several batches.
        private const string LatinParagraph =
            "The archive group compared chunking strategies for retrieval quality across embedding models. " +
            "Several researchers measured how overlapping windows preserved context between adjacent passages. ";

        private const string MixedScriptParagraph =
            "An English sentence opens the document. 这是第一句中文。这是第二句中文。" +
            "هذا سؤال؟ نعم هذا صحيح. यह एक हिंदी वाक्य है। Ελληνικά και ру́сский текст. " +
            "Emoji land here 😀🎉 and a tab\tplus  double  spaces. ";

        private MicrosoftMLTokenizerAdapter _tokenizer;

        [SetUp]
        public void SetUp()
        {
            _tokenizer = new MicrosoftMLTokenizerAdapter("cl100k_base");
        }

        private static string Repeat(string paragraph, int approxChars)
        {
            var builder = new StringBuilder(approxChars + paragraph.Length);

            while (builder.Length < approxChars)
            {
                builder.Append(paragraph);
            }

            return builder.ToString();
        }

        // A document that fits in a single batch exercises none of the batching, so length is derived from the batch size rather than hard-coded.
        // The paragraph's own token density converts the token target into a character count, which keeps these documents as small as the property under test allows for both dense and sparse scripts.
        private static string RepeatForBatches(string paragraph, ITokenizer tokenizer, int batches)
        {
            var tokensPerChar = (double)tokenizer.CountTokens(paragraph) / paragraph.Length;

            return Repeat(paragraph, (int)(DocumentEncoding.BatchTokens * batches / tokensPerChar) + paragraph.Length);
        }

        // One token per character, with no batching capability, standing in for a custom ITokenizer from outside this library.
        private class CharTokenizer : ITokenizer
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
                    decoded.Append((char)token);
                }

                return decoded.ToString();
            }

            public int CountTokens(string text) => text.Length;
        }

        // Adds the batching capability the shipped adapter gets from Microsoft.ML.Tokenizers, so the loop can be driven without depending on a real tokenizer's batch boundaries.
        private sealed class BatchingCharTokenizer : CharTokenizer, IBatchEncodingTokenizer
        {
            internal int BatchCalls { get; private set; }

            // Runs inside a batch, which is the only place a test can act while the encode is still in flight.
            internal Action? DuringBatch { get; set; }

            // Reports no progress, the pathological case the loop has to survive without spinning.
            internal bool Stall { get; set; }

            IReadOnlyList<int> IBatchEncodingTokenizer.EncodeUpTo(ReadOnlySpan<char> text, int maxTokens, out int charsConsumed)
            {
                BatchCalls++;
                DuringBatch?.Invoke();

                if (Stall)
                {
                    charsConsumed = 0;
                    return [];
                }

                charsConsumed = Math.Min(maxTokens, text.Length);

                var tokens = new int[charsConsumed];
                for (var i = 0; i < charsConsumed; i++)
                {
                    tokens[i] = text[i];
                }

                return tokens;
            }
        }

        [Test]
        public void Encode_BatchingTokenizerOverLatinText_ProducesSameTokensAsWholeDocumentEncode()
        {
            // Arrange
            var document = RepeatForBatches(LatinParagraph, _tokenizer, batches: 3);

            // Act
            var batched = DocumentEncoding.Encode(_tokenizer, document, CancellationToken.None);

            // Assert - batching must never move a token, because every chunk boundary is derived from these positions
            Assert.That(batched, Is.EqualTo(_tokenizer.Encode(document)).AsCollection);
        }

        [Test]
        public void Encode_BatchingTokenizerOverMixedScripts_ProducesSameTokensAsWholeDocumentEncode()
        {
            // Arrange
            var document = RepeatForBatches(MixedScriptParagraph, _tokenizer, batches: 3);

            // Act
            var batched = DocumentEncoding.Encode(_tokenizer, document, CancellationToken.None);

            // Assert - CJK, Arabic, Devanagari, Greek, Cyrillic and emoji all span multiple tokens per character, which is where a naive split would drift
            Assert.That(batched, Is.EqualTo(_tokenizer.Encode(document)).AsCollection);
        }

        [Test]
        public void Encode_NonBatchingTokenizer_FallsBackToWholeDocumentEncode()
        {
            // Arrange
            var tokenizer = new CharTokenizer();
            var document = RepeatForBatches(LatinParagraph, tokenizer, batches: 3);

            // Act
            var encoded = DocumentEncoding.Encode(tokenizer, document, CancellationToken.None);

            // Assert - a custom tokenizer keeps exactly the behaviour it had before batching existed
            Assert.That(encoded, Is.EqualTo(tokenizer.Encode(document)).AsCollection);
        }

        [Test]
        public void Encode_TokenCancelledInsideFirstBatch_ThrowsBeforeEncodingCompletes()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            var tokenizer = new BatchingCharTokenizer { DuringBatch = cts.Cancel };
            var document = RepeatForBatches(LatinParagraph, tokenizer, batches: 3);

            // Act & Assert - the point of batching: cancellation lands inside the encode instead of waiting for all of it
            Assert.Throws<OperationCanceledException>(() => DocumentEncoding.Encode(tokenizer, document, cts.Token));
            Assert.That(tokenizer.BatchCalls, Is.EqualTo(1));
        }

        [Test]
        public void Encode_BatchThatConsumesNothing_EncodesTheRemainderInsteadOfSpinning()
        {
            // Arrange
            var tokenizer = new BatchingCharTokenizer { Stall = true };
            var document = RepeatForBatches(LatinParagraph, tokenizer, batches: 3);

            // Act
            var encoded = DocumentEncoding.Encode(tokenizer, document, CancellationToken.None);

            // Assert - a tokenizer that cannot advance must still yield the whole document rather than a dropped tail
            Assert.That(encoded, Is.EqualTo(tokenizer.Encode(document)).AsCollection);
        }

        [Test]
        public void ChunkAsync_TokenCancelledDuringEncoding_ThrowsBeforeFirstChunk()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            var tokenizer = new BatchingCharTokenizer { DuringBatch = cts.Cancel };
            var chunker = new FixedSizeChunker(tokenizer, maxTokens: 64, overlap: 0);
            var document = RepeatForBatches(LatinParagraph, tokenizer, batches: 3);
            var consumed = new List<Contracts.Chunk>();

            // Act
            async Task Consume()
            {
                await foreach (var chunk in chunker.ChunkAsync(document, cts.Token))
                {
                    consumed.Add(chunk);
                }
            }

            // Assert - the whole point is that this no longer waits for the document to be tokenized first
            Assert.ThrowsAsync<OperationCanceledException>(Consume);
            Assert.That(consumed, Is.Empty);
        }

        [Test]
        public async Task ChunkAsync_BatchingTokenizer_ProducesTheSameChunksAsBefore()
        {
            // Arrange
            var document = RepeatForBatches(LatinParagraph, _tokenizer, batches: 3);
            var chunker = new FixedSizeChunker(_tokenizer, maxTokens: 512, overlap: 50);
            var expected = _tokenizer.Encode(document);

            // Act
            var result = await chunker.ChunkAsync(document).ToListAsync();

            // Assert
            Assert.That(result, Is.Not.Empty);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result[0].SourceStart, Is.Zero);
                Assert.That(result[^1].SourceStart + result[^1].SourceLength, Is.EqualTo(document.Length));
                Assert.That(result.All(c => c.TokenCount <= 512), Is.True);
                Assert.That(result.All(c => c.SliceOf(document) == c.Text), Is.True);
                Assert.That(DocumentEncoding.Encode(_tokenizer, document, CancellationToken.None), Is.EqualTo(expected).AsCollection);
            }
        }
    }
}
