using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;

namespace MinimalNotepad.Formatting
{
    class CodeBlockLineNumberGenerator : VisualLineElementGenerator
    {
        static readonly Brush LightFg = Freeze(new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)));
        static readonly Brush DarkFg  = Freeze(new SolidColorBrush(Color.FromRgb(0x58, 0x58, 0x58)));

        IReadOnlyList<CodeBlockRegion> _regions = Array.Empty<CodeBlockRegion>();

        public double FontSize   { get; set; } = 12;
        public bool   IsDarkMode { get; set; }

        // Prevents re-generation of a 0-length element at the same offset
        int _skipOffset = -1;

        public void UpdateRegions(IReadOnlyList<CodeBlockRegion> regions) => _regions = regions;

        public override int GetFirstInterestedOffset(int startOffset)
        {
            if (startOffset == _skipOffset) { _skipOffset = -1; return -1; }

            int ln = CurrentContext.VisualLine.FirstDocumentLine.LineNumber;
            foreach (var region in _regions)
            {
                if (!region.LineNumbers) continue;
                if (ln <= region.FenceOpenLine || ln >= region.FenceCloseLine) continue;

                int lineStart = CurrentContext.VisualLine.FirstDocumentLine.Offset;
                if (lineStart >= startOffset) return lineStart;
                break;
            }
            return -1;
        }

        public override VisualLineElement? ConstructElement(int offset)
        {
            var docLine = CurrentContext.VisualLine.FirstDocumentLine;
            if (offset != docLine.Offset) return null;

            int ln = docLine.LineNumber;
            foreach (var region in _regions)
            {
                if (!region.LineNumbers) continue;
                if (ln <= region.FenceOpenLine || ln >= region.FenceCloseLine) continue;

                int lineNum    = ln - region.FenceOpenLine;
                int maxLineNum = region.FenceCloseLine - region.FenceOpenLine - 1;
                int digits     = maxLineNum.ToString().Length;

                string text = lineNum.ToString().PadLeft(digits) + " ";

                var tb = new TextBlock
                {
                    Text              = text,
                    FontFamily        = new FontFamily("Consolas"),
                    FontSize          = FontSize,
                    Foreground        = IsDarkMode ? DarkFg : LightFg,
                    VerticalAlignment = VerticalAlignment.Center,
                    Padding           = new Thickness(2, 0, 0, 0),
                };

                _skipOffset = offset; // skip this offset on the next GetFirstInterestedOffset call
                return new InlineObjectElement(0, tb);
            }
            return null;
        }

        static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
    }
}
