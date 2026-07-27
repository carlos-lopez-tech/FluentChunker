using System.Text.Json;
using FluentChunker.Contracts;

namespace FluentChunker.Tests
{
    public class ChunkMetadataTests
    {
        [Test]
        public void Empty_NoEntriesConfigured_IsASharedInstanceWithNoEntries()
        {
            // Arrange & Act
            var first = ChunkMetadata.Empty;
            var second = ChunkMetadata.Empty;

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(first, Is.SameAs(second));
                Assert.That(first, Is.Empty);
            }
        }

        [Test]
        public void With_NewKey_AddsAnEntryWithoutModifyingTheOriginal()
        {
            // Arrange
            var original = ChunkMetadata.Empty.With("Source", "doc.txt");

            // Act
            var extended = original.With("Page", 4);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(original, Has.Count.EqualTo(1));
                Assert.That(extended, Has.Count.EqualTo(2));
                Assert.That(extended["Source"].GetString(), Is.EqualTo("doc.txt"));
                Assert.That(extended["Page"].GetInt32(), Is.EqualTo(4));
            }
        }

        [Test]
        public void With_SameKeyTwice_KeepsTheLastValue()
        {
            // Arrange & Act
            var metadata = ChunkMetadata.Empty.With("Version", 1).With("Version", 2);

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(metadata, Has.Count.EqualTo(1));
                Assert.That(metadata["Version"].GetInt32(), Is.EqualTo(2));
            }
        }

        [Test]
        public void With_NullKey_ThrowsArgumentNullException()
        {
            // Arrange & Act & Assert
            Assert.That(() => ChunkMetadata.Empty.With(null!, "value"), Throws.ArgumentNullException);
        }

        [Test]
        public void Equals_SeparatelyBuiltInstancesWithIdenticalEntries_AreEqual()
        {
            // Arrange
            var a = ChunkMetadata.Empty.With("Source", "doc.txt").With("Page", 4);
            var b = ChunkMetadata.Empty.With("Page", 4).With("Source", "doc.txt");

            // Act & Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(a, Is.EqualTo(b));
                Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            }
        }

        [Test]
        public void Equals_DifferingValue_AreNotEqual()
        {
            // Arrange
            var a = ChunkMetadata.Empty.With("Source", "a.txt");
            var b = ChunkMetadata.Empty.With("Source", "b.txt");

            // Act & Assert
            Assert.That(a, Is.Not.EqualTo(b));
        }

        [Test]
        public void Equals_DifferingKey_AreNotEqual()
        {
            // Arrange
            var a = ChunkMetadata.Empty.With("Source", "doc.txt");
            var b = ChunkMetadata.Empty.With("Origin", "doc.txt");

            // Act & Assert
            Assert.That(a, Is.Not.EqualTo(b));
        }

        [Test]
        public void Equals_ExtraEntry_AreNotEqual()
        {
            // Arrange
            var a = ChunkMetadata.Empty.With("Source", "doc.txt");
            var b = a.With("Page", 4);

            // Act & Assert
            Assert.That(a, Is.Not.EqualTo(b));
        }

        [Test]
        public void Equals_KeysDifferingOnlyInCase_AreDistinctEntries()
        {
            // Arrange - keys are ordinal by construction, so "source" and "Source" are different keys.
            var a = ChunkMetadata.Empty.With("Source", "doc.txt");
            var b = ChunkMetadata.Empty.With("source", "doc.txt");

            // Act & Assert
            Assert.That(a, Is.Not.EqualTo(b));
        }

        [Test]
        public void Equals_EquivalentNumberFormats_AreNotEqualBecauseComparisonIsRawJsonText()
        {
            // Arrange - 1e-3 and 0.001 are the same number but different raw text.
            // The strictness is pinned deliberately: it keeps equality identical on net8.0 and net10.0, since JsonElement.DeepEquals needs .NET 9.
            var a = ChunkMetadata.Empty.With("n", JsonDocument.Parse("1e-3").RootElement);
            var b = ChunkMetadata.Empty.With("n", JsonDocument.Parse("0.001").RootElement);

            // Act & Assert
            Assert.That(a, Is.Not.EqualTo(b));
        }

        [Test]
        public void Equals_ComparedToNull_IsFalse()
        {
            // Arrange
            var metadata = ChunkMetadata.Empty.With("Source", "doc.txt");

            // Act & Assert
            Assert.That(metadata, Is.Not.Null);
        }

        [Test]
        public void ReadMembers_PopulatedInstance_BehaveLikeAReadOnlyDictionary()
        {
            // Arrange
            var metadata = ChunkMetadata.Empty.With("Source", "doc.txt").With("Page", 4);

            // Act & Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(metadata.ContainsKey("Source"), Is.True);
                Assert.That(metadata.ContainsKey("Missing"), Is.False);
                Assert.That(metadata.TryGetValue("Page", out var page), Is.True);
                Assert.That(page.GetInt32(), Is.EqualTo(4));
                Assert.That(metadata.Keys, Is.EquivalentTo(new[] { "Source", "Page" }));
                Assert.That(metadata.Values.Count(), Is.EqualTo(2));
                Assert.That(metadata.Select(kvp => kvp.Key), Is.EquivalentTo(new[] { "Source", "Page" }));
            }
        }
    }
}
