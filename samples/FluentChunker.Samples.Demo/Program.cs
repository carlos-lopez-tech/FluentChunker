using System.Text;
using FluentChunker.Contracts;
using FluentChunker.Tokenizers;

namespace FluentChunker.Samples.Demo
{
    internal static class Program
    {
        static void Main()
        {
            // Without this the console renders the non-Latin examples as question marks on Windows.
            Console.OutputEncoding = Encoding.UTF8;

            var content = File.ReadAllText(SampleFile("testfile.txt"));

            // The encoding FluentChunker ships, used by the OpenAI embedding models.
            // Any other encoding needs its own Microsoft.ML.Tokenizers.Data package referenced first.
            const string EncodingName = "cl100k_base";

            var microsoftMLTokenizer = new MicrosoftMLTokenizerAdapter(EncodingName);

            // Example 1: Create Pipeline by using Fixed-Size Chunking.
            var pipeline1 = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithFixedSizeChunking()
                    .WithMaxTokenSize(512)
                    .WithOverlap(50))
                .AddMetadata(meta => meta.Set("SourceType", "sample-files"))
                .Build();

            var chunks1 = pipeline1.Chunk(content, meta =>
            {
                meta.Set("Source", "testfile.txt");
                meta.Set("Title", "Demo Test File");
                meta.Set("CreatedAt", DateTime.UtcNow);
            });

            Console.WriteLine("Each chunk reports the source range it was sliced from, as an inclusive start-end pair with a character count.");
            Console.WriteLine("The note printed between two chunks states how their ranges relate:");
            Console.WriteLine("  OVERLAP   - the following range begins before the preceding one ends, by the configured overlap");
            Console.WriteLine("  ADJACENT  - the following range begins at the next index, with no characters in between");
            Console.WriteLine("  SEPARATOR - the characters between the two ranges delimited the units and belong to neither chunk");
            Console.WriteLine("In every case a chunk's text equals document.Substring(SourceStart, SourceLength).\n\n");

            Console.WriteLine(">>> Example 1: Fixed-Size Chunking <<<\n");
            PrintChunks(chunks1, content);


            // Example 2: Create Pipeline by using Sentence-Aware Chunking.
            var pipeline2 = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithSentenceAwareChunking()
                    .WithMaxTokenSize(512)
                    .WithOverlap(50))
                .AddMetadata(meta => meta.Set("SourceType", "sample-files"))
                .Build();

            var chunks2 = pipeline2.Chunk(content, meta =>
            {
                meta.Set("Source", "testfile.txt");
                meta.Set("Title", "Demo Test File");
                meta.Set("CreatedAt", DateTime.UtcNow);
            });

            Console.WriteLine(">>> Example 2: Sentence-Aware Chunking <<<\n");
            PrintChunks(chunks2, content);


