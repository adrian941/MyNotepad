using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using MinimalNotepad.Config;
using MinimalNotepad.Formatting;

namespace MinimalNotepad
{
    class NotepadWindow : Window
    {
        private readonly AppSettings              _settings;
        private readonly string                   _settingsFile;
        private readonly IReadOnlyDictionary<int, string> _textColorMap;
        private readonly IReadOnlyDictionary<int, string> _highlightColorMap;
        private readonly IReadOnlyDictionary<int, string> _darkTextColorMap;
        private readonly IReadOnlyDictionary<int, string> _strongHighlightMap;
        private readonly IReadOnlyDictionary<int, string> _codeHighlightMap;
        private readonly IReadOnlyDictionary<int, string> _codeStrongHighlightMap;
        private readonly IReadOnlyList<ColorEntry>        _colorEntries;
        private readonly List<NotepadWindow>      _allWindows;

        private string     _prefixTitle   = "";
        private string?    _savedFileName = null;   // null = never saved (library files: name without ext)
        private string?    _externalPath  = null;   // non-null = opened from outside the Saved folder; Ctrl+S saves here in-place
        private bool       _isDirty       = false;  // unsaved changes since last save
        private bool       _isCodeOnlyMode = false;

        public string? SavedFileName => _savedFileName;
        private TextEditor _editor        = null!;
        private FormattingManager _fmtManager = null!;
        private FoldingManager                _foldingManager         = null!;
        private readonly List<FoldingSection>                     _codeBlockFoldings  = new();
        private readonly Dictionary<FoldingSection, CodeBlockRegion> _foldingToRegion = new();
        private CodeBlockCollapseGenerator    _collapseGenerator      = null!;
        private CodeSyntaxColorizer           _codeColorizer          = null!;
        private CodeBlockBackgroundRenderer    _codeRenderer              = null!;
        private CodeBlockLineNumberRenderer    _codeLineNumberRenderer    = null!;
        private CodeBlockLineNumberGenerator   _codeLineNumberGenerator   = null!;
        private CodeBlockPaddingGenerator      _codePaddingGenerator      = null!;
        private CodeBlockFontSizeTransformer  _codeFontSizeTransformer = null!;
        private CodeBlockCopyOverlay          _copyOverlay            = null!;
        private MultiCaretController          _multiCaret             = null!;
        private bool                          _formattingGroupPending;
        private System.Windows.Controls.Canvas _overlayCanvas         = null!;
        private System.Windows.Threading.DispatcherTimer _reParseTimer = null!;
        private System.Windows.Threading.DispatcherTimer? _windowStateSaveTimer;

        public NotepadWindow(
            AppSettings               settings,
            string                    settingsFile,
            IReadOnlyList<ColorEntry> colorEntries,
            List<NotepadWindow>       allWindows,
            double offsetX = -1,
            double offsetY = -1)
        {
            var (textColorMap, highlightColorMap, darkTextColorMap, strongHighlightMap, codeHighlightMap, codeStrongHighlightMap) = ConfigLoader.BuildColorMaps(colorEntries);
            _settings          = settings;
            _settingsFile      = settingsFile;
            _textColorMap      = textColorMap;
            _highlightColorMap = highlightColorMap;
            _darkTextColorMap  = darkTextColorMap;
            _strongHighlightMap     = strongHighlightMap;
            _codeHighlightMap       = codeHighlightMap;
            _codeStrongHighlightMap = codeStrongHighlightMap;
            _colorEntries      = colorEntries;
            _allWindows        = allWindows;

            InitializeWindow(offsetX, offsetY);
            InitializeEditor();
            InitializeFormatting();
            WireEvents();

            _allWindows.Add(this);

            // Global clipboard monitor — started once; subsequent windows are no-ops
            GlobalClipboardMonitor.IsEnabled = _settings.SaveGlobalClipboard;
            GlobalClipboardMonitor.EnabledChanged += OnGlobalClipboardEnabledChanged;

            // Ensure code blocks are parsed after window is fully loaded
            Loaded += (_, _) => ReparseCodeBlocks();
        }

        void OnGlobalClipboardEnabledChanged(bool enabled)
        {
            _settings.SaveGlobalClipboard = enabled;
            ConfigLoader.SaveSettings(_settings, _settingsFile);
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
        }

