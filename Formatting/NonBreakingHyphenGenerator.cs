using System.Windows.Media.TextFormatting;
using ICSharpCode.AvalonEdit.Rendering;

namespace MinimalNotepad.Formatting
{
    /// <summary>
    /// Prevents WPF word-wrap from breaking a line at a hyphen that has a
    /// non-space character after it (e.g. "intr-un" or "->" never get split).
    ///
    /// Works at render time without modifying the document. The document still
    /// stores U+002D, which copies and pastes correctly to external apps.
    ///
    /// How: renders the single '-' character as U+2011 NON-BREAKING HYPHEN
    /// (visually identical, line-break class GL). The element covers exactly
    /// one document character, so caret movement and selection are per-character.
    /// </summary>
    class NonBreakingHyphenGenerator : VisualLineElementGenerator
    {
        public override int GetFirstInterestedOffset(int startOffset)
        {
            var doc = CurrentContext.Document;
            int end = CurrentContext.VisualLine.LastDocumentLine.EndOffset;

            for (int i = startOffset; i < end - 1; i++)
            {
                if (doc.GetCharAt(i) != '-') continue;
                char next = doc.GetCharAt(i + 1);
                if (next != ' ' && next != '\t')
                    return i;
            }
            return -1;
        }

        public override VisualLineElement ConstructElement(int offset)
        {
            return new NonBreakingHyphenText(CurrentContext.VisualLine);
        }
    }

    /// <summary>
    /// A 1-character visual element that renders '-' as U+2011 (non-breaking hyphen).
    /// Inherits VisualLineText so caret, selection, and hit-testing stay per-character.
    /// </summary>
    class NonBreakingHyphenText : VisualLineText
    {
        public NonBreakingHyphenText(VisualLine parentLine) : base(parentLine, 1) { }

        public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context)
        {
            return new TextCharacters("‑", 0, 1, TextRunProperties);
        }

        protected override VisualLineText CreateInstance(int length) =>
            new NonBreakingHyphenText(ParentVisualLine);
    }
}
