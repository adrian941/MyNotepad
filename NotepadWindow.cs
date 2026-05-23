using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
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
        private readonly IReadOnlyList<ColorEntry>        _colorEntries;
        private readonly List<NotepadWindow>      _allWindows;

        private string     _prefixTitle   = "";
        private string?    _savedFileName = null;   // null = never saved
        private TextEditor _editor        = null!;
        private FormattingManager _fmtManager = null!;

        public NotepadWindow(
            AppSettings               settings,
            string                    settingsFile,
            IReadOnlyList<ColorEntry> colorEntries,
            List<NotepadWindow>       allWindows,
            double offsetX = -1,
            double offsetY = -1)
        {
            var (textColorMap, highlightColorMap) = ConfigLoader.BuildColorMaps(colorEntries);
            _settings          = settings;
            _settingsFile      = settingsFile;
            _textColorMap      = textColorMap;
            _highlightColorMap = highlightColorMap;
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
        }

        void OnGlobalClipboardEnabledChanged(bool enabled)
        {
            _settings.SaveGlobalClipboard = enabled;
            ConfigLoader.SaveSettings(_settings, _settingsFile);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // Daemon owns its own HWND for clipboard listening — nothing to do here
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
            dock.Children.Add(_editor);

            Content = dock;

            // Register Ctrl+Alt+V as a window-level command binding — this has higher
            // priority than WPF's Alt menu activation and fires reliably even when
            // Alt briefly captures the focus system.
            var openHistoryCmd = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(openHistoryCmd, (_, _) =>
                ClipboardHistoryWindow.ShowOrActivate(this)));
            InputBindings.Add(new KeyBinding(openHistoryCmd,
                new KeyGesture(Key.V, ModifierKeys.Control | ModifierKeys.Alt)));
        }

        // ── Rich-text formatting setup ────────────────────────────────────────

        void InitializeFormatting()
        {
            _fmtManager = new FormattingManager(_editor.Document);
            _editor.TextArea.TextView.LineTransformers.Add(new RichTextColorizer(_fmtManager));
            _editor.TextArea.TextView.ElementGenerators.Add(new NonBreakingHyphenGenerator());
        }

        // ── Event wiring ──────────────────────────────────────────────────────

        void InitializeEditor()
        {
            // Placeholder — ordering: InitializeEditor runs before InitializeFormatting
            // so _editor is ready; event wiring happens in WireEvents after both.
        }

        void WireEvents()
        {
            _editor.TextChanged += OnTextChanged;
            _editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
            _editor.PreviewMouseWheel += OnPreviewMouseWheel;
            _editor.PreviewKeyDown    += OnPreviewKeyDown;
            _editor.TextArea.TextEntered += OnTextEntered;
            Closed += OnWindowClosed;

            // Show hint title for 3 s, then revert to the normal Ln/Col title
            var hintTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            hintTimer.Tick += (_, _) =>
            {
                hintTimer.Stop();
                // Revert to normal title (using current caret position)
                var caret = _editor.TextArea.Caret;
                string prefix = string.IsNullOrEmpty(_prefixTitle) ? "" : _prefixTitle + " - ";
                Title = $"{prefix}Minimal Notepad - Ln {caret.Line}, Col {caret.Column - 1}";
            };
            hintTimer.Start();
        }

        // ── Event handlers ────────────────────────────────────────────────────

        void OnTextChanged(object? sender, EventArgs e)
        {
            _editor.TextArea.Caret.BringCaretToView();

            if (_editor.Text.Length > 80000)
                _editor.Dispatcher.BeginInvoke(new Action(() => _editor.Clear()));
        }

        void OnCaretPositionChanged(object? sender, EventArgs e)
        {
            var caret = _editor.TextArea.Caret;
            string prefix = string.IsNullOrEmpty(_prefixTitle) ? "" : _prefixTitle + " - ";
            Title = $"{prefix}Minimal Notepad - Ln {caret.Line}, Col {caret.Column - 1}";
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
                e.Handled = true;
            }
        }

        void OnPreviewKeyDown(object? sender, KeyEventArgs e)
        {
            bool ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            bool alt  = Keyboard.IsKeyDown(Key.LeftAlt)  || Keyboard.IsKeyDown(Key.RightAlt);

            // Ctrl+Alt+V → clipboard history window
            if (ctrl && alt && (e.Key == Key.V || (e.Key == Key.System && e.SystemKey == Key.V)))
            {
                ClipboardHistoryWindow.ShowOrActivate(this);
                e.Handled = true;
                return;
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

            // Replace regular space with non-breaking space (prevents word-wrap at spaces)
            if (e.Key == Key.Space && !ctrl && !alt)
            {
                e.Handled = true;
                int offset = _editor.CaretOffset;
                _editor.Document.Insert(offset, "\u00A0");
                _editor.CaretOffset = offset + 1;
                ApplyStickyFormatting(offset, 1);
            }
        }

        void HandleCtrlShortcut(KeyEventArgs e)
        {
            // ── Clipboard shortcuts ───────────────────────────────────────────

            // Copy: put rich + plain text on clipboard; AvalonEdit won't see it
            if (e.Key == Key.C)
            {
                if (_editor.SelectionLength > 0)
                {
                    var richJson = RichClipboard.Copy(
                        _editor.SelectedText,
                        _fmtManager.TakeSnapshot(),
                        _editor.SelectionStart);
                    ClipboardHistory.Push(_editor.SelectedText, richJson);
                    e.Handled = true;
                }
                return; // no selection → let AvalonEdit copy line as usual
            }

            // Cut: copy rich, then remove text (AvalonEdit handles undo for text)
            if (e.Key == Key.X)
            {
                if (_editor.SelectionLength > 0)
                {
                    var richJson = RichClipboard.Copy(
                        _editor.SelectedText,
                        _fmtManager.TakeSnapshot(),
                        _editor.SelectionStart);
                    ClipboardHistory.Push(_editor.SelectedText, richJson);
                    _editor.Document.Remove(_editor.SelectionStart, _editor.SelectionLength);
                    e.Handled = true;
                }
                return;
            }

            // Paste: plain text from any source; rich spans only from MinimalNotepad
            if (e.Key == Key.V)
            {
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

            // ── Text color: Ctrl+1 … Ctrl+5 ───────────────────────────────────
            int digitKey = DigitKeyNumber(e.Key);
            if (digitKey >= 1 && digitKey <= 5 && _textColorMap.TryGetValue(digitKey, out var fgColor))
            {
                ApplyFormatting((s, end) => _fmtManager.ToggleForeColor(s, end, fgColor));
                e.Handled = true; return;
            }

            // ── Highlight: Ctrl+6 … Ctrl+9, Ctrl+0 ───────────────────────────
            if ((digitKey >= 6 || digitKey == 0) && _highlightColorMap.TryGetValue(digitKey, out var bgColor))
            {
                ApplyFormatting((s, end) => _fmtManager.ToggleBackColor(s, end, bgColor));
                e.Handled = true; return;
            }

            // ── Window title (Ctrl+T) ─────────────────────────────────────────
            if (e.Key == Key.T)
            {
                e.Handled = true;
                ShowTitleDialog();
                return;
            }

            // ── Save file (Ctrl+S / Ctrl+Shift+S) ────────────────────────────
            if (e.Key == Key.S)
            {
                bool shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
                e.Handled = true;
                if (shift || _savedFileName == null)
                    ShowSaveFileDialog();
                else
                    SaveCurrentFile(_savedFileName);
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
                e.Handled = true;
            }
            else if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
            {
                _editor.FontSize = Math.Max(6, _editor.FontSize - 1);
                _settings.FontSize = _editor.FontSize;
                e.Handled = true;
            }
        }

        void OnWindowClosed(object? sender, EventArgs e)
        {
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

        // ── Formatting apply + undo ───────────────────────────────────────────

        void ApplyFormatting(Action<int, int> action)
        {
            int start = _editor.SelectionStart;
            int len   = _editor.SelectionLength;
            if (len == 0) return;

            var before = _fmtManager.TakeSnapshot();
            action(start, start + len);
            var after = _fmtManager.TakeSnapshot();

            _editor.Document.UndoStack.Push(
                new FormattingUndoOperation(_fmtManager, before, after, _editor.TextArea.TextView));

            _editor.TextArea.TextView.Redraw();
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

            // Single Replace → single undo entry
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < count; i++) sb.Append(contents[i]).Append(delimiters[i]);
            doc.Replace(regionStart, regionEnd - regionStart, sb.ToString());

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

        // ── Window title dialog (Ctrl+S / Ctrl+T) ────────────────────────────

        void ShowTitleDialog()
        {
            var dialog = new Window
            {
                Title                 = "Introdu titlul:",
                Width                 = 300,
                Height                = 120,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode            = ResizeMode.NoResize,
                Owner                 = this,
                Topmost               = true,
                Background            = Brushes.White
            };

            var stack = new StackPanel
            {
                Margin              = new Thickness(10),
                VerticalAlignment   = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var textBox = new TextBox { Text = _prefixTitle, Margin = new Thickness(0, 0, 0, 10) };

            var buttonPanel = new StackPanel
            {
                Orientation         = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var okButton = new Button { Content = "OK", Width = 60 };

            void ApplyTitle()
            {
                _prefixTitle = textBox.Text.Trim();
                string prefix = string.IsNullOrEmpty(_prefixTitle) ? "" : _prefixTitle + " - ";
                Title = $"{prefix}Minimal Notepad - Ln {_editor.TextArea.Caret.Line}, Col {_editor.TextArea.Caret.Column - 1}";
                dialog.Close();
            }

            okButton.Click += (_, _) => ApplyTitle();
            textBox.PreviewKeyDown += (_, e) =>
            {
                if      (e.Key == Key.Enter)  { ApplyTitle();    e.Handled = true; }
                else if (e.Key == Key.Escape) { dialog.Close();  e.Handled = true; }
            };

            buttonPanel.Children.Add(okButton);
            stack.Children.Add(textBox);
            stack.Children.Add(buttonPanel);
            dialog.Content = stack;

            dialog.Loaded += (_, _) => { textBox.Focus(); textBox.SelectAll(); };
            dialog.Show(); // non-modal → nu blochează alte ferestre
        }

        // ── Save-to-file dialogs (Ctrl+S / Ctrl+Shift+S) ─────────────────────

        void ShowSaveFileDialog()
        {
            var dialog = new Window
            {
                Title                 = "Salvează ca:",
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
                    errorLabel.Text       = "Numele nu poate fi gol.";
                    errorLabel.Visibility = Visibility.Visible;
                    return;
                }

                // Forbid path-separator and other illegal chars
                char[] illegal = Path.GetInvalidFileNameChars();
                if (name.IndexOfAny(illegal) >= 0)
                {
                    errorLabel.Text       = "Numele conține caractere invalide.";
                    errorLabel.Visibility = Visibility.Visible;
                    return;
                }

                // Block overwrite of a different file (allow overwrite of *own* file)
                if (name != _savedFileName && SavedFileStore.FileExists(name))
                {
                    errorLabel.Text       = $"\"{name}\" exista deja in folderul Saved.";
                    errorLabel.Visibility = Visibility.Visible;
                    return;
                }

                dialog.Close();
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
            SavedFileStore.Save(name, text, richJson);
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
    }
}
