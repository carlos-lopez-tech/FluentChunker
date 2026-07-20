using FluentChunker.Chunkers.FixedSize;
using FluentChunker.Chunkers.SentenceAware;
using FluentChunker.Contracts;
using FluentChunker.Tokenizers;

namespace FluentChunker
{
    public static class Splitter
    {
        public class Builder
        {
            private readonly ITokenizer _tokenizer;
            private Func<IChunker>? _chunkerFactory;
            private int _maxTokens;
            private int _overlap;
            private HashSet<string>? _abbreviations;

            internal Builder(ITokenizer tokenizer) => _tokenizer = tokenizer;

            /// <summary>
            /// Splits the document into fixed-size token windows, ignoring sentence boundaries.
            /// Each chunk holds up to the configured max token count; overlap repeats the trailing
            /// tokens of one chunk at the start of the next.
            /// </summary>
            /// <returns>The next step, which requires a max token size.</returns>
            public ChunkingStrategyStep WithFixedSizeChunking()
            {
                _chunkerFactory = () => new FixedSizeChunker(_tokenizer, _maxTokens, _overlap);
                return new ChunkingStrategyStep(this);
            }

            /// <summary>
            /// Splits the document at sentence boundaries and greedily packs whole sentences into
            /// each chunk up to the configured max token count. Overlap carries whole trailing
            /// sentences into the next chunk. A single sentence that alone exceeds the max falls
            /// back to evenly balanced token windows.
            /// </summary>
            /// <returns>The next step, which requires a max token size.</returns>
            public ChunkingStrategyStep WithSentenceAwareChunking()
            {
                _chunkerFactory = () => new SentenceAwareChunker(_tokenizer, _maxTokens, _overlap, _abbreviations);
                return new ChunkingStrategyStep(this);
            }

            internal IChunker Build()
            {
                if (_chunkerFactory is null)
                    throw new InvalidOperationException("A chunking strategy is required — call WithFixedSizeChunking() or WithSentenceAwareChunking().");
                if (_maxTokens <= 0)
                    throw new InvalidOperationException("A max token size is required — call WithMaxTokenSize().");

                return _chunkerFactory();
            }

            public class ChunkingStrategyStep
            {
                private readonly Builder _builder;

                internal ChunkingStrategyStep(Builder builder) => _builder = builder;

                /// <summary>
                /// Sets the maximum number of tokens a single chunk may contain.
                /// </summary>
                /// <param name="maxTokens">The chunk size limit in tokens. Must be positive.</param>
                /// <returns>The final step, where overlap and abbreviations can optionally be configured.</returns>
                /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxTokens"/> is zero or negative.</exception>
                public MaxTokenSizeStep WithMaxTokenSize(int maxTokens)
                {
                    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTokens);

                    _builder._maxTokens = maxTokens;
                    return new MaxTokenSizeStep(_builder);
                }
            }

            public class MaxTokenSizeStep
            {
                private readonly Builder _builder;

                internal MaxTokenSizeStep(Builder builder) => _builder = builder;

                /// <summary>
                /// Sets how many tokens of context are carried from the end of one chunk into the
                /// start of the next. Each chunk's <c>OverlapCount</c> reports the tokens it received.
                /// Sentence-aware chunking rounds the overlap to whole sentences.
                /// </summary>
                /// <param name="overlap">The overlap size in tokens. Must be non-negative and less than the max token size.</param>
                /// <returns>The same step, for further optional configuration.</returns>
                /// <exception cref="ArgumentOutOfRangeException"><paramref name="overlap"/> is negative, or greater than or equal to the max token size.</exception>
                public MaxTokenSizeStep WithOverlap(int overlap)
                {
                    ArgumentOutOfRangeException.ThrowIfNegative(overlap);
                    // overlap == maxTokens would make FixedSizeChunker's step size zero — an infinite loop
                    ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(overlap, _builder._maxTokens);

                    _builder._overlap = overlap;
                    return this;
                }

                /// <summary>
                /// Adds abbreviations whose trailing period should not end a sentence
                /// (e.g. <c>"Prof."</c>, <c>"approx."</c>), extending the built-in EN/DE/FR/ES set.
                /// Matching is case-insensitive; trailing dots are trimmed before storing.
                /// Only used by sentence-aware chunking.
                /// </summary>
                /// <param name="abbreviations">The abbreviations to add. Entries must be non-empty after trimming trailing dots.</param>
                /// <returns>The same step, for further optional configuration.</returns>
                /// <exception cref="ArgumentNullException"><paramref name="abbreviations"/> is null.</exception>
                /// <exception cref="ArgumentException">An entry is empty or consists only of dots or whitespace.</exception>
                public MaxTokenSizeStep WithAbbreviations(params string[] abbreviations)
                {
                    ArgumentNullException.ThrowIfNull(abbreviations);

                    // Validate the whole batch before mutating any state, so a bad entry
                    // (e.g. an all-dots string that trims down to empty) never partially applies.
                    var trimmed = new string[abbreviations.Length];
                    for (var i = 0; i < abbreviations.Length; i++)
                    {
                        // Stored without the trailing dot — the splitter matches the word before the period.
                        trimmed[i] = abbreviations[i]?.TrimEnd('.') ?? string.Empty;

                        if (string.IsNullOrWhiteSpace(trimmed[i]))
                            throw new ArgumentException("Abbreviations must be non-empty.", nameof(abbreviations));
                    }

                    _builder._abbreviations ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var entry in trimmed)
                    {
                        _builder._abbreviations.Add(entry);
                    }

                    return this;
                }
            }
        }
    }
}
