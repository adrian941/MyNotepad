using System.Linq;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Rendering;

namespace MinimalNotepad.Formatting
{
    // Renders a non-interactive "  ···" indicator for folded code blocks instead of
    // AvalonEdit's clickable [···] box. We keep the built-in FoldingElementGenerator
    // installed (it does the HeightTree collapse + TextView registration) but insert
    // this generator at index 0 so it wins the offset tie and supplies the visual.
    class CodeBlockCollapseGenerator : VisualLineElementGenerator
    {
        static readonly Brush MarkerBrush = MakeFrozen(Color.FromRgb(0x85, 0x99, 0xAA));

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

            // Build a TextLine ourselves with a custom gray foreground. We must use the
            // (TextLine, int) constructor: FormattedTextElement.CreateTextRun only honours
            // a pre-built TextLine — the (FormattedText, int) overload leaves its `text`
            // field null and crashes in PrepareText.
            var props = new VisualLineElementTextRunProperties(CurrentContext.GlobalTextRunProperties);
            props.SetForegroundBrush(MarkerBrush);

            var formatter = TextFormatter.Create();
            var line = FormattedTextElement.PrepareText(formatter, "  ···", props);
            return new FormattedTextElement(line, docLen);
        }

        static Brush MakeFrozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    }
}
