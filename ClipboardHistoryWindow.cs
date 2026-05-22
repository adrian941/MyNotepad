using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using MinimalNotepad.Formatting;

namespace MinimalNotepad
{
    class ClipboardHistoryWindow : Window
    {
        enum HistoryMode { App, Global }

        private static ClipboardHistoryWindow? _instance;

        private NotepadWindow _targetWindow;
        private StackPanel    _cardsPanel = null!;
        private bool          _suppressDeactivationClose;
        private HistoryMode   _mode = HistoryMode.App;

        // ── Singleton ─────────────────────────────────────────────────────────

        public static void ShowOrActivate(NotepadWindow target)
        {
            if (_instance != null)
            {
                _instance._targetWindow = target;
                _instance.RefreshCards();
                _instance.Activate();
                return;
            }

            _instance = new ClipboardHistoryWindow(target);
            _instance.Show();
        }

        // ── Constructor ───────────────────────────────────────────────────────

        ClipboardHistoryWindow(NotepadWindow target)
        {
            _targetWindow = target;

            Title                 = "Clipboard History";
            Width                 = 380;
            Height                = 560;
            MinWidth              = 260;
            MinHeight             = 300;
            ResizeMode            = ResizeMode.CanResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background            = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
            ShowInTaskbar         = false;

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(10, 10, 10, 10)
            };

            _cardsPanel = new StackPanel();
            scroll.Content = _cardsPanel;

            // ── Footer: open folder  ·  clear all  ·  toggle mode ────────────
            var openFolderLink = MakeFooterLink("📂  Open history folder");
            openFolderLink.MouseLeftButtonUp += (_, _) =>
            {
                var dir = System.IO.Path.GetDirectoryName(
                    _mode == HistoryMode.App ? ClipboardHistory.SavePath : NormalClipboardHistory.SavePath)!;
                if (System.IO.Directory.Exists(dir))
                    System.Diagnostics.Process.Start("explorer.exe", dir);
            };

            var clearAllLink = MakeFooterLink("🗑  Clear all");
            clearAllLink.MouseLeftButtonUp += (_, _) =>
            {
                _suppressDeactivationClose = true;
                var result = MessageBox.Show(
                    "Are you sure you want to clear the entire clipboard history?",
                    "Clear History",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                _suppressDeactivationClose = false;
                if (result == MessageBoxResult.Yes)
                {
                    if (_mode == HistoryMode.App) ClipboardHistory.ClearAll();
                    else                          NormalClipboardHistory.ClearAll();
                }
                Activate();
            };

            // Toggle pill: shows current mode, click to switch
            var toggleLink = MakeFooterLink("📋  App");
            toggleLink.MouseLeftButtonUp += (_, _) =>
            {
                if (_mode == HistoryMode.App)
                {
                    _mode = HistoryMode.Global;
                    toggleLink.Text = "🌐  Global";
                    Title = "Clipboard History — Global";
                    ClipboardHistory.HistoryChanged       -= OnHistoryChanged;
                    NormalClipboardHistory.HistoryChanged += OnHistoryChanged;
                }
                else
                {
                    _mode = HistoryMode.App;
                    toggleLink.Text = "📋  App";
                    Title = "Clipboard History — App";
                    NormalClipboardHistory.HistoryChanged -= OnHistoryChanged;
                    ClipboardHistory.HistoryChanged       += OnHistoryChanged;
                }
                RefreshCards();
            };

            var dot1 = MakeDot();
            var dot2 = MakeDot();

            var footerRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin      = new Thickness(12, 5, 12, 9)
            };
            footerRow.Children.Add(openFolderLink);
            footerRow.Children.Add(dot1);
            footerRow.Children.Add(clearAllLink);
            footerRow.Children.Add(dot2);
            footerRow.Children.Add(toggleLink);

            var separator = new Border
            {
                Height     = 1,
                Margin     = new Thickness(0, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8))
            };

            var outerDock = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(separator,  Dock.Bottom);
            DockPanel.SetDock(footerRow,  Dock.Bottom);
            outerDock.Children.Add(separator);
            outerDock.Children.Add(footerRow);
            outerDock.Children.Add(scroll);
            Content = outerDock;

            RefreshCards();

            // Live updates
            ClipboardHistory.HistoryChanged += OnHistoryChanged;

            // Close when user clicks outside any Minimal Notepad window
            Deactivated += OnDeactivated;

