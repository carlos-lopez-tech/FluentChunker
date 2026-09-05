namespace FluentChunker.Tests
{
    // A sweep runs over thousands of chunks, so asserting per chunk would report one failure and hide the shape of the rest.
    // Collecting instead lets a single assertion say how many chunks broke the invariant and show the first few, which is what identifies the pattern.
    internal sealed class InvariantViolations(string invariant)
    {
        private const int SamplesShown = 5;

        private readonly List<string> _samples = [];

        internal int Count { get; private set; }

        internal void Add(string description)
        {
            Count++;

            if (_samples.Count < SamplesShown)
                _samples.Add(description);
        }

        internal string Message =>
            Count == 0
                ? $"{invariant}: held"
                : $"{invariant}: broken by {Count:N0} chunks. First {_samples.Count}: {string.Join(" | ", _samples)}";
    }
}
