using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MinimalNotepad.Formatting;

namespace MinimalNotepad
{
    /// <summary>
    /// Owns a hidden, taskbar-invisible WPF window whose sole purpose is to receive
    /// WM_CLIPBOARDUPDATE messages for global clipboard monitoring.
    /// Independent of any NotepadWindow — survives after all editor windows are closed.
    /// No admin rights required.
    /// </summary>
    static class ClipboardDaemon
    {
        const int    WM_CLIPBOARDUPDATE = 0x031D;
        const string AppClipboardFormat = "MinimalNotepad.RichText.v1";

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        static DaemonWindow? _window;

        public static bool IsRunning => _window != null;

        /// <summary>Start the daemon (no-op if already running).</summary>
        public static void Start()
        {
            if (_window != null) return;
            _window = new DaemonWindow();
            _window.Show();   // must Show() so the HWND is created; window is invisible
        }

        /// <summary>Stop the daemon and release the HWND.</summary>
        public static void Stop()
        {
            _window?.Close();
            _window = null;
        }

        // ── Hidden window ─────────────────────────────────────────────────────

        sealed class DaemonWindow : Window
        {
            IntPtr _hwnd;

            public DaemonWindow()
            {
                // Completely invisible — no chrome, no taskbar entry, zero size
                WindowStyle       = WindowStyle.None;
                ShowInTaskbar     = false;
                Width             = 0;
                Height            = 0;
                Left              = -32000;
                Top               = -32000;
                AllowsTransparency= true;
                Opacity           = 0;
                Background        = null;
                ResizeMode        = ResizeMode.NoResize;
                ShowActivated     = false;
            }

            protected override void OnSourceInitialized(EventArgs e)
            {
                base.OnSourceInitialized(e);
                _hwnd = new WindowInteropHelper(this).Handle;

                var src = HwndSource.FromHwnd(_hwnd);
                src.AddHook(WndProc);
                AddClipboardFormatListener(_hwnd);
            }

            protected override void OnClosed(EventArgs e)
            {
                base.OnClosed(e);
                if (_hwnd != IntPtr.Zero)
                    RemoveClipboardFormatListener(_hwnd);
            }

            static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
            {
                if (msg == WM_CLIPBOARDUPDATE)
                {
                    try
                    {
                        if (!Clipboard.ContainsData(AppClipboardFormat) && Clipboard.ContainsText())
                            NormalClipboardHistory.Push(Clipboard.GetText());
                    }
                    catch { }
                }
                return IntPtr.Zero;
            }
        }
    }
}
