# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.0.0] - 2026-08-02

### Breaking

- **The synchronous chunking API is removed.** `ChunkingPipeline.Chunk(...)` (both overloads) and
  `IChunker.Chunk(...)` are gone; `ChunkAsync(...)` replaces them. Migration is mechanical:

  ```csharp
  // 1.x
  var chunks = pipeline.Chunk(document);

  // 2.0 — stream
  await foreach (var chunk in pipeline.ChunkAsync(document)) { /* ... */ }

  // 2.0 — batch
  var chunks = await pipeline.ChunkAsync(document).ToListAsync();
  ```

  `ToListAsync` is in the box on .NET 10; on .NET 8 add the official `System.Linq.AsyncEnumerable`
  package. Async-only is the ecosystem contract for chunking (Microsoft.Extensions.DataIngestion's
  `IngestionChunker<T>` has the same shape), it is what the coming enricher seam and semantic
  chunking need, and no released FluentChunker will ever have carried a half-async surface.

### Added

- **Streaming output with cancellation.** `ChunkAsync` returns `IAsyncEnumerable<Chunk>`: chunks are
  produced lazily, the first is available before the last is cut, and the `CancellationToken` is
  observed before every chunk (a pre-cancelled token throws before the first; `WithCancellation`
  composes, and the suite pins that composition at both the chunker and the pipeline layer).

  **Fixed-size chunking observes the token while it tokenizes, not only afterwards.** Every strategy
  tokenizes, splits or parses the whole document before it can cut the first chunk, and that pass
  used to be one opaque call no token could reach. Fixed-size chunking is the case where it is the
  entire wait, because unlike the other strategies it cannot place a single window until the whole
  document is encoded. It now encodes in bounded batches and checks the token between them, which
  cuts worst-case latency there from the full encode to roughly 7 ms, the cost of one batch. Because
  the batch is bounded by token count rather than by document size, that figure does not grow with
  the document: on a 200 MB document the old behaviour waited seconds. Chunk boundaries are
  unchanged, because the tokenizer
  chooses where each batch ends and reports how far it reached, so resuming there reproduces a
  whole-document encode exactly, asserted over Latin and over mixed CJK, Arabic, Devanagari, Greek,
  Cyrillic and emoji text. Throughput is unchanged too, at 0.98x of the single call across a
  nine-round median. Custom `ITokenizer` implementations keep the single call they always had.

  **The other three strategies observe the token while they size their units.** Sentence-aware,
  recursive and structure-aware chunking each find their boundaries and then measure every unit
  against the tokenizer before the first chunk can be cut, and that sizing is the bulk of the wait:
  on a 2 MB document, 71 ms of sentence sizing against a 5 ms boundary scan, and 30 ms of paragraph
  sizing against a 1 ms scan. The token is now checked per unit, so cancelling during sizing stops at
  the current unit instead of running through the rest of the document. Chunk boundaries are
  untouched; the checks are a volatile read per unit.

  **What cancellation still cannot interrupt.** Structure-aware chunking parses the Markdown in one
  Markdig call before any of its own work begins, and that call takes no token and cannot report
  progress, so it remains the one place a cancelled token waits — the largest part of the wait for
  that strategy, and the only one that grows with the document. The two boundary scans are single
  passes over the text and stay in single-digit milliseconds. The new **Streaming and Cancellation**
  section carries the per-strategy table, the `WithCancellation` form for a layer handed nothing but
  a stream, and the warning that `MoveNextAsync` completes synchronously, so a large document blocks
  the calling thread unless the enumeration is wrapped in `Task.Run`.

- **Chunk documents larger than a .NET string.** `ChunkingPipeline.ChunkAsync(TextReader, ...)`
  streams fixed-size, sentence-aware and recursive chunking through one pooled fixed-size buffer
  (262,144 chars), so memory stays flat regardless of input size — no hidden `ReadToEnd()`.
  The token budget, the multilingual sentence machinery and exact source offsets all hold;
  `SourceStart` values are absolute `long` positions in the stream.

  ```csharp
  using var reader = File.OpenText("very-large-corpus.txt");

  await foreach (var chunk in pipeline.ChunkAsync(reader))
  {
      // first chunks arrive while the rest of the file is still unread
  }
  ```

