using System.Collections.Generic;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace MinimalNotepad.Formatting
{
    class FormattingUndoOperation : IUndoableOperation
    {
        private readonly FormattingManager                  _manager;
        private readonly List<FormattingManager.SpanRecord> _before;
        private readonly List<FormattingManager.SpanRecord> _after;
        private readonly TextView                           _textView;

        public FormattingUndoOperation(
            FormattingManager                  manager,
            List<FormattingManager.SpanRecord> before,
            List<FormattingManager.SpanRecord> after,
            TextView                           textView)
        {
            _manager  = manager;
            _before   = before;
            _after    = after;
            _textView = textView;
        }

        public void Undo() => _manager.RestoreSnapshot(_before, _textView);
        public void Redo() => _manager.RestoreSnapshot(_after,  _textView);
    }
}
