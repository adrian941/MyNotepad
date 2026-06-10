using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;
using MinimalNotepad.Config;
using MinimalNotepad.Formatting;

namespace MinimalNotepad
{
    class FindReplaceWindow : Window
    {
        [DllImport("user32.dll")] static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
        static readonly IntPtr HWND_NOTOPMOST = new(-2);
        const uint SWP_NOMOVE_NOSIZE = 0x0003;

        // ── Singleton ─────────────────────────────────────────────────────────
        static FindReplaceWindow? _instance;
        static AppSettings?       _appSettings;
        static string             _appSettingsFile = "";
        static FormattingManager?         _staticFmtManager;
        static IReadOnlyList<ColorEntry>? _staticColorEntries;

        static readonly List<string> FindHistory    = new();
        static readonly List<string> ReplaceHistory = new();
        const int MaxHistory = 100;

        static Func<IReadOnlyList<(int Start, int Length)>?>? _multiSelectFunc;

        // ── Static API (called by NotepadWindow) ──────────────────────────────

        public static bool IsOpen => _instance?.IsVisible == true;

        public static void ShowFor(
            TextEditor editor, Window callerWindow,
            bool replaceMode, string? initialText = null,
            AppSettings? settings = null, string settingsFile = "",
            FormattingManager? fmtManager = null,
            IReadOnlyList<ColorEntry>? colorEntries = null,
            Func<IReadOnlyList<(int Start, int Length)>?>? multiSelectFunc = null)
        {
            if (settings != null)      { _appSettings = settings; _appSettingsFile = settingsFile; }
            if (fmtManager != null)    _staticFmtManager   = fmtManager;
            if (colorEntries != null)  _staticColorEntries = colorEntries;
            if (multiSelectFunc != null) _multiSelectFunc  = multiSelectFunc;

            if (_instance != null && _instance.IsLoaded && _instance.Owner != callerWindow)
            {
                // Owner changed — preserve state, recreate under new owner
                var savedFind    = _instance._findBox.Text;
                var savedReplace = _instance._replaceBox.Text;
                var savedCase    = _instance._caseBtn.IsChecked;
                var savedWord    = _instance._wordBtn.IsChecked;
                _instance.Close(); // triggers OnClosed → _instance = null

                _instance        = new FindReplaceWindow();
                _instance.Owner  = callerWindow;
                _instance.Left   = callerWindow.Left + callerWindow.Width - 360 - 16;
                _instance.Top    = callerWindow.Top  + 50;
                _instance._suppressSearch    = true;
                _instance._findBox.Text      = savedFind;
                _instance._replaceBox.Text   = savedReplace;
                _instance._caseBtn.IsChecked = savedCase;
                _instance._wordBtn.IsChecked = savedWord;
                _instance._suppressSearch    = false;
            }
            else if (_instance == null || !_instance.IsLoaded)
            {
                _instance       = new FindReplaceWindow();
                _instance.Owner = callerWindow;
                _instance.Left  = callerWindow.Left + callerWindow.Width - 360 - 16;
                _instance.Top   = callerWindow.Top  + 50;
            }

            _instance.SetTarget(editor);

            // ItemsSource BEFORE Text — setting ItemsSource on an editable ComboBox
            // resets its Text if the value isn't in the list yet.
            _instance._findBox.ItemsSource    = new List<string>(FindHistory);
            _instance._replaceBox.ItemsSource = new List<string>(ReplaceHistory);

            _instance._suppressSearch = true;
            _instance.SetMode(replaceMode);
            if (initialText != null)
            {
                _instance._findBox.Text = initialText;
                AddToHistory(initialText, FindHistory);
                _instance._findBox.ItemsSource = new List<string>(FindHistory);
            }
            _instance._suppressSearch = false;

            if (!_instance.IsVisible) _instance.Show();
            _instance.Activate();
            _instance._findBox.Focus();

            // Select all text in the find box so the user can immediately type a new term
            _instance.Dispatcher.BeginInvoke(() =>
            {
                var tb = _instance?._findBox.Template?.FindName(
                    "PART_EditableTextBox", _instance._findBox) as System.Windows.Controls.TextBox;
                tb?.SelectAll();
            });

            _instance.RunSearch();
        }

        public static void FindNextStatic()              => _instance?.Navigate(+1);
        public static void FindPrevStatic()              => _instance?.Navigate(-1);
        public static void CloseIfTargeting(TextEditor e)
        {
            if (_instance?._target == e) _instance.Close();
        }

        // ── Instance state ────────────────────────────────────────────────────

        TextEditor?   _target;
        bool          _replaceMode    = false;
        bool          _suppressSearch = false;

        List<(int Start, int Length)> _matches    = new();
        int                           _currentIdx = -1;

        MatchHighlightRenderer? _renderer;

        readonly ComboBox     _findBox;
        readonly ComboBox     _replaceBox;
        readonly TextBlock    _countLabel;
        readonly ToggleButton _caseBtn;
        readonly ToggleButton _wordBtn;
        readonly UIElement    _replaceComboRow;
        readonly UIElement    _replaceButtonRow;
        readonly TextBlock    _titleLabel;
        Popup?                _formatPopup;
        ToggleButton?         _fmtBoldBtn, _fmtItalicBtn, _fmtUnderBtn, _fmtStrikeBtn;
        bool                  _inSelectionOnly = false;
        CheckBox?             _inSelPopupChk;
        Brush?                _savedSelectionBrush;
        Brush?                _savedSelectionForeground;
        static readonly Brush SelectionOrange = FreezeB(new SolidColorBrush(Color.FromArgb(150, 0xFF, 0xC0, 0x40)));
        static Brush FreezeB(SolidColorBrush b) { b.Freeze(); return b; }

        // ── Constructor ───────────────────────────────────────────────────────

        FindReplaceWindow()
        {
            Title                 = "Find";
            Width                 = 380;
            SizeToContent         = SizeToContent.Height;
            WindowStyle           = WindowStyle.None;
            AllowsTransparency    = true;
            ResizeMode            = ResizeMode.NoResize;
            ShowInTaskbar         = false;
            Background            = Brushes.Transparent;

            (_findBox, _replaceBox, _countLabel, _caseBtn, _wordBtn, _replaceComboRow, _replaceButtonRow, _titleLabel)
                = BuildContent();

            Closed += OnClosed;
        }

