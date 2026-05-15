using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FluentChunker.Tests
{
    public class ChunkerTests
    {
        [Test]
        public void Chunker_Should_Split_Text_By_Newline()
        {
            // Arrange
            var chunker = Chunker.Create();
            var input = "Line 1\nLine 2\nLine 3";
            // Act
            var result = chunker.Chunk(input).ToList();
            // Assert
            Assert.That(result, Has.Count.EqualTo(3));
        }
    }
}
