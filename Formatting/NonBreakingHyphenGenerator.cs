using ICSharpCode.AvalonEdit.Rendering;

namespace MinimalNotepad.Formatting
{
    /// <summary>
    /// Prevents WPF word-wrap from splitting a hyphen/dash from the character that
    /// immediately follows it (e.g. "->" stays on the same visual line).
    ///
    /// Works at render time, so it covers both typed text AND pasted text without
    /// modifying the document.  The document still stores the original U+002D character,
    /// which copies and pastes correctly to external apps.
    ///
    /// How: combines "-" + nextChar into one FormattedTextElement (documentLength = 2).
    /// WPF TextFormatter treats the element as an indivisible block and cannot break
    /// between the two characters.
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
            char next = CurrentContext.Document.GetCharAt(offset + 1);
            // documentLength = 2 → consumes both '-' and the following char as one unit
            return new FormattedTextElement("-" + next, 2);
        }
    }
}