        void OnClosed(object? s, EventArgs e)
        {
            // Persist whatever was in the boxes at close time
            if (!string.IsNullOrEmpty(_findBox.Text))    AddToHistory(_findBox.Text,    FindHistory);
            if (!string.IsNullOrEmpty(_replaceBox.Text)) AddToHistory(_replaceBox.Text, ReplaceHistory);

            _instance = null;
            if (_target == null) return;
            RestoreSelectionStyle(_target);
            _target.Document.Changed -= OnDocumentChanged;
            if (_renderer != null)
            {
                _target.TextArea.TextView.BackgroundRenderers.Remove(_renderer);
                _renderer.Update(new(), -1);
                _target.TextArea.TextView.Redraw();
            }
        }

        // ── Target management ─────────────────────────────────────────────────

        void SetTarget(TextEditor editor)
        {
            if (_target == editor) return;

            if (_target != null)
            {
                RestoreSelectionStyle(_target);
                _target.Document.Changed -= OnDocumentChanged;
                if (_renderer != null)
                {
                    _target.TextArea.TextView.BackgroundRenderers.Remove(_renderer);
                    _renderer.Update(new(), -1);
                    _target.TextArea.TextView.Redraw();
                }
            }

            _target   = editor;
            ApplySelectionStyle(_target);
            _renderer = new MatchHighlightRenderer();
            _target.TextArea.TextView.BackgroundRenderers.Add(_renderer);
            _target.Document.Changed += OnDocumentChanged;
        }

        void ApplySelectionStyle(TextEditor editor)
        {
            _savedSelectionBrush      = editor.TextArea.SelectionBrush;
            _savedSelectionForeground = editor.TextArea.SelectionForeground;
            editor.TextArea.SelectionBrush      = SelectionOrange;
            editor.TextArea.SelectionForeground = Brushes.Black;
        }

        void RestoreSelectionStyle(TextEditor editor)
        {
            editor.TextArea.SelectionBrush      = _savedSelectionBrush;
            editor.TextArea.SelectionForeground = _savedSelectionForeground;
        }

