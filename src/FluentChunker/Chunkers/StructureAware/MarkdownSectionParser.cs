using System.Text;
using FluentChunker.Chunkers.SentenceAware;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace FluentChunker.Chunkers.StructureAware
{
    // Parses a Markdown document into sections using Markdig.
    // The ONLY type in the library that touches Markdig: everything downstream consumes the Section model,
    // so the parser can be swapped for another implementation without any public API change.
    // Content before the first heading becomes a preamble section with an empty path.
    internal static class MarkdownSectionParser
    {
        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UsePreciseSourceLocation()
            .UsePipeTables()
            .Build();

        internal static IReadOnlyList<Section> Parse(string text)
        {
            var document = Markdown.Parse(text, Pipeline);

            var sections = new List<Section>();
            var headingTexts = new List<string>();
            var headingLevels = new List<int>();
            var blocks = new List<SectionBlock>();

            foreach (var block in document)
            {
                if (block is HeadingBlock heading)
                {
                    // Close the running section under its own path BEFORE mutating the stack for the new heading.
                    FlushSection(sections, headingTexts, blocks);

                    while (headingLevels.Count > 0 && headingLevels[^1] >= heading.Level)
                    {
                        headingLevels.RemoveAt(headingLevels.Count - 1);
                        headingTexts.RemoveAt(headingTexts.Count - 1);
                    }
                    headingLevels.Add(heading.Level);
                    headingTexts.Add(ExtractHeadingText(heading));

                    // The heading itself is the first unit of its section, atomic like any other non-paragraph block.
                    AddBlock(blocks, text, block, isAtomic: true);
                    continue;
                }

                // Paragraphs are the only block where a mid-block cut still reads as prose.
                AddBlock(blocks, text, block, isAtomic: block is not ParagraphBlock);
            }

            FlushSection(sections, headingTexts, blocks);
            return sections;
        }

        private static void FlushSection(List<Section> sections, List<string> headingTexts,
            List<SectionBlock> blocks)
        {
            if (blocks.Count == 0)
                return;

            sections.Add(new Section
            {
                HeadingPath = headingTexts.ToArray(),
                Blocks = blocks.ToArray()
            });
            blocks.Clear();
        }

        // Markdig spans are inclusive of their last character.
        // Clamp defensively and trim to content so a chunk built from these spans never starts or ends on separator whitespace.
        private static void AddBlock(List<SectionBlock> blocks, string text, Block block, bool isAtomic)
        {
            var start = block.Span.Start;
            var end = block.Span.End + 1;

            if (start < 0 || start >= end)
                return;
            end = Math.Min(end, text.Length);

            while (start < end && char.IsWhiteSpace(text[start]))
                start++;
            while (end > start && char.IsWhiteSpace(text[end - 1]))
                end--;

            if (start >= end)
                return;

            blocks.Add(new SectionBlock(new SentenceSpan(start, end - start), isAtomic));
        }

        // Builds the breadcrumb text from the heading's inline tree: literal text and inline code survive,
        // formatting markers (emphasis, links, backticks) are dropped.
        // Works for ATX and Setext headings alike, unlike slicing the raw source line.
        private static string ExtractHeadingText(HeadingBlock heading)
        {
            var builder = new StringBuilder();
            AppendInlineText(heading.Inline, builder);
            return builder.ToString().Trim();
        }

        private static void AppendInlineText(ContainerInline? container, StringBuilder builder)
        {
            if (container is null)
                return;

            foreach (var inline in container)
            {
                switch (inline)
                {
                    case LiteralInline literal:
                        builder.Append(literal.Content.ToString());
                        break;
                    case CodeInline code:
                        builder.Append(code.Content);
                        break;
                    case LineBreakInline:
                        builder.Append(' ');
                        break;
                    case ContainerInline nested:
                        AppendInlineText(nested, builder);
                        break;
                }
            }
        }
    }
}
