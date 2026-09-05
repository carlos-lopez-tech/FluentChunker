using FluentChunker.Tokenizers;

namespace FluentChunker.Chunkers
{
    // Encodes a whole document while leaving cancellation somewhere to land.
    // Tokenizers that cannot encode in batches keep the single call they always had, so this only ever adds responsiveness, never changes the tokens.
    internal static class DocumentEncoding
    {
        // Each batch carries a fixed per-call cost, so a batch too small pays for cancellation with throughput.
        // This size sits far enough up that curve that the per-call cost disappears into the encode it wraps, while staying small enough that a cancelled call returns promptly.
        // Because the bound is tokens rather than characters, the work between two cancellation checks stays the same on a 200 MB document as on a 2 MB one, which is the property worth having.
        // Comfortably above the most tokens a single character can occupy, so a batch always fits at least one character and the loop always advances.
        // Visible to the tests so they can size a document past it: a document that fits in one batch exercises none of the behaviour below.
        internal const int BatchTokens = 65536;

        internal static IReadOnlyList<int> Encode(ITokenizer tokenizer, string text, CancellationToken cancellationToken)
        {
            if (tokenizer is not IBatchEncodingTokenizer batching)
                return tokenizer.Encode(text);

            var ids = new List<int>();
            var position = 0;

            while (position < text.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = batching.EncodeUpTo(text.AsSpan(position), BatchTokens, out var charsConsumed);

                // A batch that advances nothing would spin forever, so the remainder is encoded in one call instead.
                // Unreachable for a tokenizer that honours its own contract, and taking the whole remainder keeps the tail from being dropped if one does not.
                if (charsConsumed <= 0)
                {
                    ids.AddRange(tokenizer.Encode(text[position..]));
                    break;
                }

                ids.AddRange(batch);
                position += charsConsumed;
            }

            return ids;
        }
    }
}
