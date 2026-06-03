using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MinimalNotepad.Config;

namespace MinimalNotepad
{
    class HelpWindow : Window
    {
        // ── Singleton state (shared across all NotepadWindow instances) ────────

        private static readonly Mutex _crossProcessMutex =
            new(false, "MinimalNotepad_HelpWindow");

        private static HelpWindow? _instance;

        // Stored so BuildContent can wire the checkbox without coupling to NotepadWindow
        private static AppSettings?  _sharedSettings;
        private static string?       _sharedSettingsPath;

        /// <summary>
        /// Opens the help window, or activates the already-open instance
        /// (even if it was opened from another process / exe instance).
        /// All display data — names, colors, key numbers — come from <paramref name="colorEntries"/>.
        /// </summary>
        public static void ShowOrActivate(
            IReadOnlyList<ColorEntry> colorEntries,
            AppSettings settings,
            string settingsPath)
        {
            _sharedSettings     = settings;
            _sharedSettingsPath = settingsPath;

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

            _instance = new HelpWindow(colorEntries);
            _instance.Closed += (_, _) =>
            {
                _instance = null;
                try { _crossProcessMutex.ReleaseMutex(); } catch { }
            };
            _instance.Show();
        }

        // ── Constructor ───────────────────────────────────────────────────────

        HelpWindow(IReadOnlyList<ColorEntry> colorEntries)
        {
            Title                 = "Quick Guide";
            Width                 = 490;
            Height                = 828;
            ResizeMode            = ResizeMode.CanResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background            = Brushes.White;
            ShowInTaskbar         = false;

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(22, 18, 22, 18)
            };
            var root = new StackPanel();
            scroll.Content = root;

            BuildContent(root, colorEntries);

            // ── Global clipboard checkbox footer ──────────────────────────────
            var cb = new CheckBox
            {
                Content   = "Track global clipboard (save all system copies)",
                IsChecked = GlobalClipboardMonitor.IsEnabled,
                FontSize  = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                Margin    = new Thickness(22, 0, 22, 14),
                Cursor    = Cursors.Hand
            };
            cb.Checked   += (_, _) => { GlobalClipboardMonitor.IsEnabled = true;  };
            cb.Unchecked += (_, _) => { GlobalClipboardMonitor.IsEnabled = false; };

            // Keep checkbox in sync if another window changes the setting
            GlobalClipboardMonitor.EnabledChanged += v => cb.IsChecked = v;