        void SetMode(bool replaceMode)
        {
            _replaceMode = replaceMode;
            Title            = replaceMode ? "Find & Replace" : "Find";
            _titleLabel.Text = replaceMode ? "Find & Replace" : "Find";
            _replaceComboRow.Visibility  = replaceMode ? Visibility.Visible : Visibility.Collapsed;
            _replaceButtonRow.Visibility = replaceMode ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── Search logic ──────────────────────────────────────────────────────

        void OnDocumentChanged(object? s, EventArgs e)
        {
            if (!IsVisible || _suppressSearch) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (IsVisible && !_suppressSearch) RunSearch(jump: false);
            }));
        }

        // jump=true  → also select + scroll to current match (explicit user navigation)
        // jump=false → only update highlights; don't touch caret/selection (document changed)
        void RunSearch(bool jump = true)
        {
            if (_suppressSearch || _target == null) return;

            string needle = _findBox.Text ?? "";
            _matches.Clear();
            _currentIdx = -1;

            if (!string.IsNullOrEmpty(needle))
            {
                bool caseSens  = _caseBtn.IsChecked == true;
                bool wholeWord = _wordBtn.IsChecked == true;
                var  comp      = caseSens
                    ? StringComparison.Ordinal
                    : StringComparison.OrdinalIgnoreCase;

                string text = _target.Text;
                int    pos  = 0;

                while (pos < text.Length)
                {
                    int found = text.IndexOf(needle, pos, comp);
                    if (found < 0) break;

                    bool ok = true;
                    if (wholeWord)
                    {
                        bool lOk = found == 0 || !IsWordChar(text[found - 1]);
                        bool rOk = found + needle.Length >= text.Length
                                   || !IsWordChar(text[found + needle.Length]);
                        ok = lOk && rOk;
                    }

                    if (ok) _matches.Add((found, needle.Length));
                    pos = found + 1;
                }

                if (_matches.Count > 0)
                {
                    int caret = _target.CaretOffset;
                    _currentIdx = 0;
                    for (int i = 0; i < _matches.Count; i++)
                        if (_matches[i].Start >= caret) { _currentIdx = i; break; }

                    if (jump)
                    {
                        JumpToCurrent();
                    }
                    else
                    {
                        _renderer?.Update(_matches, _currentIdx);
                        RedrawHighlight();
                        UpdateCount();
                    }
                    return;
                }
            }

            _renderer?.Update(new(), -1);
            RedrawHighlight();
            UpdateCount();
            if (_formatPopup?.IsOpen == true) UpdateFormatToggles();
        }

        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        void Navigate(int dir)
        {
            if (_matches.Count == 0 || _target == null) return;
            _currentIdx = (_currentIdx + dir + _matches.Count) % _matches.Count;
            JumpToCurrent();
        }

        void JumpToCurrent()
        {
            if (_target == null || _currentIdx < 0 || _currentIdx >= _matches.Count) return;
            var (start, len) = _matches[_currentIdx];
            _target.Select(start, len);
            _target.TextArea.Caret.BringCaretToView();
            _renderer?.Update(_matches, _currentIdx);
            RedrawHighlight();
            UpdateCount();
        }

        void RedrawHighlight()
        {
            if (_target == null) return;
            var tv = _target.TextArea.TextView;
            tv.Redraw();
            // KnownLayer.Caret is a separate UIElement; Redraw() only calls InvalidateMeasure()
            // on the TextView itself, which doesn't propagate InvalidateVisual() to child layers.
            // Iterate and force every layer to re-render so the highlight stays current even
            // when keyboard focus is on the find bar rather than the editor.
            int n = VisualTreeHelper.GetChildrenCount(tv);
            for (int i = 0; i < n; i++)
                if (VisualTreeHelper.GetChild(tv, i) is UIElement layer)
                    layer.InvalidateVisual();
        }

        void UpdateCount()
        {
            bool hasText = (_findBox.Text?.Length ?? 0) > 0;
            if (_matches.Count == 0)
            {
                _countLabel.Text       = hasText ? "No results" : "";
                _countLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x30, 0x30));
            }
            else
            {
                _countLabel.Text       = $"{_currentIdx + 1} of {_matches.Count}";
                _countLabel.Foreground = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50));
            }
        }

        void DoReplace()
        {
            if (_target == null || _matches.Count == 0 || _currentIdx < 0) return;
            var (start, len) = _matches[_currentIdx];
            string rep = _replaceBox.Text ?? "";
            AddToHistory(rep, ReplaceHistory);

            _suppressSearch = true;
            _target.Document.Replace(start, len, rep);
            _suppressSearch = false;
            RunSearch();
        }

        IEnumerable<(int Start, int Length)> MatchesInScope()
        {
            if (!_inSelectionOnly) return _matches;

            var multiRanges = _multiSelectFunc?.Invoke();
            if (multiRanges != null && multiRanges.Count > 0)
                return _matches.Where(m => multiRanges.Any(r =>
                    m.Start >= r.Start && m.Start + m.Length <= r.Start + r.Length));

            if (_target == null || _target.SelectionLength == 0) return _matches;
            int selStart = _target.SelectionStart;
            int selEnd   = selStart + _target.SelectionLength;
            return _matches.Where(m => m.Start >= selStart && m.Start + m.Length <= selEnd);
        }

        void DoReplaceAll()
        {
            if (_target == null || _matches.Count == 0) return;
            string rep = _replaceBox.Text ?? "";
            AddToHistory(rep, ReplaceHistory);

            var scope = MatchesInScope().ToList();
            if (scope.Count == 0) return;

            _suppressSearch = true;
            using (_target.Document.RunUpdate())
            {
                for (int i = scope.Count - 1; i >= 0; i--)
                {
                    var (s, l) = scope[i];
                    _target.Document.Replace(s, l, rep);
                }
            }
            _suppressSearch = false;
            RunSearch();
        }

        static void AddToHistory(string text, List<string> history)
        {
            if (string.IsNullOrEmpty(text)) return;
            history.Remove(text);
            history.Insert(0, text);
            if (history.Count > MaxHistory) history.RemoveAt(history.Count - 1);
        }

        // ── UI ────────────────────────────────────────────────────────────────

        (ComboBox findBox, ComboBox replaceBox, TextBlock countLabel,
         ToggleButton caseBtn, ToggleButton wordBtn, UIElement replaceComboRow, UIElement replaceButtonRow, TextBlock titleLabel)
        BuildContent()
        {
            var toggleStyle = BuildToggleStyle();
            var navStyle    = BuildNavButtonStyle();

            // ── Outer chrome: rounded card with subtle shadow ─────────────────
            var card = new Border
            {
                Background      = new SolidColorBrush(Color.FromRgb(0xFB, 0xFB, 0xFC)),
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0xC4, 0xC4, 0xCC)),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(10),
                Margin          = new Thickness(10),
                Effect          = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 16, ShadowDepth = 2, Opacity = 0.22,
                    Color = Colors.Black, Direction = 270,
                },
            };

            var outer = new StackPanel();
            card.Child = outer;
            Content = card;

            // ── Title bar (drag to move + close button) ───────────────────────
            var titleBar = new Grid { Height = 38, Background = Brushes.Transparent };
            titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleLabel = new TextBlock
            {
                Text              = "Find",
                FontFamily        = new FontFamily("Segoe UI Semibold"),
                FontSize          = 13,
                Foreground        = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x38)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(16, 0, 0, 0),
            };
            Grid.SetColumn(titleLabel, 0);

            var closeBtn = new Button
            {
                ToolTip           = "Close (Esc)",
                Style             = BuildCloseButtonStyle(),
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 8, 0),
            };
            Grid.SetColumn(closeBtn, 1);

            titleBar.Children.Add(titleLabel);
            titleBar.Children.Add(closeBtn);
            titleBar.MouseLeftButtonDown += (_, me) =>
            {
                if (me.ButtonState == MouseButtonState.Pressed) DragMove();
            };
            outer.Children.Add(titleBar);

            // ── Body ──────────────────────────────────────────────────────────
            var root = new StackPanel { Margin = new Thickness(16, 0, 16, 16) };
            outer.Children.Add(root);

            // ── Comboboxes with labels ────────────────────────────────────────
            var findBox = MakeComboBox();
            root.Children.Add(MakeLabeledRow("Find", findBox));

            // ── Toolbar (toggles left, nav right) ─────────────────────────────
            var toolbar = new DockPanel
            {
                LastChildFill = false,
                Margin        = new Thickness(0, 8, 0, 0),
            };

            var caseBtn = new ToggleButton
            {
                Content   = "Aa",
                ToolTip   = "Match Case",
                Style     = toggleStyle,
                IsChecked = _appSettings?.FindMatchCase ?? false,
            };
            var wordBtn = new ToggleButton
            {
                Content   = "ab",
                ToolTip   = "Match Whole Word",
                Style     = toggleStyle,
                IsChecked = _appSettings?.FindWholeWord ?? false,
            };
            caseBtn.Margin = new Thickness(0, 0, 6, 0);

            var countLabel = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                FontSize          = 12,
                MinWidth          = 76,
                TextAlignment     = TextAlignment.Right,
                Foreground        = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50)),
            };

            var prevBtn = new Button { Content = "‹", ToolTip = "Previous Match  (Shift+Enter / Shift+F3)", Style = navStyle };
            var nextBtn = new Button { Content = "›", ToolTip = "Next Match  (Enter / F3)", Style = navStyle };
            prevBtn.Margin = new Thickness(0, 0, 6, 0);
            nextBtn.Margin = new Thickness(0, 0, 8, 0);

            // Left: toggles
            DockPanel.SetDock(caseBtn, Dock.Left);
            DockPanel.SetDock(wordBtn, Dock.Left);
            toolbar.Children.Add(caseBtn);
            toolbar.Children.Add(wordBtn);

            Activated   += (_, _) => Opacity = 1.0;
            Deactivated += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Input,
                (Action)(() => { if (!IsActive) Opacity = 0.55; }));

            // Format-all button (far right)
            var formatBtn = new Button
            {
                Content = MakeFormatIcon(),
                ToolTip = "Format all matches",
                Style   = navStyle,
                Width   = 30, Height = 30,
                Cursor  = Cursors.Hand,
                Margin  = new Thickness(4, 0, 0, 0),
            };
            formatBtn.Click += (_, _) =>
            {
                _formatPopup ??= BuildFormatPopup();
                if (!_formatPopup.IsOpen)
                {
                    UpdateFormatToggles();
                    _formatPopup.PlacementTarget = formatBtn;
                    _formatPopup.IsOpen = true;
                }
                else
                {
                    _formatPopup.IsOpen = false;
                }
            };

            // Right: format, count, next, prev (added right-to-left → format is far right)
            DockPanel.SetDock(formatBtn,  Dock.Right);
            DockPanel.SetDock(countLabel, Dock.Right);
            DockPanel.SetDock(nextBtn,    Dock.Right);
            DockPanel.SetDock(prevBtn,    Dock.Right);
            toolbar.Children.Add(formatBtn);
            toolbar.Children.Add(countLabel);
            toolbar.Children.Add(nextBtn);
            toolbar.Children.Add(prevBtn);

            var replaceBox      = MakeComboBox();
            var replaceComboRow = MakeLabeledRow("Replace", replaceBox, topMargin: 8);
            replaceComboRow.Visibility = Visibility.Collapsed;
            root.Children.Add(replaceComboRow);

            root.Children.Add(toolbar);

            var replaceBtnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin      = new Thickness(0, 10, 0, 0),
                Visibility  = Visibility.Collapsed,
            };
            var replaceBtn    = MakeActionButton("Replace",     "Replace this match (Enter)", primary: false);
            var replaceAllBtn = MakeActionButton("Replace All", "Replace all matches",        primary: true);
            replaceBtn.Margin = new Thickness(0, 0, 8, 0);
            replaceBtnRow.Children.Add(replaceBtn);
            replaceBtnRow.Children.Add(replaceAllBtn);

            var inSelChk = new CheckBox
            {
                Content             = "In Selection",
                VerticalAlignment   = VerticalAlignment.Center,
                Margin              = new Thickness(10, 0, 0, 0),
                ToolTip             = "Replace All only within the active selection",
                IsChecked           = _inSelectionOnly,
                FontSize            = 11,
            };
            inSelChk.Checked   += (_, _) => { _inSelectionOnly = true;  if (_inSelPopupChk != null) _inSelPopupChk.IsChecked = true; };
            inSelChk.Unchecked += (_, _) => { _inSelectionOnly = false; if (_inSelPopupChk != null) _inSelPopupChk.IsChecked = false; };
            replaceBtnRow.Children.Add(inSelChk);

            root.Children.Add(replaceBtnRow);

            // ── Wire events ───────────────────────────────────────────────────
            closeBtn.Click += (_, _) => Close();

            void SaveToggles()
            {
                if (_appSettings == null || string.IsNullOrEmpty(_appSettingsFile)) return;
                _appSettings.FindMatchCase = caseBtn.IsChecked == true;
                _appSettings.FindWholeWord = wordBtn.IsChecked == true;
                ConfigLoader.SaveSettings(_appSettings, _appSettingsFile);
            }

            caseBtn.Checked   += (_, _) => { RunSearch(jump: false); SaveToggles(); };
            caseBtn.Unchecked += (_, _) => { RunSearch(jump: false); SaveToggles(); };
            wordBtn.Checked   += (_, _) => { RunSearch(jump: false); SaveToggles(); };
            wordBtn.Unchecked += (_, _) => { RunSearch(jump: false); SaveToggles(); };
            prevBtn.Click     += (_, _) => Navigate(-1);
            nextBtn.Click     += (_, _) => Navigate(+1);
            replaceBtn.Click    += (_, _) => DoReplace();
            replaceAllBtn.Click += (_, _) => DoReplaceAll();

            findBox.Loaded += (_, _) =>
            {
                var tb = findBox.Template.FindName("PART_EditableTextBox", findBox)
                         as System.Windows.Controls.TextBox;
                if (tb != null)
                    tb.TextChanged += (_, _) => { if (!_suppressSearch) RunSearch(); };
            };

            findBox.PreviewKeyDown   += OnFindKeyDown;
            replaceBox.PreviewKeyDown += OnReplaceKeyDown;
            replaceBox.DropDownClosed += (_, _) =>
            {
                if (replaceBox.SelectedItem is string s) replaceBox.Text = s;
            };

            PreviewKeyDown += (_, ke) =>
            {
                if (ke.Key == Key.Escape) { ke.Handled = true; Close(); }
            };

            return (findBox, replaceBox, countLabel, caseBtn, wordBtn, replaceComboRow, replaceBtnRow, titleLabel);
        }

        void OnFindKeyDown(object sender, KeyEventArgs e)
        {
            bool shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
            switch (e.Key)
            {
                case Key.Enter:
                    AddToHistory(_findBox.Text, FindHistory);
                    Navigate(shift ? -1 : +1);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    Close();
                    e.Handled = true;
                    break;
                case Key.Tab when _replaceMode:
                    _replaceBox.Focus();
                    e.Handled = true;
                    break;
                case Key.F3:
                    Navigate(shift ? -1 : +1);
                    e.Handled = true;
                    break;
                case Key.Left when Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl):
                    Navigate(-1);
                    e.Handled = true;
                    break;
                case Key.Right when Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl):
                    Navigate(+1);
                    e.Handled = true;
                    break;
            }
        }

        void OnReplaceKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    AddToHistory(_replaceBox.Text, ReplaceHistory);
                    DoReplace();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    Close();
                    e.Handled = true;
                    break;
            }
        }

        // ── Widget factories ──────────────────────────────────────────────────

        static Grid MakeLabeledRow(string labelText, UIElement input, double topMargin = 0)
        {
            var g = new Grid { Margin = new Thickness(0, topMargin, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var lbl = new TextBlock
            {
                Text              = labelText,
                FontFamily        = new FontFamily("Segoe UI"),
                FontSize          = 12,
                Foreground        = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x5E)),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(lbl,   0);
            Grid.SetColumn(input, 1);
            g.Children.Add(lbl);
            g.Children.Add(input);
            return g;
        }

        static ComboBox MakeComboBox() =>
            new ComboBox
            {
                IsEditable               = true,
                IsTextSearchEnabled      = false,
                FontFamily               = new FontFamily("Segoe UI"),
                FontSize                 = 13,
                Height                   = 30,
                VerticalContentAlignment = VerticalAlignment.Center,
                Style                    = BuildComboBoxStyle(),
            };

        static Button MakeActionButton(string content, string tooltip, bool primary) =>
            new Button
            {
                Content  = content,
                ToolTip  = tooltip,
                FontSize = 12,
                Style    = BuildActionButtonStyle(primary),
            };

        // ── Parsed XAML styles ────────────────────────────────────────────────

        static Style BuildToggleStyle() => Parse(
            "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'" +
            "       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'" +
            "       TargetType='ToggleButton'>" +
            "  <Setter Property='FontFamily' Value='Segoe UI'/>" +
            "  <Setter Property='FontSize'   Value='12'/>" +
            "  <Setter Property='FontWeight' Value='SemiBold'/>" +
            "  <Setter Property='Width'      Value='34'/>" +
            "  <Setter Property='Height'     Value='30'/>" +
            "  <Setter Property='Cursor'     Value='Hand'/>" +
            "  <Setter Property='Foreground' Value='#55555E'/>" +
            "  <Setter Property='Template'>" +
            "    <Setter.Value>" +
            "      <ControlTemplate TargetType='ToggleButton'>" +
            "        <Border x:Name='bd' CornerRadius='6' BorderThickness='1'" +
            "                Background='Transparent' BorderBrush='Transparent'>" +
            "          <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>" +
            "        </Border>" +
            "        <ControlTemplate.Triggers>" +
            "          <Trigger Property='IsChecked' Value='True'>" +
            "            <Setter TargetName='bd' Property='Background'  Value='#0F6CBD'/>" +
            "            <Setter TargetName='bd' Property='BorderBrush' Value='#0F6CBD'/>" +
            "            <Setter Property='Foreground' Value='White'/>" +
            "          </Trigger>" +
            "          <MultiTrigger>" +
            "            <MultiTrigger.Conditions>" +
            "              <Condition Property='IsMouseOver' Value='True'/>" +
            "              <Condition Property='IsChecked'   Value='False'/>" +
            "            </MultiTrigger.Conditions>" +
            "            <Setter TargetName='bd' Property='Background'  Value='#E8E8EE'/>" +
            "            <Setter TargetName='bd' Property='BorderBrush' Value='#D2D2DA'/>" +
            "          </MultiTrigger>" +
            "          <MultiTrigger>" +
            "            <MultiTrigger.Conditions>" +
            "              <Condition Property='IsMouseOver' Value='True'/>" +
            "              <Condition Property='IsChecked'   Value='True'/>" +
            "            </MultiTrigger.Conditions>" +
            "            <Setter TargetName='bd' Property='Background' Value='#1A7BD0'/>" +
            "          </MultiTrigger>" +
            "        </ControlTemplate.Triggers>" +
            "      </ControlTemplate>" +
            "    </Setter.Value>" +
            "  </Setter>" +
            "</Style>");

        static Style BuildNavButtonStyle() => Parse(
            "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'" +
            "       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'" +
            "       TargetType='Button'>" +
            "  <Setter Property='FontFamily' Value='Segoe UI'/>" +
            "  <Setter Property='FontSize'  Value='19'/>" +
            "  <Setter Property='Foreground' Value='#44444C'/>" +
            "  <Setter Property='Width'     Value='32'/>" +
            "  <Setter Property='Height'    Value='30'/>" +
            "  <Setter Property='Cursor'    Value='Hand'/>" +
            "  <Setter Property='Template'>" +
            "    <Setter.Value>" +
            "      <ControlTemplate TargetType='Button'>" +
            "        <Border x:Name='bd' CornerRadius='6' BorderThickness='1'" +
            "                Background='Transparent' BorderBrush='Transparent'>" +
            "          <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' Margin='0,-3,0,0'/>" +
            "        </Border>" +
            "        <ControlTemplate.Triggers>" +
            "          <Trigger Property='IsMouseOver' Value='True'>" +
            "            <Setter TargetName='bd' Property='Background'  Value='#E8E8EE'/>" +
            "            <Setter TargetName='bd' Property='BorderBrush' Value='#D2D2DA'/>" +
            "          </Trigger>" +
            "          <Trigger Property='IsPressed' Value='True'>" +
            "            <Setter TargetName='bd' Property='Background' Value='#D6D6DE'/>" +
            "          </Trigger>" +
            "        </ControlTemplate.Triggers>" +
            "      </ControlTemplate>" +
            "    </Setter.Value>" +
            "  </Setter>" +
            "</Style>");

        static Style BuildCloseButtonStyle() => Parse(
            "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'" +
            "       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'" +
            "       TargetType='Button'>" +
            "  <Setter Property='Width'  Value='30'/>" +
            "  <Setter Property='Height' Value='26'/>" +
            "  <Setter Property='Cursor' Value='Hand'/>" +
            "  <Setter Property='Template'>" +
            "    <Setter.Value>" +
            "      <ControlTemplate TargetType='Button'>" +
            "        <Border x:Name='bd' CornerRadius='6' Background='Transparent'>" +
            "          <Path x:Name='x' Stroke='#66666E' StrokeThickness='1.4'" +
            "                Data='M 0,0 L 9,9 M 9,0 L 0,9'" +
            "                HorizontalAlignment='Center' VerticalAlignment='Center'/>" +
            "        </Border>" +
            "        <ControlTemplate.Triggers>" +
            "          <Trigger Property='IsMouseOver' Value='True'>" +
            "            <Setter TargetName='bd' Property='Background' Value='#E81123'/>" +
            "            <Setter TargetName='x'  Property='Stroke'     Value='White'/>" +
            "          </Trigger>" +
            "        </ControlTemplate.Triggers>" +
            "      </ControlTemplate>" +
            "    </Setter.Value>" +
            "  </Setter>" +
            "</Style>");

        static Style BuildActionButtonStyle(bool primary)
        {
            string bg     = primary ? "#0F6CBD" : "#FFFFFF";
            string bgHov  = primary ? "#1A7BD0" : "#F0F0F4";
            string bgDown = primary ? "#0C5BA0" : "#E4E4EA";
            string fg     = primary ? "White"   : "#33333A";
            string border = primary ? "#0F6CBD" : "#C8C8D0";

            return Parse(
            "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'" +
            "       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'" +
            "       TargetType='Button'>" +
            "  <Setter Property='FontFamily' Value='Segoe UI'/>" +
            "  <Setter Property='FontSize'  Value='12'/>" +
            "  <Setter Property='Height'    Value='30'/>" +
            "  <Setter Property='Cursor'    Value='Hand'/>" +
           $"  <Setter Property='Foreground' Value='{fg}'/>" +
            "  <Setter Property='Template'>" +
            "    <Setter.Value>" +
            "      <ControlTemplate TargetType='Button'>" +
           $"        <Border x:Name='bd' CornerRadius='6' BorderThickness='1' Padding='16,4,16,4'" +
           $"                Background='{bg}' BorderBrush='{border}'>" +
            "          <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>" +
            "        </Border>" +
            "        <ControlTemplate.Triggers>" +
           $"          <Trigger Property='IsMouseOver' Value='True'>" +
           $"            <Setter TargetName='bd' Property='Background' Value='{bgHov}'/>" +
            "          </Trigger>" +
           $"          <Trigger Property='IsPressed' Value='True'>" +
           $"            <Setter TargetName='bd' Property='Background' Value='{bgDown}'/>" +
            "          </Trigger>" +
            "        </ControlTemplate.Triggers>" +
            "      </ControlTemplate>" +
            "    </Setter.Value>" +
            "  </Setter>" +
            "</Style>");
        }

        static Style BuildComboBoxStyle() => Parse(
            "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'" +
            "       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'" +
            "       TargetType='ComboBox'>" +
            "  <Setter Property='SnapsToDevicePixels' Value='True'/>" +
            "  <Setter Property='Template'>" +
            "    <Setter.Value>" +
            "      <ControlTemplate TargetType='ComboBox'>" +
            "        <Grid>" +
            "          <Border x:Name='bd' CornerRadius='6' BorderThickness='1'" +
            "                  Background='White' BorderBrush='#C8C8D0'/>" +
            "          <ToggleButton x:Name='tgl' Focusable='False' ClickMode='Press'" +
            "                        Background='Transparent' BorderThickness='0'" +
            "                        IsChecked='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'>" +
            "            <ToggleButton.Template>" +
            "              <ControlTemplate TargetType='ToggleButton'>" +
            "                <Border Background='Transparent'>" +
            "                  <Path HorizontalAlignment='Right' VerticalAlignment='Center'" +
            "                        Margin='0,0,11,0' Fill='#70707A'" +
            "                        Data='M 0,0 L 4,4 L 8,0 Z'/>" +
            "                </Border>" +
            "              </ControlTemplate>" +
            "            </ToggleButton.Template>" +
            "          </ToggleButton>" +
            "          <TextBox x:Name='PART_EditableTextBox' Margin='10,0,28,0'" +
            "                   VerticalContentAlignment='Center' Background='Transparent'" +
            "                   BorderThickness='0' Foreground='#22222A'" +
            "                   IsReadOnly='{TemplateBinding IsReadOnly}'/>" +
            "          <Popup x:Name='PART_Popup' AllowsTransparency='True' Placement='Bottom'" +
            "                 IsOpen='{TemplateBinding IsDropDownOpen}' Focusable='False'" +
            "                 PopupAnimation='Slide'>" +
            "            <Border CornerRadius='6' Background='White' BorderBrush='#C8C8D0'" +
            "                    BorderThickness='1' MinWidth='{TemplateBinding ActualWidth}'" +
            "                    Margin='0,2,0,0' SnapsToDevicePixels='True'>" +
            "              <Border.Effect>" +
            "                <DropShadowEffect BlurRadius='10' ShadowDepth='2' Opacity='0.2'/>" +
            "              </Border.Effect>" +
            "              <ScrollViewer MaxHeight='220'>" +
            "                <ItemsPresenter/>" +
            "              </ScrollViewer>" +
            "            </Border>" +
            "          </Popup>" +
            "        </Grid>" +
            "        <ControlTemplate.Triggers>" +
            "          <Trigger Property='IsMouseOver' Value='True'>" +
            "            <Setter TargetName='bd' Property='BorderBrush' Value='#A8A8B2'/>" +
            "          </Trigger>" +
            "          <Trigger Property='IsKeyboardFocusWithin' Value='True'>" +
            "            <Setter TargetName='bd' Property='BorderBrush' Value='#0F6CBD'/>" +
            "            <Setter TargetName='bd' Property='BorderThickness' Value='1.5'/>" +
            "          </Trigger>" +
            "        </ControlTemplate.Triggers>" +
            "      </ControlTemplate>" +
            "    </Setter.Value>" +
            "  </Setter>" +
            "</Style>");

        // ── Format-all: icon, popup, apply ────────────────────────────────────

        static UIElement MakeFormatIcon()
        {
            var sp = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
            void Bar(string hex, double bottom = 1.5) =>
                sp.Children.Add(new Border
                {
                    Height = 2.5, Width = 14, CornerRadius = new CornerRadius(1.2),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                    Margin = new Thickness(0, 0, 0, bottom),
                });
            Bar("#4EC9B0");
            Bar("#569CD6");
            Bar("#CE9178", 0);
            return sp;
        }

        Popup BuildFormatPopup()
        {
            var popup = new Popup { StaysOpen = true, AllowsTransparency = true, Placement = PlacementMode.Bottom };
            popup.PreviewMouseDown += (_, _) => Activate();
            popup.Opened += (_, _) =>
            {
                if (PresentationSource.FromVisual(popup.Child) is HwndSource src)
                    SetWindowPos(src.Handle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE_NOSIZE);
            };
            LocationChanged += (_, _) =>
            {
                if (!popup.IsOpen) return;
                var h = popup.HorizontalOffset;
                popup.HorizontalOffset = h + 1;
                popup.HorizontalOffset = h;
            };

            var card = new Border
            {
                Background      = new SolidColorBrush(Color.FromRgb(0xFB, 0xFB, 0xFC)),
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0xC4, 0xC4, 0xCC)),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(10),
                Margin          = new Thickness(4, 2, 4, 8),
                Padding         = new Thickness(12, 10, 12, 10),
                MinWidth        = 218,
                Effect          = new System.Windows.Media.Effects.DropShadowEffect
                    { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.20, Color = Colors.Black, Direction = 270 },
            };
            BindingOperations.SetBinding(card, UIElement.OpacityProperty,
                new Binding("Opacity") { Source = this });
            var root = new StackPanel();
            card.Child  = root;
            popup.Child = card;

            var toggleStyle = BuildToggleStyle();
            var navStyle    = BuildNavButtonStyle();
            var swatchStyle = BuildSwatchBtnStyle();
            var sep = new Func<UIElement>(() =>
                new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xEC)), Margin = new Thickness(0, 0, 0, 8) });
            var label = new Func<string, UIElement>(t =>
                new TextBlock
                {
                    Text       = t, FontFamily = new FontFamily("Segoe UI"), FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x94)),
                    Margin     = new Thickness(0, 0, 0, 2),
                });

            // ── Style ──────────────────────────────────────────────────────────
            root.Children.Add(label("STYLE"));
            var styleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 10) };

            ToggleButton StyleToggle(UIElement icon, string tip, Func<int, int, bool> isActive, Action<int, int, bool> setValue)
            {
                var b = new ToggleButton { Content = icon, ToolTip = tip, Width = 30, Height = 30, Margin = new Thickness(0, 0, 4, 0), Cursor = Cursors.Hand, Style = toggleStyle };
                b.Click += (_, _) =>
                {
                    var fm    = _staticFmtManager;
                    var scope = MatchesInScope().ToList();
                    if (fm == null || scope.Count == 0) return;
                    bool allActive = scope.All(m => isActive(m.Start, m.Start + m.Length));
                    bool target = !allActive;
                    ApplyFormattingToAllMatches((s, e) => setValue(s, e, target));
                    UpdateFormatToggles();
                };
                return b;
            }

            _fmtBoldBtn   = StyleToggle(new TextBlock { Text = "B", FontWeight = FontWeights.Bold,   FontSize = 13, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }, "Bold all matches",          (s, e) => _staticFmtManager?.IsRangeBold(s, e)          == true, (s, e, v) => _staticFmtManager?.SetBold(s, e, v));
            _fmtItalicBtn = StyleToggle(new TextBlock { Text = "I", FontStyle  = FontStyles.Italic,  FontSize = 13, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }, "Italic all matches",        (s, e) => _staticFmtManager?.IsRangeItalic(s, e)        == true, (s, e, v) => _staticFmtManager?.SetItalic(s, e, v));
            _fmtUnderBtn  = StyleToggle(new TextBlock { Text = "U", TextDecorations = TextDecorations.Underline,     FontSize = 13, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }, "Underline all matches",     (s, e) => _staticFmtManager?.IsRangeUnderline(s, e)     == true, (s, e, v) => _staticFmtManager?.SetUnderline(s, e, v));
            _fmtStrikeBtn = StyleToggle(new TextBlock { Text = "S", TextDecorations = TextDecorations.Strikethrough, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }, "Strikethrough all matches", (s, e) => _staticFmtManager?.IsRangeStrikethrough(s, e) == true, (s, e, v) => _staticFmtManager?.SetStrikethrough(s, e, v));

            Button TransformBtn(string text, string tip, Func<string, string> transform)
            {
                var b = new Button
                {
                    Content = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center },
                    ToolTip = tip, Width = 30, Height = 30, Margin = new Thickness(0, 0, 4, 0), Cursor = Cursors.Hand, Style = navStyle,
                };
                b.Click += (_, _) => ApplyTextTransformToAllMatches(transform);
                return b;
            }

            styleRow.Children.Add(_fmtBoldBtn);
            styleRow.Children.Add(_fmtItalicBtn);
            styleRow.Children.Add(_fmtUnderBtn);
            styleRow.Children.Add(_fmtStrikeBtn);
            styleRow.Children.Add(TransformBtn("AA", "UPPERCASE all matches", s => s.ToUpper()));
            styleRow.Children.Add(TransformBtn("aa", "lowercase all matches", s => s.ToLower()));
            root.Children.Add(styleRow);
            root.Children.Add(sep());

            // ── Text colors ────────────────────────────────────────────────────
            var entries = _staticColorEntries;
            if (entries != null)
            {
                Button DotBtn(string hex, string name)
                {
                    bool isWhite = hex.Equals("#FFFFFF", StringComparison.OrdinalIgnoreCase);
                    var swatch = new Border
                    {
                        Width = 16, Height = 16, CornerRadius = new CornerRadius(8),
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                        BorderBrush = isWhite ? new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)) : null,
                        BorderThickness = isWhite ? new Thickness(1) : new Thickness(0),
                    };
                    var b = new Button { Content = swatch, ToolTip = name, Width = 24, Height = 24, Margin = new Thickness(0, 0, 3, 0), Cursor = Cursors.Hand, Style = swatchStyle };
                    b.Click += (_, _) => { ApplyFormattingToAllMatches((s, e) => _staticFmtManager?.ToggleForeColor(s, e, hex)); };
                    return b;
                }

                root.Children.Add(label("TEXT COLOR"));
                var tRow1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 3) };
                var tRow2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
                foreach (int k in new[] { 1, 2, 3, 4, 5 })
                {
                    var ce1 = entries.FirstOrDefault(e => e.TypeId == 1 && e.KeyNumber == k);
                    if (ce1?.ColorHex != null) tRow1.Children.Add(DotBtn(ce1.ColorHex, ce1.Name ?? ce1.ColorHex));
                    var ce3 = entries.FirstOrDefault(e => e.TypeId == 3 && e.KeyNumber == k);
                    if (ce3?.ColorHex != null) tRow2.Children.Add(DotBtn(ce3.ColorHex, (ce3.Name ?? ce3.ColorHex)));
                }
                root.Children.Add(tRow1);
                root.Children.Add(tRow2);
                root.Children.Add(sep());

                // ── Highlight colors ───────────────────────────────────────────
                Button SquareBtn(string hex, string name)
                {
                    var swatch = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)) };
                    var b = new Button { Content = swatch, ToolTip = name, Width = 24, Height = 24, Margin = new Thickness(0, 0, 3, 0), Cursor = Cursors.Hand, Style = swatchStyle };
                    b.Click += (_, _) => { ApplyFormattingToAllMatches((s, e) => _staticFmtManager?.ToggleBackColor(s, e, hex)); };
                    return b;
                }

                root.Children.Add(label("HIGHLIGHT"));
                var hRow1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 3) };
                var hRow2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
                foreach (int k in new[] { 6, 7, 8, 9, 0 })
                {
                    var ce2 = entries.FirstOrDefault(e => e.TypeId == 2 && e.KeyNumber == k);
                    if (ce2?.ColorHex != null) hRow1.Children.Add(SquareBtn(ce2.ColorHex, ce2.Name ?? ce2.ColorHex));
                    var ce4 = entries.FirstOrDefault(e => e.TypeId == 4 && e.KeyNumber == k);
                    if (ce4?.ColorHex != null) hRow2.Children.Add(SquareBtn(ce4.ColorHex, ce4.Name ?? ce4.ColorHex));
                }
                root.Children.Add(hRow1);
                root.Children.Add(hRow2);
                root.Children.Add(sep());
            }

            // ── Clear ──────────────────────────────────────────────────────────
            var clearRow = new DockPanel { LastChildFill = false };
            var clearBtn = MakeActionButton("✕  Clear all", "Remove all formatting from all matches", primary: false);
            clearBtn.Click += (_, _) => { ApplyFormattingToAllMatches((s, e) => _staticFmtManager?.ClearFormatting(s, e)); };
            DockPanel.SetDock(clearBtn, Dock.Right);
            clearRow.Children.Add(clearBtn);
            var inSelPopupChk = new CheckBox
            {
                Content           = "In Selection",
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 8, 0),
                ToolTip           = "Apply formatting only to matches within the active selection",
                IsChecked         = _inSelectionOnly,
                FontSize          = 11,
            };
            _inSelPopupChk = inSelPopupChk;
            inSelPopupChk.Checked   += (_, _) => _inSelectionOnly = true;
            inSelPopupChk.Unchecked += (_, _) => _inSelectionOnly = false;
            clearRow.Children.Add(inSelPopupChk);
            root.Children.Add(clearRow);

            return popup;
        }

        void ApplyFormattingToAllMatches(Action<int, int> action)
        {
            var fm = _staticFmtManager;
            if (_target == null || fm == null) return;
            var scope = MatchesInScope().ToList();
            if (scope.Count == 0) return;
            var before = fm.TakeSnapshot();
            foreach (var (start, len) in scope)
                action(start, start + len);
            var after = fm.TakeSnapshot();
            _target.Document.UndoStack.Push(new FormattingUndoOperation(fm, before, after, _target.TextArea.TextView));
            _target.TextArea.TextView.Redraw();
        }

        void ApplyTextTransformToAllMatches(Func<string, string> transform)
        {
            if (_target == null || _matches.Count == 0) return;
            _suppressSearch = true;
            using (_target.Document.RunUpdate())
            {
                for (int i = _matches.Count - 1; i >= 0; i--)
                {
                    var (start, len) = _matches[i];
                    string original    = _target.Document.GetText(start, len);
                    string transformed = transform(original);
                    if (original != transformed)
                        _target.Document.Replace(start, len, transformed);
                }
            }
            _suppressSearch = false;
            RunSearch(jump: false);
        }

        void UpdateFormatToggles()
        {
            if (_fmtBoldBtn == null) return;
            var fm = _staticFmtManager;
            bool hasMatches = fm != null && _matches.Count > 0;
            _fmtBoldBtn.IsChecked   = hasMatches && _matches.All(m => fm!.IsRangeBold(m.Start, m.Start + m.Length));
            _fmtItalicBtn!.IsChecked = hasMatches && _matches.All(m => fm!.IsRangeItalic(m.Start, m.Start + m.Length));
            _fmtUnderBtn!.IsChecked  = hasMatches && _matches.All(m => fm!.IsRangeUnderline(m.Start, m.Start + m.Length));
            _fmtStrikeBtn!.IsChecked = hasMatches && _matches.All(m => fm!.IsRangeStrikethrough(m.Start, m.Start + m.Length));
        }

        static Style BuildSwatchBtnStyle() => Parse(
            "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'" +
            "       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'" +
            "       TargetType='Button'>" +
            "  <Setter Property='Cursor'   Value='Hand'/>" +
            "  <Setter Property='Template'>" +
            "    <Setter.Value>" +
            "      <ControlTemplate TargetType='Button'>" +
            "        <Border x:Name='bd' CornerRadius='5' BorderThickness='2'" +
            "                Background='Transparent' BorderBrush='Transparent'>" +
            "          <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>" +
            "        </Border>" +
            "        <ControlTemplate.Triggers>" +
            "          <Trigger Property='IsMouseOver' Value='True'>" +
            "            <Setter TargetName='bd' Property='BorderBrush' Value='#66000000'/>" +
            "          </Trigger>" +
            "          <Trigger Property='IsPressed' Value='True'>" +
            "            <Setter TargetName='bd' Property='Background' Value='#22000000'/>" +
            "          </Trigger>" +
            "        </ControlTemplate.Triggers>" +
            "      </ControlTemplate>" +
            "    </Setter.Value>" +
            "  </Setter>" +
            "</Style>");

        static Style Parse(string xaml) => (Style)System.Windows.Markup.XamlReader.Parse(xaml);
    }

    // ── Highlight renderer ────────────────────────────────────────────────────

    class MatchHighlightRenderer : IBackgroundRenderer
    {
        static readonly Brush AllBrush    = Freeze(new SolidColorBrush(Color.FromArgb(110, 0xFF, 0xC0, 0x40)));
        static readonly Pen   CurWhitePen = MakePen(Colors.White, 1.8, null);
        static readonly Pen   CurBlackPen = MakePen(Colors.Black, 1.8, new DashStyle(new double[] { 3, 3 }, 0));

        List<(int Start, int Length)> _matches    = new();
        int                           _currentIdx = -1;

        public KnownLayer Layer => KnownLayer.Background;

        public void Update(List<(int Start, int Length)> matches, int currentIdx)
        {
            _matches    = matches;
            _currentIdx = currentIdx;
        }

        public void Draw(TextView textView, DrawingContext dc)
        {
            if (_matches.Count == 0) return;
            textView.EnsureVisualLines();

            for (int i = 0; i < _matches.Count; i++)
            {
                var (start, len) = _matches[i];
                bool isCur = i == _currentIdx;
                var  seg   = new TextSpan(start, len);

                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, seg))
                {
                    dc.DrawRectangle(AllBrush, null, rect);
                    if (isCur)
                    {
                        dc.DrawRectangle(null, CurWhitePen, rect);
                        dc.DrawRectangle(null, CurBlackPen, rect);
                    }
                }
            }
        }

        static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

        static Pen MakePen(Color color, double thickness, DashStyle? dash)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            var p = new Pen(brush, thickness);
            if (dash != null) { dash.Freeze(); p.DashStyle = dash; }
            p.Freeze();
            return p;
        }
    }

    readonly struct TextSpan : ICSharpCode.AvalonEdit.Document.ISegment
    {
        public int Offset    { get; }
        public int Length    { get; }
        public int EndOffset => Offset + Length;
        public TextSpan(int offset, int length) { Offset = offset; Length = length; }
    }
}
