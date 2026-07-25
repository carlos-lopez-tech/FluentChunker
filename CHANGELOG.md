# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0] - 2026-07-25

First public release.

### Added

- **Fluent pipeline builder.** `ChunkingPipeline.CreateBuilder()` walks through the required
  steps in order (tokenizer, then splitter, then optional metadata), so invalid configurations are
  not expressible.
- **Fixed-size chunking.** Splits into fixed token windows, ignoring sentence boundaries.
- **Sentence-aware chunking.** Greedily packs whole sentences into each chunk. A sentence that alone
  exceeds the budget falls back to evenly balanced token windows. Ships a built-in EN/DE/FR/ES
  abbreviation set so a trailing period does not falsely end a sentence, extendable via
  `AddAbbreviations`.
- **Recursive chunking.** Descends a fixed hierarchy of separators (paragraphs, then lines, then
  sentences), going a level deeper only for pieces that exceed the budget. Chunks never merge across
  a descent boundary, and overlap resets there.
- **Structure-aware chunking for Markdown.** Splits at heading boundaries and stamps each chunk with
  its heading breadcrumb under the reserved `headingPath` metadata key. Code fences, tables, lists
  and quotes stay atomic while they fit the budget. Text without Markdown structure degrades to
  paragraph packing. Applies no overlap by design.
- **Configurable overlap.** `WithOverlap` carries context from the end of one chunk into the start of
  the next. Overlap is budgeted inside the max token size, so chunks never exceed it, and each
  chunk's `OverlapCount` reports the tokens it received.
- **Two metadata scopes.** Pipeline-level metadata (`AddMetadata`, constant for every run) and
  document-level metadata (the `Chunk` overload, per call). Document values win on key collisions
  and never leak into later calls.
- **Tokenizer abstraction.** `ITokenizer` is the extension point for custom tokenization;
  `MicrosoftMLTokenizerAdapter` provides accurate OpenAI-compatible counting via
  Microsoft.ML.Tokenizers.
- **Reusable pipelines.** Build once and execute against many documents.
- XML documentation shipped in the package, so the full API is available in IntelliSense.
- Source Link, deterministic builds, and a symbol package for step-into debugging.

### Notes

- Targets `net8.0` and `net10.0`.
- Only the `cl100k_base` tokenizer encoding ships, covering every current OpenAI embedding model.
  Other encodings, including `o200k_base` for GPT-5, the o-series and GPT-4o, require the matching
  `Microsoft.ML.Tokenizers.Data.*` package in your own project.
- Size chunks with the encoding of the model that consumes them. The same text can differ by roughly
  10% between encodings, so the wrong one silently misreports the budget.

[Unreleased]: https://github.com/carlos-lopez-tech/FluentChunker/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/carlos-lopez-tech/FluentChunker/releases/tag/v1.0.0
