using System.Buffers;

namespace FluentChunker.Chunkers
{
    // Buffered reading for the streaming chunkers: fills from a caller-owned TextReader, exposes one bounded segment at a time, and compacts consumed text away.
    // The reader is never disposed here, ownership stays with the caller; IDisposable exists only to return the rented buffer.
    internal sealed class SlidingTextWindow : IDisposable
    {
        internal const int DefaultCapacity = 262_144;

        private readonly TextReader _reader;
        private readonly char[] _buffer;
        private readonly int _capacity;
        private int _count;
        private bool _disposed;

        // A capacity below 2 could not honour the safe-cut hold-back and still expose a segment.
        // The buffer is rented because the default capacity is a 512 KB array, above the large object heap threshold, and a pipeline that streams many documents would otherwise leave one LOH allocation per call.
        // Rent can hand back a larger array than asked for, so the requested capacity stays the authoritative bound below, never the array's length.
        internal SlidingTextWindow(TextReader reader, int capacity = DefaultCapacity, long basePosition = 0)
        {
            ArgumentNullException.ThrowIfNull(reader);
            ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 2);
            ArgumentOutOfRangeException.ThrowIfNegative(basePosition);

            _reader = reader;
            _capacity = capacity;
            _buffer = ArrayPool<char>.Shared.Rent(capacity);
            BasePosition = basePosition;
        }

        // Absolute offset of the buffer start in the stream, long because streamed positions can exceed int.MaxValue.
        // A non-zero start exists only for tests, which seed a base near int.MaxValue to prove the arithmetic without streaming gigabytes.
        internal long BasePosition { get; private set; }

        internal bool EndOfStream { get; private set; }

        // One ReadBlockAsync fills to capacity or drains the stream: it loops over partial reads inside the BCL and returns fewer chars than requested only at end of stream.
        // After this returns, the buffer is either full or EndOfStream is true, the invariant the strategies build on.
        internal async ValueTask FillAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (EndOfStream || _count == _capacity)
                return;

            var requested = _capacity - _count;
            var read = await _reader.ReadBlockAsync(_buffer.AsMemory(_count, requested), cancellationToken).ConfigureAwait(false);

            _count += read;

            if (read < requested)
                EndOfStream = true;
        }

        // One bounded string per fill is a deliberate allocation: SentenceSplitter, BlockSplitter and the tokenizer all take strings, and span-based splitting is a future optimization outside this feature.
        // While more input may follow, a segment never ends between a high and low surrogate or between '\r' and '\n', because both pairs must reach a splitter together; the dangling char stays buffered for the next segment.
        // At end of stream everything is exposed, a trailing '\r' or lone surrogate half is then document content.
        internal string PeekSegment()
        {
            var length = _count;

            if (!EndOfStream && length > 0)
            {
                var last = _buffer[length - 1];
                if (char.IsHighSurrogate(last) || last == '\r')
                    length--;
            }

            return new string(_buffer, 0, length);
        }

        // Advances the window: the consumed prefix leaves the buffer, the tail moves to the front, and BasePosition grows by exactly the consumed count.
        internal void Consume(int charCount)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(charCount);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(charCount, _count);

            Array.Copy(_buffer, charCount, _buffer, 0, _count - charCount);
            _count -= charCount;
            BasePosition += charCount;
        }

        // Returning twice would let the pool hand the same array to two owners, so disposal is idempotent.
        // A missed dispose only costs the pool a reuse, the GC still reclaims the array; correctness never depends on the return.
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            ArrayPool<char>.Shared.Return(_buffer);
        }
    }
}
