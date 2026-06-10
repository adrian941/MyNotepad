using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MinimalNotepad
{
    class OpenWithRecord
    {
        public string Path { get; set; } = "";
        public string Name { get; set; } = "";
    }

    class OpenWithStoreData
    {
        public string?              DefaultPath { get; set; }
        public List<OpenWithRecord> Recent      { get; set; } = new();
    }

    static class OpenWithStore
    {
        static readonly string _savePath = System.IO.Path.Combine(
            MinimalNotepad.Config.AppDataPath.Root, "openwith.json");

        const int MaxRecent = 5;

        static OpenWithStoreData _data = new();

        static OpenWithStore() => Load();

        static void Load()
        {
            try
            {
                if (File.Exists(_savePath))
                    _data = JsonSerializer.Deserialize<OpenWithStoreData>(File.ReadAllText(_savePath)) ?? new();
            }
            catch { _data = new(); }
        }

        static void Save()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_savePath)!);
                File.WriteAllText(_savePath,
                    JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        public static string? DefaultPath => _data.DefaultPath;

        public static IReadOnlyList<OpenWithRecord> GetRecent() => _data.Recent;

        /// <summary>Record usage: moves to top of recent list, optionally sets as default.</summary>
        public static void Use(string path, string name, bool setDefault = false)
        {
            _data.Recent.RemoveAll(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
            _data.Recent.Insert(0, new OpenWithRecord { Path = path, Name = name });
            while (_data.Recent.Count > MaxRecent)
                _data.Recent.RemoveAt(_data.Recent.Count - 1);
            if (setDefault)
                _data.DefaultPath = path;
            Save();
        }

        // ── Known editor detection ────────────────────────────────────────────

        public static List<(string Name, string Path)> GetKnownEditors()
        {
            var result = new List<(string, string)>();
            var addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Try(string name, string rawPath)
            {
                string path = Environment.ExpandEnvironmentVariables(rawPath);
                if (!string.IsNullOrEmpty(path) && File.Exists(path) && addedPaths.Add(path))
                    result.Add((name, path));
            }

            // ── Built-in / always-present ─────────────────────────────────────
            Try("Notepad",          @"%windir%\notepad.exe");
            Try("Notepad",          @"C:\Windows\notepad.exe");

            // ── Common editors (direct paths) ─────────────────────────────────
            Try("Notepad++",        @"%ProgramFiles%\Notepad++\notepad++.exe");
            Try("Notepad++",        @"%ProgramFiles(x86)%\Notepad++\notepad++.exe");
            Try("Visual Studio Code", @"%LOCALAPPDATA%\Programs\Microsoft VS Code\Code.exe");
            Try("Visual Studio Code", @"%ProgramFiles%\Microsoft VS Code\Code.exe");
            Try("Sublime Text",     @"%ProgramFiles%\Sublime Text\subl.exe");
            Try("Sublime Text",     @"%ProgramFiles%\Sublime Text 3\sublime_text.exe");
            Try("Atom",             @"%LOCALAPPDATA%\atom\atom.exe");
            Try("Vim",              @"%ProgramFiles%\Vim\vim91\gvim.exe");
            Try("Vim",              @"%ProgramFiles(x86)%\Vim\vim91\gvim.exe");

            // ── Registry App Paths (HKLM + HKCU) ─────────────────────────────
            var regNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["code.exe"]         = "Visual Studio Code",
                ["code - insiders.exe"] = "VS Code Insiders",
                ["notepad++.exe"]    = "Notepad++",
                ["sublime_text.exe"] = "Sublime Text",
                ["subl.exe"]         = "Sublime Text",
                ["atom.exe"]         = "Atom",
                ["gvim.exe"]         = "Vim",
                ["nvim-qt.exe"]      = "Neovim",
                ["kate.exe"]         = "Kate",
                ["gedit.exe"]        = "gedit",
            };

            foreach (var hive in new[] {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths" })
            {
                foreach (var rootKey in new[] { Registry.LocalMachine, Registry.CurrentUser })
                {
                    using var key = rootKey.OpenSubKey(hive);
                    if (key == null) continue;
                    foreach (var (exe, friendlyName) in regNames)
                    {
                        using var sub = key.OpenSubKey(exe);
                        if (sub?.GetValue("") is string path)
                            Try(friendlyName, path);
                    }
                }
            }

            return result;
        }
    }
}
