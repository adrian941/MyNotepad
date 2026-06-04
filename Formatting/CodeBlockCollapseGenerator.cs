using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Rendering;

namespace MinimalNotepad.Formatting
{
    // Replaces AvalonEdit's default FoldingElementGenerator with a non-interactive
    // version. FoldingManager still manages the HeightTree collapsing (required),
    // but we render "  ···" as plain text instead of a clickable element.
    class CodeBlockCollapseGenerator : VisualLineElementGenerator
    {
        readonly FoldingManager _fm;

        public CodeBlockCollapseGenerator(FoldingManager fm) => _fm = fm;

        public override int GetFirstInterestedOffset(int startOffset)
        {
            int best = -1;
            foreach (var fs in _fm.AllFoldings)
            {
                if (!fs.IsFolded || fs.StartOffset < startOffset) continue;
                if (best == -1 || fs.StartOffset < best)
                    best = fs.StartOffset;
            }
            return best;
        }

        public override VisualLineElement? ConstructElement(int offset)
        {
            var section = _fm.AllFoldings.FirstOrDefault(fs => fs.IsFolded && fs.StartOffset == offset);
            if (section == null) return null;

            int docLen = section.EndOffset - section.StartOffset;
            if (docLen <= 0) return null;

            var props    = CurrentContext.GlobalTextRunProperties;
            double size  = props?.FontRenderingEmSize > 0 ? props.FontRenderingEmSize : 13.0;
            var typeface = props?.Typeface ?? new Typeface("Consolas");

            var brush = new SolidColorBrush(Color.FromRgb(0x85, 0x99, 0xAA));
            brush.Freeze();

            var ft = new FormattedText(
                "  ···",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                size,
                brush,
                1.0);

            return new FormattedTextElement(ft, docLen);
        }
    }
}
