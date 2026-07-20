using FluentChunker.Tokenizers;

namespace FluentChunker.Tests
{
    public class SharpTokenizerAdapterTests
    {
        private SharpTokenizerAdapter _tokenizer;
        private const string InputText = "A pragmatic developer chooses the least complicated implementation that reliably solves the problem.";

        [SetUp]
        public void SetUp()
        {
            _tokenizer = new SharpTokenizerAdapter("o200k_base");
        }

        [Test]
        public void CountTokens_NonEmptyText_ReturnsPositiveCount()
        {
            // Act
            var count = _tokenizer.CountTokens(InputText);

            // Assert
            Assert.That(count, Is.GreaterThan(0));
        }

        [Test]
        public void CountTokens_EmptyText_ReturnsZero()
        {
            // Act
            var count = _tokenizer.CountTokens(string.Empty);

            // Assert
            Assert.That(count, Is.Zero);
        }

        [Test]
        public void Encode_NonEmptyText_ReturnsNonEmptyList()
        {
            // Act
            var tokens = _tokenizer.Encode(InputText);

            // Assert
            Assert.That(tokens, Is.Not.Empty);
        }

        [Test]
        public void Decode_EncodedText_ReturnsOriginalString()
        {
            // Arrange
            var tokens = _tokenizer.Encode(InputText);

            // Act
            var decoded = _tokenizer.Decode(tokens);

            // Assert
            Assert.That(decoded, Is.EqualTo(InputText));
        }
    }
}
