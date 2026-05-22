using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
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
        private bool          _singleLineMode = false;
        private TextBlock     _lineToggleIcon = null!;

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

            Title     = "Clipboard History";
            MinWidth  = 260;
            MinHeight = 300;
            ResizeMode            = ResizeMode.CanResize;
            Background            = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
            ShowInTaskbar         = false;

            // ── Restore last position / size ──────────────────────────────────
            var state = LoadWindowState();
            Width  = state.Width;
            Height = state.Height;
            _singleLineMode = state.SingleLine;
            if (state.HasPosition)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = state.Left;
                Top  = state.Top;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            // ── Slim scrollbar style (5 px, rounded thumb, no arrows) ─────────
            Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(@"
<ResourceDictionary
    xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style TargetType='{x:Type ScrollBar}'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Width'     Value='5'/>
    <Setter Property='MinWidth'  Value='5'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='{x:Type ScrollBar}'>
          <Grid Background='Transparent' Width='5'>
            <Track x:Name='PART_Track' IsDirectionReversed='true'>
              <Track.DecreaseRepeatButton>
                <RepeatButton Command='ScrollBar.PageUpCommand'
                              Opacity='0' Focusable='false' Height='0'/>
              </Track.DecreaseRepeatButton>
              <Track.Thumb>
                <Thumb>
                  <Thumb.Template>
                    <ControlTemplate TargetType='{x:Type Thumb}'>
                      <Border x:Name='Bd' Background='#C8C8C8'
                              CornerRadius='2.5' Margin='1,3,1,3'/>
                      <ControlTemplate.Triggers>
                        <Trigger Property='IsMouseOver' Value='True'>
                          <Setter TargetName='Bd' Property='Background' Value='#999999'/>
                        </Trigger>
                        <Trigger Property='IsDragging' Value='True'>
                          <Setter TargetName='Bd' Property='Background' Value='#777777'/>
                        </Trigger>
                      </ControlTemplate.Triggers>
                    </ControlTemplate>
                  </Thumb.Template>
                </Thumb>
              </Track.Thumb>
              <Track.IncreaseRepeatButton>
                <RepeatButton Command='ScrollBar.PageDownCommand'
                              Opacity='0' Focusable='false' Height='0'/>
              </Track.IncreaseRepeatButton>
            </Track>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>"));

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
            var toggleLabel = new TextBlock
            {
                Text              = "⇄  System History",
                FontSize          = 10.5,
                FontFamily        = new FontFamily("Segoe UI"),
                FontWeight        = FontWeights.Normal,
                Foreground        = new SolidColorBrush(Color.FromRgb(0x44, 0x72, 0xC4)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(6, 0, 6, 0)
            };
            var togglePill = new Border
            {
                Background      = new SolidColorBrush(Color.FromArgb(0x18, 0x44, 0x72, 0xC4)),
                BorderBrush     = new SolidColorBrush(Color.FromArgb(0x55, 0x44, 0x72, 0xC4)),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(10),
                Padding         = new Thickness(0, 2, 0, 2),
                Cursor          = Cursors.Hand,
                Child           = toggleLabel
            };
            togglePill.MouseEnter += (_, _) =>
                togglePill.Background = new SolidColorBrush(Color.FromArgb(0x30, 0x44, 0x72, 0xC4));
            togglePill.MouseLeave += (_, _) =>
                togglePill.Background = new SolidColorBrush(Color.FromArgb(0x18, 0x44, 0x72, 0xC4));
            togglePill.MouseLeftButtonUp += (_, _) =>
            {
                if (_mode == HistoryMode.App)
                {
                    _mode = HistoryMode.Global;
                    toggleLabel.Text = "⇄  Application History";
                    Title = "Clipboard History — System";
                    ClipboardHistory.HistoryChanged       -= OnHistoryChanged;
                    NormalClipboardHistory.HistoryChanged += OnHistoryChanged;
                }
                else
                {
                    _mode = HistoryMode.App;
                    toggleLabel.Text = "⇄  System History";
                    Title = "Clipboard History — Application";
                    NormalClipboardHistory.HistoryChanged -= OnHistoryChanged;
                    ClipboardHistory.HistoryChanged       += OnHistoryChanged;
                }
                RefreshCards();
            };

            var dot1 = MakeDot();
            var dot2 = MakeDot();

            var dot3 = MakeDot();

            _lineToggleIcon = new TextBlock
            {
                Text              = _singleLineMode ? "⊟" : "☰",
                FontSize          = 13,
                Cursor            = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground        = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)),
                ToolTip           = _singleLineMode ? "Switch to multi-line view" : "Switch to single-line view"
            };
            _lineToggleIcon.MouseEnter += (_, _) =>
                _lineToggleIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40));
            _lineToggleIcon.MouseLeave += (_, _) =>
                _lineToggleIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));
            _lineToggleIcon.MouseLeftButtonUp += (_, _) =>
            {
                _singleLineMode = !_singleLineMode;
                _lineToggleIcon.Text    = _singleLineMode ? "⊟" : "☰";
                _lineToggleIcon.ToolTip = _singleLineMode ? "Switch to multi-line view" : "Switch to single-line view";
                RefreshCards();
            };

            var footerRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin      = new Thickness(12, 5, 12, 9)
            };
            footerRow.Children.Add(openFolderLink);
            footerRow.Children.Add(dot1);
            footerRow.Children.Add(clearAllLink);
            footerRow.Children.Add(dot2);
            footerRow.Children.Add(togglePill);
            footerRow.Children.Add(dot3);
            footerRow.Children.Add(_lineToggleIcon);

            // Hide the toggle pill (and its separator dot) when global monitoring is off
            void UpdateToggleVisibility()
            {
                bool show = GlobalClipboardMonitor.IsEnabled;
                dot2.Visibility      = show ? Visibility.Visible : Visibility.Collapsed;
                togglePill.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                // If global was just turned off and we're in Global mode, snap back to App
                if (!show && _mode == HistoryMode.Global)
                {
                    _mode = HistoryMode.App;
                    toggleLabel.Text = "⇄  System History";
                    Title = "Clipboard History";
                    NormalClipboardHistory.HistoryChanged -= OnHistoryChanged;
                    ClipboardHistory.HistoryChanged       += OnHistoryChanged;
                    RefreshCards();
                }
            }
            UpdateToggleVisibility();
            GlobalClipboardMonitor.EnabledChanged += _ =>
                Dispatcher.InvokeAsync(UpdateToggleVisibility);

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
                SaveWindowState(Left, Top, Width, Height, _singleLineMode);
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

            // ── Preview ───────────────────────────────────────────────────────
            // single-line mode: NoWrap + Ellipsis per line, but lines still stack
            // and the card still has MaxHeight + fadeout for too-tall content.
            var previewBlock = BuildFormattedBlock(entry.PlainText, spans, 11, _singleLineMode);

            UIElement previewArea;
            Border?   fadeOverlay  = null;
            Border?   clipBox      = null;

            // Both modes use MaxHeight + fadeout; single-line mode just adds
            // NoWrap+Ellipsis so long lines truncate instead of wrapping.
            clipBox = new Border
            {
                MaxHeight    = 180,
                ClipToBounds = true,
                Child        = previewBlock
            };

            fadeOverlay = new Border
            {
                Height            = 24,
                VerticalAlignment = VerticalAlignment.Bottom,
                IsHitTestVisible  = false,
                Background        = MakeFade(Colors.White),
                Visibility        = Visibility.Collapsed
            };

            var previewGrid = new Grid();
            previewGrid.Children.Add(clipBox);
            previewGrid.Children.Add(fadeOverlay);
            previewArea = previewGrid;

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
            outerStack.Children.Add(previewArea);
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
                card.Background = new SolidColorBrush(hoverBgColor);
                if (fadeOverlay != null)
                    fadeOverlay.Background = MakeFade(hoverBgColor);
            };
            card.MouseLeave += (_, _) =>
            {
                card.Background = Brushes.White;
                if (fadeOverlay != null)
                    fadeOverlay.Background = MakeFade(Colors.White);
            };

            // ── After layout: decide fade + bubble based on actual overflow ─────
            {
                bool bubbleAttached = false;
                previewBlock.Loaded += (_, _) =>
                {
                    // Defer until after layout so ActualWidth is valid.
                    // Using DispatcherPriority.Background ensures we run after
                    // the full measure/arrange pass completes.
                    Dispatcher.InvokeAsync(() =>
                    {
                        if (previewBlock.ActualWidth <= 0) return;

                        previewBlock.Measure(new Size(previewBlock.ActualWidth, double.PositiveInfinity));
                        bool overflows = previewBlock.DesiredSize.Height > clipBox.MaxHeight;

                        fadeOverlay.Visibility = overflows ? Visibility.Visible : Visibility.Collapsed;
                        if (overflows && !bubbleAttached)
                        {
                            bubbleAttached = true;
                            AttachFollowMouseBubble(card, entry, spans);
                        }
                    }, System.Windows.Threading.DispatcherPriority.Background);
                };
            }

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
            double fontSize,
            bool singleLine = false)
        {
            var tb = new TextBlock
            {
                FontFamily   = new FontFamily("Segoe UI"),
                FontSize     = fontSize,
                TextWrapping = singleLine ? TextWrapping.NoWrap : TextWrapping.Wrap,
                TextTrimming = singleLine ? TextTrimming.CharacterEllipsis : TextTrimming.None,
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

        // ── Follow-mouse popup bubble ─────────────────────────────────────────

        static void AttachFollowMouseBubble(
            Border card,
            ClipboardEntry entry,
            List<FormattingManager.SpanRecord>? spans)
        {
            double maxH = SystemParameters.WorkArea.Height * 0.75;

            // Content TextBlock — clipped, no scrollbar
            var content = BuildFormattedBlock(entry.PlainText, spans, 12);
            content.Margin = new Thickness(12, 10, 12, 30); // extra bottom padding for fade

            var clipHost = new Border
            {
                MaxHeight    = maxH,
                ClipToBounds = true,
                Child        = content
            };

            // Bottom fade gradient — only visible when content overflows
            var fadeBar = new System.Windows.Shapes.Rectangle
            {
                Height            = 32,
                VerticalAlignment = VerticalAlignment.Bottom,
                IsHitTestVisible  = false,
                Fill              = new LinearGradientBrush(
                    Color.FromArgb(0, 0xFF, 0xFF, 0xFF),
                    Colors.White,
                    90)
            };

            var bubbleGrid = new Grid { MaxWidth = 480, MinWidth = 180 };
            bubbleGrid.Children.Add(clipHost);
            bubbleGrid.Children.Add(fadeBar);

            var bubbleBorder = new Border
            {
                Background      = Brushes.White,
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(6),
                Child           = bubbleGrid,
                Effect          = new DropShadowEffect
                {
                    Color       = Colors.Black,
                    Opacity     = 0.15,
                    BlurRadius  = 10,
                    ShadowDepth = 2,
                    Direction   = 270
                }
            };

            var popup = new Popup
            {
                Child              = bubbleBorder,
                AllowsTransparency = true,
                Placement          = PlacementMode.Absolute,
                StaysOpen          = true,
                PopupAnimation     = PopupAnimation.None
            };

            // ── 1.5 s hover-still delay before showing ────────────────────────
            var hoverTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(750)
            };

            Point lastMouseDiu = default;

            hoverTimer.Tick += (_, _) =>
            {
                hoverTimer.Stop();
                popup.HorizontalOffset = lastMouseDiu.X + 18;
                popup.VerticalOffset   = lastMouseDiu.Y + 18;
                popup.IsOpen = true;
            };

            card.MouseEnter += (_, me) =>
            {
                lastMouseDiu = ToScreenDiu(card, me.GetPosition(card));
                hoverTimer.Stop();
                hoverTimer.Start();
            };

            card.MouseMove += (_, me) =>
            {
                lastMouseDiu = ToScreenDiu(card, me.GetPosition(card));
                if (popup.IsOpen)
                {
                    // already open — just reposition
                    PositionPopup(popup, bubbleBorder, lastMouseDiu);
                }
                else
                {
                    // not yet open — reset the delay on movement
                    hoverTimer.Stop();
                    hoverTimer.Start();
                }
            };

            popup.Opened += (_, _) =>
                PositionPopup(popup, bubbleBorder, lastMouseDiu);

            card.MouseLeave += (_, _) =>
            {
                hoverTimer.Stop();
                popup.IsOpen = false;
            };
        }

        /// <summary>Converts a local element point to screen coords in device-independent units.</summary>
        static Point ToScreenDiu(Visual source, Point localPoint)
        {
            var physPx = source.PointToScreen(localPoint);
            var ps     = PresentationSource.FromVisual(source);
            if (ps?.CompositionTarget == null) return physPx;
            return ps.CompositionTarget.TransformFromDevice.Transform(physPx);
        }

        static void PositionPopup(Popup popup, FrameworkElement child, Point mouseDiu)
        {
            double popupW = child.ActualWidth  > 0 ? child.ActualWidth  : 480;
            double popupH = child.ActualHeight > 0 ? child.ActualHeight : 200;

            var work = SystemParameters.WorkArea;

            // ── X: appear to the right of cursor; flip left if it overflows ────
            double x = mouseDiu.X + 18;
            if (x + popupW > work.Right)
                x = mouseDiu.X - popupW - 8;
            x = Math.Max(work.Left, x);

            // ── Y: try below → try above → center vertically ──────────────────
            double y;
            double yBelow = mouseDiu.Y + 18;
            double yAbove = mouseDiu.Y - popupH - 8;

            if (yBelow + popupH <= work.Bottom)
                y = yBelow;                                       // fits below
            else if (yAbove >= work.Top)
                y = yAbove;                                       // fits above
            else
                y = work.Top + (work.Height - popupH) / 2.0;     // center as last resort

            y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - popupH));

            popup.HorizontalOffset = x;
            popup.VerticalOffset   = y;
        }

        // ── Window state persistence ──────────────────────────────────────────

        static readonly string WindowStatePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MinimalNotepad", "clipboard_window_state.json");

        record ClipboardWindowState(double Left, double Top, double Width, double Height, bool HasPosition, bool SingleLine = false);

        static ClipboardWindowState LoadWindowState()
        {
            try
            {
                if (File.Exists(WindowStatePath))
                {
                    var d = JsonSerializer.Deserialize<Dictionary<string, double>>(
                                File.ReadAllText(WindowStatePath));
                    if (d != null)
                    {
                        bool singleLine = d.TryGetValue("SingleLine", out double sl) && sl > 0;
                        return new ClipboardWindowState(d["Left"], d["Top"], d["Width"], d["Height"], true, singleLine);
                    }
                }
            }
            catch { }
            return new ClipboardWindowState(0, 0, 380, 560, false);
        }

        static void SaveWindowState(double left, double top, double width, double height, bool singleLine = false)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(WindowStatePath)!);
                var d = new Dictionary<string, double>
                {
                    ["Left"] = left, ["Top"] = top, ["Width"] = width, ["Height"] = height,
                    ["SingleLine"] = singleLine ? 1.0 : 0.0
                };
                File.WriteAllText(WindowStatePath, JsonSerializer.Serialize(d));
            }
            catch { }
        }
    }
}
