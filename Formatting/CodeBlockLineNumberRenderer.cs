using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;

namespace MinimalNotepad.Formatting
{
    // Replaced by CodeBlockLineNumberGenerator (InlineObjectElement approach).
    class CodeBlockLineNumberRenderer : IBackgroundRenderer
    {
        public KnownLayer Layer      => KnownLayer.Background;
        public bool       IsDarkMode { get; set; }
        public void Draw(TextView textView, DrawingContext dc) { }
    }
}
