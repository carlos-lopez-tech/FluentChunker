# FluentChunker

A .NET library for building fluent, composable text chunking pipelines. 
Split text into meaningful chunks for RAG (Retrieval-Augmented Generation), embeddings, search indexing, and other NLP workflows with a clean, readable API.
- **Multiple strategies** — Fixed-size, sentence-based, paragraph-based, recursive, and semantic chunking

## Features

- **Fluent Builder API** — Chain-based pipeline configuration with clear, expressive syntax
- **Token-aware chunking** — Split text by token count with accurate token counting
- **Customizable tokenizers** — Plug in your own tokenizer implementation or use built-in Microsoft.ML.Tokenizers, TiktokenSharp, SharpToken integration
- **Multiple strategies** — Fixed-size, sentence-based, paragraph-based, recursive, and semantic chunking
- **Overlap support** — Configure token overlap between chunks to preserve context
- **Metadata enrichment** — Attach custom metadata to each chunk for tracking and context
- **Pipeline composition** — Execute chunking transformations in a single call

## Quick Start

```csharp
using FluentChunker;

// Create tokenizer and fluent pipeline
var tokenizer = new SharpTokenizer("o200k_base");

var pipeline = new ChunkingPipeline.Builder()
    .UseTokenizer(tokenizer)
    .ByFixedSize(maxTokens: 512, overlap: 50)
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
    Console.WriteLine($"  Source: {chunk.Metadata.GetValueOrDefault("Source")}");
    Console.WriteLine($"  Model: {chunk.Metadata.GetValueOrDefault("Model")}");
}
```

## Installation

```bash
dotnet add package FluentChunker
```

## Usage Examples

### Basic Fixed-Size Chunking

```csharp
var tokenizer = new SharpTokenizer("o200k_base");

var pipeline = new ChunkingPipeline.Builder()
    .UseTokenizer(tokenizer)
    .ByFixedSize(maxTokens: 1024)
    .Build();

var chunks = pipeline.Execute(text);
```

### Chunking with Overlap

```csharp
var tokenizer = new SharpTokenizer("o200k_base");

var pipeline = new ChunkingPipeline.Builder()
    .UseTokenizer(tokenizer)
    .ByFixedSize(maxTokens: 512, overlap: 128)
    .Build();

var chunks = pipeline.Execute(text);
```

### Adding Custom Metadata

```csharp
var tokenizer = new SharpTokenizer("o200k_base");

var pipeline = new ChunkingPipeline.Builder()
    .UseTokenizer(tokenizer)
    .ByFixedSize(maxTokens: 512)
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
    Console.WriteLine($"  Source: {chunk.Metadata.GetValueOrDefault("Source")}");
    Console.WriteLine($"  Text: {chunk.Text[..Math.Min(80, chunk.Text.Length)]}...");
}
```

## Tokenizers

### SharpToken Integration

FluentChunker includes built-in support for [SharpToken](https://github.com/dmitry-brazhenko/SharpToken), enabling accurate token counting for OpenAI models:

```csharp
var tokenizer = new SharpTokenizer("o200k_base"); // GPT-4o tokenizer
var pipeline = new ChunkingPipelineBuilder()
    .UseTokenizer(tokenizer)
    .ByFixedSize(maxTokens: 1024)
    .Build();
```

Supported encoding models include `cl100k_base` (GPT-3.5, GPT-4), `o200k_base` (GPT-4o), and others.

### Custom Tokenizers

Implement `ITokenizer` to use your own tokenization logic:

```csharp
public class CustomTokenizer : ITokenizer
{
    public IReadOnlyList<int> Encode(string text) => /* your tokenization */;

    public string Decode(int[] tokens) => /* your detokenization */;

    public int CountTokens(string text)
    {
        // Efficiently count tokens without materializing the full list
        return Encode(text).Count;
    }
}

var tokenizer = new CustomTokenizer();
var pipeline = new ChunkingPipeline.Builder()
    .UseTokenizer(tokenizer)
    .ByFixedSize(maxTokens: 512)
    .Build();
```

All three methods (`CountTokens`, `Encode`, `Decode`) are required by the `ITokenizer` interface.

## API Reference

### ChunkingPipeline.Builder

- `.UseTokenizer(ITokenizer tokenizer)` — Set the tokenizer for token counting
- `.ByFixedSize(int maxTokens, int overlap = 0)` — Configure fixed-size chunking strategy
- `.WithMetadata(Action<MetadataBuilder> configure)` — Add metadata to all chunks
- `.Build()` — Create the pipeline

### ChunkingPipeline

- `.Execute(string document)` — Execute the pipeline and return chunks

### ChunkDto

Each chunk contains:
- `Index` — Zero-based position in the chunk sequence
- `Text` — The actual chunk content
- `TokenCount` — Number of tokens in this chunk
- `Metadata` — Dictionary of custom key-value pairs (access with `GetValueOrDefault(key)`)

### ITokenizer

Implement to use custom tokenization:
- `IReadOnlyList<int> Encode(string text)` — Convert text to token IDs
- `string Decode(int[] tokens)` — Convert token IDs back to text
- `int CountTokens(string text)` — Count the number of tokens in the text without materializing the token list

## Requirements

- .NET 10.0 or later
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
