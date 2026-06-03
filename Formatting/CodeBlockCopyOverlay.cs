using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace MinimalNotepad.Formatting
{
    class CodeBlockCopyOverlay
    {
        const double BtnWidth   = 56;
        const double LnBtnWidth = 30;
        const double BtnHeight  = 20;
        const double BtnGap     = 4;
        const double BtnMargin  = 8;

        readonly Canvas       _canvas;
        readonly TextView     _textView;
        readonly TextDocument _doc;

        record BlockUI(Button LnBtn, Button CopyBtn, Button DelBtn, CodeBlockRegion Region);

        readonly List<BlockUI> _uis = new();

        public CodeBlockCopyOverlay(Canvas canvas, TextView textView, TextDocument doc)
        {
            _canvas   = canvas;
            _textView = textView;
            _doc      = doc;
        }

        // ── Region update ─────────────────────────────────────────────────────

        public void UpdateRegions(IReadOnlyList<CodeBlockRegion> regions)
        {
            foreach (var ui in _uis)
            {
                _canvas.Children.Remove(ui.LnBtn);
                _canvas.Children.Remove(ui.CopyBtn);
                _canvas.Children.Remove(ui.DelBtn);
            }
            _uis.Clear();

            foreach (var region in regions)
            {
                if (region.FenceCloseLine <= region.FenceOpenLine + 1) continue;
                var r = region;

                var lnBtn = MakeButton("#", LnBtnWidth, active: r.LineNumbers);
                lnBtn.Click += (_, _) => HandleToggleLineNumbers(r);

                var copyBtn = MakeButton("Copy");
                copyBtn.Click += (_, _) => HandleCopy(copyBtn, r);

                var delBtn = MakeButton("Delete");
                delBtn.Click += (_, _) => HandleDelete(delBtn, r);

                _canvas.Children.Add(lnBtn);
                _canvas.Children.Add(copyBtn);
                _canvas.Children.Add(delBtn);
                _uis.Add(new BlockUI(lnBtn, copyBtn, delBtn, region));
            }

            UpdatePositions();
        }

        // ── Position update ───────────────────────────────────────────────────

        public void UpdatePositions()
        {
            if (!_textView.VisualLinesValid || _textView.VisualLines.Count == 0)
            {
                foreach (var ui in _uis) { ui.LnBtn.Visibility = Visibility.Collapsed; ui.CopyBtn.Visibility = Visibility.Collapsed; ui.DelBtn.Visibility = Visibility.Collapsed; }
                return;
            }

            double scrollY    = _textView.ScrollOffset.Y;
            double viewHeight = _textView.ActualHeight;

            foreach (var ui in _uis)
            {
                var r = ui.Region;

                double cTop = GetLineBot(r.FenceOpenLine, scrollY);
                double cBot = GetLineTop(r.FenceCloseLine, scrollY);

                bool topOff = double.IsNaN(cTop);
                bool botOff = double.IsNaN(cBot);

                if (topOff && botOff)
                {
                    bool spans = false;
                    foreach (var vl in _textView.VisualLines)
                    {
                        int ln = vl.FirstDocumentLine.LineNumber;
                        if (ln > r.FenceOpenLine && ln < r.FenceCloseLine) { spans = true; break; }
                    }
                    if (!spans) { ui.CopyBtn.Visibility = Visibility.Collapsed; ui.DelBtn.Visibility = Visibility.Collapsed; continue; }
                    cTop = 0; cBot = viewHeight;
                }
                else
                {
                    if (topOff) cTop = 0;
                    if (botOff) cBot = viewHeight;
                }

                double bTop = cBot - BtnHeight - BtnMargin;
                bTop = Math.Max(bTop, cTop + BtnMargin);
                bTop = Math.Min(bTop, viewHeight - BtnHeight - BtnMargin);

                bool vis = cTop < viewHeight && cBot > 0 && bTop >= cTop - 0.5;
                ui.LnBtn.Visibility   = vis ? Visibility.Visible : Visibility.Collapsed;
                ui.CopyBtn.Visibility = vis ? Visibility.Visible : Visibility.Collapsed;
                ui.DelBtn.Visibility  = vis ? Visibility.Visible : Visibility.Collapsed;

                if (!vis) continue;

                double rightEdge = _textView.ActualWidth - BtnMargin;
                PlaceButton(ui.DelBtn,  rightEdge - BtnWidth,                                         bTop);
                PlaceButton(ui.CopyBtn, rightEdge - BtnWidth - BtnGap - BtnWidth,                    bTop);
                PlaceButton(ui.LnBtn,   rightEdge - BtnWidth - BtnGap - BtnWidth - BtnGap - LnBtnWidth, bTop);
            }
        }

        // ── Copy / Delete ─────────────────────────────────────────────────────

        void HandleCopy(Button btn, CodeBlockRegion region)
        {
            var sb = new StringBuilder();
            for (int ln = region.FenceOpenLine + 1; ln < region.FenceCloseLine; ln++)
            {
                if (sb.Length > 0) sb.AppendLine();
                var docLine = _doc.GetLineByNumber(ln);
                sb.Append(_doc.GetText(docLine.Offset, docLine.Length));
            }
            string content = sb.ToString();

            try
            {
                var data = new System.Windows.DataObject();
                data.SetText(content);
                string withMarkers = $"```{region.Language}\n{content}\n```";
                data.SetData("application/x-mynotepad-codeblock", withMarkers);
                System.Windows.Clipboard.SetDataObject(data);
            }
            catch { }

            Flash(btn, "Copied!", "Copy");
        }

        void HandleToggleLineNumbers(CodeBlockRegion region)
        {
            var openLine = _doc.GetLineByNumber(region.FenceOpenLine);
            string text  = _doc.GetText(openLine.Offset, openLine.Length);
            string newText = region.LineNumbers
                ? (text.EndsWith(":ln") ? text[..^3] : text)
                : text + ":ln";
            _doc.Replace(openLine.Offset, openLine.Length, newText);
        }

        void HandleDelete(Button btn, CodeBlockRegion region)
        {
            try
            {
                var openLine  = _doc.GetLineByNumber(region.FenceOpenLine);
                var closeLine = _doc.GetLineByNumber(region.FenceCloseLine);
                int delStart  = openLine.Offset;
                int delEnd    = closeLine.Offset + closeLine.Length + closeLine.DelimiterLength;
                _doc.Remove(delStart, delEnd - delStart);
            }
            catch { }

            Flash(btn, "Deleted!", "Delete");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        void PlaceButton(FrameworkElement btn, double left, double top)
        {
            var pt = _textView.TranslatePoint(new Point(left, top), _canvas);
            if (!double.IsNaN(pt.X))
            {
                Canvas.SetLeft(btn, pt.X);
                Canvas.SetTop(btn, pt.Y);
            }
        }

        static void Flash(Button btn, string msg, string original)
        {
            btn.Content = msg;
            var t = new System.Windows.Threading.DispatcherTimer
                { Interval = TimeSpan.FromSeconds(1.5) };
            t.Tick += (_, _) => { t.Stop(); btn.Content = original; };
            t.Start();
        }

        double GetLineTop(int ln, double scrollY)
        {
            foreach (var vl in _textView.VisualLines)
                if (vl.FirstDocumentLine.LineNumber == ln)
                    return vl.VisualTop - scrollY;
            return double.NaN;
        }

        double GetLineBot(int ln, double scrollY)
        {
            foreach (var vl in _textView.VisualLines)
                if (vl.FirstDocumentLine.LineNumber == ln)
                    return vl.VisualTop + vl.Height - scrollY;
            return double.NaN;
        }

        static Button MakeButton(string label, double width = BtnWidth, bool active = false)
        {
            var btn = new Button
            {
                Content    = label,
                Width      = width,
                Height     = BtnHeight,
                Focusable  = false,
                Cursor     = Cursors.Hand,
                Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize   = 11,
            };

            var tpl    = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border), "bd");
            var bgColor = active
                ? Color.FromArgb(0xCC, 0x35, 0x55, 0x28)
                : Color.FromArgb(0xBB, 0x3A, 0x3A, 0x3A);
            border.SetValue(Border.BackgroundProperty,      new SolidColorBrush(bgColor));
            border.SetValue(Border.BorderBrushProperty,     new SolidColorBrush(Color.FromArgb(0x99, 0x77, 0x77, 0x77)));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.CornerRadiusProperty,    new CornerRadius(3));

            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty,   VerticalAlignment.Center);
            border.AppendChild(cp);
            tpl.VisualTree = border;

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush(Color.FromArgb(0xDD, 0x60, 0x60, 0x60)), "bd"));
            tpl.Triggers.Add(hover);

            btn.Template = tpl;
            return btn;
        }
    }
}
