using System.Collections.Generic;
using ICSharpCode.AvalonEdit.Rendering;

namespace MinimalNotepad.Formatting
{
    /// <summary>
    /// Inserts a leading space before the first character of every non-empty code block line.
    /// Uses FormattedTextElement (documentLength=1, consumes the first char and re-renders it
    /// with a leading space), so it is safe, well-tested, and works with ChangeLinePart coloring.
    /// Empty lines inside code blocks are left as-is.
    /// </summary>
    class CodeBlockPaddingGenerator : VisualLineElementGenerator
    {
        IReadOnlyList<CodeBlockRegion> _regions = System.Array.Empty<CodeBlockRegion>();

        public void UpdateRegions(IReadOnlyList<CodeBlockRegion> regions) => _regions = regions;

        public override int GetFirstInterestedOffset(int startOffset)
        {
            int ln = CurrentContext.VisualLine.FirstDocumentLine.LineNumber;
            foreach (var region in _regions)
            {
                if (ln >= region.FenceOpenLine && ln <= region.FenceCloseLine)
                {
                    int lineStart = CurrentContext.VisualLine.FirstDocumentLine.Offset;
                    // Only interested at the very beginning of a non-empty line
                    if (lineStart >= startOffset
                        && CurrentContext.VisualLine.FirstDocumentLine.Length > 0)
                        return lineStart;
                    break;
                }
            }
            return -1;
        }

        public override VisualLineElement ConstructElement(int offset)
        {
            var docLine = CurrentContext.VisualLine.FirstDocumentLine;
            if (offset != docLine.Offset || docLine.Length == 0) return null!;

            int ln = docLine.LineNumber;
            foreach (var region in _regions)
            {
                if (ln >= region.FenceOpenLine && ln <= region.FenceCloseLine)
                {
                    char first = CurrentContext.Document.GetCharAt(offset);
                    // Consume 1 doc char; prepend a visual space for left inner-padding.
                    // documentLength=1 → ChangeLinePart coloring still reaches this element.
                    return new FormattedTextElement(" " + first, 1);
                }
            }
            return null!;
        }
    }
}