            // Example 3: Create Pipeline by using Sentence-Aware Chunking with custom abbreviations.
            // "Fig.", "No." and "approx." are not in the built-in EN/DE/FR/ES set,
            // so without AddAbbreviations the splitter would falsely end a sentence after each of them.
            // Trailing dots are optional and matching is case-insensitive.
            var pipeline3 = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithSentenceAwareChunking()
                    .WithMaxTokenSize(32)
                    .WithOverlap(8)
                    .AddAbbreviations("Fig.", "No.", "approx."))
                .AddMetadata(meta => meta.Set("SourceType", "inline-sample"))
                .Build();

            var abbreviationText =
                "The results are shown in Fig. 4 of the report. " +
                "Order No. 1234 took approx. three weeks to arrive. " +
                "A follow-up shipment is expected next month.";

            var chunks3 = pipeline3.Chunk(abbreviationText, meta =>
            {
                meta.Set("Source", "inline");
                meta.Set("CreatedAt", DateTime.UtcNow);
            });

            Console.WriteLine(">>> Example 3: Sentence-Aware Chunking with custom abbreviations <<<\n");
            PrintChunks(chunks3, abbreviationText);


            // Example 4: Create Pipeline by using Recursive Chunking.
            // Splits along paragraphs -> lines -> sentences -> token windows,
            // descending only for pieces that exceed the budget,
            // chunks never merge across a structural seam and overlap resets to zero there.
            var pipeline4 = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithRecursiveChunking()
                    .WithMaxTokenSize(512)
                    .WithOverlap(50))
                .AddMetadata(meta => meta.Set("SourceType", "sample-files"))
                .Build();

            var chunks4 = pipeline4.Chunk(content, meta =>
            {
                meta.Set("Source", "testfile.txt");
                meta.Set("Title", "Demo Test File");
                meta.Set("CreatedAt", DateTime.UtcNow);
            });

            Console.WriteLine(">>> Example 4: Recursive Chunking <<<\n");
            PrintChunks(chunks4, content);


            // Example 5: Recursive Chunking descending into an oversized paragraph.
            // A tight 30-token budget over the same archive/retrieval corpus: the short title paragraph fits and stays its own chunk (strict seam, never merged with its oversized neighbor),
            // while the following paragraph alone blows the budget,
            // so the chunker descends paragraph -> lines -> sentences and packs whole sentences up to the budget (each sentence exceeds the 10-token overlap,
            // so no whole sentence is carried and OverlapCount stays 0).
            var pipeline5 = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithRecursiveChunking()
                    .WithMaxTokenSize(30)
                    .WithOverlap(10))
                .AddMetadata(meta => meta.Set("SourceType", "inline-sample"))
                .Build();

            var descentText =
                "Chunking Test Document\n\n" +
                "Another participant demonstrated a visualization showing how overlapping chunks " +
                "sometimes preserved context more effectively than fixed windows without overlap. " +
                "Several researchers compared retrieval results generated from synthetic benchmark " +
                "documents against outputs produced from real company reports and customer support transcripts.";

            var chunks5 = pipeline5.Chunk(descentText, meta => meta.Set("Source", "inline"));

            Console.WriteLine(">>> Example 5: Recursive Chunking (descent into an oversized paragraph) <<<\n");
            PrintChunks(chunks5, descentText);


            // Example 6: Structure-Aware Chunking over a Markdown document.
            // Headings are hard boundaries: a chunk never spans two sections,
            // and every chunk carries its heading path in Metadata["headingPath"].
            // Code fences, tables and lists stay whole while they fit, only an oversized paragraph descends to sentences.
            // This strategy has no overlap by design.
            var markdownContent = File.ReadAllText(SampleFile("testfile.md"));

            var pipeline6 = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithStructureAwareChunking()
                    .WithMaxTokenSize(512))
                .AddMetadata(meta => meta.Set("SourceType", "sample-files"))
                .Build();

            var chunks6 = pipeline6.Chunk(markdownContent, meta =>
            {
                meta.Set("Source", "testfile.md");
                meta.Set("Title", "Demo Test File (Markdown)");
                meta.Set("CreatedAt", DateTime.UtcNow);
            });

            Console.WriteLine(">>> Example 6: Structure-Aware Chunking (Markdown sections) <<<\n");
            PrintChunks(chunks6, markdownContent);


            // Example 7: Sentence-Aware Chunking over a document that mixes writing systems.
            // Nothing here is configured for language: the terminators of each script are recognised out of the box,
            // including the fullwidth forms, which end a sentence without the trailing space Latin punctuation requires.
            // Devanagari costs far more tokens per character than Latin under cl100k_base, so the budget has to clear 23 tokens for the Hindi sentence alone.
            var pipeline7 = ChunkingPipeline.CreateBuilder()
                .UseTokenizer(microsoftMLTokenizer)
                .UseSplitter(s => s
                    .WithSentenceAwareChunking()
                    .WithMaxTokenSize(30))
                .AddMetadata(meta => meta.Set("SourceType", "inline-sample"))
                .Build();

            var multilingualText =
                "An English sentence opens the document. " +
                "这是第一句中文。这是第二句中文。" +
                "Then English resumes for a sentence. " +
                "هذا سؤال؟ نعم هذا صحيح. " +
                "यह एक हिंदी वाक्य है। " +
                "And a final English sentence closes it.";

            var chunks7 = pipeline7.Chunk(multilingualText, meta => meta.Set("Source", "inline"));

            Console.WriteLine(">>> Example 7: Sentence-Aware Chunking across writing systems <<<\n");
            PrintChunks(chunks7, multilingualText);

            if (!Console.IsInputRedirected)
            {
                Console.ReadKey();
            }
        }

        private static string SampleFile(string fileName) =>
            Path.Combine(AppContext.BaseDirectory, "testfiles", fileName);

        private static void PrintChunks(IReadOnlyList<Chunk> chunks, string document)
        {
            for (var i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];

                // The end is the last index the chunk actually covers, so adjacent chunks read as 55 then 56 rather than repeating a boundary number.
                var sourceEnd = chunk.SourceStart + chunk.SourceLength - 1;
                Console.WriteLine($"Index: [{chunk.Index}] ({chunk.TokenCount} tokens) ({chunk.OverlapCount} overlap) (source {chunk.SourceStart}-{sourceEnd}, {chunk.SourceLength} chars)");
                foreach (var kvp in chunk.Metadata)
                {
                    Console.WriteLine($"  {kvp.Key}: {kvp.Value}");
                }
                Console.WriteLine($"  Text:  {chunk.Text}");

                Console.WriteLine(i + 1 < chunks.Count
                    ? DescribeSeam(chunk, chunks[i + 1], document)
                    : string.Concat(Enumerable.Repeat("- ", 50)));
                Console.WriteLine("\n");
            }
            Console.WriteLine("\n");
        }

        // Consecutive chunk ranges relate in one of three ways, and stating which one applies keeps the reported offsets unambiguous.
        // The blank line before the note separates it from the rule closing the preceding chunk, so the note reads as belonging to the seam rather than to that chunk.
        private static string DescribeSeam(Chunk left, Chunk right, string document)
        {
            var separator = string.Concat(Enumerable.Repeat("- ", 50));

            if (left.SourceStart < 0 || right.SourceStart < 0)
                return separator;

            var leftEnd = left.SourceStart + left.SourceLength;

            if (right.SourceStart < leftEnd)
                return $"{separator}\n\n  ^ OVERLAP: [{right.Index}] begins at {right.SourceStart}, {leftEnd - right.SourceStart} characters before [{left.Index}] ends. The shared characters appear in both chunks.";

            if (right.SourceStart == leftEnd)
                return $"{separator}\n\n  ^ ADJACENT: [{right.Index}] begins at {right.SourceStart}, immediately after {leftEnd - 1}. No characters lie between the two ranges.";

            var between = document[leftEnd..right.SourceStart];
            return $"{separator}\n\n  ^ SEPARATOR: {between.Length} character(s) at {leftEnd}-{right.SourceStart - 1} ({Visualise(between)}) lie between the ranges and belong to neither chunk.";
        }

        // Separator characters are whitespace, which the console renders as nothing, so each one is printed by name instead.
        private static string Visualise(string text) =>
            string.Join(" ", text.Select(c => c switch
            {
                ' ' => "SP",
                '\n' => "LF",
                '\r' => "CR",
                '\t' => "TAB",
                _ => $"U+{(int)c:X4}"
            }));
    }
}
