using ICSharpCode.AvalonEdit.Document;

namespace MinimalNotepad.Formatting
{
    class FormattingSpan
    {
        public ITextAnchor    StartAnchor { get; }
        public ITextAnchor    EndAnchor   { get; }
        public TextFormatting Format      { get; }

        public bool IsDeleted => StartAnchor.IsDeleted || EndAnchor.IsDeleted;
        public int  Start     => StartAnchor.Offset;
        public int  End       => EndAnchor.Offset;
        public bool IsEmpty   => !IsDeleted && Start >= End;

        public FormattingSpan(ITextAnchor start, ITextAnchor end, TextFormatting fmt)
        {
            StartAnchor = start;
            EndAnchor   = end;
            Format      = fmt;
        }
    }
}