- **`IStreamingChunker`** — the capability interface behind it. Custom strategies opt into
  streaming by implementing it. Structure-aware chunking deliberately does not (Markdown structure
  needs the whole document in memory); handing its pipeline a reader throws an
  `InvalidOperationException` that says so and names the strategies that stream.
- **Seam semantics, documented:** a chunk never spans an internal buffer seam and overlap resets
  there; a document that fits one buffer produces output identical to `ChunkAsync(string)`.
  Boundary-free stretches longer than the buffer (a sentence or paragraph that never ends) degrade
  to hard cuts instead of unbounded buffering, keeping every chunk within the token budget.
- **`Chunk.SliceOf(document)`** — the verification idiom replacing `document.Substring(...)`:
  returns the verbatim source slice (equal to `Text` for every built-in strategy) and throws
  `InvalidOperationException` when the chunk does not track offsets.

### Changed

- **`Chunk.SourceStart` is now a `long`** (was `int`). Positions in streamed sources
  (`ChunkAsync(TextReader, ...)`, above) can exceed `int.MaxValue`. In-memory documents are
  unaffected, and NUnit-style numeric assertions compare across `int`/`long` unchanged.
- **The tokenizer round-trip guard surfaces during enumeration.** The `InvalidOperationException`
  for length-changing decode drift now throws at the `await foreach`/`ToListAsync` consuming the
  stream, not at the `ChunkAsync` call, because chunks are produced lazily.

## [1.3.0] -2026-07-31

### Added

- **Chunk against the tokenizer your embedding model actually uses.** `MicrosoftMLTokenizerAdapter`
  now accepts any `Microsoft.ML.Tokenizers.Tokenizer` instance, not only a tiktoken encoding name.
  Sizing chunks against the model that consumes them is the whole point of counting tokens rather
  than characters, and until now only OpenAI models could be sized accurately. This opens the
  tokenizers behind the open embedding models: **BERT and WordPiece** for BGE and E5,
  **SentencePiece** for Nomic and Qwen, and the **Llama** family.

  ```csharp
  using Microsoft.ML.Tokenizers;

  var bert = BertTokenizer.Create("vocab.txt", new BertOptions { LowerCaseBeforeTokenization = false });

  var pipeline = ChunkingPipeline.CreateBuilder()
      .UseTokenizer(new MicrosoftMLTokenizerAdapter(bert))
      .UseSplitter(s => s
          .WithSentenceAwareChunking()
          .WithMaxTokenSize(512))
      .Build();
  ```

  The existing `MicrosoftMLTokenizerAdapter(string)` constructor is unchanged, and `cl100k_base`
  remains the only encoding shipped in the box.

  **One caller contract comes with it:** the tokenizer's `Decode` must reproduce the encoded text
  exactly, because safe cut detection and chunk extents are both derived by decoding token runs. BERT
  lowercases by default and any out-of-vocabulary word decodes as `[UNK]`, either of which breaks it.
  Run the round-trip check from the README against your own corpus before indexing; the guard under
  **Fixed** catches the corrupting cases at chunking time if you do not. The shipped `cl100k_base`
  path round-trips exactly and the suite now asserts that directly, across Latin, Cyrillic, Greek,
  CJK, Indic, Arabic, Hebrew, Thai and emoji text.

### Changed

- **Non-Latin scripts cost 2x to 5x more tokens, and the README now says so.** 1.1.0 made sentence
  detection work for Chinese, Japanese, Hindi, Arabic, Hebrew, Thai and more, but a `maxTokens` value
  chosen against English is too small for those scripts, so sentences exceed the budget on their own
  and fall through to the token-window fallback. The output looks like a detection bug and is not
  one. The **Tokenizers** section now carries the measured per-script multipliers, explains the
  fallback, and says to scale the budget. Greek and Russian are in the table too, at 3.9x and 2.1x,
  because both are easy to assume are Latin-priced. `WithMaxTokenSize` points at the section.

### Fixed

