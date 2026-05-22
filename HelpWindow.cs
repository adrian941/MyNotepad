using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MinimalNotepad
{
    class HelpWindow : Window
    {
        // ── Singleton state (shared across all NotepadWindow instances) ────────

        private static readonly Mutex _crossProcessMutex =
            new(false, "MinimalNotepad_HelpWindow");

        private static HelpWindow? _instance;

        /// <summary>
        /// Opens the help window, or activates the already-open instance
        /// (even if it was opened from another process / exe instance).
        /// </summary>
        public static void ShowOrActivate(
            IReadOnlyDictionary<int, string> textColorMap,
            IReadOnlyDictionary<int, string> highlightColorMap)
        {
            // Already open in this process → just bring to front
            if (_instance != null) { _instance.Activate(); return; }

            // Try to claim the cross-process mutex (non-blocking)
            bool acquired;
            try   { acquired = _crossProcessMutex.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; } // previous process crashed

            if (!acquired)
            {
                // Open in another exe instance → find and activate it via Win32
                var hwnd = FindWindow(null, "Quick Guide");
                if (hwnd != System.IntPtr.Zero)
                {
                    ShowWindow(hwnd, SW_RESTORE);
                    SetForegroundWindow(hwnd);
                }
                return;
            }

            _instance = new HelpWindow(textColorMap, highlightColorMap);
            _instance.Closed += (_, _) =>
            {
                _instance = null;
                try { _crossProcessMutex.ReleaseMutex(); } catch { }
            };
            _instance.Show();
        }

        // ── Constructor ───────────────────────────────────────────────────────

        HelpWindow(
            IReadOnlyDictionary<int, string> textColorMap,
            IReadOnlyDictionary<int, string> highlightColorMap)
        {
            Title                 = "Quick Guide";
            Width                 = 490;
            Height                = 720;
            ResizeMode            = ResizeMode.CanResize;
            // CenterScreen: no Owner, so it positions itself independently
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background            = Brushes.White;
            // Not owned → never forced on top of unrelated OS windows
            ShowInTaskbar         = false;

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(22, 18, 22, 18)
            };
            var root = new StackPanel();
            scroll.Content = root;
            Content = scroll;

            BuildContent(root, textColorMap, highlightColorMap);
        }

        // ── Content builder ───────────────────────────────────────────────────

        static void BuildContent(
            StackPanel root,
            IReadOnlyDictionary<int, string> textColorMap,
            IReadOnlyDictionary<int, string> highlightColorMap)
        {
            string TC(int key) => textColorMap.TryGetValue(key, out var h)      ? h : "#000000";
            string HL(int key) => highlightColorMap.TryGetValue(key, out var h) ? h : "#FFFFFF";

            // ── Header ────────────────────────────────────────────────────────
            root.Children.Add(new TextBlock
            {
                Text = "Minimal Notepad",
                FontSize = 18, FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 3)
            });
            root.Children.Add(new TextBlock
            {
                Text = "made by Adrian Alexandrescu · .NET Developer",
                FontSize = 12, Foreground = Brush("#666666"),
                Margin = new Thickness(0, 0, 0, 2)
            });
            root.Children.Add(new TextBlock
            {
                Text = "A minimalist, distraction-free notepad with rich text formatting.",
                FontSize = 12, Foreground = Brush("#888888"),
                TextWrapping = TextWrapping.Wrap
            });

            // ── Text Colors ───────────────────────────────────────────────────
            root.Children.Add(Section("Text Colors  (select text first)"));

            var colorWrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
            foreach (var (k, name) in new[] { (1,"Green"), (2,"Amber"), (3,"Red"), (4,"Blue"), (5,"Violet") })
                colorWrap.Children.Add(Row(Badge($"Ctrl+{k}"), Dot(TC(k)), Label(name)));
            root.Children.Add(colorWrap);
            root.Children.Add(Note("Same key again → resets to black"));

            // ── Highlights ────────────────────────────────────────────────────
            root.Children.Add(Section("Highlights  (select text first)"));

            var hlWrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
            foreach (var (k, name) in new[] { (6,"Green"), (7,"Blue"), (8,"Orange"), (9,"Red"), (0,"Violet") })
                hlWrap.Children.Add(Row(Badge($"Ctrl+{k}"), Swatch(HL(k)), Label(name)));
            root.Children.Add(hlWrap);
            root.Children.Add(Note("Same key again → removes highlight"));

            // ── Move Lines ────────────────────────────────────────────────────
            root.Children.Add(Section("Move Lines"));

            root.Children.Add(Row(Badge("Alt+↑"), Badge("Alt+↓"),
                Label("Move current line (or selected block) up / down")));
            root.Children.Add(Note("Works with multi-line selections · fully undoable"));

            // ── Formatting ────────────────────────────────────────────────────
            root.Children.Add(Section("Text Formatting  (select text first)"));

            var fmtWrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            fmtWrap.Children.Add(Row(Badge("Ctrl+B"),  Label("Bold")));
            fmtWrap.Children.Add(Row(Badge("Ctrl+I"),  Label("Italic")));
            fmtWrap.Children.Add(Row(Badge("Ctrl+U"),  Label("Underline")));
            fmtWrap.Children.Add(Row(Badge("Ctrl+F5"), Label("Strikethrough")));
            root.Children.Add(fmtWrap);
            root.Children.Add(Note("All formatting is undoable with Ctrl+Z"));

            // ── Window ────────────────────────────────────────────────────────
            root.Children.Add(Section("Window"));

            root.Children.Add(Row(Badge("Ctrl+N"),      Label("New window")));
            root.Children.Add(Row(Badge("Ctrl+S"),      Label("Set window title")));
            root.Children.Add(Row(Badge("Ctrl+H"),      Label("This help window")));
            root.Children.Add(Row(Badge("Ctrl+±"),      Label("Increase/decrease font size")));
            root.Children.Add(Row(Badge("Ctrl+scroll"), Label("Also increase/decreases font size")));
        }

        // ── UI element factories ──────────────────────────────────────────────

        static SolidColorBrush Brush(string hex) =>
            new((Color)ColorConverter.ConvertFromString(hex));

        static UIElement Section(string title)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 16, 0, 6) };
            sp.Children.Add(new TextBlock
            {
                Text = title.ToUpperInvariant(),
                FontSize = 9.5, FontWeight = FontWeights.Bold,
                Foreground = Brush("#909090"),
                Margin = new Thickness(0, 0, 0, 4)
            });
            sp.Children.Add(new Border { Height = 1, Background = Brush("#E0E0E0") });
            return sp;
        }

        static UIElement Badge(string key) => new Border
        {
            Background = Brush("#F4F4F4"),
            BorderBrush = Brush("#C8C8C8"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 1, 5, 2),
            Margin = new Thickness(0, 0, 7, 0),
            Child = new TextBlock
            {
                Text = key, FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 11, VerticalAlignment = VerticalAlignment.Center
            }
        };

        // Colored dot (●) for text color swatches
        static UIElement Dot(string hex) => new TextBlock
        {
            Text = "●", FontSize = 13,
            Foreground = Brush(hex),
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        // Colored rectangle for highlight swatches
        static UIElement Swatch(string hex) => new Border
        {
            Width = 16, Height = 11,
            Background = Brush(hex),
            BorderBrush = Brush("#BBBBBB"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        static UIElement Row(params UIElement[] items)
        {
            var sp = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 3, 0, 3)
            };
            foreach (var item in items) sp.Children.Add(item);
            return sp;
        }

        static UIElement Label(string text) => new TextBlock
        {
            Text = text, FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 18, 0)
        };

        static UIElement Note(string text) => new TextBlock
        {
            Text = text, FontSize = 11,
            Foreground = Brush("#888888"),
            Margin = new Thickness(0, 2, 0, 0),
            FontStyle = FontStyles.Italic
        };

        // ── Win32 P/Invoke (cross-process singleton) ──────────────────────────

        const int SW_RESTORE = 9;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern System.IntPtr FindWindow(string? lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(System.IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(System.IntPtr hWnd, int nCmdShow);
    }
}
