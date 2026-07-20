using FluentChunker.Chunkers.SentenceAware;

namespace FluentChunker.Tests
{
    public class SentenceSplitterTests
    {
        private static string Slice(string text, SentenceSpan span)
            => text.Substring(span.Start, span.Length);

        [Test]
        public void Split_SimpleSentences_SplitsOnTerminators()
        {
            // Arrange
            var text = "First sentence. Second one! Third here?";

            // Act
            var result = SentenceSplitter.Split(text);

            using (Assert.EnterMultipleScope())
            {
                // Assert
                Assert.That(result, Has.Count.EqualTo(3));
                Assert.That(Slice(text, result[0]), Is.EqualTo("First sentence."));
                Assert.That(Slice(text, result[1]), Is.EqualTo("Second one!"));
                Assert.That(Slice(text, result[2]), Is.EqualTo("Third here?"));
            }
        }

        [Test]
        public void Split_NoTerminator_ReturnsSingleSpan()
        {
            // Arrange
            var text = "no terminator at all here";

            // Act
            var result = SentenceSplitter.Split(text);

            using (Assert.EnterMultipleScope())
            {
                // Assert
                Assert.That(result, Has.Count.EqualTo(1));
                Assert.That(Slice(text, result[0]), Is.EqualTo(text));
            }
        }

        [TestCase("")]
        [TestCase("   ")]
        public void Split_EmptyOrWhitespaceText_ReturnsEmptyList(string text)
        {
            // Arrange & Act
            var result = SentenceSplitter.Split(text);

            // Assert
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void Split_LowercaseAfterTerminator_DoesNotSplit()
        {
            // Arrange
            var text = "He did it. and then left.";

            // Act
            var result = SentenceSplitter.Split(text);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result, Has.Count.EqualTo(1));
                Assert.That(Slice(text, result[0]), Is.EqualTo("He did it. and then left."));
            }
        }

