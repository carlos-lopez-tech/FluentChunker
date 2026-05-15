# FluentChunker

A .NET library for building fluent, composable text chunking pipelines. 
Split text into meaningful chunks for RAG, embeddings, search indexing, and other NLP workflows - with a clean, readable API.

## Features

- **Fluent API** — Build chunking pipelines with a chainable, expressive syntax
- **Multiple strategies** — Fixed-size, sentence-based, paragraph-based, recursive, and semantic chunking
- **Overlap control** — Configure token or character overlap between chunks for better context preservation
- **Customizable tokenizers** — Plug in your own tokenizer or use built-in character/word/token counting
- **Metadata tracking** — Each chunk carries source position, index, and custom metadata
- **Pipeline composition** — Chain multiple chunking and transformation steps together

## Quick Start

```csharp
using FluentChunker;

var pipeline = new ChunkingPipelineBuilder()
    .UseTokenizer(new SharpTokenizer("o200k_base"))
    .BySentence(maxTokens: 512, overlap: 50)
    .EnrichFromContent(enricher =>
    {
        enricher.InheritMarkdownHeadings();
    })
    .WithMetadata(meta =>
    {
        meta.Set("Source", "readme.md");
        meta.Set("Author", "John Doe");
    })
    .Build();

var chunks = pipeline.Execute(document);

foreach (var chunk in chunks)
{
    Console.WriteLine($"[{chunk.Index}] {chunk.Text} ({chunk.TokenCount} tokens)");
    Console.WriteLine($"  Heading: {chunk.Metadata["Heading"]}");
}
```

## Installation

```bash
dotnet add package FluentChunker
```

## Usage Examples

### Fixed-Size Chunking

```csharp
var pipeline = new ChunkingPipelineBuilder()
    .UseTokenizer(new SharpTokenizer("o200k_base"))
    .ByFixedSize(maxTokens: 100)
    .Build();

var chunks = pipeline.Execute(document);
```

### Recursive Chunking

```csharp
var pipeline = new ChunkingPipelineBuilder()
    .UseTokenizer(new SharpTokenizer("o200k_base"))
    .Recursive(options => options
        .SplitOn("\n\n", "\n", ". ", " ")
        .WithMaxTokens(256))
    .Build();

var chunks = pipeline.Execute(document);
```

### Pipeline with Content Enrichment

```csharp
var pipeline = new ChunkingPipelineBuilder()
    .UseTokenizer(new SharpTokenizer("o200k_base"))
    .ByParagraph(maxTokens: 512, overlap: 50)
    .EnrichFromContent(enricher =>
    {
        enricher.InheritMarkdownHeadings();
    })
    .WithMetadata(meta =>
    {
        meta.Set("Source", "readme.md");
        meta.Set("Date", DateTime.UtcNow.ToString("o"));
    })
    .Build();

var chunks = pipeline.Execute(document);
```

## Requirements

- .NET 10.0 or later

## License

MIT
