using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace MinimalNotepad
{
    // ── Formatting model ──────────────────────────────────────────────────────

    class TextFormatting
    {
        public bool Bold          { get; set; }
        public bool Italic        { get; set; }
        public bool Underline     { get; set; }
        public bool Strikethrough { get; set; }
        public string? ForeColorHex { get; set; }   // null = default (black)
        public string? BackColorHex { get; set; }   // null = no highlight

        public bool IsDefault =>
            !Bold && !Italic && !Underline && !Strikethrough
            && ForeColorHex == null && BackColorHex == null;

        public TextFormatting Clone() => new()
        {
            Bold = Bold, Italic = Italic, Underline = Underline,
            Strikethrough = Strikethrough,
            ForeColorHex = ForeColorHex, BackColorHex = BackColorHex
        };

        public bool SameAs(TextFormatting o) =>
            Bold == o.Bold && Italic == o.Italic && Underline == o.Underline
            && Strikethrough == o.Strikethrough
            && ForeColorHex == o.ForeColorHex && BackColorHex == o.BackColorHex;
    }

    class FormattingSpan
    {
        public ITextAnchor StartAnchor { get; }
        public ITextAnchor EndAnchor   { get; }
        public TextFormatting Format   { get; }

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

    // ── Span manager ──────────────────────────────────────────────────────────

    class FormattingManager
    {
        private readonly List<FormattingSpan> _spans = new();
        private readonly TextDocument         _doc;

        public FormattingManager(TextDocument doc) => _doc = doc;

        public IReadOnlyList<FormattingSpan> Spans => _spans;

        // ── anchor helpers ────────────────────────────────────────────────────

        ITextAnchor MakeAnchor(int offset, AnchorMovementType movement)
        {
            var a = _doc.CreateAnchor(offset);
            a.MovementType    = movement;
            a.SurviveDeletion = true;
            return a;
        }

        // ── split ─────────────────────────────────────────────────────────────

        void SplitAt(int offset)
        {
            if (offset <= 0 || offset >= _doc.TextLength) return;
            for (int i = _spans.Count - 1; i >= 0; i--)
            {
                var s = _spans[i];
                if (s.IsDeleted || s.IsEmpty) { _spans.RemoveAt(i); continue; }
                if (s.Start < offset && s.End > offset)
                {
                    var leftEnd    = MakeAnchor(offset, AnchorMovementType.BeforeInsertion);
                    var rightStart = MakeAnchor(offset, AnchorMovementType.AfterInsertion);
                    _spans[i] = new FormattingSpan(s.StartAnchor, leftEnd,    s.Format.Clone());
                    _spans.Insert(i + 1,
                                  new FormattingSpan(rightStart,  s.EndAnchor, s.Format.Clone()));
                }
            }
        }

        // ── coverage helpers ──────────────────────────────────────────────────

        List<int> CoveragePoints(int start, int end)
        {
            var pts = new SortedSet<int> { start, end };
            foreach (var s in _spans)
            {
                if (s.IsDeleted || s.IsEmpty) continue;
                if (s.Start >= start && s.Start <= end) pts.Add(s.Start);
                if (s.End   >= start && s.End   <= end) pts.Add(s.End);
            }
            return pts.ToList();
        }

        FormattingSpan? SpanFor(int segStart, int segEnd) =>
            _spans.FirstOrDefault(s =>
                !s.IsDeleted && !s.IsEmpty && s.Start == segStart && s.End == segEnd);

        // ── modify a range ────────────────────────────────────────────────────

        void ModifyRange(int start, int end, Action<TextFormatting> modify)
        {
            if (start >= end) return;
            SplitAt(start);
            SplitAt(end);
            var points = CoveragePoints(start, end);
            for (int i = 0; i < points.Count - 1; i++)
            {
                int segStart = points[i], segEnd = points[i + 1];
                var existing = SpanFor(segStart, segEnd);
                if (existing != null)
                {
                    modify(existing.Format);
                }
                else
                {
                    var fmt = new TextFormatting();
                    modify(fmt);
                    if (!fmt.IsDefault)
                    {
                        _spans.Add(new FormattingSpan(
                            MakeAnchor(segStart, AnchorMovementType.AfterInsertion),
                            MakeAnchor(segEnd,   AnchorMovementType.BeforeInsertion),
                            fmt));
                    }
                }
            }
            Cleanup();
        }

        // ── cleanup ───────────────────────────────────────────────────────────

        void Cleanup()
        {
            for (int i = _spans.Count - 1; i >= 0; i--)
            {
                var s = _spans[i];
                if (s.IsDeleted || s.IsEmpty || s.Format.IsDefault)
                    _spans.RemoveAt(i);
            }
            _spans.Sort((a, b) => a.Start.CompareTo(b.Start));
            for (int i = _spans.Count - 2; i >= 0; i--)
            {
                var a = _spans[i]; var b = _spans[i + 1];
                if (a.End == b.Start && a.Format.SameAs(b.Format))
                {
                    _spans[i] = new FormattingSpan(a.StartAnchor, b.EndAnchor, a.Format.Clone());
                    _spans.RemoveAt(i + 1);
                }
            }
        }

        // ── query helpers ─────────────────────────────────────────────────────

        bool IsUniform(int start, int end, Func<TextFormatting, bool> pred)
        {
            SplitAt(start); SplitAt(end);
            var points = CoveragePoints(start, end);
            for (int i = 0; i < points.Count - 1; i++)
            {
                var s = SpanFor(points[i], points[i + 1]);
                if (s == null || !pred(s.Format)) return false;
            }
            return true;
        }

        // Returns the uniform hex color, or null if all default, or "MIXED" if not uniform.
        string? GetUniformColor(int start, int end, Func<TextFormatting, string?> getter)
        {
            SplitAt(start); SplitAt(end);
            var points = CoveragePoints(start, end);
            const string Unset = "__UNSET__";
            string? uniform = Unset;
            for (int i = 0; i < points.Count - 1; i++)
            {
                var s = SpanFor(points[i], points[i + 1]);
                string? c = s != null ? getter(s.Format) : null;
                if (uniform == Unset) uniform = c;
                else if (uniform != c) return "MIXED";
            }
            return uniform == Unset ? null : uniform;
        }

        // ── public formatting actions ─────────────────────────────────────────

        public void ToggleBold(int start, int end)
        {
            bool allBold = IsUniform(start, end, f => f.Bold);
            ModifyRange(start, end, f => f.Bold = !allBold);
        }

        public void ToggleItalic(int start, int end)
        {
            bool allItalic = IsUniform(start, end, f => f.Italic);
            ModifyRange(start, end, f => f.Italic = !allItalic);
        }

        public void ToggleUnderline(int start, int end)
        {
            bool allUnder = IsUniform(start, end, f => f.Underline);
            ModifyRange(start, end, f => f.Underline = !allUnder);
        }

        public void ToggleStrikethrough(int start, int end)
        {
            bool allStrike = IsUniform(start, end, f => f.Strikethrough);
            ModifyRange(start, end, f => f.Strikethrough = !allStrike);
        }

        public void ToggleForeColor(int start, int end, string targetHex)
        {
            string? current = GetUniformColor(start, end, f => f.ForeColorHex);
            bool isSame = string.Equals(current, targetHex, StringComparison.OrdinalIgnoreCase);
            string? newColor = isSame ? null : targetHex;
            ModifyRange(start, end, f => f.ForeColorHex = newColor);
        }

        public void ToggleBackColor(int start, int end, string targetHex)
        {
            string? current = GetUniformColor(start, end, f => f.BackColorHex);
            bool isSame = string.Equals(current, targetHex, StringComparison.OrdinalIgnoreCase);
            string? newColor = isSame ? null : targetHex;
            ModifyRange(start, end, f => f.BackColorHex = newColor);
        }

        // ── Snapshot support for undo/redo ────────────────────────────────────

        public record SpanRecord(int Start, int End, TextFormatting Format);

        public List<SpanRecord> TakeSnapshot() =>
            _spans
                .Where(s => !s.IsDeleted && !s.IsEmpty)
                .Select(s => new SpanRecord(s.Start, s.End, s.Format.Clone()))
                .ToList();

        public void RestoreSnapshot(List<SpanRecord> snapshot, ICSharpCode.AvalonEdit.Rendering.TextView textView)
        {
            _spans.Clear();
            int docLen = _doc.TextLength;
            foreach (var r in snapshot)
            {
                int s = Math.Max(0, Math.Min(r.Start, docLen));
                int e = Math.Max(s,  Math.Min(r.End,   docLen));
                if (s >= e) continue;
                _spans.Add(new FormattingSpan(
                    MakeAnchor(s, AnchorMovementType.AfterInsertion),
                    MakeAnchor(e, AnchorMovementType.BeforeInsertion),
                    r.Format.Clone()));
            }
            textView.Redraw();
        }
    }

    // ── Formatting undo operation (integrates with AvalonEdit's undo stack) ──

    class FormattingUndoOperation : IUndoableOperation
    {
        private readonly FormattingManager _manager;
        private readonly List<FormattingManager.SpanRecord> _before;
        private readonly List<FormattingManager.SpanRecord> _after;
        private readonly ICSharpCode.AvalonEdit.Rendering.TextView _textView;

        public FormattingUndoOperation(
            FormattingManager manager,
            List<FormattingManager.SpanRecord> before,
            List<FormattingManager.SpanRecord> after,
            ICSharpCode.AvalonEdit.Rendering.TextView textView)
        {
            _manager  = manager;
            _before   = before;
            _after    = after;
            _textView = textView;
        }

        public void Undo() => _manager.RestoreSnapshot(_before, _textView);
        public void Redo() => _manager.RestoreSnapshot(_after,  _textView);
    }

    // ── Colorizer (AvalonEdit rendering transformer) ──────────────────────────

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

            // Collect segment boundaries within this line
            var pts = new SortedSet<int> { line.Offset, line.EndOffset };
            foreach (var span in spans)
            {
                if (span.IsDeleted || span.IsEmpty) continue;
                int s = span.Start, e = span.End;
                if (s < line.EndOffset && e > line.Offset)
                {
                    if (s > line.Offset) pts.Add(s);
                    if (e < line.EndOffset) pts.Add(e);
                }
            }

            var points = pts.ToList();
            for (int i = 0; i < points.Count - 1; i++)
            {
                int segStart = points[i], segEnd = points[i + 1];

                // Accumulate all formatting that covers this segment
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

                // Capture for lambda (avoid closure over loop vars)
                bool cBold = bold, cItalic = italic, cUnder = underline, cStrike = strike;
                string? cFore = foreColor, cBack = backColor;

                ChangeLinePart(segStart, segEnd, el =>
                {
                    var tf = el.TextRunProperties.Typeface;
                    if (cBold || cItalic)
                        el.TextRunProperties.SetTypeface(new Typeface(
                            tf.FontFamily,
                            cItalic ? FontStyles.Italic  : tf.Style,
                            cBold   ? FontWeights.Bold   : tf.Weight,
                            tf.Stretch));

                    if (cFore != null) el.TextRunProperties.SetForegroundBrush(BrushFor(cFore));
                    if (cBack != null) el.TextRunProperties.SetBackgroundBrush(BrushFor(cBack));

                    if (cUnder || cStrike)
                    {
                        var dec = new TextDecorationCollection();
                        if (cUnder)  dec.Add(TextDecorations.Underline[0]);
                        if (cStrike) dec.Add(TextDecorations.Strikethrough[0]);
                        el.TextRunProperties.SetTextDecorations(dec);
                    }
                });
            }
        }
    }

    // ── Color config (colors.json) ────────────────────────────────────────────

    class ColorEntry
    {
        // keyNumber : 1-5 = text colors, 6-9 = highlights, 0 = highlight (Violet)
        // typeId    : 1 = textColor, 2 = highlight
        // colorHex  : hex color string (e.g. "#2E7D32"), null = transparent/default

        [JsonPropertyName("keyNumber")] public int    KeyNumber { get; set; }
        [JsonPropertyName("typeId")]    public int    TypeId    { get; set; }
        [JsonPropertyName("colorHex")] public string? ColorHex  { get; set; }
    }

    class ColorConfig
    {
        [JsonPropertyName("_legend")]
        public string Legend { get; set; } =
            "keyNumber: 1=Green 2=Yellow 3=Orange 4=Red 5=Violet (textColor, typeId=1) | " +
            "6=Green 7=Yellow 8=Orange 9=Red 0=Violet (highlight, typeId=2) | " +
            "same color → reverts to black/transparent";

        [JsonPropertyName("colors")]
        public List<ColorEntry> Colors { get; set; } = new();
    }

    // ─────────────────────────────────────────────────────────────────────────

    class Program
    {
        class AppSettings
        {
            public double WindowLeft { get; set; } = 100;
            public double WindowTop { get; set; } = 100;
            public double WindowWidth { get; set; } = 800;
            public double WindowHeight { get; set; } = 600;
            public double FontSize { get; set; } = 12;
        }

        static ColorConfig LoadOrCreateColorConfig(string path)
        {
            var defaultConfig = new ColorConfig
            {
                Colors = new List<ColorEntry>
                {
                    // ── Text colors (typeId = 1) ── VS Code Light style, easy on the eyes ──
                    new() { KeyNumber = 1, TypeId = 1, ColorHex = "#2E7D32" }, // Green
                    new() { KeyNumber = 2, TypeId = 1, ColorHex = "#C17A00" }, // Yellow (dark amber, readable on white)
                    new() { KeyNumber = 3, TypeId = 1, ColorHex = "#D32F2F" }, // Red
                    new() { KeyNumber = 4, TypeId = 1, ColorHex = "#1565C0" }, // Blue (deep, not link-like)
                    new() { KeyNumber = 5, TypeId = 1, ColorHex = "#7B22AC" }, // Violet

                    // ── Highlights (typeId = 2) ── very light pastels, Material 50 palette ──
                    new() { KeyNumber = 6, TypeId = 2, ColorHex = "#E8F5E9" }, // Green highlight
                    new() { KeyNumber = 7, TypeId = 2, ColorHex = "#BBDEFB" }, // Blue highlight
                    new() { KeyNumber = 8, TypeId = 2, ColorHex = "#FFF3E0" }, // Orange highlight
                    new() { KeyNumber = 9, TypeId = 2, ColorHex = "#FFCDD2" }, // Red highlight (more vivid)
                    new() { KeyNumber = 0, TypeId = 2, ColorHex = "#F3E5F5" }, // Violet highlight
                }
            };

            try
            {
                if (File.Exists(path))
                {
                    var loaded = JsonSerializer.Deserialize<ColorConfig>(File.ReadAllText(path));
                    if (loaded?.Colors?.Count > 0) return loaded;
                }
            }
            catch { }

            File.WriteAllText(path, JsonSerializer.Serialize(defaultConfig,
                new JsonSerializerOptions { WriteIndented = true }));
            return defaultConfig;
        }

        [STAThread]
        static void Main()
        {
            var app = new Application();
            var windows = new List<Window>();
            string settingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
            string colorsFile   = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "colors.json");
            AppSettings settings;

            try
            {
                if (File.Exists(settingsFile))
                    settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsFile)) ?? new AppSettings();
                else
                    settings = new AppSettings();
            }
            catch { settings = new AppSettings(); }

            var colorConfig = LoadOrCreateColorConfig(colorsFile);

            // Build lookup: keyNumber → colorHex  (for textColor keys 1-5, highlight keys 6-9, 0)
            var textColorMap      = new Dictionary<int, string>();
            var highlightColorMap = new Dictionary<int, string>();
            foreach (var entry in colorConfig.Colors)
            {
                if (entry.ColorHex == null) continue;
                if (entry.TypeId == 1) textColorMap[entry.KeyNumber]      = entry.ColorHex;
                if (entry.TypeId == 2) highlightColorMap[entry.KeyNumber] = entry.ColorHex;
            }

            // Map WPF Key enum → keyNumber for digits 0-9
            static int DigitKeyNumber(Key k) => k switch
            {
                Key.D1 => 1, Key.D2 => 2, Key.D3 => 3, Key.D4 => 4, Key.D5 => 5,
                Key.D6 => 6, Key.D7 => 7, Key.D8 => 8, Key.D9 => 9, Key.D0 => 0,
                _ => -1
            };

            void OpenNewWindow(double offsetX = -1, double offsetY = -1)
            {
                string prefixTitle = "";
                var window = new Window
                {
                    Title = $"{prefixTitle}Minimal Notepad",
                    Width = settings.WindowWidth,
                    Height = settings.WindowHeight,
                    Background = Brushes.White,
                    Left = offsetX >= 0 ? offsetX : settings.WindowLeft,
                    Top = offsetY >= 0 ? offsetY : settings.WindowTop,
                    Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/notepad.ico"))
                };

                var dock = new DockPanel();

                var border = new Border
                {
                    Height = 1,
                    Background = Brushes.Gray
                };
                DockPanel.SetDock(border, Dock.Top);
                dock.Children.Add(border);

                var editor = new TextEditor
                {
                    ShowLineNumbers = false,
                    WordWrap = true,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = settings.FontSize,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Padding = new Thickness(8)
                };
                editor.TextChanged += (s, e) => 
                { 
                    editor.TextArea.Caret.BringCaretToView();

                    if (editor.Text.Length > 80000)
                    {
                        editor.Dispatcher.BeginInvoke(new Action(() => editor.Clear()));
                    }

                };
                dock.Children.Add(editor);

                // ── Rich-text formatting ───────────────────────────────────────
                var fmtManager = new FormattingManager(editor.Document);
                var colorizer  = new RichTextColorizer(fmtManager);
                editor.TextArea.TextView.LineTransformers.Add(colorizer);

                void ApplyFormatting(Action<int, int> action)
                {
                    int start = editor.SelectionStart;
                    int len   = editor.SelectionLength;
                    if (len == 0) return;

                    var before = fmtManager.TakeSnapshot();
                    action(start, start + len);
                    var after = fmtManager.TakeSnapshot();

                    // Push into AvalonEdit's undo stack → Ctrl+Z / Ctrl+Y work seamlessly
                    editor.Document.UndoStack.Push(
                        new FormattingUndoOperation(fmtManager, before, after, editor.TextArea.TextView));

                    editor.TextArea.TextView.Redraw();
                }
                // ─────────────────────────────────────────────────────────────

                editor.TextArea.Caret.PositionChanged += (s, e) =>
                {
                    var caret = editor.TextArea.Caret;
                    string viewPrefixTitle = string.IsNullOrEmpty(prefixTitle) ? "" : prefixTitle + " - ";
                    window.Title = $"{viewPrefixTitle}Minimal Notepad - Ln {caret.Line}, Col {caret.Column}";
                };

                editor.PreviewMouseWheel += (s, e) =>
                {
                    if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                    {
                        editor.FontSize += e.Delta > 0 ? 1 : -1;
                        settings.FontSize = editor.FontSize;
                        e.Handled = true;
                    }
                };

                editor.PreviewKeyDown += (s, e) =>
                {
                    if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                    {
                        // ── Formatting shortcuts ───────────────────────────────
                        if (e.Key == Key.B) { ApplyFormatting(fmtManager.ToggleBold);          e.Handled = true; return; }
                        if (e.Key == Key.I) { ApplyFormatting(fmtManager.ToggleItalic);        e.Handled = true; return; }
                        if (e.Key == Key.U) { ApplyFormatting(fmtManager.ToggleUnderline);     e.Handled = true; return; }
                        if (e.Key == Key.F5)
                        {
                            ApplyFormatting(fmtManager.ToggleStrikethrough);
                            e.Handled = true; return;
                        }

                        // ── Text color: Ctrl+1 … Ctrl+5 ───────────────────────
                        int digitKey = DigitKeyNumber(e.Key);
                        if (digitKey >= 1 && digitKey <= 5 && textColorMap.TryGetValue(digitKey, out var fgColor))
                        {
                            ApplyFormatting((s2, e2) => fmtManager.ToggleForeColor(s2, e2, fgColor));
                            e.Handled = true; return;
                        }
                        // ── Highlight: Ctrl+6 … Ctrl+9, Ctrl+0 ───────────────
                        if ((digitKey >= 6 || digitKey == 0) && highlightColorMap.TryGetValue(digitKey, out var bgColor))
                        {
                            ApplyFormatting((s2, e2) => fmtManager.ToggleBackColor(s2, e2, bgColor));
                            e.Handled = true; return;
                        }
                        // ─────────────────────────────────────────────────────
                        if (e.Key == Key.S || e.Key == Key.T)
                        {
                            e.Handled = true;

                            var inputWindow = new Window
                            {
                                Title = "Introdu titlul:",
                                Width = 300,
                                Height = 120,
                                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                                ResizeMode = ResizeMode.NoResize,
                                Owner = window,
                                Topmost = true,
                                Background = Brushes.White
                            };

                            var mainStack = new StackPanel
                            {
                                Margin = new Thickness(10),
                                VerticalAlignment = VerticalAlignment.Center,
                                HorizontalAlignment = HorizontalAlignment.Stretch
                            };

                            var textBox = new TextBox
                            {
                                Text = prefixTitle,
                                Margin = new Thickness(0, 0, 0, 10)
                            };

                            var buttonPanel = new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                HorizontalAlignment = HorizontalAlignment.Right
                            };

                            var okButton = new Button
                            {
                                Content = "OK",
                                Width = 60
                            };

                            void ApplyTitle()
                            {
                                prefixTitle = textBox.Text.Trim();
                                string viewPrefixTitle = string.IsNullOrEmpty(prefixTitle) ? "" : prefixTitle + " - ";
                                window.Title = $"{viewPrefixTitle}Minimal Notepad - Ln {editor.TextArea.Caret.Line}, Col {editor.TextArea.Caret.Column}";
                                inputWindow.Close();
                            }

                            okButton.Click += (s2, e2) => ApplyTitle();

                            textBox.PreviewKeyDown += (s3, e3) =>
                            {
                                if (e3.Key == Key.Enter)
                                {
                                    ApplyTitle();
                                    e3.Handled = true;
                                }
                                else if (e3.Key == Key.Escape)
                                {
                                    inputWindow.Close();
                                    e3.Handled = true;
                                }
                            };

                            buttonPanel.Children.Add(okButton);
                            mainStack.Children.Add(textBox);
                            mainStack.Children.Add(buttonPanel);
                            inputWindow.Content = mainStack;

                            // Focus după încărcare
                            inputWindow.Loaded += (s4, e4) =>
                            {
                                textBox.Focus();
                                textBox.SelectAll();
                            };

                            inputWindow.Show(); // NON-MODAL → nu blochează alte ferestre
                        }

                        if (e.Key == Key.N)
                        {
                            OpenNewWindow(window.Left + 30, window.Top + 30);
                            e.Handled = true;

                            var newWindow = windows[^1];
                            newWindow.Activate();
                            if (newWindow.Content is DockPanel dockNew)
                            {
                                foreach (var child in dockNew.Children)
                                {
                                    if (child is TextEditor ed)
                                    {
                                        ed.Focus();
                                        ed.TextArea.Caret.BringCaretToView();
                                        break;
                                    }
                                }
                            }
                        }
                        else if (e.Key == Key.OemPlus || e.Key == Key.Add)
                        {
                            editor.FontSize += 1;
                            settings.FontSize = editor.FontSize;
                            e.Handled = true;
                        }
                        else if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
                        {
                            editor.FontSize = Math.Max(6, editor.FontSize - 1);
                            settings.FontSize = editor.FontSize;
                            e.Handled = true;
                        }
                    }
                    else if (e.Key == Key.Space)
                    {
                        e.Handled = true;
                        int offset = editor.CaretOffset;
                        string fakeSpace = "\u00A0"; // non-breaking space
                        editor.Document.Insert(offset, fakeSpace);
                        editor.CaretOffset = offset + fakeSpace.Length;
                    }
                    else if ((e.Key == Key.Up || e.Key == Key.Down)
                             && (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
                             && !Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl))
                    {
                        MoveLines(e.Key == Key.Up);
                        e.Handled = true;
                    }
                };

                window.Content = dock;

                // ── Move lines with Alt+Up / Alt+Down ────────────────────────
                void MoveLines(bool moveUp)
                {
                    var doc      = editor.Document;
                    int selStart = editor.SelectionStart;
                    int selLength = editor.SelectionLength;

                    // Determine which lines the selection covers
                    int startLine = doc.GetLineByOffset(selStart).LineNumber;
                    int endLine;
                    if (selLength == 0)
                    {
                        endLine = startLine;
                    }
                    else
                    {
                        var lastSel = doc.GetLineByOffset(selStart + selLength);
                        // Selection ending exactly at the start of a line → don't include that line
                        endLine = (lastSel.Offset == selStart + selLength && lastSel.LineNumber > startLine)
                                  ? lastSel.LineNumber - 1 : lastSel.LineNumber;
                    }

                    if (moveUp   && startLine == 1)            return;
                    if (!moveUp  && endLine == doc.LineCount)  return;

                    // The full region: pivot line + block lines
                    int firstNum = moveUp ? startLine - 1 : startLine;
                    int lastNum  = moveUp ? endLine       : endLine + 1;
                    int count    = lastNum - firstNum + 1;

                    var contents   = new string[count];
                    var delimiters = new string[count];
                    int regionStart = doc.GetLineByNumber(firstNum).Offset;
                    int regionEnd   = regionStart;

                    for (int i = 0; i < count; i++)
                    {
                        var line      = doc.GetLineByNumber(firstNum + i);
                        contents[i]   = doc.GetText(line.Offset, line.Length);
                        delimiters[i] = doc.GetText(line.Offset + line.Length, line.DelimiterLength);
                        regionEnd     = line.Offset + line.Length + line.DelimiterLength;
                    }

                    // Rotate only the line contents; delimiters stay in their slot
                    // → newline structure is always preserved, including the no-newline last line
                    if (moveUp)
                    {
                        // [pivot, b1..bN] → [b1..bN, pivot]
                        string first = contents[0];
                        for (int i = 0; i < count - 1; i++) contents[i] = contents[i + 1];
                        contents[count - 1] = first;
                    }
                    else
                    {
                        // [b1..bN, pivot] → [pivot, b1..bN]
                        string last = contents[count - 1];
                        for (int i = count - 1; i > 0; i--) contents[i] = contents[i - 1];
                        contents[0] = last;
                    }

                    // Save caret offset within its line before the replace
                    var origStart = doc.GetLineByNumber(startLine);
                    var origEnd   = doc.GetLineByNumber(endLine);
                    int caretInLine  = selStart - origStart.Offset;
                    int selEndInLine = selLength > 0 ? (selStart + selLength) - origEnd.Offset : 0;

                    // Single Replace → single undo entry, no grouping needed
                    var sb = new System.Text.StringBuilder();
                    for (int i = 0; i < count; i++) sb.Append(contents[i]).Append(delimiters[i]);
                    doc.Replace(regionStart, regionEnd - regionStart, sb.ToString());

                    // Restore caret / selection at the moved block's new position
                    int newStartLine = moveUp ? startLine - 1 : startLine + 1;
                    int newEndLine   = moveUp ? endLine - 1   : endLine + 1;
                    var newFirst     = doc.GetLineByNumber(newStartLine);
                    var newLast      = doc.GetLineByNumber(newEndLine);

                    int newCaret = newFirst.Offset + Math.Min(caretInLine, newFirst.Length);
                    if (selLength == 0)
                    {
                        editor.CaretOffset = newCaret;
                    }
                    else
                    {
                        int newSelEnd = newLast.Offset + Math.Min(selEndInLine, newLast.Length);
                        editor.Select(newCaret, Math.Max(0, newSelEnd - newCaret));
                    }
                    editor.TextArea.Caret.BringCaretToView();
                }

                window.Closed += (s, e) =>
                {
                    settings.WindowLeft = window.Left;
                    settings.WindowTop = window.Top;
                    settings.WindowWidth = window.Width;
                    settings.WindowHeight = window.Height;
                    settings.FontSize = editor.FontSize;
                    File.WriteAllText(settingsFile, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
                    windows.Remove(window);
                };

                windows.Add(window);
                window.Show();
            }

            OpenNewWindow();
            app.Run();
        }
    }
}