- **A tokenizer that did not round-trip silently dropped the end of every document.** Chunk positions
  are derived by decoding token runs, so a tokenizer whose `Decode` returned fewer characters than it
  was given made the character extents fall progressively short of the source while the loop still
  consumed every token. The tail was never emitted, chunks ran far past the token budget, and nothing
  threw or warned. Measured on a `BertTokenizer` with an incomplete vocabulary: a 185-character
  document at an 8-token budget lost its last 4 characters and produced a 180-character chunk. **Every
  chunking strategy was affected, with and without overlap.** The failure was undetectable from the
  output, because `document.Substring(chunk.SourceStart, chunk.SourceLength) == chunk.Text` held for
  every chunk throughout: the offsets stayed self-consistent while describing a shrinking picture of
  the document. Chunking now throws `InvalidOperationException` naming the tokenizer as the cause when
  the decoded length of a unit does not match its source.

  **Drift in the other direction crashed with an unhelpful exception.** When a tokenizer decodes to
  *more* characters than it was given, the chunk extents run past the end of the document and the
  failure surfaced as `ArgumentOutOfRangeException` from `String.Substring`, naming nothing the
  reader could act on. This is reachable at defaults, not in theory: `BertTokenizer` inserts a
  separator between CJK characters unless `IndividuallyTokenizeCjk` is set to `false`. Measured on a
  real `BertTokenizer` over an 18-character Chinese document, decode returned 35 characters and all
  four strategies threw. Both drift directions now produce the same actionable exception, and the
  message names `IndividuallyTokenizeCjk` among the causes, since it is the one that is a settable
  option rather than a property of your corpus.

  **A pipeline that appeared to work may now throw.** That reveals a pre-existing defect rather than
  causing a new one: the previous output was silently truncated, and nothing can legitimately depend
  on that. The check is a net-length comparison per encoded unit, so a normalisation that preserves
  length, such as BERT lowercasing plain ASCII, keeps producing correct chunks and deliberately does
  not throw. The round-trip check in the README remains the stronger, earlier discipline.

## [1.2.0] - 2026-07-27

### Breaking

- **`Chunk.Metadata` is now an immutable `ChunkMetadata` instead of `Dictionary<string, JsonElement>`.**
  A breaking change ships in a minor version here as a deliberate, one-time exception: the library
  is days old, and this is the last moment the fix is nearly free. Reading is unchanged:
  `ChunkMetadata` implements `IReadOnlyDictionary<string, JsonElement>`, so indexers, `TryGetValue`,
  `Count`, enumeration and LINQ all keep compiling. The one migration is in-place writes:

  ```csharp
  // before
  chunk.Metadata["Reviewed"] = JsonSerializer.SerializeToElement(true);
  // after
  chunk = chunk with { Metadata = chunk.Metadata.With("Reviewed", true) };
  ```
- **`ChunkingPipeline` is a `sealed class` instead of a `record`.** Its compiler-generated value
  equality was reference-broken and meaningless for a configuration object; `with` expressions on a
  pipeline no longer compile. Build a new pipeline with `CreateBuilder()` instead.

### Added

- **Chunks are value objects, for real.** `Chunk` is a `record`, which always promised value
  equality, but the mutable metadata dictionary compared by reference, so no two chunks were ever
  equal, silently breaking `Distinct()`, `HashSet<Chunk>`, chunk-keyed caches and test assertions.
  With metadata immutable, the compiler-generated equality is correct: deduplication before
  embedding, caching by chunk and whole-chunk assertions now work. Metadata participates in
  equality and hashing, so same-text chunks tagged with different `Source` values stay distinct.
- **`ChunkMetadata`**: immutable, value-equal metadata map. `ChunkMetadata.Empty` is a shared
  singleton; `With<T>(key, value)` returns a new instance with an entry added or replaced. Keys are
  ordinal; values compare by raw JSON text (stricter than semantic equality, identical on both
  target frameworks; documented on the type).

### Changed

- **Metadata stamping allocates less.** Chunks without metadata share one `Empty` instance instead
  of allocating a dictionary each; the pipeline builds the merged pipeline+document metadata once
  per call and shares it across chunks; structure-aware chunking builds one metadata instance per
  section instead of one dictionary per chunk. `with` expressions share the immutable instance
  instead of aliasing a mutable one, so the copy-mutates-the-original defect is gone by construction.

## [1.1.0] - 2026-07-26

### Added

