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
        private readonly List<NotepadWindow>      _allWindows;

        private string     _prefixTitle = "";
        private TextEditor _editor      = null!;
        private FormattingManager _fmtManager = null!;

        public NotepadWindow(
            AppSettings                       settings,
            string                            settingsFile,
            IReadOnlyDictionary<int, string>  textColorMap,
            IReadOnlyDictionary<int, string>  highlightColorMap,
            List<NotepadWindow>               allWindows,
            double offsetX = -1,
            double offsetY = -1)
        {
            _settings          = settings;
            _settingsFile      = settingsFile;
            _textColorMap      = textColorMap;
            _highlightColorMap = highlightColorMap;
            _allWindows        = allWindows;

            InitializeWindow(offsetX, offsetY);
            InitializeEditor();
            InitializeFormatting();
            WireEvents();

            _allWindows.Add(this);
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
        }

        // ── Rich-text formatting setup ────────────────────────────────────────

        void InitializeFormatting()
        {
            _fmtManager = new FormattingManager(_editor.Document);
            _editor.TextArea.TextView.LineTransformers.Add(new RichTextColorizer(_fmtManager));
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
            }

            // Replace hyphen-minus with non-breaking hyphen (U+2011, identical visually)
            // Prevents word-wrap from splitting sequences like "->", "--", etc.
            // Key.OemMinus = main keyboard "-";  Key.Subtract = numpad "-"
            if ((e.Key == Key.OemMinus || e.Key == Key.Subtract) && !ctrl && !alt && !Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
            {
                e.Handled = true;
                int offset = _editor.CaretOffset;
                _editor.Document.Insert(offset, "\u2011");
                _editor.CaretOffset = offset + 1;
            }
        }

        void HandleCtrlShortcut(KeyEventArgs e)
        {
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

            // ── Window title (Ctrl+S or Ctrl+T) ───────────────────────────────
            if (e.Key == Key.S || e.Key == Key.T)
            {
                e.Handled = true;
                ShowTitleDialog();
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
                    _settings, _settingsFile, _textColorMap, _highlightColorMap, _allWindows,
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

        // ── Helper: WPF digit key → int (0-9) ────────────────────────────────

        static int DigitKeyNumber(Key k) => k switch
        {
            Key.D1 => 1, Key.D2 => 2, Key.D3 => 3, Key.D4 => 4, Key.D5 => 5,
            Key.D6 => 6, Key.D7 => 7, Key.D8 => 8, Key.D9 => 9, Key.D0 => 0,
            _ => -1
        };

        // ── Help / Quick Guide window (Ctrl+H) ────────────────────────────────

        void ShowHelpWindow() =>
            HelpWindow.ShowOrActivate(_textColorMap, _highlightColorMap);
    }
}
