namespace FluentChunker.Tests
{
    // Generates a long themed stream on demand and counts what it hands out, so tests can bound how far ahead of the yielded chunks the reader has been drained.
    // Overriding Read(char[], int, int) is enough: the TextReader base class routes ReadBlockAsync(Memory<char>, ...) through it.
    internal sealed class MeteredRepeatingReader(string phrase, long totalChars) : TextReader
    {
        public long CharsProvided { get; private set; }

        public override int Read(char[] buffer, int index, int count)
        {
            var provided = 0;

            while (provided < count && CharsProvided + provided < totalChars)
            {
                buffer[index + provided] = phrase[(int)((CharsProvided + provided) % phrase.Length)];
                provided++;
            }

            CharsProvided += provided;
            return provided;
        }
    }
}
