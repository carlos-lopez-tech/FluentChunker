using FluentChunker.Chunkers;
using FluentChunker.Tokenizers;

namespace FluentChunker.Tests
{
    public class TokenWindowsTests
    {
        private const string Ascii = "one two three four five six seven eight nine ten eleven twelve";
        private const string MultiByte = "Emoji run: 😀😃😄😁😆😅🤣😂🙂🙃 and more 🎉🎊🎈 done.";
        private MicrosoftMLTokenizerAdapter _tokenizer;

        [SetUp]
        public void SetUp()
        {
            _tokenizer = new MicrosoftMLTokenizerAdapter("cl100k_base");
        }

        [Test]
        public void Strided_AsciiWithoutOverlap_TilesTheSequenceExactly()
        {
            // Arrange
            var tokens = _tokenizer.Encode(Ascii);
            var boundary = new TokenBoundary(_tokenizer, tokens);

            // Act
            var windows = TokenWindows.Strided(tokens.Count, maxTokens: 5, overlap: 0, boundary).ToList();

            // Assert
            Assert.That(windows, Is.Not.Empty);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(windows[0].Start, Is.Zero);
                Assert.That(windows.Sum(w => w.Length), Is.EqualTo(tokens.Count));
                Assert.That(windows.All(w => w.Overlap == 0), Is.True);
                Assert.That(windows[^1].Start + windows[^1].Length, Is.EqualTo(tokens.Count));
            }
        }

