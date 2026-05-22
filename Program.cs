using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using MinimalNotepad.Config;

namespace MinimalNotepad
{
    class Program
    {
        const string MutexName     = "MinimalNotepad_SingleInstance";
        const string OpenWinEvent  = "MinimalNotepad_OpenNewWindow";

        static Mutex? _singleInstanceMutex;

        [STAThread]
        static void Main()
        {
            // ── Single-instance guard ─────────────────────────────────────────
            _singleInstanceMutex = new Mutex(true, MutexName, out bool isFirstInstance);
            if (!isFirstInstance)
            {
                // Signal the running instance to open a new window, then exit
                try
                {
                    var ev = EventWaitHandle.OpenExisting(OpenWinEvent);
                    ev.Set();
                }
                catch { }
                return;
            }

            // ── First instance ────────────────────────────────────────────────
            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            string baseDir      = AppDomain.CurrentDomain.BaseDirectory;
            string settingsFile = Path.Combine(baseDir, "settings.json");
            string colorsFile   = Path.Combine(baseDir, "colors.json");

            var settings    = ConfigLoader.LoadSettings(settingsFile);
            var colorConfig = ConfigLoader.LoadOrCreateColorConfig(colorsFile);

            MinimalNotepad.Formatting.ClipboardHistory.Load();
            MinimalNotepad.Formatting.NormalClipboardHistory.Load();

            if (settings.SaveGlobalClipboard)
                ClipboardDaemon.Start();

            GlobalClipboardMonitor.EnabledChanged += enabled =>
            {
                if (enabled)
                    ClipboardDaemon.Start();
                else
                {
                    ClipboardDaemon.Stop();
                    TryShutdownIfIdle();
                }
            };

            var allWindows  = new List<NotepadWindow>();

            // ── Named event: a second EXE launch signals us to open a new window
            var openWinHandle = new EventWaitHandle(false, EventResetMode.AutoReset, OpenWinEvent);
            var listenerThread = new Thread(() =>
            {
                while (true)
                {
                    openWinHandle.WaitOne();   // blocks until signalled
                    app.Dispatcher.InvokeAsync(() =>
                    {
                        var w = new NotepadWindow(settings, settingsFile, colorConfig.Colors, allWindows);
                        w.Show();
                        w.Activate();
                    });
                }
            })
            { IsBackground = true, Name = "OpenWindowListener" };
            listenerThread.Start();

            var firstWindow = new NotepadWindow(settings, settingsFile, colorConfig.Colors, allWindows);
            firstWindow.Show();

            app.Run();

            try { _singleInstanceMutex.ReleaseMutex(); } catch { }
        }

        /// <summary>
        /// Shuts the application down if there are no open editor windows
        /// and the background daemon is not running.
        /// </summary>
        public static void TryShutdownIfIdle()
        {
            foreach (Window w in Application.Current.Windows)
                if (w is NotepadWindow) return;

            if (!ClipboardDaemon.IsRunning)
                Application.Current.Shutdown();
        }
    }
}
