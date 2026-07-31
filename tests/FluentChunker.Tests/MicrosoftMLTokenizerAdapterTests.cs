using FluentChunker.Tokenizers;
using Microsoft.ML.Tokenizers;

namespace FluentChunker.Tests
{
    public class MicrosoftMLTokenizerAdapterTests
    {
        private MicrosoftMLTokenizerAdapter _tokenizer;
        private const string InputText = "A pragmatic developer chooses the least complicated implementation that reliably solves the problem.";

        [SetUp]
        public void SetUp()
        {
            _tokenizer = new MicrosoftMLTokenizerAdapter("cl100k_base");
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

        // Safe cut detection and source offset mapping both decode token runs and compare the result against the source, so a normalising tokenizer would break chunking and offsets rather than merely counting differently.
        // The shipped encoding is asserted to round-trip here so that assumption is stated in the suite instead of implied by the strategies that rest on it.
        [TestCase("A pragmatic developer chooses the least complicated implementation.", TestName = "Decode_TextInAnyScriptOrEmoji_ReturnsOriginalExactly(English)")]
        [TestCase("Der Zustand einer Software ist das Ergebnis vieler kleiner Entscheidungen.", TestName = "Decode_TextInAnyScriptOrEmoji_ReturnsOriginalExactly(German)")]
        [TestCase("Состояние программы складывается из множества мелких решений.", TestName = "Decode_TextInAnyScriptOrEmoji_ReturnsOriginalExactly(Russian)")]
        [TestCase("Η κατάσταση ενός προγράμματος προκύπτει από πολλές μικρές αποφάσεις.", TestName = "Decode_TextInAnyScriptOrEmoji_ReturnsOriginalExactly(Greek)")]
        [TestCase("程序的状态源于许多微小的决定。", TestName = "Decode_TextInAnyScriptOrEmoji_ReturnsOriginalExactly(Chinese)")]
        [TestCase("プログラムの状態は多くの小さな決定の結果です。", TestName = "Decode_TextInAnyScriptOrEmoji_ReturnsOriginalExactly(Japanese)")]
        [TestCase("프로그램의 상태는 수많은 작은 결정의 결과입니다.", TestName = "Decode_TextInAnyScriptOrEmoji_ReturnsOriginalExactly(Korean)")]
        [TestCase("किसी प्रोग्राम की स्थिति कई छोटे निर्णयों का परिणाम है।", TestName = "Decode_TextInAnyScriptOrEmoji_ReturnsOriginalExactly(Hindi)")]
        [TestCase("حالة البرنامج هي نتيجة العديد من القرارات الصغيرة.", TestName = "Decode_TextInAnyScriptOrEmoji_ReturnsOriginalExactly(Arabic)")]
        [TestCase("מצב התוכנית הוא תוצאה של החלטות קטנות רבות.", TestName = "Decode_TextInAnyScriptOrEmoji_ReturnsOriginalExactly(Hebrew)")]
        [TestCase("สถานะของโปรแกรมเป็นผลมาจากการตัดสินใจเล็ก ๆ หลายอย่าง", TestName = "Decode_TextInAnyScriptOrEmoji_ReturnsOriginalExactly(Thai)")]
        // The family emoji is a zero-width-joiner sequence, the case most likely to survive encoding but not decoding.
        [TestCase("Ship it 🚀 with tests ✅ and a family 👨‍👩‍👧‍👦 in the docs.", TestName = "Decode_TextInAnyScriptOrEmoji_ReturnsOriginalExactly(Emoji)")]
        [TestCase("Mixed: café naïve 中文 مرحبا done.", TestName = "Decode_TextInAnyScriptOrEmoji_ReturnsOriginalExactly(Mixed)")]
        public void Decode_TextInAnyScriptOrEmoji_ReturnsOriginalExactly(string text)
        {
            // Arrange
            var tokens = _tokenizer.Encode(text);

            // Act
            var decoded = _tokenizer.Decode(tokens);

            // Assert
            Assert.That(decoded, Is.EqualTo(text));
        }

        [Test]
        public void Constructor_TokenizerInstance_BehavesIdenticallyToEncodingName()
        {
            // Arrange
            var adapter = new MicrosoftMLTokenizerAdapter(TiktokenTokenizer.CreateForEncoding("cl100k_base"));

            // Act
            var tokens = adapter.Encode(InputText);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(tokens, Is.EqualTo(_tokenizer.Encode(InputText)));
                Assert.That(adapter.CountTokens(InputText), Is.EqualTo(_tokenizer.CountTokens(InputText)));
                Assert.That(adapter.Decode(tokens), Is.EqualTo(InputText));
            }
        }

        [Test]
        public void Constructor_NullTokenizer_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.That(() => new MicrosoftMLTokenizerAdapter((Tokenizer)null!), Throws.ArgumentNullException);
        }
    }
}
