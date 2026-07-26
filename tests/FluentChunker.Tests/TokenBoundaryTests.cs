using FluentChunker.Chunkers;
using FluentChunker.Tokenizers;

namespace FluentChunker.Tests
{
    public class TokenBoundaryTests
    {
        // A four-byte scalar that cl100k_base splits across several tokens, so cuts inside it are illegal.
        private const string Emoji = "Hello 🥸 world";
        private MicrosoftMLTokenizerAdapter _tokenizer;

        [SetUp]
        public void SetUp()
        {
            _tokenizer = new MicrosoftMLTokenizerAdapter("cl100k_base");
        }

        [Test]
        public void IsSafeCut_IndexAtOrOutsideSequenceEnds_ReturnsTrue()
        {
            // Arrange
            var tokens = _tokenizer.Encode(Emoji);
            var boundary = new TokenBoundary(_tokenizer, tokens);

            // Act & Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(boundary.IsSafeCut(0), Is.True);
                Assert.That(boundary.IsSafeCut(tokens.Count), Is.True);
                Assert.That(boundary.IsSafeCut(-5), Is.True);
                Assert.That(boundary.IsSafeCut(tokens.Count + 5), Is.True);
            }
        }

        [Test]
        public void IsSafeCut_AsciiOnlyText_ReturnsTrueForEveryIndex()
        {
            // Arrange
            var tokens = _tokenizer.Encode("A pragmatic developer chooses the simplest thing that works.");
            var boundary = new TokenBoundary(_tokenizer, tokens);

            // Act
            var unsafeCuts = Enumerable.Range(0, tokens.Count + 1).Where(i => !boundary.IsSafeCut(i)).ToList();

            // Assert
            Assert.That(unsafeCuts, Is.Empty);
        }

        [Test]
        public void IsSafeCut_TextWithMultiTokenCharacter_ReportsAtLeastOneIllegalCut()
        {
            // Arrange
            var tokens = _tokenizer.Encode(Emoji);
            var boundary = new TokenBoundary(_tokenizer, tokens);

            // Act
            var unsafeCuts = Enumerable.Range(0, tokens.Count + 1).Where(i => !boundary.IsSafeCut(i)).ToList();

            // Assert
            Assert.That(unsafeCuts, Is.Not.Empty, "cl100k_base splits an emoji across tokens, so some cut must be illegal");
        }

        [TestCase("Hello 👨‍👩‍👧‍👦 family. 这是第一句话。Ünïcödé test.")]
        [TestCase("Emoji run: 😀😃😄😁😆😅🤣😂🙂🙃 and more 🎉🎊🎈 done.")]
        [TestCase("混合 text 日本語のテキスト العربية עברית ไทย देवनागरी mixed.")]
        [TestCase("Plain ASCII only, no surprises here at all.")]
        [TestCase("🇩🇪🇫🇷 flags are regional indicator pairs.")]
        [TestCase("a�b literal replacement char already in source � end.")]
        public void IsSafeCut_AnyScript_AgreesWithTokenizerCharacterOffsets(string text)
        {
            // Arrange - ground truth: a cut is illegal when a token's character span starts before the running maximum end of the tokens before it, which is what happens when tokens share a character.
            var encoded = Microsoft.ML.Tokenizers.TiktokenTokenizer
                .CreateForEncoding("cl100k_base")
                .EncodeToTokens(text, out _);
            var expected = new bool[encoded.Count + 1];
            expected[0] = true;
            expected[encoded.Count] = true;
            var maxEnd = 0;
            for (var i = 0; i < encoded.Count; i++)
            {
                var (start, length) = encoded[i].Offset.GetOffsetAndLength(text.Length);
                if (i > 0)
                    expected[i] = start >= maxEnd;
                maxEnd = Math.Max(maxEnd, start + length);
            }

            var boundary = new TokenBoundary(_tokenizer, _tokenizer.Encode(text));

            // Act
            var actual = Enumerable.Range(0, encoded.Count + 1).Select(boundary.IsSafeCut).ToArray();

            // Assert
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void SnapToSafeCut_CandidateAlreadyLegal_ReturnsCandidateUnchanged()
        {
            // Arrange
            var tokens = _tokenizer.Encode("one two three four five six seven eight");
            var boundary = new TokenBoundary(_tokenizer, tokens);

            // Act
            var result = boundary.SnapToSafeCut(candidate: 4, floor: 0);

            // Assert
            Assert.That(result, Is.EqualTo(4));
        }

        [Test]
        public void SnapToSafeCut_CandidateInsideMultiTokenCharacter_MovesBackwardToLegalCut()
        {
            // Arrange
            var tokens = _tokenizer.Encode(Emoji);
            var boundary = new TokenBoundary(_tokenizer, tokens);
            var illegal = Enumerable.Range(1, tokens.Count - 1).First(i => !boundary.IsSafeCut(i));

            // Act
            var result = boundary.SnapToSafeCut(candidate: illegal, floor: 0);

            // Assert
            Assert.That(result, Is.LessThan(illegal));
            Assert.That(boundary.IsSafeCut(result), Is.True);
        }

        [Test]
        public void SnapToSafeCut_NoLegalCutAboveFloor_MovesForwardPastTheIndivisibleCharacter()
        {
            // Arrange - floor sits immediately before the illegal cut, so nothing below the candidate is available.
            var tokens = _tokenizer.Encode(Emoji);
            var boundary = new TokenBoundary(_tokenizer, tokens);
            var illegal = Enumerable.Range(1, tokens.Count - 1).First(i => !boundary.IsSafeCut(i));

            // Act
            var result = boundary.SnapToSafeCut(candidate: illegal, floor: illegal - 1);

            // Assert
            Assert.That(result, Is.GreaterThan(illegal));
            Assert.That(boundary.IsSafeCut(result), Is.True);
        }

        [Test]
        public void SnapToSafeCut_AnyCandidateAndFloor_AlwaysReturnsLegalCutAboveFloor()
        {
            // Arrange
            var text = "Emoji run: 😀😃😄😁😆😅🤣😂🙂🙃 and more 🎉🎊🎈 done.";
            var tokens = _tokenizer.Encode(text);
            var boundary = new TokenBoundary(_tokenizer, tokens);

            // Act
            var violations = new List<string>();
            for (var floor = 0; floor < tokens.Count; floor++)
            {
                for (var candidate = floor + 1; candidate <= tokens.Count; candidate++)
                {
                    var result = boundary.SnapToSafeCut(candidate, floor);
                    if (result <= floor || result > tokens.Count || !boundary.IsSafeCut(result))
                        violations.Add($"floor={floor} candidate={candidate} result={result}");
                }
            }

            // Assert
            Assert.That(violations, Is.Empty);
        }
    }
}
