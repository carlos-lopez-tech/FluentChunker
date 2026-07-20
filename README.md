# FluentChunker

A .NET library for building fluent, composable text chunking pipelines. 
Split text into meaningful chunks for RAG (Retrieval-Augmented Generation), embeddings, search indexing, and other NLP workflows with a clean, readable API.

## Features

- **Stepped Builder API** — Chain-based pipeline configuration with clear, expressive syntax; the step order is enforced at compile time
- **Fixed-size chunking** — Split text into windows of at most N tokens (structure-aware strategy planned)
- **Sentence-aware chunking** (also known as sentence-based chunking) — Pack whole sentences into token-bounded chunks; never cuts mid-sentence except for a token-window fallback on sentences that alone exceed the budget
- **Token-aware chunking** — Split text by token count with accurate token counting
- **Customizable tokenizers** — Plug in your own tokenizer implementation or use built-in Microsoft.ML.Tokenizers, SharpToken integration
- **Overlap support** — Configure token overlap between chunks to preserve context; overlap is budgeted inside the max token size, so chunks never exceed it
- **Two metadata scopes** — Attach pipeline-level metadata (constant per run) and document-level metadata (per `Execute` call) to each chunk
- **Reusable pipelines** — Build once, execute against many documents; metadata never leaks between calls

## Quick Start

```csharp
using FluentChunker;

// Create tokenizer and fluent pipeline
var tokenizer = new MicrosoftMLTokenizerAdapter("o200k_base");

var pipeline = ChunkingPipeline.Builder.Initialize()
    .UseTokenizer(tokenizer)
    .UseSplitter(s => s
        .WithFixedSizeChunking()
        .WithMaxTokenSize(512)
        .WithOverlap(50))
    .WithMetadata(meta =>
    {
        meta.Set("Source", "document.txt");
        meta.Set("Model", "gpt-4o");
    })
    .Build();

// Execute and process chunks
var chunks = pipeline.Execute(largeDocument);

foreach (var chunk in chunks)
{
    Console.WriteLine($"Chunk {chunk.Index}: {chunk.Text.Length} chars, {chunk.TokenCount} tokens");
    Console.WriteLine($"  Source: {chunk.Metadata["Source"].GetString()}");
    Console.WriteLine($"  Model: {chunk.Metadata["Model"].GetString()}");
}
```

## Installation

Not yet published to NuGet — reference the project directly:

```bash
dotnet add reference path/to/src/FluentChunker/FluentChunker.csproj
```

## Usage Examples

### Basic Fixed-Size Chunking

```csharp
var tokenizer = new MicrosoftMLTokenizerAdapter("o200k_base");

var pipeline = ChunkingPipeline.Builder.Initialize()
    .UseTokenizer(tokenizer)
    .UseSplitter(s => s
        .WithFixedSizeChunking()
        .WithMaxTokenSize(1024))
    .Build();

var chunks = pipeline.Execute(text);
```

### Chunking with Overlap

```csharp
var tokenizer = new MicrosoftMLTokenizerAdapter("o200k_base");

var pipeline = ChunkingPipeline.Builder.Initialize()
    .UseTokenizer(tokenizer)
    .UseSplitter(s => s
        .WithFixedSizeChunking()
        .WithMaxTokenSize(512)
        .WithOverlap(128))
    .Build();

var chunks = pipeline.Execute(text);
```

### Sentence-Aware Chunking

```csharp
var tokenizer = new MicrosoftMLTokenizerAdapter("o200k_base");

var pipeline = ChunkingPipeline.Builder.Initialize()
    .UseTokenizer(tokenizer)
    .UseSplitter(s => s
        .WithSentenceAwareChunking()
        .WithMaxTokenSize(512)
        .WithOverlap(50)
        .WithAbbreviations("Vgl.", "sog."))   // optional, additive to the built-in list
    .Build();

var chunks = pipeline.Execute(document);
```

