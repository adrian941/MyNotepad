using System.Collections.Generic;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace MinimalNotepad.Formatting
{
    /// <summary>
    /// Reduces the font rendering size for content lines inside code blocks (```...```).
    /// Fence lines (``` opening/closing) are left at the normal editor font size.
    /// Runs after CodeSyntaxColorizer so syntax colors are not affected.
    /// </summary>
    class CodeBlockFontSizeTransformer : DocumentColorizingTransformer
    {
        const double ContentFontScale = 0.88;

        private IReadOnlyList<CodeBlockRegion> _regions = System.Array.Empty<CodeBlockRegion>();

        public void UpdateRegions(IReadOnlyList<CodeBlockRegion> regions) => _regions = regions;

        protected override void ColorizeLine(DocumentLine line)
        {
            if (line.Length == 0) return;

            int ln = line.LineNumber;
            foreach (var region in _regions)
            {
                int contentStart = region.FenceOpenLine + 1;
                int contentEnd   = region.FenceCloseLine - 1;

                if (ln < contentStart || ln > contentEnd) continue;

                ChangeLinePart(line.Offset, line.EndOffset, el =>
                {
                    double current = el.TextRunProperties.FontRenderingEmSize;
                    el.TextRunProperties.SetFontRenderingEmSize(current * ContentFontScale);
                });
                return;
            }
        }
    }
}