            var footerSep = new Border
            {
                Height     = 1,
                Margin     = new Thickness(22, 6, 22, 6),
                Background = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0))
            };

            var outerDock = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(cb,        Dock.Bottom);
            DockPanel.SetDock(footerSep, Dock.Bottom);
            outerDock.Children.Add(cb);
            outerDock.Children.Add(footerSep);
            outerDock.Children.Add(scroll);
            Content = outerDock;
        }

        // ── Content builder ───────────────────────────────────────────────────

        static void BuildContent(StackPanel root, IReadOnlyList<ColorEntry> colorEntries)
        {
            var textEntries = colorEntries
                .Where(e => e.TypeId == 1)
                .OrderBy(e => e.KeyNumber)
                .ToList();

            // Highlights: key 6-9 first, then 0 last
            var hlEntries = colorEntries
                .Where(e => e.TypeId == 2)
                .OrderBy(e => e.KeyNumber == 0 ? 10 : e.KeyNumber)
                .ToList();

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
            foreach (var e in textEntries)
                colorWrap.Children.Add(Row(
                    Badge($"Ctrl+{e.KeyNumber}"),
                    Dot(e.ColorHex ?? "#000000"),
                    Label(e.Name ?? $"Color {e.KeyNumber}")));
            root.Children.Add(colorWrap);
            root.Children.Add(Note("Same key again → resets to black  ·  +Shift = darker shade"));

            // ── Highlights ────────────────────────────────────────────────────
            root.Children.Add(Section("Highlights  (select text first)"));

            var hlWrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
            foreach (var e in hlEntries)
                hlWrap.Children.Add(Row(
                    Badge($"Ctrl+{e.KeyNumber}"),
                    Swatch(e.ColorHex ?? "#FFFFFF"),
                    Label(e.Name ?? $"Color {e.KeyNumber}")));
            root.Children.Add(hlWrap);
            root.Children.Add(Note("Same key again → removes highlight  ·  +Shift = stronger color"));

            // ── Move Lines ────────────────────────────────────────────────────
            root.Children.Add(Section("Move Lines"));

            root.Children.Add(Row(Badge("Alt+↑"), Badge("Alt+↓"),
                Label("Move current line (or selected block) up / down")));
            root.Children.Add(Note("Works with multi-line selections · fully undoable"));

            // ── Formatting ────────────────────────────────────────────────────
            root.Children.Add(Section("Text Formatting  (select text first)"));

            var fmtWrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            fmtWrap.Children.Add(Row(Badge("Ctrl+B"),      Label("Bold")));
            fmtWrap.Children.Add(Row(Badge("Ctrl+I"),      Label("Italic")));
            fmtWrap.Children.Add(Row(Badge("Ctrl+U"),      Label("Underline")));
            fmtWrap.Children.Add(Row(Badge("Ctrl+F5"),     Label("Strikethrough")));
            fmtWrap.Children.Add(Row(Badge("Alt+U"),       Label("Lowercase selection")));
            fmtWrap.Children.Add(Row(Badge("Alt+Shift+U"), Label("Uppercase selection")));
            root.Children.Add(fmtWrap);
            root.Children.Add(Note("All formatting is undoable with Ctrl+Z"));

            // ── Find & Replace ─────────────────────────────────────────────────
            root.Children.Add(Section("Find & Replace"));

            root.Children.Add(Row(Badge("Ctrl+F"), Label("Find  (uses current selection as search term)")));
            root.Children.Add(Row(Badge("Ctrl+R"), Label("Find & Replace")));
            root.Children.Add(Row(Badge("Enter"), Badge("F3"), Label("Next match")));
            root.Children.Add(Row(Badge("Shift+Enter"), Badge("Shift+F3"), Label("Previous match")));
            root.Children.Add(Note("Toggle Aa for case-sensitive, ab for whole word · one shared window across all instances"));

            // ── Window ────────────────────────────────────────────────────────
            root.Children.Add(Section("Window"));

            root.Children.Add(Row(Badge("Ctrl+N"),        Label("New window")));
            root.Children.Add(Row(Badge("Ctrl+S"),        Label("Save file")));
            root.Children.Add(Row(Badge("Ctrl+Shift+S"),  Label("Save file as new name")));
            root.Children.Add(Row(Badge("Ctrl+Shift+R"),  Label("Rename saved file")));
            root.Children.Add(Row(Badge("Ctrl+O"),        Label("Open saved files view")));
            root.Children.Add(Row(Badge("Ctrl+Alt+V"),    Label("Clipboard history (App / System)")));
            root.Children.Add(Row(Badge("Ctrl+H"),        Label("This help window")));
            root.Children.Add(Row(Badge("Ctrl+±"), Badge("Ctrl+scroll"), Label("Increase / decrease font size")));
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

        // Colored dot (●) for text-color swatches.
        // Light colors (e.g. white) get an outlined circle so they stay visible on white bg.
        // The outlined border is sized to match the visual diameter of "●" at FontSize 13.
        static UIElement Dot(string hex)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            bool isLight = color.R + color.G + color.B > 600;
            if (isLight)
                return new Border
                {
                    Width = 10, Height = 10,
                    CornerRadius = new CornerRadius(5),
                    Background = Brush(hex),
                    BorderBrush = Brush("#999999"), BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
            return new TextBlock
            {
                Text = "●", FontSize = 13,
                Foreground = Brush(hex),
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

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
