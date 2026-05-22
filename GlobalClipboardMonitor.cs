using System;

namespace MinimalNotepad
{
    /// <summary>
    /// Holds the global "save system clipboard" toggle.
    /// Actual WM_CLIPBOARDUPDATE listening is done by ClipboardDaemon.
    /// </summary>
    static class GlobalClipboardMonitor
    {
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

        /// <summary>Raised when IsEnabled changes.</summary>
        public static event Action<bool>? EnabledChanged;
    }
}
