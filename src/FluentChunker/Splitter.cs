using FluentChunker.Chunkers.FixedSize;
using FluentChunker.Chunkers.Recursive;
using FluentChunker.Chunkers.SentenceAware;
using FluentChunker.Chunkers.StructureAware;
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
            /// </summary>
            /// <returns>The next step, which requires a max token size.</returns>
            public ChunkingStrategyStep WithFixedSizeChunking()
            {
                _chunkerFactory = () => new FixedSizeChunker(_tokenizer, _maxTokens, _overlap);
                return new ChunkingStrategyStep(this);
            }

            /// <summary>
            /// Splits at sentence boundaries, greedily packing whole sentences into each chunk.
            /// A sentence that alone exceeds the max falls back to evenly balanced token windows.
            /// </summary>
            /// <returns>The next step, which requires a max token size.</returns>
            public ChunkingStrategyStep WithSentenceAwareChunking()
            {
                _chunkerFactory = () => new SentenceAwareChunker(_tokenizer, _maxTokens, _overlap, _abbreviations);
                return new ChunkingStrategyStep(this);
            }

            /// <summary>
            /// Splits along a fixed hierarchy of separators (paragraphs, then lines, then sentences),
            /// descending a level only for pieces that exceed the max. Chunks never merge across a
            /// descent boundary and overlap resets to zero there. A sentence that alone exceeds the
            /// max falls back to evenly balanced token windows.
            /// </summary>
            /// <returns>The next step, which requires a max token size.</returns>
            public ChunkingStrategyStep WithRecursiveChunking()
            {
                _chunkerFactory = () => new RecursiveChunker(_tokenizer, _maxTokens, _overlap, _abbreviations);
                return new ChunkingStrategyStep(this);
            }

            /// <summary>
            /// Splits Markdown at its headings. Sections are hard boundaries: 
            /// a chunk never spans two, and each one carries its heading path (e.g. <c>"Guide > Setup"</c>) 
            /// in <c>Chunk.Metadata["headingPath"]</c>, a key reserved for this strategy. 
            /// Code fences, tables, lists and quotes stay atomic while they fit the budget; an oversized paragraph descends to sentences. 
            /// Text without Markdown structure degrades to paragraph packing with an empty heading path. Applies no overlap by design.
            /// </summary>
            /// <returns>The next step, which requires a max token size.</returns>
            public StructureAwareStrategyStep WithStructureAwareChunking()
            {
                _chunkerFactory = () => new StructureAwareChunker(_tokenizer, _maxTokens, _abbreviations);
                return new StructureAwareStrategyStep(this);
            }

            internal IChunker Build()
            {
                if (_chunkerFactory is null)
                    throw new InvalidOperationException("A chunking strategy is required: call WithFixedSizeChunking(), WithSentenceAwareChunking(), WithRecursiveChunking() or WithStructureAwareChunking().");
                if (_maxTokens <= 0)
                    throw new InvalidOperationException("A max token size is required: call WithMaxTokenSize().");

                return _chunkerFactory();
            }

            private void SetMaxTokenSize(int maxTokens)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTokens);
                _maxTokens = maxTokens;
            }

            private void AddAbbreviations(string[] abbreviations)
            {
                ArgumentNullException.ThrowIfNull(abbreviations);

                // Validate the whole batch before mutating any state, so a bad entry
                // (e.g. an all-dots string that trims down to empty) never partially applies.
                var trimmed = new string[abbreviations.Length];
                for (var i = 0; i < abbreviations.Length; i++)
                {
                    // Stored without the trailing dot: the splitter matches the word before the period.
                    trimmed[i] = abbreviations[i]?.TrimEnd('.') ?? string.Empty;

                    if (string.IsNullOrWhiteSpace(trimmed[i]))
                        throw new ArgumentException("Abbreviations must be non-empty.", nameof(abbreviations));
                }

                _abbreviations ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in trimmed)
                {
                    _abbreviations.Add(entry);
                }
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
                    _builder.SetMaxTokenSize(maxTokens);
                    return new MaxTokenSizeStep(_builder);
                }
            }

            public class MaxTokenSizeStep
            {
                private readonly Builder _builder;

                internal MaxTokenSizeStep(Builder builder) => _builder = builder;

                /// <summary>
                /// Carries context from the end of one chunk into the start of the next; each chunk's <c>OverlapCount</c> reports the tokens it received. 
                /// Sentence-aware and recursive chunking round to whole units, and recursive chunking resets at structural seams.
                /// </summary>
                /// <param name="overlap">The overlap size in tokens. Must be non-negative and less than the max token size.</param>
                /// <returns>The same step, for further optional configuration.</returns>
                /// <exception cref="ArgumentOutOfRangeException"><paramref name="overlap"/> is negative, or greater than or equal to the max token size.</exception>
                public MaxTokenSizeStep WithOverlap(int overlap)
                {
                    ArgumentOutOfRangeException.ThrowIfNegative(overlap);
                    // overlap == maxTokens would make FixedSizeChunker's step size zero, so it would loop forever
                    ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(overlap, _builder._maxTokens);

                    _builder._overlap = overlap;
                    return this;
                }

                /// <summary>
                /// Adds abbreviations whose trailing period should not end a sentence (e.g. <c>"Prof."</c>), extending the built-in EN/DE/FR/ES set. 
                /// Matching is case-insensitive, and repeated calls accumulate instead of replacing. Only sentence-aware and recursive chunking consult them.
                /// </summary>
                /// <param name="abbreviations">The abbreviations to add. Entries must be non-empty after trimming trailing dots.</param>
                /// <returns>The same step, for further optional configuration.</returns>
                /// <exception cref="ArgumentNullException"><paramref name="abbreviations"/> is null.</exception>
                /// <exception cref="ArgumentException">An entry is empty or consists only of dots or whitespace.</exception>
                public MaxTokenSizeStep AddAbbreviations(params string[] abbreviations)
                {
                    _builder.AddAbbreviations(abbreviations);
                    return this;
                }
            }

            public class StructureAwareStrategyStep
            {
                private readonly Builder _builder;

                internal StructureAwareStrategyStep(Builder builder) => _builder = builder;

                /// <summary>
                /// Sets the maximum number of tokens a single chunk may contain.
                /// </summary>
                /// <param name="maxTokens">The chunk size limit in tokens. Must be positive.</param>
                /// <returns>The final step. Structure-aware chunking has no overlap, so it offers
                /// only abbreviations, no <c>WithOverlap</c>.</returns>
                /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxTokens"/> is zero or negative.</exception>
                public StructureAwareOptionsStep WithMaxTokenSize(int maxTokens)
                {
                    _builder.SetMaxTokenSize(maxTokens);
                    return new StructureAwareOptionsStep(_builder);
                }
            }

            public class StructureAwareOptionsStep
            {
                private readonly Builder _builder;

                internal StructureAwareOptionsStep(Builder builder) => _builder = builder;

                /// <summary>
                /// Adds abbreviations whose trailing period should not end a sentence (e.g. <c>"Prof."</c>), extending the built-in EN/DE/FR/ES set. 
                /// Matching is case-insensitive, and repeated calls accumulate instead of replacing. Consulted only when an oversized paragraph descends to sentences.
                /// </summary>
                /// <param name="abbreviations">The abbreviations to add. Entries must be non-empty after trimming trailing dots.</param>
                /// <returns>The same step, for further optional configuration.</returns>
                /// <exception cref="ArgumentNullException"><paramref name="abbreviations"/> is null.</exception>
                /// <exception cref="ArgumentException">An entry is empty or consists only of dots or whitespace.</exception>
                public StructureAwareOptionsStep AddAbbreviations(params string[] abbreviations)
                {
                    _builder.AddAbbreviations(abbreviations);
                    return this;
                }
            }
        }
    }
}