Chunks contain whole sentences and are verbatim slices of the source text — whitespace and paragraph breaks between packed sentences are preserved exactly, not re-joined or trimmed. Every chunk's `TokenCount` stays within the configured max: a sentence that alone exceeds the budget is split into evenly-sized token windows as a fallback (with `OverlapCount = 0` on those windows). Note that fallback windows are *decoded from tokens*, not verbatim slices — their text can carry a leading space from the tokenizer, so trim before joining chunk texts if that matters to you.

`WithOverlap(n)` carries whole trailing sentences into the next chunk, as many as fit within `n` tokens. `OverlapCount` reports the tokens carried *into* a chunk (0 on the first chunk) — the same semantics both strategies use.

**Language support:** works for any language using `. ! ?` as sentence terminators with a bicameral script (Latin, Greek, Cyrillic) — covers all European and North/South American languages. Uppercase detection is Unicode-aware, and `¿ ¡ « „` are recognized as valid sentence openers (Spanish/French/German). A built-in abbreviation guard covers common EN/DE/FR/ES abbreviations (`Dr`, `Mr`, `Nr`, `bzw`, `M`, `Sra`, etc.); extend it with `.WithAbbreviations(...)` — entries may include a trailing dot, matching is case-insensitive.

**Known limitations:** German ordinal periods (e.g. "am 3. Oktober") are falsely split — no code disambiguates them from sentence ends. CJK terminators (`。`) aren't recognized; such text is treated as one long sentence and falls back to fixed-size-style token windowing.

### Adding Custom Metadata

```csharp
var tokenizer = new MicrosoftMLTokenizerAdapter("o200k_base");

var pipeline = ChunkingPipeline.Builder.Initialize()
    .UseTokenizer(tokenizer)
    .UseSplitter(s => s
        .WithFixedSizeChunking()
        .WithMaxTokenSize(512))
    .WithMetadata(meta =>
    {
        meta.Set("Source", "document.md");
        meta.Set("Version", "1.0");
    })
    .Build();

var chunks = pipeline.Execute(largeDocument);

foreach (var chunk in chunks)
{
    Console.WriteLine($"Chunk {chunk.Index}: {chunk.TokenCount} tokens");
    Console.WriteLine($"  Source: {chunk.Metadata["Source"].GetString()}");
    Console.WriteLine($"  Text: {chunk.Text[..Math.Min(80, chunk.Text.Length)]}...");
}
```

### Per-Document Metadata (Reusing One Pipeline)

Pipeline-level metadata (`WithMetadata`) is constant for the whole run; document-level metadata is passed per `Execute` call. On key collision, the document value wins.

```csharp
var tokenizer = new MicrosoftMLTokenizerAdapter("o200k_base");

var pipeline = ChunkingPipeline.Builder.Initialize()
    .UseTokenizer(tokenizer)
    .UseSplitter(s => s
        .WithFixedSizeChunking()
        .WithMaxTokenSize(512)
        .WithOverlap(50))
    .WithMetadata(meta => meta.Set("SourceType", "wiki"))   // run-constant
    .Build();                                                // built once

foreach (var doc in documents)
{
    var chunks = pipeline.Execute(doc.Markdown, meta =>     // per document
    {
        meta.Set("Url", doc.Url);
        meta.Set("Title", doc.Title);
        meta.Set("CreatedAt", DateTime.UtcNow);
    });
}
```

## Tokenizers

### MicrosoftMLTokenizer Integration

