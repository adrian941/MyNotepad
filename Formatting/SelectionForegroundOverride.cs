using System;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace MinimalNotepad.Formatting
{
    // Runs last in LineTransformers — re-applies SelectionForeground on selected text
    // so our custom syntax/highlight transformers cannot override it.
    class SelectionForegroundOverride : DocumentColorizingTransformer
    {
        readonly TextArea _textArea;
        public SelectionForegroundOverride(TextArea textArea) => _textArea = textArea;

        protected override void ColorizeLine(DocumentLine line)
        {
            var fg = _textArea.SelectionForeground;
            if (fg == null || _textArea.Selection.IsEmpty) return;

            var seg = _textArea.Selection.SurroundingSegment;
            if (seg == null) return;

            int start = Math.Max(line.Offset, seg.Offset);
            int end   = Math.Min(line.EndOffset, seg.Offset + seg.Length);
            if (start >= end) return;

            ChangeLinePart(start, end, el => el.TextRunProperties.SetForegroundBrush(fg));
        }
    }
}