        [Test]
        public void Split_ParagraphBreakBetweenSentences_SpansSliceBackToOriginalText()
        {
            // Arrange
            var text = "One is here.\n\nTwo follows.";

            // Act
            var result = SentenceSplitter.Split(text);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result, Has.Count.EqualTo(2));
                Assert.That(Slice(text, result[0]), Is.EqualTo("One is here."));
                Assert.That(Slice(text, result[1]), Is.EqualTo("Two follows."));
            }
        }

        [Test]
        public void Split_DigitAfterTerminator_SplitsSentence()
        {
            // Arrange
            // Next char after the terminator is a digit (not uppercase), so the split
            // can only happen via the char.IsDigit branch of IsSentenceBoundary.
            var text = "Done here. 2 more to go.";

            // Act
            var result = SentenceSplitter.Split(text);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result, Has.Count.EqualTo(2));
                Assert.That(Slice(text, result[0]), Is.EqualTo("Done here."));
                Assert.That(Slice(text, result[1]), Is.EqualTo("2 more to go."));
            }
        }

        [Test]
        public void Split_TrailingWhitespaceAfterLastTerminator_ExcludesItFromSpan()
        {
            // Arrange
            var text = "Hello.   ";

            // Act
            var result = SentenceSplitter.Split(text);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result, Has.Count.EqualTo(1));
                Assert.That(Slice(text, result[0]), Is.EqualTo("Hello."));
            }
        }

        [Test]
        public void Split_KnownAbbreviation_DoesNotSplit()
        {
            // Arrange
            var text = "Dr. Smith arrived. He sat down.";

            // Act
            var result = SentenceSplitter.Split(text);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result, Has.Count.EqualTo(2));
                Assert.That(Slice(text, result[0]), Is.EqualTo("Dr. Smith arrived."));
                Assert.That(Slice(text, result[1]), Is.EqualTo("He sat down."));
            }
        }

        [Test]
        public void Split_GermanAbbreviation_DoesNotSplit()
        {
            // Arrange
            var text = "Das steht in Nr. 5 im Anhang. Danach kommt mehr.";

            // Act
            var result = SentenceSplitter.Split(text);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result, Has.Count.EqualTo(2));
                Assert.That(Slice(text, result[0]), Is.EqualTo("Das steht in Nr. 5 im Anhang."));
                Assert.That(Slice(text, result[1]), Is.EqualTo("Danach kommt mehr."));
            }
        }

        [Test]
        public void Split_MultiDotAbbreviation_DoesNotSplit()
        {
            // Arrange
            var text = "Use tools, e.g. This hammer works. It really does.";

            // Act
            var result = SentenceSplitter.Split(text);

            using (Assert.EnterMultipleScope())
            {
                // Assert
                Assert.That(result, Has.Count.EqualTo(2));
                Assert.That(Slice(text, result[0]), Is.EqualTo("Use tools, e.g. This hammer works."));
                Assert.That(Slice(text, result[1]), Is.EqualTo("It really does."));
            }
        }

        [Test]
        public void Split_DecimalNumber_DoesNotSplit()
        {
            // Arrange
            var text = "The value is 3.5 today and rising.";

            // Act
            var result = SentenceSplitter.Split(text);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result, Has.Count.EqualTo(1));
                Assert.That(Slice(text, result[0]), Is.EqualTo(text));
            }
        }

        [Test]
        public void Split_CustomAbbreviation_DoesNotSplit()
        {
            // Arrange
            var text = "Vgl. Abschnitt 3 im Buch. Weiter geht es.";
            var additional = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Vgl" };

            // Act
            var result = SentenceSplitter.Split(text, additional);

            using (Assert.EnterMultipleScope())
            {
                // Assert
                Assert.That(result, Has.Count.EqualTo(2));
                Assert.That(Slice(text, result[0]), Is.EqualTo("Vgl. Abschnitt 3 im Buch."));
            }
        }

        [Test]
        public void Split_UnknownAbbreviationWithoutCustomSet_SplitsFalsely()
        {
            // Arrange — documents the behavior Split_CustomAbbreviation_DoesNotSplit fixes
            var text = "Vgl. Abschnitt 3 im Buch. Weiter geht es.";

            // Act
            var result = SentenceSplitter.Split(text);

            // Assert
            Assert.That(result, Has.Count.EqualTo(3));
        }

        [Test]
        public void Split_LowercaseAbbreviation_DoesNotSplit()
        {
            // Arrange
            var text = "dr. Smith left early. He waved.";

            // Act
            var result = SentenceSplitter.Split(text);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result, Has.Count.EqualTo(2));
                Assert.That(Slice(text, result[0]), Is.EqualTo("dr. Smith left early."));
                Assert.That(Slice(text, result[1]), Is.EqualTo("He waved."));
            }
        }

        [Test]
        public void Split_AbbreviationLikeWordBeforeExclamation_StillSplits()
        {
            // Arrange
            var text = "Wait, Dr! Now go.";

            // Act
            var result = SentenceSplitter.Split(text);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result, Has.Count.EqualTo(2));
                Assert.That(Slice(text, result[0]), Is.EqualTo("Wait, Dr!"));
                Assert.That(Slice(text, result[1]), Is.EqualTo("Now go."));
            }
        }

        [Test]
        public void Split_QuoteAfterTerminator_SplitsAfterQuote()
        {
            // Arrange
            var text = "He said \"Stop.\" Then he left.";

            // Act
            var result = SentenceSplitter.Split(text);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result, Has.Count.EqualTo(2));
                Assert.That(Slice(text, result[0]), Is.EqualTo("He said \"Stop.\""));
                Assert.That(Slice(text, result[1]), Is.EqualTo("Then he left."));
            }
        }

        [Test]
        public void Split_SpanishInvertedPunctuation_SplitsBeforeOpener()
        {
            // Arrange
            var text = "Hola. ¿Qué tal?";

            // Act
            var result = SentenceSplitter.Split(text);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result, Has.Count.EqualTo(2));
                Assert.That(Slice(text, result[0]), Is.EqualTo("Hola."));
                Assert.That(Slice(text, result[1]), Is.EqualTo("¿Qué tal?"));
            }
        }

        [Test]
        public void Split_AccentedUppercaseAfterTerminator_Splits()
        {
            // Arrange
            var text = "C'est fini. Épatant!";

            // Act
            var result = SentenceSplitter.Split(text);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result, Has.Count.EqualTo(2));
                Assert.That(Slice(text, result[0]), Is.EqualTo("C'est fini."));
                Assert.That(Slice(text, result[1]), Is.EqualTo("Épatant!"));
            }
        }

        [Test]
        public void Split_MultipleClosersAfterTerminator_SplitsAfterAll()
        {
            // Arrange
            var text = "He said (\"Stop.\") Then he left.";

            // Act
            var result = SentenceSplitter.Split(text);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result, Has.Count.EqualTo(2));
                Assert.That(Slice(text, result[0]), Is.EqualTo("He said (\"Stop.\")"));
                Assert.That(Slice(text, result[1]), Is.EqualTo("Then he left."));
            }
        }

        [Test]
        public void Split_BracketOpenerAfterTerminator_SplitsBeforeBracket()
        {
            // Arrange
            var text = "Done here. [Note] Then continues.";

            // Act
            var result = SentenceSplitter.Split(text);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result, Has.Count.EqualTo(2));
                Assert.That(Slice(text, result[0]), Is.EqualTo("Done here."));
                Assert.That(Slice(text, result[1]), Is.EqualTo("[Note] Then continues."));
            }
        }

        [Test]
        public void Split_AbbreviationInsideParentheses_DoesNotSplit()
        {
            // Arrange
            var text = "He met (Dr. Smith) yesterday. She left.";

            // Act
            var result = SentenceSplitter.Split(text);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result, Has.Count.EqualTo(2));
                Assert.That(Slice(text, result[0]), Is.EqualTo("He met (Dr. Smith) yesterday."));
                Assert.That(Slice(text, result[1]), Is.EqualTo("She left."));
            }
        }
    }
}
