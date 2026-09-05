using BenchmarkDotNet.Attributes;
using FluentChunker.Chunkers;
using FluentChunker.Contracts;
using FluentChunker.Tokenizers;

namespace FluentChunker.Benchmarks
{
    /// <summary>
    /// The cost of deciding whether a token index is a legal cut, measured directly rather than inferred from end-to-end throughput.
    /// </summary>
    /// <remarks>
    /// P003 added a decode-based legality check to every window boundary and end-to-end throughput did not move, which is only explicable by the memoisation.
    /// Comparing a cold instance against a warmed one is what turns that explanation into a number.
    /// </remarks>
    [MemoryDiagnoser]
    public class TokenBoundaryBenchmarks
    {
        private ITokenizer _tokenizer = null!;
        private IReadOnlyList<int> _tokens = null!;
        private TokenBoundary _warm = null!;
        private int[] _indices = null!;

        /// <summary>How many boundary decisions one invocation makes.</summary>
        [Params(1_000, 10_000)]
        public int Probes { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _tokenizer = new MicrosoftMLTokenizerAdapter("cl100k_base");

            // A prefix rather than the whole corpus, because the verdict array is allocated per instance and the cold benchmark builds one per invocation.
            var document = CorpusLoader.Load("english-prose.md")[..200_000];
            _tokens = _tokenizer.Encode(document);

            // Spread the probes over the sequence rather than clustering them, so the scan is not measuring one cached neighbourhood.
            var stride = Math.Max(1, _tokens.Count / Probes);
            _indices = Enumerable.Range(0, Probes).Select(i => Math.Min(_tokens.Count - 1, i * stride)).ToArray();

            _warm = new TokenBoundary(_tokenizer, _tokens);
            foreach (var index in _indices)
                _warm.IsSafeCut(index);
        }

        /// <summary>Every verdict decided for the first time, which is three decodes each.</summary>
        /// <remarks>
        /// The allocation column includes the instance's verdict array, one byte per token, because a cold measurement cannot reuse an instance.
        /// </remarks>
        [Benchmark(Baseline = true)]
        public int Cold()
        {
            var boundary = new TokenBoundary(_tokenizer, _tokens);
            var legal = 0;

            foreach (var index in _indices)
            {
                if (boundary.IsSafeCut(index)) legal++;
            }

            return legal;
        }

        /// <summary>The same verdicts read back from the memo, which is what snapping actually does after the first pass.</summary>
        [Benchmark]
        public int Warm()
        {
            var legal = 0;

            foreach (var index in _indices)
            {
                if (_warm.IsSafeCut(index)) legal++;
            }

            return legal;
        }
    }
}
