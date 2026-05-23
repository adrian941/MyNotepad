using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace MinimalNotepad.Formatting
{
    record SavedFileEntry(string FileName, string PlainText, string? RichJson, DateTime LastModified);

    static class SavedFileStore
    {
        public static readonly string SavedFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MinimalNotepad", "Saved");

        private static FileSystemWatcher? _watcher;

        public static event Action? SavedFilesChanged;

        static SavedFileStore()
        {
            EnsureFolder();
            StartWatcher();
        }

        static void EnsureFolder()
        {
            try { Directory.CreateDirectory(SavedFolder); } catch { }
        }

        static void StartWatcher()
        {
            try
            {
                EnsureFolder();
                _watcher = new FileSystemWatcher(SavedFolder, "*.mnp")
                {
                    NotifyFilter        = NotifyFilters.FileName | NotifyFilters.LastWrite,
                    EnableRaisingEvents = true
                };
                _watcher.Created += (_, _) => RaiseSavedFilesChanged();
                _watcher.Deleted += (_, _) => RaiseSavedFilesChanged();
                _watcher.Changed += (_, _) => RaiseSavedFilesChanged();
                _watcher.Renamed += (_, _) => RaiseSavedFilesChanged();
            }
            catch { }
        }

        static void RaiseSavedFilesChanged()
        {
            Application.Current?.Dispatcher.InvokeAsync(() => SavedFilesChanged?.Invoke());
        }

        public static bool FileExists(string name)
        {
            EnsureFolder();
            return File.Exists(GetFilePath(name));
        }

        public static string GetFilePath(string name) =>
            Path.Combine(SavedFolder, name + ".mnp");

        public static void Save(string name, string plainText, string? richJson)
        {
            EnsureFolder();
            var dto  = new SavedFileDto { PlainText = plainText, RichJson = richJson };
            var json = JsonSerializer.Serialize(dto);
            File.WriteAllText(GetFilePath(name), json);
            // SavedFilesChanged fires via FileSystemWatcher
        }

        public static void Delete(string name)
        {
            try { File.Delete(GetFilePath(name)); } catch { }
            // SavedFilesChanged fires via FileSystemWatcher
        }

        public static void DeleteAll()
        {
            try
            {
                foreach (var path in Directory.GetFiles(SavedFolder, "*.mnp"))
                    try { File.Delete(path); } catch { }
            }
            catch { }
        }

        public static SavedFileEntry? LoadFromPath(string filePath)
        {
            try
            {
                var json     = File.ReadAllText(filePath);
                var dto      = JsonSerializer.Deserialize<SavedFileDto>(json);
                if (dto == null) return null;
                var name     = Path.GetFileNameWithoutExtension(filePath);
                var modified = File.GetLastWriteTime(filePath);
                return new SavedFileEntry(name, dto.PlainText, dto.RichJson, modified);
            }
            catch { return null; }
        }

        /// <summary>Returns all saved files sorted by last-modified descending.</summary>
        public static List<SavedFileEntry> LoadAll()
        {
            EnsureFolder();
            var result = new List<SavedFileEntry>();
            try
            {
                foreach (var path in Directory.GetFiles(SavedFolder, "*.mnp"))
                {
                    try
                    {
                        var json     = File.ReadAllText(path);
                        var dto      = JsonSerializer.Deserialize<SavedFileDto>(json);
                        if (dto == null) continue;
                        var name     = Path.GetFileNameWithoutExtension(path);
                        var modified = File.GetLastWriteTime(path);
                        result.Add(new SavedFileEntry(name, dto.PlainText, dto.RichJson, modified));
                    }
                    catch { }
                }
            }
            catch { }
            return result.OrderByDescending(e => e.LastModified).ToList();
        }

        class SavedFileDto
        {
            public string  PlainText { get; set; } = "";
            public string? RichJson  { get; set; }
        }
    }
}
