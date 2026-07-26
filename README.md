# FluentChunker

[![NuGet](https://img.shields.io/nuget/v/FluentChunker.svg)](https://www.nuget.org/packages/FluentChunker)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-512BD4?logo=dotnet&logoColor=white)](#requirements)
[![License: MIT](https://img.shields.io/github/license/carlos-lopez-tech/FluentChunker)](LICENSE.txt)
[![CI](https://github.com/carlos-lopez-tech/FluentChunker/actions/workflows/ci.yml/badge.svg)](https://github.com/carlos-lopez-tech/FluentChunker/actions/workflows/ci.yml)
[![Coverage](https://img.shields.io/coverallsCoverage/github/carlos-lopez-tech/FluentChunker?branch=main)](https://coveralls.io/github/carlos-lopez-tech/FluentChunker?branch=main)
[![Downloads](https://img.shields.io/nuget/dt/FluentChunker.svg)](https://www.nuget.org/packages/FluentChunker)
[![Stars](https://img.shields.io/github/stars/carlos-lopez-tech/FluentChunker)](https://github.com/carlos-lopez-tech/FluentChunker/stargazers)
[![Open issues](https://img.shields.io/github/issues/carlos-lopez-tech/FluentChunker)](https://github.com/carlos-lopez-tech/FluentChunker/issues)

Production-ready fluent document chunking for .NET.
Build composable document chunking pipelines for RAG, embeddings, semantic search, vector databases, and AI applications with an intuitive fluent, step-by-step builder API.

[Features](#features) • [Quick Start](#quick-start) • [Installation](#installation) • [Usage Examples](#usage-examples) • [Tokenizers](#tokenizers) • [API Reference](#api-reference) • [Development](#development) • [License](#license)

## Features

- **Stepped Builder API**: Chain-based pipeline configuration with clear, expressive syntax; the step order is enforced at compile time
- **Fixed-size chunking**: Split text into windows of at most N tokens
- **Sentence-aware chunking** (also known as sentence-based chunking): Pack whole sentences into token-bounded chunks; never cuts mid-sentence except for a token-window fallback on sentences that alone exceed the budget
- **Recursive chunking**: Split along a fixed structural hierarchy (paragraphs, then lines, then sentences), descending a level only for pieces that exceed the budget; chunks respect the largest structural boundary possible
- **Structure-aware chunking (Markdown)**: Split Markdown documents at heading boundaries with a `headingPath` breadcrumb stamped into each chunk's metadata; code fences, tables and lists stay atomic; no overlap by design
- **Token-aware chunking**: Split text by token count with accurate token counting
- **Customizable tokenizers**: Use the built-in Microsoft.ML.Tokenizers integration or plug in your own tokenizer implementation
- **Overlap support**: Configure token overlap between chunks to preserve context; overlap is budgeted inside the max token size, so no chunk ever exceeds it
- **Two metadata scopes**: Attach pipeline-level metadata (constant per run) and document-level metadata (per `Chunk` call) to each chunk
- **Reusable pipelines**: Build once, execute against many documents; metadata never leaks between calls

## Quick Start

```csharp
using FluentChunker;
using FluentChunker.Tokenizers;

// Create tokenizer and fluent, step-by-step builder pipeline
// cl100k_base is the encoding of the OpenAI embedding models, the usual target when indexing for RAG.
// It is the encoding included with FluentChunker. See Tokenizers below for chat model encodings.
var tokenizer = new MicrosoftMLTokenizerAdapter("cl100k_base");

var pipeline = ChunkingPipeline.CreateBuilder()
    .UseTokenizer(tokenizer)
    .UseSplitter(s => s
        .WithFixedSizeChunking()
        .WithMaxTokenSize(512)
        .WithOverlap(50))
    .AddMetadata(meta =>
    {
        meta.Set("Source", "document.txt");
        meta.Set("Model", "text-embedding-3-small");
    })
    .Build();

// Chunk the document and read the results
var chunks = pipeline.Chunk(largeDocument);

foreach (var chunk in chunks)
{
    Console.WriteLine($"Chunk {chunk.Index}: {chunk.Text.Length} chars, {chunk.TokenCount} tokens");
    Console.WriteLine($"  Source: {chunk.Metadata["Source"].GetString()}");
    Console.WriteLine($"  Model: {chunk.Metadata["Model"].GetString()}");
}
```

## Installation

```bash
dotnet add package FluentChunker
```

## Usage Examples

### Basic Fixed-Size Chunking

```csharp
var tokenizer = new MicrosoftMLTokenizerAdapter("cl100k_base");

var pipeline = ChunkingPipeline.CreateBuilder()
    .UseTokenizer(tokenizer)
    .UseSplitter(s => s
        .WithFixedSizeChunking()
        .WithMaxTokenSize(1024))
    .Build();

var chunks = pipeline.Chunk(text);
```

### Chunking with Overlap

```csharp
var tokenizer = new MicrosoftMLTokenizerAdapter("cl100k_base");

var pipeline = ChunkingPipeline.CreateBuilder()
    .UseTokenizer(tokenizer)
    .UseSplitter(s => s
        .WithFixedSizeChunking()
        .WithMaxTokenSize(512)
        .WithOverlap(128))
    .Build();

var chunks = pipeline.Chunk(text);
```

### Sentence-Aware Chunking

```csharp
var tokenizer = new MicrosoftMLTokenizerAdapter("cl100k_base");

var pipeline = ChunkingPipeline.CreateBuilder()
    .UseTokenizer(tokenizer)
    .UseSplitter(s => s
        .WithSentenceAwareChunking()
        .WithMaxTokenSize(512)
        .WithOverlap(50)
        .AddAbbreviations("Vgl.", "sog."))   // optional, additive to the built-in list
    .Build();

var chunks = pipeline.Chunk(document);
```

Chunks contain whole sentences and are verbatim slices of the source text: whitespace and paragraph breaks between packed sentences are preserved exactly, not re-joined or trimmed. Every chunk's `TokenCount` stays within the configured max at any max token size of 4 or above: a sentence that alone exceeds the budget is split into evenly-sized token windows as a fallback (with `OverlapCount = 0` on those windows). Fallback windows are *decoded from tokens* rather than sliced from the source, but every boundary lands on a cut that does not fall inside a character, so the windows reconstruct the sentence exactly. Do not trim them before joining.

`WithOverlap(n)` carries whole trailing sentences into the next chunk, as many as fit within `n` tokens. `OverlapCount` reports the tokens carried *into* a chunk (0 on the first chunk), the same semantics both strategies use.

**Language support:** works for any language using `. ! ?` as sentence terminators with a bicameral script (Latin, Greek, Cyrillic), which covers all European and North/South American languages. Uppercase detection is Unicode-aware, and `¿ ¡ « „` are recognized as valid sentence openers (Spanish/French/German). A built-in abbreviation guard covers common EN/DE/FR/ES abbreviations (`Dr`, `Mr`, `Nr`, `bzw`, `M`, `Sra`, etc.); extend it with `.AddAbbreviations(...)`; entries may include a trailing dot, and matching is case-insensitive.

**Known limitations:** German ordinal periods (e.g. "am 3. Oktober") are falsely split; no code disambiguates them from sentence ends. CJK terminators (`。`) aren't recognized; such text is treated as one long sentence and falls back to fixed-size-style token windowing.

### Recursive Chunking

```csharp
var tokenizer = new MicrosoftMLTokenizerAdapter("cl100k_base");

var pipeline = ChunkingPipeline.CreateBuilder()
    .UseTokenizer(tokenizer)
    .UseSplitter(s => s
        .WithRecursiveChunking()
        .WithMaxTokenSize(512)
        .WithOverlap(64)
        .AddAbbreviations("Vgl.", "sog."))   // optional, feeds the sentence tier
    .Build();

var chunks = pipeline.Chunk(document);
```

The document is split along a fixed hierarchy of structural separators: **paragraphs** (blank-line separated), then **lines**, then **sentences** (using the same abbreviation-aware detection as sentence-aware chunking), then **balanced token windows** as a last resort. Only a unit that *alone* exceeds the max token size descends to the next level; adjacent small units are packed together up to the budget. Chunks never merge across a descent boundary: a small paragraph before or after an oversized one always stays in its own chunk, so every chunk respects the largest structural boundary possible.

Chunks are verbatim slices of the source (interior line breaks preserved); only token-window fallback chunks are decoded from tokens, and those reconstruct the unit exactly because every boundary lands on a cut that does not fall inside a character, exactly like the sentence-aware fallback.

`WithOverlap(n)` carries whole trailing units (paragraphs, lines or sentences) into the next chunk, as many as fit within `n` tokens. Overlap applies *within* a packing run and **resets to zero at every structural seam** (before the first chunk inside an oversized unit, after the last one, and around token-window fallbacks), so carried context always comes from the same structural region. `OverlapCount` reports the tokens carried *into* a chunk (0 on the first chunk and wherever a seam resets it).

### Structure-Aware Chunking (Markdown)

```csharp
var tokenizer = new MicrosoftMLTokenizerAdapter("cl100k_base");

var pipeline = ChunkingPipeline.CreateBuilder()
    .UseTokenizer(tokenizer)
    .UseSplitter(s => s
        .WithStructureAwareChunking()
        .WithMaxTokenSize(512)
        .AddAbbreviations("Vgl.", "sog."))   // optional, feeds the sentence descent
    .Build();

var chunks = pipeline.Chunk(markdownDocument);

foreach (var chunk in chunks)
{
    Console.WriteLine($"[{chunk.Metadata["headingPath"].GetString()}] {chunk.Text[..Math.Min(60, chunk.Text.Length)]}");
}
```

Designed for Markdown content: every heading (ATX `#`..`######` or Setext underline) starts a new section, and **sections are hard boundaries**: a chunk never spans two sections, even small neighboring ones. Every chunk carries its heading breadcrumb in `Metadata["headingPath"]` (heading texts joined with `" > "`, empty for content before the first heading), a reserved key for this strategy. Code fences, tables, lists and quotes are atomic: never split while they fit the budget, so a fence containing blank lines stays whole where recursive chunking's paragraph tier would cut it. An oversized paragraph descends to whole sentences; oversized atomic blocks and single sentences fall back to balanced token windows (decoded from tokens, like the other strategies' fallbacks).

**No overlap by design:** structure is the sole boundary authority, so this strategy's builder path does not offer `WithOverlap` (misuse is a compile error) and `OverlapCount` is always `0`. Use recursive or sentence-aware chunking when overlap continuity matters.

**Plain text degrades gracefully** (Markdown is a superset of plain text, so nothing throws): the whole document becomes one heading-less section packed by paragraphs, with an empty `headingPath` on every chunk, which doubles as the "no structure found" signal. For plain-text corpora, `WithRecursiveChunking()` is the better choice; route by file type in your ingestion code, as the sample project does (`testfile.md` → structure-aware, `testfile.txt` → the other strategies).

### Adding Custom Metadata

```csharp
var tokenizer = new MicrosoftMLTokenizerAdapter("cl100k_base");

var pipeline = ChunkingPipeline.CreateBuilder()
    .UseTokenizer(tokenizer)
    .UseSplitter(s => s
        .WithFixedSizeChunking()
        .WithMaxTokenSize(512))
    .AddMetadata(meta =>
    {
        meta.Set("Source", "document.md");
        meta.Set("Version", "1.0");
    })
    .Build();

var chunks = pipeline.Chunk(largeDocument);

foreach (var chunk in chunks)
{
    Console.WriteLine($"Chunk {chunk.Index}: {chunk.TokenCount} tokens");
    Console.WriteLine($"  Source: {chunk.Metadata["Source"].GetString()}");
    Console.WriteLine($"  Text: {chunk.Text[..Math.Min(80, chunk.Text.Length)]}...");
}
```

### Per-Document Metadata (Reusing One Pipeline)

Pipeline-level metadata (`AddMetadata`) is constant for the whole run; document-level metadata is passed per `Chunk` call. On key collision, the document value wins.

```csharp
var tokenizer = new MicrosoftMLTokenizerAdapter("cl100k_base");

var pipeline = ChunkingPipeline.CreateBuilder()
    .UseTokenizer(tokenizer)
    .UseSplitter(s => s
        .WithFixedSizeChunking()
        .WithMaxTokenSize(512)
        .WithOverlap(50))
    .AddMetadata(meta => meta.Set("SourceType", "wiki"))   // run-constant
    .Build();                                                // built once

foreach (var doc in documents)
{
    var chunks = pipeline.Chunk(doc.Markdown, meta =>     // per document
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
var tokenizer = new MicrosoftMLTokenizerAdapter("cl100k_base"); // ships with FluentChunker
var pipeline = ChunkingPipeline.CreateBuilder()
    .UseTokenizer(tokenizer)
    .UseSplitter(s => s
        .WithFixedSizeChunking()
        .WithMaxTokenSize(1024))
    .Build();
```

Need a different tokenizer? Implement `ITokenizer` and pass it to `UseTokenizer` (see [Custom Tokenizers](#custom-tokenizers) below).

#### Choosing an encoding

**Pick the encoding of the model that consumes the chunks.** In a RAG pipeline that is usually two different models: you size chunks against the *embedding* model when indexing, and against the *chat* model when assembling a prompt from retrieved chunks. The counts are not interchangeable: the same text can differ by roughly 10% between encodings, in either direction, which is enough to push prose chunked to 8,000 tokens past the 8,191 token limit of `text-embedding-3-large`.

| Encoding | Use it when your chunks feed | Included? |
|---|---|---|
| `cl100k_base` | Embedding models: `text-embedding-3-small`, `text-embedding-3-large`, `text-embedding-ada-002`. Also legacy chat: GPT-4, GPT-3.5 | **Yes** |
| Every other encoding | Newer chat and reasoning models, and the legacy GPT-3 and Codex models | No, add the matching data package |

`cl100k_base` is the encoding FluentChunker ships with, and every example in this README uses it. Indexing documents for retrieval is the most common reason to reach for a chunker, and that means sizing against an embedding model.

Any other encoding needs its own `Microsoft.ML.Tokenizers.Data.*` package referenced in your project. Without it the constructor throws an `InvalidOperationException` that names the exact package to add, so the failure tells you how to fix it.

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
var pipeline = ChunkingPipeline.CreateBuilder()
    .UseTokenizer(tokenizer)
    .UseSplitter(s => s
        .WithFixedSizeChunking()
        .WithMaxTokenSize(512))
    .Build();
```

All three methods (`Encode`, `Decode`, `CountTokens`) are required by the `ITokenizer` interface.

## API Reference

### Building a pipeline

The builder is stepped: each method returns the next step, so the order below is enforced at compile time:

- `ChunkingPipeline.CreateBuilder()`: Start a new pipeline configuration
- `.UseTokenizer(ITokenizer tokenizer)`: Set the tokenizer for token counting
- `.UseSplitter(Action<Splitter.Builder> configure)`: Configure the chunking strategy
- `.AddMetadata(Action<MetadataBuilder> configure)` (optional): add pipeline-level metadata to all chunks (may be called multiple times; callbacks compose)
- `.Build()`: Create the pipeline

### Splitter.Builder (inside UseSplitter)

Also stepped, in this order:

- `.WithFixedSizeChunking()`: Select the fixed-size strategy
- `.WithSentenceAwareChunking()`: Select the sentence-aware strategy (peer of `WithFixedSizeChunking`)
- `.WithRecursiveChunking()`: Select the recursive strategy (paragraphs, then lines, then sentences, then token windows), descending only for oversized pieces (peer of the other two strategy methods)
- `.WithStructureAwareChunking()`: Select the structure-aware Markdown strategy (peer of the other three strategy methods); its follow-up steps offer `WithMaxTokenSize` and `AddAbbreviations` but deliberately no `WithOverlap`
- `.WithMaxTokenSize(int maxTokens)`: Maximum tokens per chunk (must be at least 4, the most tokens a single character can span, since a character is never split across chunks)
- `.WithOverlap(int overlap)` (optional): tokens shared between consecutive chunks (must be non-negative and smaller than the max token size; budgeted inside it, not added on top)
- `.AddAbbreviations(params string[] abbreviations)` (optional): additional abbreviations (beyond the built-in EN/DE/FR/ES set) that should not be treated as sentence endings; entries may include a trailing dot, matching is case-insensitive. May be called multiple times; entries accumulate rather than replace. Used by the strategies with a sentence tier (`WithSentenceAwareChunking()`, `WithRecursiveChunking()` and `WithStructureAwareChunking()`); has no effect when combined with `WithFixedSizeChunking()`.

### ChunkingPipeline

- `.Chunk(string document)`: Run the pipeline and return chunks
- `.Chunk(string document, Action<MetadataBuilder> configureMetadata)`: Run with additional document-level metadata; merged over the pipeline metadata, document values win on key collision

### Chunk

Each chunk contains:
- `Index`: Zero-based position in the chunk sequence (resets per `Chunk` call)
- `Text`: The actual chunk content
- `TokenCount`: Number of tokens in this chunk; never exceeds the max token size
- `OverlapCount`: Tokens carried into this chunk from the previous chunk (`0` on the first chunk)
- `Metadata`: Dictionary of custom key-value pairs as `JsonElement` (access with e.g. `chunk.Metadata["Source"].GetString()`)

### ITokenizer

Implement to use custom tokenization:
- `IReadOnlyList<int> Encode(string text)`: Convert text to token IDs
- `string Decode(IReadOnlyList<int> tokens)`: Convert token IDs back to text
- `int CountTokens(string text)`: Count the number of tokens in the text without materializing the token list

## Requirements

- .NET 8.0 or later (the package targets `net8.0` and `net10.0`)
- Microsoft.ML.Tokenizers 2.0.0 or later, with the `cl100k_base` data package (included as dependencies)
- Markdig 1.3.2 or later (included as dependency; used by structure-aware chunking)

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