- **`Chunk.SourceStart` and `Chunk.SourceLength`.** Every chunk now reports where it came from, so
  `document.Substring(chunk.SourceStart, chunk.SourceLength)` returns the chunk text exactly. This is
  what citation, highlight-in-source and re-ranking against surrounding text need, and searching the
  document for the chunk text was never a reliable substitute once overlap duplicated content. Both
  default to `-1`, meaning "not tracked", so an `IChunker` implemented outside this library keeps
  working unchanged. Every built-in strategy reports real values, including the token-window
  fallbacks. With overlap configured, consecutive chunks deliberately report overlapping ranges.
- **Sentence boundaries in non-Latin scripts.** Chinese, Japanese, Hindi, Urdu, Arabic, Hebrew and Thai
  text produced no sentence boundaries at all, so it bypassed sentence-aware, recursive and
  structure-aware chunking entirely and was cut on raw token counts. Two rules caused it: only `.!?`
  counted as terminators, and a boundary additionally required the following character to be
  uppercase, which caseless scripts can never satisfy. Terminators now include `。！？．` as
  unconditional boundaries for scripts written without inter-sentence spaces, and `؟ ۔ । ॥` alongside
  `.!?` for scripts that do use them, and a caseless letter counts as a sentence start. This also
  fixes a single non-Latin passage inside an otherwise English document, which used to defeat the
  splitter at that point. `AddAbbreviations` is unchanged: only the abbreviation guard is
  language-specific, and it never applied to any of the added terminators.

### Changed

- **Every chunk is now a verbatim slice of the source document.** The token-window fallbacks used to
  rebuild text from token IDs; they now select it by character offset. Text content is unchanged,
  since 1.0.2 already made those windows reconstruct the source exactly, but the caveat that fallback
  chunks are decoded rather than sliced is gone for good.

## [1.0.2] - 2026-07-26

### Changed

- **A max token size below 4 is now rejected** with `ArgumentOutOfRangeException` instead of being
  accepted and silently overrun. A single character can span up to 4 tokens under `cl100k_base` and
  is never split across chunks, so a smaller budget could not be honoured. The 4-token bound was
  verified by encoding all 1,112,032 Unicode characters: none needs more, with or without a preceding
  space. Together with the packing fix below, **no chunk can now exceed the max token size, for any
  input, under any strategy.**

### Fixed

- **Token windows no longer split a character in half.** Fixed-size chunking, and the fallback that
  every strategy uses for a unit it cannot split structurally, cut the token sequence on arithmetic
  alone. Because tiktoken encodings are byte-level, a cut could land inside a character and the chunk
  came back with U+FFFD in place of it. This affected every document containing emoji, Chinese,
  Japanese, Arabic, Hebrew, Thai or Devanagari text: a 40-line emoji document at a 20-token budget
  produced 45 chunks, 32 of them corrupted. Boundaries now move onto the nearest legal cut, so chunks
  reconstruct the source exactly.
- **`OverlapCount` on fixed-size chunks is now measured rather than assumed.** It reported the
  requested overlap; it now reports the tokens the chunk actually received, which can differ when a
  boundary moves to avoid splitting a character.
- **A packed chunk could exceed the max token size.** Sentence-aware, recursive and structure-aware
  chunking measured each unit together with the whitespace in front of it, since a chunk is sliced as
  one continuous span. The unit that *begins* a chunk is sliced from its own start instead and loses
  that whitespace, and in a byte-level BPE a leading space makes the following word cheaper, not
  dearer: `" Demonstrated"` costs 2 tokens where `"Demonstrated"` costs 3. Dropping it therefore
  raised the count the packer had already budgeted for. Each unit is now measured in both positions
  and budgeted at the larger of the two. The overrun needed the unit's cost to coincide exactly with
  the budget, so it was rare in practice, but it was unbounded in principle.

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

[Unreleased]: https://github.com/carlos-lopez-tech/FluentChunker/compare/v2.0.0...HEAD
[2.0.0]: https://github.com/carlos-lopez-tech/FluentChunker/compare/v1.3.0...v2.0.0
[1.3.0]: https://github.com/carlos-lopez-tech/FluentChunker/compare/v1.2.0...v1.3.0
[1.2.0]: https://github.com/carlos-lopez-tech/FluentChunker/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/carlos-lopez-tech/FluentChunker/compare/v1.0.2...v1.1.0
[1.0.2]: https://github.com/carlos-lopez-tech/FluentChunker/compare/v1.0.0...v1.0.2
[1.0.0]: https://github.com/carlos-lopez-tech/FluentChunker/releases/tag/v1.0.0
