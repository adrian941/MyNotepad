using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
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

        static readonly JsonSerializerOptions _writeOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder       = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static void Save(string name, string plainText, string? richJson)
        {
            EnsureFolder();
            File.WriteAllText(GetFilePath(name), SerializeDto(plainText, richJson));
            // SavedFilesChanged fires via FileSystemWatcher
        }

        /// <summary>
        /// Writes a .mnp document to an arbitrary absolute path (used when a file was
        /// opened from outside the managed Saved folder — Ctrl+S then saves in-place
        /// instead of dumping a copy into the library).
        /// </summary>
        public static void SaveToPath(string fullPath, string plainText, string? richJson)
        {
            File.WriteAllText(fullPath, SerializeDto(plainText, richJson));
        }

        static string SerializeDto(string plainText, string? richJson)
        {
            var dto = new SavedFileDto
            {
                PlainText = plainText,
                RichData  = richJson != null ? JsonDocument.Parse(richJson).RootElement : null
            };
            return JsonSerializer.Serialize(dto, _writeOpts);
        }

        public static void Delete(string name)
        {
            try { File.Delete(GetFilePath(name)); } catch { }
            // SavedFilesChanged fires via FileSystemWatcher
        }

        public static void Restore(SavedFileEntry entry)
        {
            Save(entry.FileName, entry.PlainText, entry.RichJson);
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

        static SavedFileEntry? DtoToEntry(SavedFileDto? dto, string filePath)
        {
            if (dto == null) return null;
            var name     = Path.GetFileNameWithoutExtension(filePath);
            var modified = File.GetLastWriteTime(filePath);
            var richJson = dto.RichData?.ValueKind == JsonValueKind.Undefined ? null
                         : dto.RichData?.GetRawText();
            return new SavedFileEntry(name, dto.PlainText, richJson, modified);
        }

        public static SavedFileEntry? LoadFromPath(string filePath)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var dto  = JsonSerializer.Deserialize<SavedFileDto>(json);
                return DtoToEntry(dto, filePath);
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
                        var json = File.ReadAllText(path);
                        var dto  = JsonSerializer.Deserialize<SavedFileDto>(json);
                        var entry = DtoToEntry(dto, path);
                        if (entry != null) result.Add(entry);
                    }
                    catch { }
                }
            }
            catch { }
            return result.OrderByDescending(e => e.LastModified).ToList();
        }

        class SavedFileDto
        {
            public string       PlainText { get; set; } = "";
            public JsonElement? RichData  { get; set; }
        }
    }
}
