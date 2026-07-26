using FluentChunker.Tokenizers;

namespace FluentChunker.Chunkers
{
    // Decides where a token sequence may be cut without splitting a character in half.
    // Tiktoken encodings are byte-level BPE, so one character can span several tokens and a single token can hold a fragment of a UTF-8 sequence that is not valid text on its own.
    // Decoding a slice that begins or ends inside such a run yields U+FFFD instead of the original character.
    //
    // A cut is legal exactly when decoding the two sides separately produces the same text as decoding them together.
    // That test needs only ITokenizer, so custom tokenizers are protected too, and unlike scanning for U+FFFD it does not misfire on a document that already contains that character.
    internal sealed class TokenBoundary(ITokenizer tokenizer, IReadOnlyList<int> tokens)
    {
        /// <summary>
        /// The most tokens a single character can occupy, and therefore the smallest budget that can always hold one.
        /// </summary>
        /// <remarks>
        /// A UTF-8 scalar is at most four bytes and every token carries at least one byte.
        /// </remarks>
        internal const int MaxTokensPerCharacter = 4;

        // Scanning this far on each side always spans the whole character a cut could fall inside.
        // This is what turns the O(n^2) whole-sequence definition into an O(1) check per boundary.
        private const int Radius = MaxTokensPerCharacter;

        private const byte Unknown = 0;
        private const byte Legal = 1;
        private const byte Illegal = 2;

        private readonly ITokenizer _tokenizer = tokenizer;
        private readonly IReadOnlyList<int> _tokens = tokens;

        // Snapping walks over the same indices repeatedly, so each verdict is decided once.
        private readonly byte[] _verdicts = new byte[tokens.Count + 1];

        internal bool IsSafeCut(int index)
        {
            if (index <= 0 || index >= _tokens.Count)
                return true;

            if (_verdicts[index] != Unknown)
                return _verdicts[index] == Legal;

            var low = Math.Max(0, index - Radius);
            var high = Math.Min(_tokens.Count, index + Radius);

            var legal = _tokenizer.Decode(TokenWindows.Slice(_tokens, low, index - low))
                        + _tokenizer.Decode(TokenWindows.Slice(_tokens, index, high - index))
                        == _tokenizer.Decode(TokenWindows.Slice(_tokens, low, high - low));

            _verdicts[index] = legal ? Legal : Illegal;
            return legal;
        }

        /// <summary>
        /// Moves a candidate cut onto a legal boundary strictly above <paramref name="floor"/>.
        /// </summary>
        /// <remarks>
        /// Searches downward first, so a window snapped this way never grows past its budget.
        /// When the character under the candidate runs all the way back to the floor, there is no lower cut to take, so the first legal cut above the candidate is returned instead.
        /// That character is indivisible, so the window it lands in is as large as the character itself requires.
        /// </remarks>
        internal int SnapToSafeCut(int candidate, int floor)
        {
            for (var index = Math.Min(candidate, _tokens.Count); index > floor; index--)
            {
                if (IsSafeCut(index))
                    return index;
            }

            for (var index = candidate + 1; index < _tokens.Count; index++)
            {
                if (IsSafeCut(index))
                    return index;
            }

            return _tokens.Count;
        }
    }
}