FluentChunker includes built-in support for [Microsoft.ML.Tokenizers](https://www.nuget.org/packages/Microsoft.ML.Tokenizers), enabling accurate token counting for OpenAI models:

```csharp
var tokenizer = new MicrosoftMLTokenizerAdapter("o200k_base"); // GPT-4o tokenizer
var pipeline = ChunkingPipeline.Builder.Initialize()
    .UseTokenizer(tokenizer)
    .UseSplitter(s => s
        .WithFixedSizeChunking()
        .WithMaxTokenSize(1024))
    .Build();
```

A second adapter, `SharpTokenizerAdapter`, wraps [SharpToken](https://github.com/dmitry-brazhenko/SharpToken) with the same encodings.

Supported encoding models include `cl100k_base` (GPT-3.5, GPT-4), `o200k_base` (GPT-4o), and others.

### Custom Tokenizers

Implement `ITokenizer` to use your own tokenization logic:

```csharp
public class CustomTokenizer : ITokenizer
{
    public IReadOnlyList<int> Encode(string text) => /* your tokenization */;

    public string Decode(IReadOnlyList<int> tokens) => /* your detokenization */;

    public int CountTokens(string text)
    {
        // Efficiently count tokens without materializing the full list
        return Encode(text).Count;
    }
}

var tokenizer = new CustomTokenizer();
var pipeline = ChunkingPipeline.Builder.Initialize()
    .UseTokenizer(tokenizer)
    .UseSplitter(s => s
        .WithFixedSizeChunking()
        .WithMaxTokenSize(512))
    .Build();
```

All three methods (`Encode`, `Decode`, `CountTokens`) are required by the `ITokenizer` interface.

## API Reference

### ChunkingPipeline.Builder

The builder is stepped — each method returns the next step, so the order below is enforced at compile time:

- `Builder.Initialize()` — Start a new pipeline configuration
- `.UseTokenizer(ITokenizer tokenizer)` — Set the tokenizer for token counting
- `.UseSplitter(Action<Splitter.Builder> configure)` — Configure the chunking strategy
- `.WithMetadata(Action<MetadataBuilder> configure)` — Optional: add pipeline-level metadata to all chunks (may be called multiple times; callbacks compose)
- `.Build()` — Create the pipeline

### Splitter.Builder (inside UseSplitter)

Also stepped, in this order:

- `.WithFixedSizeChunking()` — Select the fixed-size strategy
- `.WithSentenceAwareChunking()` — Select the sentence-aware strategy (peer of `WithFixedSizeChunking`)
- `.WithMaxTokenSize(int maxTokens)` — Maximum tokens per chunk (must be positive)
- `.WithOverlap(int overlap)` — Optional: tokens shared between consecutive chunks (must be non-negative and smaller than the max token size; budgeted inside it, not added on top)
- `.WithAbbreviations(params string[] abbreviations)` — Optional, sentence-aware only: additional abbreviations (beyond the built-in EN/DE/FR/ES set) that should not be treated as sentence endings; entries may include a trailing dot, matching is case-insensitive. Has no effect when combined with `WithFixedSizeChunking()`.

### ChunkingPipeline

- `.Execute(string document)` — Execute the pipeline and return chunks
- `.Execute(string document, Action<MetadataBuilder> configureMetadata)` — Execute with additional document-level metadata; merged over the pipeline metadata, document values win on key collision

### Chunk

Each chunk contains:
- `Index` — Zero-based position in the chunk sequence (resets per `Execute` call)
- `Text` — The actual chunk content
- `TokenCount` — Number of tokens in this chunk
- `OverlapCount` — Tokens carried into this chunk from the previous chunk (`0` on the first chunk)
- `Metadata` — Dictionary of custom key-value pairs as `JsonElement` (access with e.g. `chunk.Metadata["Source"].GetString()`)

### ITokenizer

Implement to use custom tokenization:
- `IReadOnlyList<int> Encode(string text)` — Convert text to token IDs
- `string Decode(IReadOnlyList<int> tokens)` — Convert token IDs back to text
- `int CountTokens(string text)` — Count the number of tokens in the text without materializing the token list

## Requirements

- .NET 10.0 or later
- Microsoft.ML.Tokenizers 2.0.0 or later (included as dependency)
- SharpToken 2.0.6 or later (included as dependency)

## Development

### Build

```bash
dotnet build FluentChunker.slnx
```

### Test

```bash
dotnet test tests/FluentChunker.Tests
```

### Sample/Demo

```bash
dotnet run --project samples/FluentChunker.Samples.Demo
```

## License

MIT
