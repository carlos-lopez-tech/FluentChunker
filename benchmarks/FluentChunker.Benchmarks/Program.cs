using BenchmarkDotNet.Running;
using FluentChunker.Benchmarks;

// The quality report is a separate mode rather than a benchmark, because what it measures is output correctness, which a timing harness cannot express.
if (args.Length > 0 && args[0] == "verify")
{
    await QualityReport.RunAsync();
    return 0;
}

// Returns an exit code so a CI step can gate on it: a moved boundary is a breaking change no API diff would show.
if (args.Length > 0 && args[0] == "drift")
{
    return await BoundaryDrift.RunAsync(record: args.Contains("--record"));
}

// BenchmarkSwitcher rather than BenchmarkRunner so --filter selects a class from the command line without a code change.
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
return 0;