            Closed += (_, _) =>
            {
                ClipboardHistory.HistoryChanged       -= OnHistoryChanged;
                NormalClipboardHistory.HistoryChanged -= OnHistoryChanged;
                _instance = null;
            };
        }

        // ── Event handlers ────────────────────────────────────────────────────

        void OnHistoryChanged() => RefreshCards();

        void OnDeactivated(object? sender, EventArgs e)
        {
            if (_suppressDeactivationClose) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_suppressDeactivationClose) return;
                foreach (Window w in Application.Current.Windows)
                    if (w != this && w.IsActive) return;
                Close();
            }));
        }

        // ── Card list ─────────────────────────────────────────────────────────

        void RefreshCards()
        {
            _cardsPanel.Children.Clear();

            var entries = _mode == HistoryMode.App
                ? ClipboardHistory.Entries
                : NormalClipboardHistory.Entries;

            if (entries.Count == 0)
            {
                _cardsPanel.Children.Add(new TextBlock
                {
                    Text                = "No clipboard history yet.",
                    FontSize            = 12,
                    Foreground          = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
                    Margin              = new Thickness(0, 20, 0, 0),
                    TextAlignment       = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                });
                return;
            }

            foreach (var entry in entries)
                _cardsPanel.Children.Add(BuildCard(entry));
        }

        UIElement BuildCard(ClipboardEntry entry)
        {
            var spans = RichClipboard.DeserializeSpans(entry.RichJson);

            // ── Preview (clipped at 180px = 2× original) ──────────────────────
            var previewBlock = BuildFormattedBlock(entry.PlainText, spans, 11);

            var clipBox = new Border
            {
                MaxHeight    = 180,
                ClipToBounds = true,
                Child        = previewBlock
            };

            // Gradient fade at the bottom of the clip area
            var fadeOverlay = new Border
            {
                Height            = 24,
                VerticalAlignment = VerticalAlignment.Bottom,
                IsHitTestVisible  = false,
                Background        = MakeFade(Colors.White)
            };

            var previewGrid = new Grid();
            previewGrid.Children.Add(clipBox);
            previewGrid.Children.Add(fadeOverlay);

            // ── Footer: timestamp left, delete button right ───────────────────
            var timestamp = new TextBlock
            {
                Text              = entry.CopiedAt.ToString("d MMM yyyy  H:mm:ss"),
                FontSize          = 10,
                Foreground        = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 5, 0, 0)
            };

            var deleteBtn = new TextBlock
            {
                Text              = "✕",
                FontSize          = 11,
                Foreground        = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB)),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor            = Cursors.Hand,
                Margin            = new Thickness(8, 5, 0, 0),
                ToolTip           = "Remove from history"
            };
            deleteBtn.MouseEnter += (_, _) =>
                deleteBtn.Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0x30, 0x30));
            deleteBtn.MouseLeave += (_, _) =>
                deleteBtn.Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB));
            deleteBtn.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true; // don't trigger card paste
                if (_mode == HistoryMode.App) ClipboardHistory.Remove(entry);
                else                          NormalClipboardHistory.Remove(entry);
            };

            var footer = new DockPanel { LastChildFill = false, Margin = new Thickness(0) };
            DockPanel.SetDock(deleteBtn, Dock.Right);
            DockPanel.SetDock(timestamp, Dock.Left);
            footer.Children.Add(deleteBtn);
            footer.Children.Add(timestamp);

            // ── Outer layout: preview + footer ────────────────────────────────
            var outerStack = new StackPanel();
            outerStack.Children.Add(previewGrid);
            outerStack.Children.Add(footer);

            // ── Card border ───────────────────────────────────────────────────
            var card = new Border
            {
                Background      = Brushes.White,
                CornerRadius    = new CornerRadius(6),
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
                BorderThickness = new Thickness(1),
                Margin          = new Thickness(0, 0, 0, 8),
                Padding         = new Thickness(12, 10, 12, 10),
                Cursor          = Cursors.Hand,
                Child           = outerStack,
                Effect          = new DropShadowEffect
                {
                    Color       = Colors.Black,
                    Opacity     = 0.07,
                    BlurRadius  = 5,
                    ShadowDepth = 1,
                    Direction   = 270
                }
            };

            // ── Hover tint ────────────────────────────────────────────────────
            var hoverBgColor = Color.FromRgb(0xEE, 0xF4, 0xFF);

            card.MouseEnter += (_, _) =>
            {
                card.Background        = new SolidColorBrush(hoverBgColor);
                fadeOverlay.Background = MakeFade(hoverBgColor);
            };
            card.MouseLeave += (_, _) =>
            {
                card.Background        = Brushes.White;
                fadeOverlay.Background = MakeFade(Colors.White);
            };

            // ── Tooltip bubble — full content, max 75% screen height ──────────
            double maxTooltipH    = SystemParameters.WorkArea.Height * 0.75;
            var    tooltipContent = BuildFormattedBlock(entry.PlainText, spans, 12);

            var tooltipScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = maxTooltipH,
                MaxWidth  = 480,
                Padding   = new Thickness(12, 10, 12, 10),
                Content   = tooltipContent
            };

            card.ToolTip = new ToolTip
            {
                Content         = tooltipScroll,
                HasDropShadow   = true,
                Padding         = new Thickness(0),
                Background      = Brushes.White,
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                BorderThickness = new Thickness(1)
            };
            ToolTipService.SetInitialShowDelay(card, 350);
            ToolTipService.SetShowDuration(card, int.MaxValue);

            // ── Click → paste ─────────────────────────────────────────────────
            card.MouseLeftButtonUp += (_, _) => PasteEntry(entry, spans);

            return card;
        }

        void PasteEntry(ClipboardEntry entry, List<FormattingManager.SpanRecord>? spans)
        {
            _targetWindow.Activate();
            _targetWindow.PasteContent(entry.PlainText, spans);
        }

        // ── Footer helpers ────────────────────────────────────────────────────

        static TextBlock MakeFooterLink(string text)
        {
            var tb = new TextBlock
            {
                Text              = text,
                FontSize          = 11,
                FontWeight        = FontWeights.Light,
                Foreground        = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)),
                Cursor            = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            tb.MouseEnter += (_, _) =>
                tb.Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
            tb.MouseLeave += (_, _) =>
                tb.Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));
            return tb;
        }

        static TextBlock MakeDot() => new TextBlock
        {
            Text              = "·",
            FontSize          = 11,
            Foreground        = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(6, 0, 6, 0)
        };

        // ── Rich text helpers ─────────────────────────────────────────────────

        static TextBlock BuildFormattedBlock(
            string text,
            List<FormattingManager.SpanRecord>? spans,
            double fontSize)
        {
            var tb = new TextBlock
            {
                FontFamily   = new FontFamily("Segoe UI"),
                FontSize     = fontSize,
                TextWrapping = TextWrapping.Wrap,
                Foreground   = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A))
            };

            if (string.IsNullOrEmpty(text)) return tb;

            if (spans == null || spans.Count == 0)
            {
                tb.Inlines.Add(new Run(text));
                return tb;
            }

            // Collect span break-points and sort them
            var pts = new SortedSet<int> { 0, text.Length };
            foreach (var s in spans)
            {
                pts.Add(Math.Clamp(s.Start, 0, text.Length));
                pts.Add(Math.Clamp(s.End,   0, text.Length));
            }

            var sorted = pts.ToList();
            for (int i = 0; i < sorted.Count - 1; i++)
            {
                int segStart = sorted[i];
                int segEnd   = sorted[i + 1];
                if (segStart >= segEnd) continue;

                string segment = text.Substring(segStart, segEnd - segStart);

                // Merge all spans whose clamped range fully covers this segment
                bool    bold = false, italic = false, under = false, strike = false;
                string? fore = null, back = null;

                foreach (var s in spans)
                {
                    int ss = Math.Clamp(s.Start, 0, text.Length);
                    int se = Math.Clamp(s.End,   0, text.Length);
                    if (ss > segStart || se < segEnd) continue;

                    bold   |= s.Format.Bold;
                    italic |= s.Format.Italic;
                    under  |= s.Format.Underline;
                    strike |= s.Format.Strikethrough;
                    if (s.Format.ForeColorHex != null) fore = s.Format.ForeColorHex;
                    if (s.Format.BackColorHex != null) back = s.Format.BackColorHex;
                }

                var run = new Run(segment);
                if (bold)   run.FontWeight = FontWeights.Bold;
                if (italic) run.FontStyle  = FontStyles.Italic;
                if (fore != null) run.Foreground = HexBrush(fore);
                if (back != null) run.Background = HexBrush(back);

                if (under || strike)
                {
                    var td = new TextDecorationCollection();
                    if (under)  td.Add(TextDecorations.Underline[0]);
                    if (strike) td.Add(TextDecorations.Strikethrough[0]);
                    run.TextDecorations = td;
                }

                tb.Inlines.Add(run);
            }

            return tb;
        }

        static SolidColorBrush HexBrush(string hex) =>
            new((Color)ColorConverter.ConvertFromString(hex));

        static LinearGradientBrush MakeFade(Color solidColor)
        {
            var transparent = Color.FromArgb(0, solidColor.R, solidColor.G, solidColor.B);
            return new LinearGradientBrush(transparent, solidColor, 90);
        }
    }
}
