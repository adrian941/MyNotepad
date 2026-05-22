using System.Collections.Generic;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace MinimalNotepad.Formatting
{
    class RichTextColorizer : DocumentColorizingTransformer
    {
        private readonly FormattingManager _manager;
        private static readonly Dictionary<string, SolidColorBrush> _brushCache = new();

        public RichTextColorizer(FormattingManager manager) => _manager = manager;

        static SolidColorBrush BrushFor(string hex)
        {
            if (!_brushCache.TryGetValue(hex, out var brush))
            {
                brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                brush.Freeze();
                _brushCache[hex] = brush;
            }
            return brush;
        }

        protected override void ColorizeLine(DocumentLine line)
        {
            var spans = _manager.Spans;
            if (spans.Count == 0) return;

            // Collect all segment boundaries that fall within this line
            var pts = new System.Collections.Generic.SortedSet<int> { line.Offset, line.EndOffset };
            foreach (var span in spans)
            {
                if (span.IsDeleted || span.IsEmpty) continue;
                int s = span.Start, e = span.End;
                if (s < line.EndOffset && e > line.Offset)
                {
                    if (s > line.Offset)    pts.Add(s);
                    if (e < line.EndOffset) pts.Add(e);
                }
            }

            var points = new System.Collections.Generic.List<int>(pts);
            for (int i = 0; i < points.Count - 1; i++)
            {
                int segStart = points[i], segEnd = points[i + 1];

                // Accumulate formatting from all spans covering this segment
                bool bold = false, italic = false, underline = false, strike = false;
                string? foreColor = null, backColor = null;

                foreach (var span in spans)
                {
                    if (span.IsDeleted || span.IsEmpty) continue;
                    if (span.Start <= segStart && span.End >= segEnd)
                    {
                        bold      |= span.Format.Bold;
                        italic    |= span.Format.Italic;
                        underline |= span.Format.Underline;
                        strike    |= span.Format.Strikethrough;
                        if (span.Format.ForeColorHex != null) foreColor = span.Format.ForeColorHex;
                        if (span.Format.BackColorHex != null) backColor = span.Format.BackColorHex;
                    }
                }

                if (!bold && !italic && !underline && !strike && foreColor == null && backColor == null)
                    continue;

                // Capture for lambda to avoid closure over loop vars
                bool cBold = bold, cItalic = italic, cUnder = underline, cStrike = strike;
                string? cFore = foreColor, cBack = backColor;

                ChangeLinePart(segStart, segEnd, el =>
                {
                    var tf = el.TextRunProperties.Typeface;
                    if (cBold || cItalic)
                        el.TextRunProperties.SetTypeface(new Typeface(
                            tf.FontFamily,
                            cItalic ? System.Windows.FontStyles.Italic  : tf.Style,
                            cBold   ? System.Windows.FontWeights.Bold   : tf.Weight,
                            tf.Stretch));

                    if (cFore != null) el.TextRunProperties.SetForegroundBrush(BrushFor(cFore));
                    if (cBack != null) el.TextRunProperties.SetBackgroundBrush(BrushFor(cBack));

                    if (cUnder || cStrike)
                    {
                        var dec = new System.Windows.TextDecorationCollection();
                        if (cUnder)  dec.Add(System.Windows.TextDecorations.Underline[0]);
                        if (cStrike) dec.Add(System.Windows.TextDecorations.Strikethrough[0]);
                        el.TextRunProperties.SetTextDecorations(dec);
                    }
                });
            }
        }
    }
}
