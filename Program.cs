using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using MinimalNotepad.Config;
using MinimalNotepad.Formatting;

namespace MinimalNotepad
{
    class Program
    {
        const string MutexName     = "MinimalNotepad_SingleInstance";
        const string OpenWinEvent  = "MinimalNotepad_OpenNewWindow";

        static Mutex? _singleInstanceMutex;

        // Temp file used to pass a file path from a second instance to the first
        static string OpenRequestPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MinimalNotepad", "open_request.txt");

        [STAThread]
        static void Main(string[] args)
        {
            // A file path may be passed when the OS opens a .mnp file with us
            string? fileToOpen = args.Length > 0 && File.Exists(args[0]) ? args[0] : null;

            // ── Single-instance guard ─────────────────────────────────────────
            _singleInstanceMutex = new Mutex(true, MutexName, out bool isFirstInstance);
            if (!isFirstInstance)
            {
                // Pass file path (if any) to running instance via temp file, then signal
                try
                {
                    if (fileToOpen != null)
                    {
                        Directory.CreateDirectory(
                            Path.GetDirectoryName(OpenRequestPath)!);
                        File.WriteAllText(OpenRequestPath, fileToOpen);
                    }
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

            ClipboardHistory.Load();
            NormalClipboardHistory.Load();

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

            var allWindows = new List<NotepadWindow>();

            // Register .mnp → this executable (idempotent, no admin needed)
            RegisterFileAssociation();

            // ── Named event: a second EXE launch signals us ───────────────────
            var openWinHandle  = new EventWaitHandle(false, EventResetMode.AutoReset, OpenWinEvent);
            var listenerThread = new Thread(() =>
            {
                while (true)
                {
                    openWinHandle.WaitOne();
                    app.Dispatcher.InvokeAsync(() =>
                    {
                        string? pending = TryConsumeOpenRequest();
                        if (pending != null)
                            OpenMnpFile(pending, settings, settingsFile,
                                        colorConfig.Colors, allWindows);
                        else
                        {
                            var w = new NotepadWindow(settings, settingsFile,
                                                      colorConfig.Colors, allWindows);
                            w.Show();
                            w.Activate();
                        }
                    });
                }
            })
            { IsBackground = true, Name = "OpenWindowListener" };
            listenerThread.Start();

            var firstWindow = new NotepadWindow(settings, settingsFile,
                                                colorConfig.Colors, allWindows);
            firstWindow.Show();

            // If launched via file association, load the file into the first window
            if (fileToOpen != null)
            {
                var entry = SavedFileStore.LoadFromPath(fileToOpen);
                if (entry != null)
                    firstWindow.LoadSavedFile(entry);
            }

            app.Run();

            try { _singleInstanceMutex.ReleaseMutex(); } catch { }
        }

        // ── Inter-process file-open handoff ───────────────────────────────────

        static string? TryConsumeOpenRequest()
        {
            try
            {
                if (!File.Exists(OpenRequestPath)) return null;
                var path = File.ReadAllText(OpenRequestPath).Trim();
                File.Delete(OpenRequestPath);
                return File.Exists(path) ? path : null;
            }
            catch { return null; }
        }

        static void OpenMnpFile(string filePath, AppSettings settings,
            string settingsFile, IReadOnlyList<ColorEntry> colorEntries,
            List<NotepadWindow> allWindows)
        {
            var entry = SavedFileStore.LoadFromPath(filePath);
            if (entry == null) return;

            var refWin = allWindows.Count > 0 ? allWindows[0] : null;
            if (refWin != null)
            {
                NotepadWindow.OpenOrFocusSavedFile(entry, refWin);
            }
            else
            {
                var newWin = new NotepadWindow(settings, settingsFile,
                                               colorEntries, allWindows);
                newWin.Show();
                newWin.LoadSavedFile(entry);
                newWin.Activate();
            }
        }

        // ── .mnp file association (HKCU — no admin required) ─────────────────

        static void RegisterFileAssociation()
        {
            try
            {
                string exePath = System.Diagnostics.Process
                                       .GetCurrentProcess().MainModule!.FileName;
                string openCmd = $"\"{exePath}\" \"%1\"";

                // Skip if already registered with the current exe path
                using (var existing = Microsoft.Win32.Registry.CurrentUser
                           .OpenSubKey(@"Software\Classes\MinimalNotepad.mnp\shell\open\command"))
                {
                    if (existing?.GetValue("")?.ToString() == openCmd) return;
                }

                using (var ext = Microsoft.Win32.Registry.CurrentUser
                           .CreateSubKey(@"Software\Classes\.mnp"))
                    ext.SetValue("", "MinimalNotepad.mnp");

                using (var prog = Microsoft.Win32.Registry.CurrentUser
                           .CreateSubKey(@"Software\Classes\MinimalNotepad.mnp"))
                    prog.SetValue("", "Minimal Notepad File");

                using (var icon = Microsoft.Win32.Registry.CurrentUser
                           .CreateSubKey(@"Software\Classes\MinimalNotepad.mnp\DefaultIcon"))
                    icon.SetValue("", $"{exePath},0");

                using (var cmd = Microsoft.Win32.Registry.CurrentUser
                           .CreateSubKey(@"Software\Classes\MinimalNotepad.mnp\shell\open\command"))
                    cmd.SetValue("", openCmd);

                // Tell the Windows shell to refresh icon/association cache
                SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
            }
            catch { }
        }

        [DllImport("shell32.dll")]
        static extern void SHChangeNotify(uint wEventId, uint uFlags,
                                          IntPtr dwItem1, IntPtr dwItem2);

        // ── Shutdown helper ───────────────────────────────────────────────────

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
