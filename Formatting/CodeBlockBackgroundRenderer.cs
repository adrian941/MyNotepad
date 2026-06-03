using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;

namespace MinimalNotepad.Formatting
{
    class CodeBlockBackgroundRenderer : IBackgroundRenderer
    {
        static readonly Brush ContentBg = Freeze(new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28)));
        static readonly Brush FenceBg   = Freeze(new SolidColorBrush(Color.FromRgb(0x48, 0x48, 0x48)));
        static readonly Pen   EdgePen   = FreezePen(new Pen(new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), 1));

        const double CornerRadius = 4;

        private List<CodeBlockRegion> _regions = new();

        public bool IsDarkMode { get; set; }

        public KnownLayer Layer => KnownLayer.Background;

        public void UpdateRegions(List<CodeBlockRegion> regions) => _regions = regions;

        public void Draw(TextView textView, DrawingContext dc)
        {
            if (_regions.Count == 0) return;

            textView.EnsureVisualLines();
            var visLines = textView.VisualLines;
            if (visLines.Count == 0) return;

            int firstVis = visLines[0].FirstDocumentLine.LineNumber;
            int lastVis  = visLines[visLines.Count - 1].LastDocumentLine.LineNumber;

            foreach (var region in _regions)
            {
                if (region.FenceOpenLine > lastVis || region.FenceCloseLine < firstVis)
                    continue;

                double blockTop    = double.MaxValue;
                double blockBottom = double.MinValue;

                // Draw each section as one merged rectangle — no per-line gaps
                DrawBand(textView, dc, region.FenceOpenLine, region.FenceOpenLine,
                         FenceBg, firstVis, lastVis, ref blockTop, ref blockBottom);

                if (region.FenceCloseLine > region.FenceOpenLine + 1)
                    DrawBand(textView, dc, region.FenceOpenLine + 1, region.FenceCloseLine - 1,
                             ContentBg, firstVis, lastVis, ref blockTop, ref blockBottom);

                DrawBand(textView, dc, region.FenceCloseLine, region.FenceCloseLine,
                         FenceBg, firstVis, lastVis, ref blockTop, ref blockBottom);

                if (blockTop >= blockBottom) continue;

                if (!IsDarkMode)
                {
                    double w = textView.ActualWidth;
                    dc.DrawRoundedRectangle(null, EdgePen,
                        new Rect(0.5, blockTop + 0.5, Math.Max(0, w - 1), blockBottom - blockTop - 1),
                        CornerRadius, CornerRadius);
                }
            }
        }

        static void DrawBand(TextView tv, DrawingContext dc, int fromLine, int toLine, Brush bg,
                             int firstVis, int lastVis,
                             ref double blockTop, ref double blockBottom)
        {
            int visFrom = Math.Max(fromLine, firstVis);
            int visTo   = Math.Min(toLine,   lastVis);
            if (visFrom > visTo) return;

            double scrollY = tv.ScrollOffset.Y;
            double top     = double.MaxValue;
            double bottom  = double.MinValue;

            foreach (var vl in tv.VisualLines)
            {
                int ln = vl.FirstDocumentLine.LineNumber;
                if (ln < visFrom || ln > visTo) continue;
                double t = vl.VisualTop - scrollY;
                double b = t + vl.Height;
                if (t < top)    top    = t;
                if (b > bottom) bottom = b;
            }

            if (top >= bottom) return;

            top    = Math.Floor(top);
            bottom = Math.Ceiling(bottom);

            dc.DrawRectangle(bg, null, new Rect(0, top, tv.ActualWidth, bottom - top));

            if (top    < blockTop)    blockTop    = top;
            if (bottom > blockBottom) blockBottom = bottom;
        }

        static Brush Freeze(SolidColorBrush b)  { b.Freeze(); return b; }
        static Pen   FreezePen(Pen p)            { p.Freeze();  return p; }
    }
}
