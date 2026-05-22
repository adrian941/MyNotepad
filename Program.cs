using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using MinimalNotepad.Config;

namespace MinimalNotepad
{
    class Program
    {
        [STAThread]
        static void Main()
        {
            var app = new Application();

            string baseDir      = AppDomain.CurrentDomain.BaseDirectory;
            string settingsFile = Path.Combine(baseDir, "settings.json");
            string colorsFile   = Path.Combine(baseDir, "colors.json");

            var settings    = ConfigLoader.LoadSettings(settingsFile);
            var colorConfig = ConfigLoader.LoadOrCreateColorConfig(colorsFile);

            var allWindows  = new List<NotepadWindow>();
            var firstWindow = new NotepadWindow(
                settings, settingsFile, colorConfig.Colors, allWindows);
            firstWindow.Show();

            app.Run();
        }
    }
}
