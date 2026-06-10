using System.Collections.Generic;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace MinimalNotepad.Formatting
{
    /// <summary>
    /// Undo-only formatting operation for text edits (Delete, Backspace, Cut).
    /// On Undo: restores the pre-deletion span snapshot (text was already re-inserted by AvalonEdit).
    /// On Redo: no-op — TextAnchors naturally collapse as text is re-deleted.
    /// </summary>
    class FormattingRestoreOperation : IUndoableOperation
    {
        readonly FormattingManager                  _manager;
        readonly List<FormattingManager.SpanRecord> _before;
        readonly TextView                           _textView;

        public FormattingRestoreOperation(
            FormattingManager                  manager,
            List<FormattingManager.SpanRecord> before,
            TextView                           textView)
        {
            _manager  = manager;
            _before   = before;
            _textView = textView;
        }

        public void Undo() => _manager.RestoreSnapshot(_before, _textView);
        public void Redo() { }
    }
}