        [Test]
        public void Strided_AsciiWithOverlap_ReportsTheTokensEachWindowShares()
        {
            // Arrange
            var tokens = _tokenizer.Encode(Ascii);
            var boundary = new TokenBoundary(_tokenizer, tokens);

            // Act
            var windows = TokenWindows.Strided(tokens.Count, maxTokens: 6, overlap: 2, boundary).ToList();

            // Assert
            Assert.That(windows, Has.Count.GreaterThan(1));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(windows[0].Overlap, Is.Zero);
                Assert.That(windows[1].Overlap, Is.EqualTo(2));
                Assert.That(windows[1].Start, Is.EqualTo(windows[0].Start + windows[0].Length - 2));
            }
        }

        [Test]
        public void Strided_MultiByteText_PlacesEveryBoundaryOnALegalCut()
        {
            // Arrange
            var tokens = _tokenizer.Encode(MultiByte);
            var boundary = new TokenBoundary(_tokenizer, tokens);

            // Act
            var windows = TokenWindows.Strided(tokens.Count, maxTokens: 5, overlap: 0, boundary).ToList();

            // Assert
            var illegal = windows.Where(w => !boundary.IsSafeCut(w.Start) || !boundary.IsSafeCut(w.Start + w.Length)).ToList();
            Assert.That(illegal, Is.Empty);
        }

        [Test]
        public void Strided_MultiByteTextWithoutOverlap_DecodedWindowsReconstructTheSource()
        {
            // Arrange
            var tokens = _tokenizer.Encode(MultiByte);
            var boundary = new TokenBoundary(_tokenizer, tokens);

            // Act
            var text = string.Concat(TokenWindows
                .Strided(tokens.Count, maxTokens: 5, overlap: 0, boundary)
                .Select(w => _tokenizer.Decode(TokenWindows.Slice(tokens, w.Start, w.Length))));

            // Assert
            Assert.That(text, Is.EqualTo(MultiByte));
        }

        [Test]
        public void Balanced_MultiByteText_PlacesEveryBoundaryOnALegalCut()
        {
            // Arrange
            var tokens = _tokenizer.Encode(MultiByte);
            var boundary = new TokenBoundary(_tokenizer, tokens);

            // Act
            var windows = TokenWindows.Balanced(tokens.Count, maxTokens: 6, boundary).ToList();

            // Assert
            var illegal = windows.Where(w => !boundary.IsSafeCut(w.Start) || !boundary.IsSafeCut(w.Start + w.Length)).ToList();
            Assert.That(illegal, Is.Empty);
        }

        [Test]
        public void Balanced_MultiByteText_DecodedWindowsReconstructTheSource()
        {
            // Arrange
            var tokens = _tokenizer.Encode(MultiByte);
            var boundary = new TokenBoundary(_tokenizer, tokens);

            // Act
            var text = string.Concat(TokenWindows
                .Balanced(tokens.Count, maxTokens: 6, boundary)
                .Select(w => _tokenizer.Decode(TokenWindows.Slice(tokens, w.Start, w.Length))));

            // Assert
            Assert.That(text, Is.EqualTo(MultiByte));
        }

        // Four rather than three, even though this fixture's widest emoji run is only three tokens.
        // Four is the universal threshold, since a single character can need up to four tokens, so a name promising three would encode a rule that holds for this fixture alone.
        // MultiByteChunkingTests pins the threshold itself against characters that really do need four.
        [TestCase(4)]
        [TestCase(6)]
        [TestCase(10)]
        public void Balanced_MultiByteTextAtAnyBudgetOfFourOrMore_KeepsEveryWindowWithinBudget(int maxTokens)
        {
            // Arrange
            var tokens = _tokenizer.Encode(MultiByte);
            var boundary = new TokenBoundary(_tokenizer, tokens);

            // Act
            var windows = TokenWindows.Balanced(tokens.Count, maxTokens, boundary).ToList();

            // Assert
            Assert.That(windows.Select(w => w.Length), Has.All.LessThanOrEqualTo(maxTokens));
        }

        [Test]
        public void Balanced_BudgetSmallerThanAnIndivisibleCharacter_EmitsThatCharacterWhole()
        {
            // Arrange - an emoji needs more tokens than this budget, and emitting it whole beats corrupting it.
            var tokens = _tokenizer.Encode(MultiByte);
            var boundary = new TokenBoundary(_tokenizer, tokens);

            // Act
            var windows = TokenWindows.Balanced(tokens.Count, maxTokens: 1, boundary).ToList();
            var text = string.Concat(windows.Select(w => _tokenizer.Decode(TokenWindows.Slice(tokens, w.Start, w.Length))));

            // Assert
            Assert.That(windows.Any(w => w.Length > 1), Is.True, "an indivisible character must overrun a one-token budget");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(text, Is.EqualTo(MultiByte));
                Assert.That(text, Does.Not.Contain("�"));
            }
        }

        // A window that snapped short leaves tokens for the rest of the sequence to carry.
        // Sizing the remaining windows against what is actually left, rather than against a split planned before any snapping happened, keeps the sequence inside the window count its budget allows.
        [Test]
        public void Balanced_SnappingShortensAWindow_DoesNotSpendAnExtraWindowOnTheTail()
        {
            // Arrange
            const int MaxTokens = 6;
            var tokens = _tokenizer.Encode(MultiByte);
            var boundary = new TokenBoundary(_tokenizer, tokens);
            var allowedWindows = (tokens.Count + MaxTokens - 1) / MaxTokens;

            // Act
            var windows = TokenWindows.Balanced(tokens.Count, MaxTokens, boundary).ToList();

            // Assert
            Assert.That(windows, Has.Count.EqualTo(allowedWindows));
        }

        // The tiny trailing window is what an even split exists to avoid, and a stale split reintroduces it by handing the earlier windows more than the snapped sequence can still fill.
        [Test]
        public void Balanced_SnappingShortensAWindow_DoesNotLeaveASingleTokenTail()
        {
            // Arrange
            var tokens = _tokenizer.Encode(MultiByte);
            var boundary = new TokenBoundary(_tokenizer, tokens);

            // Act
            var windows = TokenWindows.Balanced(tokens.Count, maxTokens: 5, boundary).ToList();

            // Assert
            Assert.That(windows[^1].Length, Is.GreaterThan(1));
        }

        [Test]
        public void Balanced_ZeroTokens_ReturnsNoWindows()
        {
            // Arrange
            var boundary = new TokenBoundary(_tokenizer, []);

            // Act
            var windows = TokenWindows.Balanced(totalTokens: 0, maxTokens: 10, boundary).ToList();

            // Assert
            Assert.That(windows, Is.Empty);
        }

        [Test]
        public void Strided_AsciiWithoutOverlap_CharacterExtentsTileTheSourceExactly()
        {
            // Arrange
            var tokens = _tokenizer.Encode(Ascii);
            var boundary = new TokenBoundary(_tokenizer, tokens);

            // Act
            var windows = TokenWindows.Strided(tokens.Count, maxTokens: 5, overlap: 0, boundary).ToList();

            // Assert
            Assert.That(windows, Is.Not.Empty);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(windows[0].CharStart, Is.Zero);
                Assert.That(windows.Sum(w => w.CharLength), Is.EqualTo(Ascii.Length));
                Assert.That(windows[^1].CharStart + windows[^1].CharLength, Is.EqualTo(Ascii.Length));
            }
        }

        [TestCase(4)]
        [TestCase(6)]
        [TestCase(10)]
        public void Strided_MultiByteTextWithOverlap_CharacterExtentsSliceTheSourceBackToTheWindow(int maxTokens)
        {
            // Arrange - the extent must select exactly the text the window decodes to, overlap included.
            var tokens = _tokenizer.Encode(MultiByte);
            var boundary = new TokenBoundary(_tokenizer, tokens);

            // Act
            var mismatched = TokenWindows
                .Strided(tokens.Count, maxTokens, overlap: 2, boundary)
                .Where(w => MultiByte.Substring(w.CharStart, w.CharLength)
                            != _tokenizer.Decode(TokenWindows.Slice(tokens, w.Start, w.Length)))
                .ToList();

            // Assert
            Assert.That(mismatched, Is.Empty);
        }

        [TestCase(3)]
        [TestCase(6)]
        [TestCase(10)]
        public void Balanced_MultiByteText_CharacterExtentsSliceTheSourceBackToTheWindow(int maxTokens)
        {
            // Arrange
            var tokens = _tokenizer.Encode(MultiByte);
            var boundary = new TokenBoundary(_tokenizer, tokens);

            // Act
            var mismatched = TokenWindows
                .Balanced(tokens.Count, maxTokens, boundary)
                .Where(w => MultiByte.Substring(w.CharStart, w.CharLength)
                            != _tokenizer.Decode(TokenWindows.Slice(tokens, w.Start, w.Length)))
                .ToList();

            // Assert
            Assert.That(mismatched, Is.Empty);
        }

        [Test]
        public void Balanced_MultiByteText_CharacterExtentsPartitionTheSource()
        {
            // Arrange - Balanced never overlaps, so its extents must butt up against each other with no gap.
            var tokens = _tokenizer.Encode(MultiByte);
            var boundary = new TokenBoundary(_tokenizer, tokens);

            // Act
            var windows = TokenWindows.Balanced(tokens.Count, maxTokens: 6, boundary).ToList();

            // Assert
            Assert.That(windows, Has.Count.GreaterThan(1));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(windows[0].CharStart, Is.Zero);
                Assert.That(windows.Sum(w => w.CharLength), Is.EqualTo(MultiByte.Length));
                Assert.That(windows.Zip(windows.Skip(1)).Where(p => p.First.CharStart + p.First.CharLength != p.Second.CharStart),
                    Is.Empty);
            }
        }
    }
}
