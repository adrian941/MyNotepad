using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MinimalNotepad.Formatting;

namespace MinimalNotepad
{
    /// <summary>
    /// Listens to system-wide clipboard changes (WM_CLIPBOARDUPDATE).
    /// When enabled, pushes non-app clipboard text to NormalClipboardHistory.
    /// Only the first NotepadWindow starts the listener; subsequent windows are no-ops.
    /// </summary>
    static class GlobalClipboardMonitor
    {
        const int    WM_CLIPBOARDUPDATE  = 0x031D;
        const string AppClipboardFormat  = "MinimalNotepad.RichText.v1";

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool AddClipboardFormatListener(IntPtr hwnd);

        static bool _started;
        static bool _isEnabled;

        public static bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled == value) return;
                _isEnabled = value;
                EnabledChanged?.Invoke(value);
            }
        }

        /// <summary>Raised when IsEnabled changes (e.g. from the HelpWindow checkbox).</summary>
        public static event Action<bool>? EnabledChanged;

        /// <summary>
        /// Registers WM_CLIPBOARDUPDATE on the given window's HWND.
        /// Safe to call from multiple windows — only the first call takes effect.
        /// </summary>
        public static void Start(Window window)
        {
            if (_started) return;
            _started = true;

            var hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            hwndSource.AddHook(WndProc);
            AddClipboardFormatListener(new WindowInteropHelper(window).Handle);
        }

        static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_CLIPBOARDUPDATE && _isEnabled)
            {
                try
                {
                    // Ignore if this copy came from our own app
                    if (Clipboard.ContainsData(AppClipboardFormat))
                        return IntPtr.Zero;

                    if (Clipboard.ContainsText())
                        NormalClipboardHistory.Push(Clipboard.GetText());
                }
                catch { }
            }
            return IntPtr.Zero;
        }
    }
}