        void UpdateCodeOnlyMode(List<CodeBlockRegion> regions)
        {
            bool dark = IsCodeBlockOnly(regions);
            if (dark == _isCodeOnlyMode) return;
            _isCodeOnlyMode = dark;

            var bgBrush = dark
                ? new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28))
                : Brushes.White;
            if (bgBrush is SolidColorBrush scb && !scb.IsFrozen) scb.Freeze();
            Background         = bgBrush;
            _editor.Background = bgBrush;
            _codeRenderer.IsDarkMode            = dark;
            _codeLineNumberRenderer.IsDarkMode  = dark;
            _codeLineNumberGenerator.IsDarkMode = dark;

            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                int colorRef = dark ? 0x002E2E2E : 0x00FFFFFF;
                DwmSetWindowAttribute(hwnd, 35 /* DWMWA_CAPTION_COLOR */, ref colorRef, sizeof(int));
            }
        }

        bool IsCodeBlockOnly(List<CodeBlockRegion> regions)
        {
            if (regions.Count != 1) return false;

            var doc       = _editor.Document;
            var r         = regions[0];
            var openLine  = doc.GetLineByNumber(r.FenceOpenLine);
            var closeLine = doc.GetLineByNumber(r.FenceCloseLine);
            int blockStart = openLine.Offset;
            int blockEnd   = closeLine.Offset + closeLine.TotalLength;

            if (blockStart > 0)
                foreach (char c in doc.GetText(0, blockStart))
                    if (!char.IsWhiteSpace(c)) return false;

            int afterLen = doc.TextLength - blockEnd;
            if (afterLen > 0)
                foreach (char c in doc.GetText(blockEnd, afterLen))
                    if (!char.IsWhiteSpace(c)) return false;

            return true;
        }

        // ── Window shell ──────────────────────────────────────────────────────

        void InitializeWindow(double offsetX, double offsetY)
        {
            Title      = "Minimal Notepad — Press Ctrl+H for help";
            Width      = _settings.WindowWidth;
            Height     = _settings.WindowHeight;
            Background = Brushes.White;
            Left       = offsetX >= 0 ? offsetX : _settings.WindowLeft;
            Top        = offsetY >= 0 ? offsetY : _settings.WindowTop;
            Icon       = new System.Windows.Media.Imaging.BitmapImage(
                             new Uri("pack://application:,,,/notepad.ico"));

            var dock = new DockPanel();

            var separator = new Border { Height = 1, Background = Brushes.Gray };
            DockPanel.SetDock(separator, Dock.Top);
            dock.Children.Add(separator);

            _editor = new TextEditor
            {
                ShowLineNumbers              = false,
                WordWrap                     = true,
                FontFamily                   = new FontFamily("Consolas"),
                FontSize                     = _settings.FontSize,
                VerticalScrollBarVisibility  = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding                      = new Thickness(8)
            };
            ApplyThinScrollBarStyle();
            var editorContainer = new Grid();
            editorContainer.Children.Add(_editor);
            _overlayCanvas = new System.Windows.Controls.Canvas();
            editorContainer.Children.Add(_overlayCanvas);

            dock.Children.Add(editorContainer);

            Content = dock;

            // Register Ctrl+Alt+V as a window-level command binding — this has higher
            // priority than WPF's Alt menu activation and fires reliably even when
            // Alt briefly captures the focus system.
            var openHistoryCmd = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(openHistoryCmd, (_, _) =>
                ClipboardHistoryWindow.ShowOrActivateClipboard(this)));
            InputBindings.Add(new KeyBinding(openHistoryCmd,
                new KeyGesture(Key.V, ModifierKeys.Control | ModifierKeys.Alt)));
        }

        // ── Rich-text formatting setup ────────────────────────────────────────

        void InitializeFormatting()
        {
            _foldingManager = FoldingManager.Install(_editor.TextArea);
            // Remove the +/- fold margin button on the left.
            var foldMargin = _editor.TextArea.LeftMargins.OfType<FoldingMargin>().FirstOrDefault();
            if (foldMargin != null) _editor.TextArea.LeftMargins.Remove(foldMargin);
            // Keep AvalonEdit's built-in FoldingElementGenerator in place — it implements
            // ITextViewConnect, which is what registers the TextView with the FoldingManager so
            // FoldingSection.IsFolded can actually collapse lines in the HeightTree. Removing it
            // breaks collapsing entirely ("Line N skipped but not collapsed").
            // Instead, we insert our own generator at index 0. When both report interest at the
            // same fold offset, AvalonEdit breaks the tie in favour of the first generator in the
            // list — so ours renders a plain non-interactive "···" while the built-in still does
            // the HeightTree collapse work behind the scenes.
            _collapseGenerator = new CodeBlockCollapseGenerator(_foldingManager);
            _editor.TextArea.TextView.ElementGenerators.Insert(0, _collapseGenerator);
            _fmtManager           = new FormattingManager(_editor.Document);
            _codeColorizer        = new CodeSyntaxColorizer(_fmtManager, _highlightColorMap, _strongHighlightMap, _codeHighlightMap, _codeStrongHighlightMap);
            _codeRenderer         = new CodeBlockBackgroundRenderer();
            _codePaddingGenerator = new CodeBlockPaddingGenerator();

            _codeLineNumberRenderer  = new CodeBlockLineNumberRenderer();
            _codeLineNumberGenerator = new CodeBlockLineNumberGenerator { FontSize = _settings.FontSize };
            _editor.TextArea.TextView.BackgroundRenderers.Add(_codeRenderer);
            _editor.TextArea.TextView.BackgroundRenderers.Add(_codeLineNumberRenderer);
            // LineNumberGenerator must be before PaddingGenerator so its 0-length element
            // is inserted first, then PaddingGenerator handles the first document char.
            _editor.TextArea.TextView.ElementGenerators.Add(_codeLineNumberGenerator);
            _editor.TextArea.TextView.ElementGenerators.Add(_codePaddingGenerator);
            _codeFontSizeTransformer = new CodeBlockFontSizeTransformer();

            _editor.TextArea.TextView.LineTransformers.Add(new MinimalNotepad.Formatting.RichTextColorizer(_fmtManager));
            _editor.TextArea.TextView.LineTransformers.Add(_codeColorizer);
            _editor.TextArea.TextView.LineTransformers.Add(_codeFontSizeTransformer);
            _editor.TextArea.TextView.LineTransformers.Add(new MinimalNotepad.Formatting.SelectionForegroundOverride(_editor.TextArea));
            _editor.TextArea.TextView.ElementGenerators.Add(new NonBreakingHyphenGenerator());

            _copyOverlay = new CodeBlockCopyOverlay(
                _overlayCanvas, _editor.TextArea.TextView, _editor.Document);

            _reParseTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _reParseTimer.Tick += (_, _) => { _reParseTimer.Stop(); ReparseCodeBlocks(); };
        }

        void ReparseCodeBlocks()
        {
            var regions = CodeBlockParser.Parse(_editor.Document);

            // Rebuild FoldingManager sections for StartMinimized blocks (> 20 content lines)
            foreach (var f in _codeBlockFoldings) _foldingManager.RemoveFolding(f);
            _codeBlockFoldings.Clear();
            _foldingToRegion.Clear();
            foreach (var region in regions)
            {
                if (!region.StartMinimized) continue;
                int contentLines = region.FenceCloseLine - region.FenceOpenLine - 1;
                if (contentLines <= 20) continue;
                int line21Num = region.FenceOpenLine + 21;
                if (line21Num >= region.FenceCloseLine) continue;
                int startOffset = _editor.Document.GetLineByNumber(line21Num).Offset;
                // End at the LAST content line, not the closing fence — otherwise the
                // FoldingSection collapses through the ``` line and it loses its gray fence
                // background. Ending here keeps the closing fence visible.
                int endOffset   = _editor.Document.GetLineByNumber(region.FenceCloseLine - 1).EndOffset;
                if (startOffset >= endOffset) continue;
                var section = _foldingManager.CreateFolding(startOffset, endOffset);
                section.Title    = " ···";
                section.IsFolded = true;
                _codeBlockFoldings.Add(section);
                _foldingToRegion[section] = region;
            }

            _codeColorizer.UpdateBlocks(_editor.Document, regions);
            _codeRenderer.UpdateRegions(regions);
            _codePaddingGenerator.UpdateRegions(regions);
            _codeFontSizeTransformer.UpdateRegions(_codeColorizer.CurrentBlocks);
            _codeLineNumberRenderer.IsDarkMode  = _isCodeOnlyMode;
            _codeLineNumberGenerator.FontSize   = _editor.FontSize;
            _codeLineNumberGenerator.UpdateRegions(regions);
            _copyOverlay.UpdateRegions(regions);
            UpdateCodeOnlyMode(regions);
            _editor.TextArea.TextView.Redraw();
        }

        // ── Event wiring ──────────────────────────────────────────────────────

        void InitializeEditor()
        {
            // Placeholder — ordering: InitializeEditor runs before InitializeFormatting
            // so _editor is ready; event wiring happens in WireEvents after both.
        }

        /// <summary>
        /// Opens an undo group, pushes a formatting-restore op (the "before" snapshot),
        /// then defers closing the group so AvalonEdit's text edit lands inside it.
        /// One Ctrl+Z then restores BOTH text AND formatting in a single undo step.
        /// Guard: _formattingGroupPending prevents nested groups on rapid keystrokes.
        /// </summary>
        void TryPushFormattingUndoBeforeEdit()
        {
            if (_fmtManager.Spans.Count == 0) return;
            if (_formattingGroupPending) return;

            var snapshot = _fmtManager.TakeSnapshot();
            _editor.Document.UndoStack.StartUndoGroup();
            _editor.Document.UndoStack.Push(new FormattingRestoreOperation(
                _fmtManager, snapshot, _editor.TextArea.TextView));
            _formattingGroupPending = true;

            _editor.Dispatcher.BeginInvoke(DispatcherPriority.Input, (Action)(() =>
            {
                _editor.Document.UndoStack.EndUndoGroup();
                _formattingGroupPending = false;
            }));
        }

        void WireEvents()
        {
            _multiCaret = new MultiCaretController(_editor, ApplyStickyFormatting);
            _multiCaret.PreEditHook = () =>
            {
                if (_fmtManager.Spans.Count == 0) return;
                var snapshot = _fmtManager.TakeSnapshot();
                _editor.Document.UndoStack.Push(new FormattingRestoreOperation(
                    _fmtManager, snapshot, _editor.TextArea.TextView));
            };

            _editor.TextChanged += OnTextChanged;
            _editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
            _editor.PreviewMouseWheel += OnPreviewMouseWheel;
            _editor.PreviewKeyDown    += OnPreviewKeyDown;
            _editor.TextArea.TextEntered += OnTextEntered;

            // When typing over a selection (single-cursor), the selection is deleted.
            // Wrap with a formatting undo group so Ctrl+Z restores spans too.
            _editor.TextArea.PreviewTextInput += (_, _) =>
            {
                if (!_editor.TextArea.Selection.IsEmpty && !_multiCaret.Active)
                    TryPushFormattingUndoBeforeEdit();
            };
            Closed += OnWindowClosed;

            _editor.TextArea.TextView.ScrollOffsetChanged += (_, _) => _copyOverlay.UpdatePositions();
            _editor.TextArea.TextView.VisualLinesChanged   += (_, _) => _copyOverlay.UpdatePositions();
            SizeChanged     += (_, _) => ScheduleWindowStateSave();
            LocationChanged += (_, _) => ScheduleWindowStateSave();



            // Show hint title for 3 s, then revert to the normal Ln/Col title
            var hintTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            hintTimer.Tick += (_, _) =>
            {
                hintTimer.Stop();
                UpdateTitle();
            };
            hintTimer.Start();
        }

        // ── Event handlers ────────────────────────────────────────────────────

        void OnTextChanged(object? sender, EventArgs e)
        {
            _editor.TextArea.Caret.BringCaretToView();

            if (_savedFileName != null && !_isDirty)
            {
                _isDirty = true;
                UpdateTitle();
            }

            if (_editor.Text.Length > 80000)
                _editor.Dispatcher.BeginInvoke(new Action(() => _editor.Clear()));

            _reParseTimer.Stop();
            _reParseTimer.Start();
        }

        void OnCaretPositionChanged(object? sender, EventArgs e)
        {
            UpdateTitle();
            UpdateCaretBrush();
            CheckAndSyncFoldState();
        }

        // Detects folding sections expanded externally (Find, Go to Line) and removes :min from fence
        void CheckAndSyncFoldState()
        {
            foreach (var section in _codeBlockFoldings.ToList())
            {
                if (!section.IsFolded && _foldingToRegion.TryGetValue(section, out var region))
                    ToggleFenceFlag(region, "min", forceOn: false);
            }
        }

        static readonly SolidColorBrush DarkCaretBrush  = MakeFrozen(Color.FromRgb(0xFF, 0xFF, 0xFF));
        static readonly SolidColorBrush LightCaretBrush = MakeFrozen(Color.FromRgb(0x00, 0x00, 0x00));
        static SolidColorBrush MakeFrozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        void UpdateCaretBrush()
        {
            int offset = _editor.TextArea.Caret.Offset;
            bool inBlock = false;
            foreach (var b in _codeColorizer.CurrentBlocks)
            {
                if (offset >= b.ContentStart && offset <= b.ContentEnd) { inBlock = true; break; }
            }
            _editor.TextArea.Caret.CaretBrush = inBlock ? DarkCaretBrush : LightCaretBrush;
        }

        void UpdateTitle()
        {
            var caret      = _editor.TextArea.Caret;
            string dirty   = (_isDirty && _savedFileName != null) ? "*" : "";
            string file    = string.IsNullOrEmpty(_prefixTitle) ? "" : _prefixTitle + " - ";
            Title = $"{dirty}{file}Minimal Notepad - Ln {caret.Line}, Col {caret.Column - 1}";
        }

        void OnTextEntered(object? sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            ApplyStickyFormatting(_editor.TextArea.Caret.Offset - e.Text.Length, e.Text.Length);
        }

        /// <summary>
        /// Inherits Bold/Italic/Underline/Strikethrough/ForeColor from the character
        /// immediately to the left of <paramref name="insertStart"/> and applies it
        /// to the <paramref name="insertedLen"/> characters just inserted there.
        /// Highlighter (BackColor) is intentionally NOT inherited.
        /// </summary>
        void ApplyStickyFormatting(int insertStart, int insertedLen)
        {
            if (insertStart < 0 || insertedLen <= 0) return;

            var style = _fmtManager.GetInlineFormattingBefore(insertStart);
            if (style == null) return;

            _fmtManager.ApplyInlineFormatting(insertStart, insertStart + insertedLen, style);
            _editor.TextArea.TextView.Redraw();
        }

        void OnPreviewMouseWheel(object? sender, MouseWheelEventArgs e)
        {
            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            {
                _editor.FontSize += e.Delta > 0 ? 1 : -1;
                _settings.FontSize = _editor.FontSize;
                ScheduleWindowStateSave();
                e.Handled = true;
            }
        }

        void OnPreviewKeyDown(object? sender, KeyEventArgs e)
        {
            bool ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            bool alt  = Keyboard.IsKeyDown(Key.LeftAlt)  || Keyboard.IsKeyDown(Key.RightAlt);

            // Multi-caret (Alt+Click): route keys to all cursors.
            if (_multiCaret.Active)
            {
                if (ctrl && !alt)
                {
                    bool isNav = e.Key is Key.Left or Key.Right or Key.Up or Key.Down
                                          or Key.Home or Key.End;
                    if (isNav)
                    {
                        // Ctrl+Arrow / Ctrl+Shift+Arrow: move/extend all secondaries;
                        // primary moves naturally via AvalonEdit (e.Handled stays false).
                        _multiCaret.HandleKey(e.Key);
                        // fall through → HandleCtrlShortcut won't match nav keys
                    }
                    else if (e.Key == Key.C || e.Key == Key.X)
                    {
                        if (MultiCaretCopyOrCut(e.Key == Key.X)) { e.Handled = true; return; }
                        _multiCaret.Clear(); // no active selections → collapse, run normal copy
                    }
                    else if (e.Key != Key.LeftCtrl && e.Key != Key.RightCtrl)
                    {
                        // Formatting keys → don't collapse; ApplyFormatting handles all carets.
                        bool isFmt = e.Key is Key.B or Key.I or Key.U or Key.F5
                                  || (e.Key >= Key.D0 && e.Key <= Key.D9)
                                  || (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9);
                        // Pass-through: undo/redo, find/replace, save, help — don't affect carets.
                        bool isPassThrough = e.Key is Key.Z or Key.Y
                                          or Key.F or Key.R or Key.H
                                          or Key.S or Key.N or Key.O
                                          or Key.M or Key.L or Key.G;
                        if (!isFmt && !isPassThrough) _multiCaret.Clear();
                        // fall through to HandleCtrlShortcut in all cases
                    }
                }
                else if (ctrl && alt)
                {
                    _multiCaret.Clear(); // Ctrl+Alt combos (e.g. clipboard history) → collapse
                }
                else if (!alt && _multiCaret.HandleKey(e.Key))
                {
                    e.Handled = true;
                    return;
                }
            }

            // Ctrl+Alt+V → clipboard history (App or Global, never Files)
            if (ctrl && alt && (e.Key == Key.V || (e.Key == Key.System && e.SystemKey == Key.V)))
            {
                ClipboardHistoryWindow.ShowOrActivateClipboard(this);
                e.Handled = true;
                return;
            }

            // Wrap Delete / Backspace with a formatting undo group so Ctrl+Z also restores spans.
            // Only for single-cursor; multi-caret EditAll uses its own PreEditHook.
            if (!ctrl && !alt && !_multiCaret.Active &&
                (e.Key == Key.Delete || e.Key == Key.Back))
            {
                TryPushFormattingUndoBeforeEdit();
            }

            if (ctrl && !alt)
            {
                HandleCtrlShortcut(e);
                return;
            }

            // Alt+Up / Alt+Down — move line(s)
            // WPF quirk: when Alt is held, e.Key == Key.System and e.SystemKey holds the actual key
            if ((e.Key == Key.System && (e.SystemKey == Key.Up || e.SystemKey == Key.Down)) && !ctrl)
            {
                MoveLines(e.SystemKey == Key.Up);
                e.Handled = true;
                return;
            }

            // Alt+U → lowercase selection, Alt+Shift+U → uppercase selection
            if (e.Key == Key.System && e.SystemKey == Key.U && !ctrl)
            {
                bool shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
                if (_multiCaret.Active)
                {
                    var sels = _multiCaret.GetAllSelections();
                    if (sels.Count > 0)
                    {
                        _editor.Document.BeginUpdate();
                        foreach (var (start, len) in sels.OrderByDescending(s => s.Start))
                        {
                            if (len == 0) continue;
                            string src         = _editor.Document.GetText(start, len);
                            string transformed = shift ? src.ToUpperInvariant() : src.ToLowerInvariant();
                            _editor.Document.Replace(start, len, transformed);
                        }
                        _editor.Document.EndUpdate();
                    }
                }
                else if (_editor.SelectionLength > 0)
                {
                    string transformed = shift
                        ? _editor.SelectedText.ToUpperInvariant()
                        : _editor.SelectedText.ToLowerInvariant();
                    int selStart = _editor.SelectionStart;
                    int selLen   = _editor.SelectionLength;
                    _editor.Document.Replace(selStart, selLen, transformed);
                    _editor.Select(selStart, transformed.Length);
                }
                e.Handled = true;
                return;
            }

            // F3 / Shift+F3 — next/previous match when find window is open
            if (e.Key == Key.F3 && !ctrl && !alt && FindReplaceWindow.IsOpen)
            {
                bool shiftF3 = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
                if (shiftF3) FindReplaceWindow.FindPrevStatic(); else FindReplaceWindow.FindNextStatic();
                e.Handled = true;
                return;
            }

            // Replace regular space with non-breaking space (prevents word-wrap at spaces)
            if (e.Key == Key.Space && !ctrl && !alt)
            {
                e.Handled = true;
                if (_multiCaret.Active) { _multiCaret.InsertNbspAll(); return; }
                int offset = _editor.CaretOffset;
                _editor.Document.Insert(offset, "\u00A0");
                _editor.CaretOffset = offset + 1;
                ApplyStickyFormatting(offset, 1);
            }
        }

        void HandleCtrlShortcut(KeyEventArgs e)
        {
            // ── Ctrl+Shift+Left/Right: word selection that stops at word boundary (no trailing spaces) ──
            bool shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
            if (shift && (e.Key == Key.Left || e.Key == Key.Right))
            {
                ExtendPrimaryWordSelection(e.Key == Key.Right);
                e.Handled = true;
                return;
            }

            // ── Clipboard shortcuts ───────────────────────────────────────────

            // Copy: put rich + plain text on clipboard; AvalonEdit won't see it
            if (e.Key == Key.C)
            {
                if (_editor.SelectionLength > 0)
                {
                    var spans    = SpansForCopy(_editor.SelectionStart, _editor.SelectionLength);
                    var richJson = RichClipboard.Copy(_editor.SelectedText, spans, _editor.SelectionStart);
                    ClipboardHistory.Push(_editor.SelectedText, richJson);

                    // If copying from within a code block, also add the custom format with markers
                    TryAddCodeBlockFormatToClipboard(_editor.SelectedText, _editor.SelectionStart, _editor.SelectionLength);

                    e.Handled = true;
                }
                return; // no selection → let AvalonEdit copy line as usual
            }

            // Cut: copy rich, then remove text (grouped with formatting restore for proper undo)
            if (e.Key == Key.X)
            {
                if (_editor.SelectionLength > 0)
                {
                    var spans    = SpansForCopy(_editor.SelectionStart, _editor.SelectionLength);
                    var richJson = RichClipboard.Copy(_editor.SelectedText, spans, _editor.SelectionStart);
                    ClipboardHistory.Push(_editor.SelectedText, richJson);
                    var fmtSnap = _fmtManager.TakeSnapshot();
                    _editor.Document.UndoStack.StartUndoGroup();
                    _editor.Document.UndoStack.Push(new FormattingRestoreOperation(
                        _fmtManager, fmtSnap, _editor.TextArea.TextView));
                    _editor.Document.Remove(_editor.SelectionStart, _editor.SelectionLength);
                    _editor.Document.UndoStack.EndUndoGroup();
                    e.Handled = true;
                }
                return;
            }

            // Paste: check for code block format first, then rich spans, then plain text
            if (e.Key == Key.V)
            {
                // Try custom code block format (from Copy button or Ctrl+C within code block)
                string? codeBlockContent = TryGetCodeBlockFormatFromClipboard();
                if (codeBlockContent != null)
                {
                    PasteContent(codeBlockContent, null);
                    e.Handled = true;
                    return;
                }

                // Fall back to rich clipboard
                var (text, spans) = RichClipboard.Paste();
                if (text == null) { e.Handled = true; return; }
                PasteContent(text, spans);
                e.Handled = true;
                return;
            }

            // ── Formatting shortcuts ───────────────────────────────────────────
            if (e.Key == Key.B)  { ApplyFormatting(_fmtManager.ToggleBold);          e.Handled = true; return; }
            if (e.Key == Key.I)  { ApplyFormatting(_fmtManager.ToggleItalic);        e.Handled = true; return; }
            if (e.Key == Key.U)  { ApplyFormatting(_fmtManager.ToggleUnderline);     e.Handled = true; return; }
            if (e.Key == Key.F5) { ApplyFormatting(_fmtManager.ToggleStrikethrough); e.Handled = true; return; }

            // ── Code block flag shortcuts (only when caret is inside a block) ──
            if (e.Key == Key.M) { var b = GetCodeBlockAtCaret(); if (b != null) { ToggleFenceFlag(b, "min"); e.Handled = true; return; } }
            if (e.Key == Key.L) { var b = GetCodeBlockAtCaret(); if (b != null) { ToggleFenceFlag(b, "ln");  e.Handled = true; return; } }
            if (e.Key == Key.G) { var b = GetCodeBlockAtCaret(); if (b != null) { GoToLineInBlock(b); e.Handled = true; return; } }

            // ── Text color: Ctrl+1 … Ctrl+5 ───────────────────────────────────
            int digitKey = DigitKeyNumber(e.Key);

            if (digitKey >= 1 && digitKey <= 5)
            {
                var map = shift ? _darkTextColorMap : _textColorMap;
                if (map.TryGetValue(digitKey, out var fgColor))
                {
                    ApplyFormatting((s, end) => _fmtManager.ToggleForeColor(s, end, fgColor));
                    e.Handled = true; return;
                }
            }

            // ── Highlight: Ctrl+6 … Ctrl+9, Ctrl+0 ───────────────────────────
            if (digitKey >= 6 || digitKey == 0)
            {
                var map = shift ? _strongHighlightMap : _highlightColorMap;
                if (map.TryGetValue(digitKey, out var bgColor))
                {
                    ApplyFormatting((s, end) => _fmtManager.ToggleBackColor(s, end, bgColor));
                    e.Handled = true; return;
                }
            }

            // ── Find (Ctrl+F) ─────────────────────────────────────────────────
            if (e.Key == Key.F)
            {
                string? init = _editor.SelectionLength > 0
                    && !_editor.SelectedText.Contains('\n')
                    && !_editor.SelectedText.Contains('\r')
                    ? _editor.SelectedText : null;
                FindReplaceWindow.ShowFor(_editor, this, replaceMode: false, initialText: init, settings: _settings, settingsFile: _settingsFile, fmtManager: _fmtManager, colorEntries: _colorEntries,
                    multiSelectFunc: () => { var s = _multiCaret.GetUserMultiSelections(); return s.Count > 0 ? s : null; });
                e.Handled = true;
                return;
            }

            // ── Open Files view (Ctrl+O) ──────────────────────────────────────
            if (e.Key == Key.O)
            {
                ClipboardHistoryWindow.ShowOrActivateFiles(this);
                e.Handled = true;
                return;
            }

            // ── Replace (Ctrl+R) / Rename saved file (Ctrl+Shift+R) ──────────
            if (e.Key == Key.R)
            {
                e.Handled = true;
                if (shift)
                {
                    if (_savedFileName != null) ShowRenameDialog();
                }
                else
                {
                    string? init = _editor.SelectionLength > 0
                        && !_editor.SelectedText.Contains('\n')
                        && !_editor.SelectedText.Contains('\r')
                        ? _editor.SelectedText : null;
                    FindReplaceWindow.ShowFor(_editor, this, replaceMode: true, initialText: init, settings: _settings, settingsFile: _settingsFile, fmtManager: _fmtManager, colorEntries: _colorEntries,
                        multiSelectFunc: () => { var s = _multiCaret.GetUserMultiSelections(); return s.Count > 0 ? s : null; });
                }
                return;
            }

            // ── Save file (Ctrl+S / Ctrl+Shift+S) ────────────────────────────
            if (e.Key == Key.S)
            {
                bool shiftDown = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
                e.Handled = true;
                if (shiftDown || _savedFileName == null)
                    ShowSaveFileDialog();
                else
                    SaveCurrentFile(_savedFileName);
                return;
            }

            // ── Code block wrap (Ctrl+, or Ctrl+.) ──────────────────────────
            if (e.Key == Key.OemComma || e.Key == Key.OemPeriod)
            {
                if (_editor.SelectionLength > 0)
                    ShowLanguagePicker();
                e.Handled = true;
                return;
            }

            // ── Help window ───────────────────────────────────────────────────
            if (e.Key == Key.H)
            {
                ShowHelpWindow();
                e.Handled = true;
                return;
            }

            // ── New window ────────────────────────────────────────────────────
            if (e.Key == Key.N)
            {
                var newWin = new NotepadWindow(
                    _settings, _settingsFile, _colorEntries, _allWindows,
                    Left + 30, Top + 30);
                newWin.Show();
                newWin.Activate();
                newWin._editor.Focus();
                e.Handled = true;
                return;
            }

            // ── Font size ─────────────────────────────────────────────────────
            if (e.Key == Key.OemPlus || e.Key == Key.Add)
            {
                _editor.FontSize += 1;
                _settings.FontSize = _editor.FontSize;
                ScheduleWindowStateSave();
                e.Handled = true;
            }
            else if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
            {
                _editor.FontSize = Math.Max(6, _editor.FontSize - 1);
                _settings.FontSize = _editor.FontSize;
                ScheduleWindowStateSave();
                e.Handled = true;
            }
        }

        void OnWindowClosed(object? sender, EventArgs e)
        {
            _windowStateSaveTimer?.Stop();
            FindReplaceWindow.CloseIfTargeting(_editor);
            _settings.WindowLeft   = Left;
            _settings.WindowTop    = Top;
            _settings.WindowWidth  = Width;
            _settings.WindowHeight = Height;
            _settings.FontSize     = _editor.FontSize;
            ConfigLoader.SaveSettings(_settings, _settingsFile);
            _allWindows.Remove(this);
            GlobalClipboardMonitor.EnabledChanged -= OnGlobalClipboardEnabledChanged;

            // If this was the last editor window and daemon is off → exit
            Program.TryShutdownIfIdle();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);

            if (!_isDirty || _savedFileName == null) return;

            var dialog = new Window
            {
                Title                 = "Unsaved Changes",
                Width                 = 340,
                Height                = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner                 = this,
                ResizeMode            = ResizeMode.NoResize,
                WindowStyle           = WindowStyle.ToolWindow,
                Background            = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3))
            };

            var msg = new TextBlock
            {
                Text              = $"\"{_savedFileName}\" has unsaved changes.\nDo you want to save before closing?",
                Margin            = new Thickness(16, 16, 16, 8),
                TextWrapping      = TextWrapping.Wrap
            };

            var btnSave = new Button { Content = "Save",         Width = 90, Margin = new Thickness(0, 0, 8, 0) };
            var btnDiscard = new Button { Content = "Don't Save", Width = 90, Margin = new Thickness(0, 0, 8, 0) };
            var btnCancel = new Button  { Content = "Cancel",     Width = 90 };

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 4, 16, 0) };
            btns.Children.Add(btnSave);
            btns.Children.Add(btnDiscard);
            btns.Children.Add(btnCancel);

            var stack = new StackPanel();
            stack.Children.Add(msg);
            stack.Children.Add(btns);
            dialog.Content = stack;

            bool? result = null;
            btnSave.Click    += (_, _) => { result = true;  dialog.Close(); };
            btnDiscard.Click += (_, _) => { result = false; dialog.Close(); };
            btnCancel.Click  += (_, _) => { result = null;  dialog.Close(); };

            dialog.ShowDialog();

            if (result == null)         { e.Cancel = true; }
            else if (result == true)    { SaveCurrentFile(_savedFileName); }
        }

        // ── Formatting apply + undo ───────────────────────────────────────────

        void ApplyFormatting(Action<int, int> action)
        {
            var before  = _fmtManager.TakeSnapshot();
            bool touched = false;

            if (_multiCaret.Active)
            {
                // Apply to every selection across all carets (primary + secondaries).
                foreach (var (start, len) in _multiCaret.GetAllSelections())
                {
                    if (len > 0) { action(start, start + len); touched = true; }
                }
            }
            else
            {
                var selection = _editor.TextArea.Selection;
                if (selection.IsEmpty) return;
                foreach (var seg in selection.Segments)
                {
                    if (seg.StartOffset < seg.EndOffset)
                    { action(seg.StartOffset, seg.EndOffset); touched = true; }
                }
            }

            if (!touched) return;

            var after = _fmtManager.TakeSnapshot();
            _editor.Document.UndoStack.Push(
                new FormattingUndoOperation(_fmtManager, before, after, _editor.TextArea.TextView));
            _editor.TextArea.TextView.Redraw();
        }

        // ── Per-file window state (size + font) ──────────────────────────────

        string? CurrentFilePath =>
            _externalPath   != null ? _externalPath :
            _savedFileName  != null ? SavedFileStore.GetFilePath(_savedFileName) :
            null;

        void ScheduleWindowStateSave()
        {
            if (CurrentFilePath == null) return;

            if (_windowStateSaveTimer == null)
            {
                _windowStateSaveTimer = new System.Windows.Threading.DispatcherTimer
                    { Interval = TimeSpan.FromMilliseconds(600) };
                _windowStateSaveTimer.Tick += (_, _) =>
                {
                    _windowStateSaveTimer.Stop();
                    string? path = CurrentFilePath;
                    if (path != null)
                        SavedFileStore.PatchWindowState(path, Width, Height, _editor.FontSize,
                            SavedFileStore.GetDisplayFingerprint(), Left, Top);
                };
            }
            _windowStateSaveTimer.Stop();
            _windowStateSaveTimer.Start();
        }

        // ── Code block helpers ────────────────────────────────────────────────

        CodeBlockRegion? GetCodeBlockAtCaret()
        {
            int offset = _editor.TextArea.Caret.Offset;
            foreach (var b in _codeColorizer.CurrentBlocks)
                if (offset >= b.ContentStart && offset <= b.ContentEnd) return b;
            return null;
        }

        void ToggleFenceFlag(CodeBlockRegion block, string flag, bool? forceOn = null)
        {
            var    doc       = _editor.Document;
            var    fenceLine = doc.GetLineByNumber(block.FenceOpenLine);
            string lineText  = doc.GetText(fenceLine.Offset, fenceLine.Length);

            string tag   = lineText.Substring(3).Trim();  // strip "```" prefix
            int    ci    = tag.IndexOf(':');
            string lang  = ci >= 0 ? tag.Substring(0, ci) : tag;
            string flags = ci >= 0 ? tag.Substring(ci + 1) : "";
            var    fList = new List<string>(flags.Split(':', StringSplitOptions.RemoveEmptyEntries));

            bool hasFlag      = fList.Contains(flag);
            bool shouldEnable = forceOn ?? !hasFlag;
            if (shouldEnable == hasFlag) return;

            if (shouldEnable) fList.Add(flag);
            else              fList.Remove(flag);

            string newLine = "```" + lang + (fList.Count > 0 ? ":" + string.Join(":", fList) : "");
            doc.Replace(fenceLine.Offset, fenceLine.Length, newLine);
        }

        void GoToLineInBlock(CodeBlockRegion block)
        {
            int maxLines = block.FenceCloseLine - block.FenceOpenLine - 1;
            if (maxLines < 1) return;

            ToggleFenceFlag(block, "ln", forceOn: true);

            var dlg = new CodeBlockGoToLineDialog(maxLines, this);
            if (dlg.ShowDialog() != true || dlg.ResultLine == null) return;

            int docLineNum  = block.FenceOpenLine + dlg.ResultLine.Value;
            var targetLine  = _editor.Document.GetLineByNumber(docLineNum);

            // If the target line is inside a folded section, unfold it first
            foreach (var section in _codeBlockFoldings)
            {
                if (section.IsFolded
                    && targetLine.Offset >= section.StartOffset
                    && targetLine.Offset <= section.EndOffset)
                {
                    section.IsFolded = false; // CheckAndSyncFoldState will remove :min on next caret event
                    break;
                }
            }

            _editor.Select(targetLine.Offset, targetLine.Length);
            _editor.ScrollToLine(docLineNum);
        }

        // ── Move line(s) up or down (Alt+Up / Alt+Down) ───────────────────────

        void MoveLines(bool moveUp)
        {
            var doc       = _editor.Document;
            int selStart  = _editor.SelectionStart;
            int selLength = _editor.SelectionLength;

            int startLine = doc.GetLineByOffset(selStart).LineNumber;
            int endLine;
            if (selLength == 0)
            {
                endLine = startLine;
            }
            else
            {
                var lastSel = doc.GetLineByOffset(selStart + selLength);
                endLine = (lastSel.Offset == selStart + selLength && lastSel.LineNumber > startLine)
                          ? lastSel.LineNumber - 1 : lastSel.LineNumber;
            }

            if (moveUp  && startLine == 1)           return;
            if (!moveUp && endLine == doc.LineCount)  return;

            int firstNum    = moveUp ? startLine - 1 : startLine;
            int lastNum     = moveUp ? endLine       : endLine + 1;
            int count       = lastNum - firstNum + 1;
            int regionStart = doc.GetLineByNumber(firstNum).Offset;
            int regionEnd   = regionStart;

            var contents   = new string[count];
            var delimiters = new string[count];
            for (int i = 0; i < count; i++)
            {
                var line      = doc.GetLineByNumber(firstNum + i);
                contents[i]   = doc.GetText(line.Offset, line.Length);
                delimiters[i] = doc.GetText(line.Offset + line.Length, line.DelimiterLength);
                regionEnd     = line.Offset + line.Length + line.DelimiterLength;
            }

            // Rotate only line contents; delimiters stay in their positional slot.
            // This handles the no-trailing-newline last-line edge case without special-casing.
            if (moveUp)
            {
                string first = contents[0];
                for (int i = 0; i < count - 1; i++) contents[i] = contents[i + 1];
                contents[count - 1] = first;
            }
            else
            {
                string last = contents[count - 1];
                for (int i = count - 1; i > 0; i--) contents[i] = contents[i - 1];
                contents[0] = last;
            }

            var origStart    = doc.GetLineByNumber(startLine);
            var origEnd      = doc.GetLineByNumber(endLine);
            int caretInLine  = selStart - origStart.Offset;
            int selEndInLine = selLength > 0 ? (selStart + selLength) - origEnd.Offset : 0;

            // Capture per-line formatting before the replace (relative to each line's start offset)
            var fmtBefore = _fmtManager.TakeSnapshot();
            var lineFormats = new List<FormattingManager.SpanRecord>[count];
            for (int i = 0; i < count; i++)
            {
                var line = doc.GetLineByNumber(firstNum + i);
                lineFormats[i] = _fmtManager.GetRelativeSpansForRange(line.Offset, line.Offset + line.Length);
            }

            // Rotate formatting the same way the line contents were rotated above
            if (moveUp)
            {
                var first = lineFormats[0];
                for (int i = 0; i < count - 1; i++) lineFormats[i] = lineFormats[i + 1];
                lineFormats[count - 1] = first;
            }
            else
            {
                var last = lineFormats[count - 1];
                for (int i = count - 1; i > 0; i--) lineFormats[i] = lineFormats[i - 1];
                lineFormats[0] = last;
            }

            // Single Replace → single undo entry; group with formatting change so Ctrl+Z undoes both
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < count; i++) sb.Append(contents[i]).Append(delimiters[i]);
            _editor.Document.UndoStack.StartUndoGroup();
            doc.Replace(regionStart, regionEnd - regionStart, sb.ToString());

            // Anchors inside the replaced region are now scrambled; clear and re-apply at rotated positions
            _fmtManager.ClearFormatting(regionStart, regionEnd);
            for (int i = 0; i < count; i++)
            {
                var newLine = doc.GetLineByNumber(firstNum + i);
                _fmtManager.ApplyRelativeSpans(newLine.Offset, lineFormats[i]);
            }
            var fmtAfter = _fmtManager.TakeSnapshot();
            _editor.Document.UndoStack.Push(
                new FormattingUndoOperation(_fmtManager, fmtBefore, fmtAfter, _editor.TextArea.TextView));
            _editor.Document.UndoStack.EndUndoGroup();
            _editor.TextArea.TextView.Redraw();

            int newStartLine = moveUp ? startLine - 1 : startLine + 1;
            int newEndLine   = moveUp ? endLine - 1   : endLine + 1;
            var newFirst     = doc.GetLineByNumber(newStartLine);
            var newLast      = doc.GetLineByNumber(newEndLine);

            int newCaret = newFirst.Offset + Math.Min(caretInLine, newFirst.Length);
            if (selLength == 0)
            {
                _editor.CaretOffset = newCaret;
            }
            else
            {
                int newSelEnd = newLast.Offset + Math.Min(selEndInLine, newLast.Length);
                _editor.Select(newCaret, Math.Max(0, newSelEnd - newCaret));
            }
            _editor.TextArea.Caret.BringCaretToView();
        }

        // ── Rename saved file dialog (Ctrl+R) ────────────────────────────────

        void ShowRenameDialog()
        {
            var oldName = _savedFileName!;
            var dialog = new Window
            {
                Title                 = "Rename File:",
                Width                 = 320,
                Height                = 140,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode            = ResizeMode.NoResize,
                Owner                 = this,
                Topmost               = true,
                Background            = Brushes.White
            };

            var stack = new StackPanel { Margin = new Thickness(12) };

            var textBox = new TextBox { Text = oldName, Margin = new Thickness(0, 0, 0, 8) };

            var errorLabel = new TextBlock
            {
                Foreground  = new SolidColorBrush(Color.FromRgb(0xCC, 0x30, 0x30)),
                FontSize    = 11,
                Margin      = new Thickness(0, 0, 0, 6),
                Visibility  = Visibility.Collapsed,
                TextWrapping = TextWrapping.Wrap
            };

            var okButton = new Button { Content = "Rename", Width = 90 };

            void TryRename()
            {
                var newName = textBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(newName)) return;
                if (newName == oldName) { dialog.Close(); return; }

                if (SavedFileStore.FileExists(newName))
                {
                    errorLabel.Text       = $"\"{newName}\" already exists in the Saved folder.";
                    errorLabel.Visibility = Visibility.Visible;
                    return;
                }

                try
                {
                    var oldPath = SavedFileStore.GetFilePath(oldName);
                    var newPath = SavedFileStore.GetFilePath(newName);
                    System.IO.File.Move(oldPath, newPath);
                    _savedFileName = newName;
                    _prefixTitle   = newName;
                    _isDirty       = false;
                    UpdateTitle();
                    dialog.Close();
                }
                catch (Exception ex)
                {
                    errorLabel.Text       = $"Error: {ex.Message}";
                    errorLabel.Visibility = Visibility.Visible;
                }
            }

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            btnRow.Children.Add(okButton);

            okButton.Click += (_, _) => TryRename();
            textBox.PreviewKeyDown += (_, ke) =>
            {
                if      (ke.Key == Key.Enter)  { TryRename();     ke.Handled = true; }
                else if (ke.Key == Key.Escape) { dialog.Close();  ke.Handled = true; }
            };

            stack.Children.Add(textBox);
            stack.Children.Add(errorLabel);
            stack.Children.Add(btnRow);
            dialog.Content = stack;

            dialog.Loaded += (_, _) => { textBox.Focus(); textBox.SelectAll(); };
            dialog.Show();
        }

        // ── Save-to-file dialogs (Ctrl+S / Ctrl+Shift+S) ─────────────────────

        void ShowSaveFileDialog()
        {
            var dialog = new Window
            {
                Title                 = "Save As:",
                Width                 = 320,
                Height                = 145,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode            = ResizeMode.NoResize,
                Owner                 = this,
                Topmost               = true,
                Background            = Brushes.White
            };

            var stack = new StackPanel
            {
                Margin            = new Thickness(12),
                VerticalAlignment = VerticalAlignment.Center
            };

            var textBox = new TextBox
            {
                Text   = _savedFileName ?? "",
                Margin = new Thickness(0, 0, 0, 6)
            };

            var errorLabel = new TextBlock
            {
                Foreground  = new SolidColorBrush(Color.FromRgb(0xCC, 0x30, 0x30)),
                FontSize    = 11,
                Margin      = new Thickness(0, 0, 0, 6),
                Visibility  = Visibility.Collapsed,
                TextWrapping = TextWrapping.Wrap
            };

            var buttonPanel = new StackPanel
            {
                Orientation         = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var okButton = new Button { Content = "OK", Width = 60 };

            void TrySave()
            {
                var name = textBox.Text.Trim();

                if (string.IsNullOrEmpty(name))
                {
                    errorLabel.Text       = "Name cannot be empty.";
                    errorLabel.Visibility = Visibility.Visible;
                    return;
                }

                // Forbid path-separator and other illegal chars
                char[] illegal = Path.GetInvalidFileNameChars();
                if (name.IndexOfAny(illegal) >= 0)
                {
                    errorLabel.Text       = "Name contains invalid characters.";
                    errorLabel.Visibility = Visibility.Visible;
                    return;
                }

                // Block overwrite of a different file (allow overwrite of *own* file)
                if (name != _savedFileName && SavedFileStore.FileExists(name))
                {
                    errorLabel.Text       = $"\"{name}\" already exists in the Saved folder.";
                    errorLabel.Visibility = Visibility.Visible;
                    return;
                }

                dialog.Close();
                _externalPath  = null;   // "Save As <name>" creates a library entry; stop tracking the external path
                _savedFileName = name;
                SaveCurrentFile(name);
            }

            okButton.Click += (_, _) => TrySave();
            textBox.PreviewKeyDown += (_, ke) =>
            {
                if      (ke.Key == Key.Enter)  { TrySave();       ke.Handled = true; }
                else if (ke.Key == Key.Escape) { dialog.Close();  ke.Handled = true; }
            };

            buttonPanel.Children.Add(okButton);
            stack.Children.Add(textBox);
            stack.Children.Add(errorLabel);
            stack.Children.Add(buttonPanel);
            dialog.Content = stack;

            dialog.Loaded += (_, _) => { textBox.Focus(); textBox.SelectAll(); };
            dialog.Show();
        }

        void SaveCurrentFile(string name)
        {
            var text     = _editor.Text;
            var spans    = _fmtManager.TakeSnapshot();
            var richJson = RichClipboard.SerializeDocument(text, spans);

            // A file opened from outside the managed Saved folder saves in-place to its
            // original path instead of dropping a copy into the library.
            string displayKey = SavedFileStore.GetDisplayFingerprint();
            if (_externalPath != null)
            {
                try
                {
                    SavedFileStore.SaveToPath(_externalPath, text, richJson,
                        Width, Height, _editor.FontSize, displayKey, Left, Top);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Could not save \"{_externalPath}\":\n{ex.Message}",
                        "Save failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else
            {
                SavedFileStore.Save(name, text, richJson,
                    Width, Height, _editor.FontSize, displayKey, Left, Top);
            }

            _prefixTitle = name;
            _isDirty     = false;
            UpdateTitle();
        }

        // ── Open a saved file (used by ClipboardHistoryWindow) ────────────────

        /// <summary>
        /// Loads the saved file content into this window (replaces entire document).
        /// Used for files in the managed Saved library — Ctrl+S saves back into the library.
        /// </summary>
        internal void LoadSavedFile(SavedFileEntry entry)
        {
            _externalPath = null;   // library file → save by name into the Saved folder
            LoadEntryContent(entry);
        }

        /// <summary>
        /// Loads a .mnp from an arbitrary path on disk (e.g. opened via file association
        /// or the command line). Records the full path so Ctrl+S saves in-place there
        /// instead of copying into the managed Saved library.
        /// Falls back to <see cref="LoadSavedFile"/> behaviour if the file is actually
        /// inside the Saved folder.
        /// </summary>
        internal void LoadExternalFile(string fullPath, SavedFileEntry entry)
        {
            // If the path is inside the managed Saved folder, treat it as a library file.
            string savedFolder = System.IO.Path.GetFullPath(SavedFileStore.SavedFolder);
            string thisFull     = System.IO.Path.GetFullPath(fullPath);
            // Only files directly in the Saved folder (not subdirectories) count as library files.
            bool inLibrary = string.Equals(
                System.IO.Path.GetDirectoryName(thisFull), savedFolder,
                StringComparison.OrdinalIgnoreCase);
            _externalPath = inLibrary ? null : thisFull;
            LoadEntryContent(entry);
        }

        void LoadEntryContent(SavedFileEntry entry)
        {
            _savedFileName        = entry.FileName;
            _prefixTitle          = entry.FileName;
            _editor.Document.Text = entry.PlainText;
            _isDirty              = false;

            var spans = RichClipboard.DeserializeSpans(entry.RichJson);
            if (spans != null && spans.Count > 0)
            {
                _fmtManager.ApplyRelativeSpans(0, spans);
                _editor.TextArea.TextView.Redraw();
            }

            // Restore per-file window size, font and position (without touching global defaults)
            if (entry.WindowWidth.HasValue && entry.WindowHeight.HasValue)
            {
                Width  = entry.WindowWidth.Value;
                Height = entry.WindowHeight.Value;
            }
            if (entry.FontSize.HasValue)
                _editor.FontSize = entry.FontSize.Value;
            if (entry.WindowLeft.HasValue && entry.WindowTop.HasValue)
            {
                double l = entry.WindowLeft.Value, t = entry.WindowTop.Value;
                double vl = SystemParameters.VirtualScreenLeft;
                double vt = SystemParameters.VirtualScreenTop;
                double vr = vl + SystemParameters.VirtualScreenWidth;
                double vb = vt + SystemParameters.VirtualScreenHeight;
                if (l >= vl && l < vr && t >= vt && t < vb)
                {
                    Left = l;
                    Top  = t;
                }
            }

            _editor.CaretOffset = 0;
            _editor.Focus();
            UpdateTitle();
            ReparseCodeBlocks();
        }

        /// <summary>
        /// If a NotepadWindow already has this file open, activates it.
        /// Otherwise opens a new window and loads the file.
        /// </summary>
        internal static void OpenOrFocusSavedFile(SavedFileEntry entry, NotepadWindow callerWindow)
        {
            // If already open in some window, just focus that window
            foreach (Window w in Application.Current.Windows)
            {
                if (w is NotepadWindow nw && nw.SavedFileName == entry.FileName)
                {
                    nw.Activate();
                    nw._editor.Focus();
                    return;
                }
            }

            // If caller window has no file attached and content is trivial (<10 chars),
            // reuse it instead of opening a new window
            if (callerWindow._savedFileName == null &&
                callerWindow._editor.Text.Trim().Length < 10)
            {
                callerWindow.LoadSavedFile(entry);
                callerWindow.Activate();
                callerWindow._editor.Focus();
                return;
            }

            var newWin = new NotepadWindow(
                callerWindow._settings,
                callerWindow._settingsFile,
                callerWindow._colorEntries,
                callerWindow._allWindows,
                callerWindow.Left + 30,
                callerWindow.Top + 30);
            newWin.Show();
            newWin.LoadSavedFile(entry);
            newWin.Activate();
        }

        /// <summary>
        /// Like <see cref="OpenOrFocusSavedFile"/> but for a file opened from an arbitrary
        /// path (file association / command line). Saves in-place to <paramref name="fullPath"/>.
        /// </summary>
        internal static void OpenOrFocusExternalFile(string fullPath, SavedFileEntry entry, NotepadWindow callerWindow)
        {
            string thisFull = System.IO.Path.GetFullPath(fullPath);

            // If already open in some window (matched by external path), focus it
            foreach (Window w in Application.Current.Windows)
            {
                if (w is NotepadWindow nw && nw._externalPath != null &&
                    string.Equals(nw._externalPath, thisFull, StringComparison.OrdinalIgnoreCase))
                {
                    nw.Activate();
                    nw._editor.Focus();
                    return;
                }
            }

            // Reuse a trivial empty caller, else open a new window
            if (callerWindow._savedFileName == null &&
                callerWindow._externalPath == null &&
                callerWindow._editor.Text.Trim().Length < 10)
            {
                callerWindow.LoadExternalFile(thisFull, entry);
                callerWindow.Activate();
                callerWindow._editor.Focus();
                return;
            }

            var newWin = new NotepadWindow(
                callerWindow._settings,
                callerWindow._settingsFile,
                callerWindow._colorEntries,
                callerWindow._allWindows,
                callerWindow.Left + 30,
                callerWindow.Top + 30);
            newWin.Show();
            newWin.LoadExternalFile(thisFull, entry);
            newWin.Activate();
        }

        // ── Helper: WPF digit key → int (0-9) ────────────────────────────────

        static int DigitKeyNumber(Key k) => k switch
        {
            Key.D1 => 1, Key.D2 => 2, Key.D3 => 3, Key.D4 => 4, Key.D5 => 5,
            Key.D6 => 6, Key.D7 => 7, Key.D8 => 8, Key.D9 => 9, Key.D0 => 0,
            _ => -1
        };

        // ── Help / Quick Guide window (Ctrl+H) ────────────────────────────────

        void ShowHelpWindow() =>
            HelpWindow.ShowOrActivate(_colorEntries, _settings, _settingsFile);

        // ── Copy helper: strips spans if selection is inside a code block ────

        void ExtendPrimaryWordSelection(bool forward)
        {
            var ta       = _editor.TextArea;
            int caretOff = ta.Caret.Offset;

            int anchor;
            if (ta.Selection.IsEmpty)
            {
                anchor = caretOff;
            }
            else
            {
                var seg = ta.Selection.SurroundingSegment;
                anchor = caretOff == seg.Offset ? seg.EndOffset : seg.Offset;
            }

            int newCaret = _multiCaret.WordBoundaryForSelect(caretOff, forward);
            ta.Selection = Selection.Create(ta, anchor, newCaret);
            ta.Caret.Offset = newCaret;
            ta.Caret.BringCaretToView();
        }

        List<FormattingManager.SpanRecord> SpansForCopy(int selStart, int selLen)
        {
            int selEnd = selStart + selLen;
            foreach (var region in _codeColorizer.CurrentBlocks)
            {
                if (selStart >= region.ContentStart && selEnd <= region.ContentEnd)
                    return new List<FormattingManager.SpanRecord>(); // plain text only
            }
            return _fmtManager.TakeSnapshot();
        }

        /// <summary>
        /// Multi-caret Ctrl+C / Ctrl+X: joins all selections with "\n", builds combined
        /// rich spans, puts rich + plain on clipboard. Returns false when no selections exist.
        /// </summary>
        bool MultiCaretCopyOrCut(bool cut)
        {
            var selections = _multiCaret.GetAllSelections();
            if (selections.Count == 0) return false;

            var texts = selections.Select(s => _editor.Document.GetText(s.Start, s.Length)).ToList();
            string combined = string.Join("\n", texts);

            // Build relative spans for the combined string.
            var combinedSpans = new List<FormattingManager.SpanRecord>();
            int writeOffset = 0;
            for (int i = 0; i < selections.Count; i++)
            {
                var (start, len) = selections[i];
                var relSpans = _fmtManager.GetRelativeSpansForRange(start, start + len);
                foreach (var sp in relSpans)
                    combinedSpans.Add(new FormattingManager.SpanRecord(
                        sp.Start + writeOffset, sp.End + writeOffset, sp.Format));
                writeOffset += texts[i].Length + 1; // +1 for the '\n' separator
            }

            // RichClipboard.Copy filters spans by [selectionStart, selectionStart+text.Length].
            // Passing selectionStart=0 keeps all our relative spans intact.
            var richJson = RichClipboard.Copy(combined, combinedSpans, 0);
            ClipboardHistory.Push(combined, richJson);

            if (cut)
            {
                var fmtSnap = _fmtManager.TakeSnapshot();
                _editor.Document.BeginUpdate();
                _editor.Document.UndoStack.Push(new FormattingRestoreOperation(
                    _fmtManager, fmtSnap, _editor.TextArea.TextView));
                foreach (var (start, len) in selections.OrderByDescending(s => s.Start))
                    _editor.Document.Remove(start, len);
                _editor.Document.EndUpdate();
                _editor.TextArea.ClearSelection();
                _multiCaret.Clear();
            }
            return true;
        }

        // ── Language picker popup (Ctrl+,) ────────────────────────────────────

        void ShowLanguagePicker()
        {
            int savedStart = _editor.SelectionStart;
            int savedLen   = _editor.SelectionLength;

            var preferred = new[] {
                ("C#", "csharp"), ("SQL", "sql"), ("HTML", "html"),
                ("JavaScript", "javascript"), ("CSS", "css"),
                ("JSON", "json"), ("XML", "xml"),
            };
            var knownDefs = new HashSet<string> { "C#", "TSQL", "HTML", "JavaScript", "CSS", "XML" };
            var restPairs = HighlightingManager.Instance.HighlightingDefinitions
                .Select(d => d.Name)
                .Where(n => !knownDefs.Contains(n))
                .OrderBy(n => n)
                .Select(n => (n, n.ToLowerInvariant()));

            // Build parallel lists: allDisplayNames (shown) + tagOf (display → fence tag)
            var allDisplayNames = new List<string>();
            var tagOf           = new Dictionary<string, string>();
            foreach (var (display, tag) in preferred.Concat(restPairs))
            {
                allDisplayNames.Add(display);
                tagOf[display] = tag;
            }

            var popup = new Window
            {
                Title                 = "Wrap as code block — choose language",
                Width                 = 220,
                Height                = 320,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner                 = this,
                Topmost               = true,
                ResizeMode            = ResizeMode.NoResize,
                WindowStyle           = WindowStyle.ToolWindow,
                Background            = Brushes.White
            };

            var search = new TextBox
            {
                Margin      = new Thickness(6, 6, 6, 4),
                FontFamily  = new FontFamily("Consolas"),
                FontSize    = 13
            };

            var list = new ListBox
            {
                FontFamily  = new FontFamily("Consolas"),
                FontSize    = 13,
                ItemsSource = allDisplayNames
            };

            search.TextChanged += (_, _) =>
            {
                string q = search.Text.ToLowerInvariant();
                list.ItemsSource = string.IsNullOrEmpty(q)
                    ? allDisplayNames
                    : allDisplayNames.Where(d =>
                        d.ToLowerInvariant().Contains(q) ||
                        tagOf.TryGetValue(d, out var t) && t.Contains(q)).ToList();
            };

            void Confirm()
            {
                string? display = list.SelectedItem as string
                    ?? (list.Items.Count == 1 ? list.Items[0] as string : null);
                if (display == null || !tagOf.TryGetValue(display, out var tag)) return;
                popup.Close();
                WrapSelectionInCodeBlock(savedStart, savedLen, tag);
            }

            list.MouseDoubleClick += (_, _) => Confirm();
            list.PreviewKeyDown   += (_, ke) =>
            {
                if (ke.Key == Key.Enter)  { Confirm();       ke.Handled = true; }
                if (ke.Key == Key.Escape) { popup.Close();   ke.Handled = true; }
            };
            search.PreviewKeyDown += (_, ke) =>
            {
                if (ke.Key == Key.Enter)  { Confirm();       ke.Handled = true; }
                if (ke.Key == Key.Escape) { popup.Close();   ke.Handled = true; }
                if (ke.Key == Key.Down)   { list.Focus(); list.SelectedIndex = Math.Max(0, list.SelectedIndex); }
            };

            var stack = new StackPanel();
            stack.Children.Add(search);
            stack.Children.Add(list);
            popup.Content = stack;

            popup.Loaded += (_, _) => search.Focus();
            popup.ShowDialog();
        }

        void WrapSelectionInCodeBlock(int selStart, int selLen, string lang)
        {
            var doc    = _editor.Document;
            int selEnd = selStart + selLen;

            var startLine = doc.GetLineByOffset(selStart);
            var endLine   = doc.GetLineByOffset(selEnd);

            bool startMidLine = selStart > startLine.Offset;
            bool endMidLine   = selEnd   < endLine.Offset + endLine.Length;

            string selectedText = doc.GetText(selStart, selLen);
            string fenceOpen    = $"```{lang}";
            const string fenceClose = "```";

            string prefix = startMidLine ? "\n" : "";
            string suffix = endMidLine   ? "\n" : "";

            string replacement = $"{prefix}{fenceOpen}\n{selectedText}{suffix}\n{fenceClose}";

            doc.Replace(selStart, selLen, replacement);

            // Place caret after closing fence
            int newOffset = selStart + replacement.Length;
            _editor.CaretOffset = Math.Min(newOffset, doc.TextLength);
        }

        void TryAddCodeBlockFormatToClipboard(string selectedText, int selStart, int selLen)
        {
            int selEnd = selStart + selLen;

            // Find if selection is entirely within exactly one code block
            CodeBlockRegion? block = null;
            foreach (var b in _codeColorizer.CurrentBlocks)
            {
                int contentStart = _editor.Document.GetLineByNumber(b.FenceOpenLine).EndOffset + 1;
                int contentEnd   = _editor.Document.GetLineByNumber(b.FenceCloseLine).Offset;

                if (selStart >= contentStart && selEnd <= contentEnd)
                {
                    block = b;
                    break;
                }
            }

            if (block == null) return;

            try
            {
                var dataObj = Clipboard.GetDataObject();
                if (dataObj == null) return;

                string withMarkers = $"```{block.Language}\n{selectedText}\n```";
                dataObj.SetData("application/x-mynotepad-codeblock", withMarkers);
                Clipboard.SetDataObject(dataObj);
            }
            catch { }
        }

        string? TryGetCodeBlockFormatFromClipboard()
        {
            try
            {
                var dataObj = Clipboard.GetDataObject();
                if (dataObj == null || !dataObj.GetDataPresent("application/x-mynotepad-codeblock"))
                    return null;

                var data = dataObj.GetData("application/x-mynotepad-codeblock") as string;
                return data;
            }
            catch { return null; }
        }

        // ── Paste content directly (used by ClipboardHistoryWindow) ──────────

        internal void PasteContent(string text, List<FormattingManager.SpanRecord>? spans)
        {
            int insertPos = _editor.SelectionStart;
            if (_editor.SelectionLength > 0)
                _editor.Document.Remove(insertPos, _editor.SelectionLength);

            _editor.Document.Insert(insertPos, text);
            _editor.CaretOffset = insertPos + text.Length;

            if (spans != null && spans.Count > 0)
            {
                var before = _fmtManager.TakeSnapshot();
                _fmtManager.ApplyRelativeSpans(insertPos, spans);
                var after = _fmtManager.TakeSnapshot();
                _editor.Document.UndoStack.Push(
                    new FormattingUndoOperation(_fmtManager, before, after, _editor.TextArea.TextView));
                _editor.TextArea.TextView.Redraw();
            }

            _editor.Focus();
        }

        void ApplyThinScrollBarStyle()
        {
            const string xaml =
                "<ResourceDictionary" +
                "    xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'" +
                "    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>" +
                "  <Style TargetType='ScrollBar'>" +
                "    <Setter Property='Width'    Value='7'/>" +
                "    <Setter Property='MinWidth' Value='7'/>" +
                "    <Setter Property='Template'>" +
                "      <Setter.Value>" +
                "        <ControlTemplate TargetType='ScrollBar'>" +
                "          <Grid Background='#18808080'>" +
                "            <Track Name='PART_Track' IsDirectionReversed='True'>" +
                "              <Track.DecreaseRepeatButton>" +
                "                <RepeatButton Opacity='0' Focusable='False'/>" +
                "              </Track.DecreaseRepeatButton>" +
                "              <Track.Thumb>" +
                "                <Thumb>" +
                "                  <Thumb.Template>" +
                "                    <ControlTemplate TargetType='Thumb'>" +
                "                      <Border Background='#88909090' CornerRadius='3' Margin='1,2,1,2'/>" +
                "                    </ControlTemplate>" +
                "                  </Thumb.Template>" +
                "                </Thumb>" +
                "              </Track.Thumb>" +
                "              <Track.IncreaseRepeatButton>" +
                "                <RepeatButton Opacity='0' Focusable='False'/>" +
                "              </Track.IncreaseRepeatButton>" +
                "            </Track>" +
                "          </Grid>" +
                "        </ControlTemplate>" +
                "      </Setter.Value>" +
                "    </Setter>" +
                "  </Style>" +
                "</ResourceDictionary>";

            try
            {
                var rd = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(xaml);
                _editor.Resources.MergedDictionaries.Add(rd);
            }
            catch { }
        }
    }
}
