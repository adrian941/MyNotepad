using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;

namespace MinimalNotepad.Formatting
{
    record SavedFileEntry(string FileName, string PlainText, string? RichJson, DateTime LastModified,
        double? WindowWidth = null, double? WindowHeight = null, double? FontSize = null,
        double? WindowLeft  = null, double? WindowTop    = null);

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

        public static void Save(string name, string plainText, string? richJson,
            double? windowWidth = null, double? windowHeight = null, double? fontSize = null,
            string? displayKey = null, double? windowLeft = null, double? windowTop = null)
        {
            EnsureFolder();
            string path = GetFilePath(name);
            var positions = MergePosition(ReadExistingPositions(path), displayKey, windowLeft, windowTop);
            File.WriteAllText(path, SerializeDto(plainText, richJson, windowWidth, windowHeight, fontSize, positions));
            // SavedFilesChanged fires via FileSystemWatcher
        }

        /// <summary>
        /// Writes a .mnp document to an arbitrary absolute path (used when a file was
        /// opened from outside the managed Saved folder — Ctrl+S then saves in-place
        /// instead of dumping a copy into the library).
        /// </summary>
        public static void SaveToPath(string fullPath, string plainText, string? richJson,
            double? windowWidth = null, double? windowHeight = null, double? fontSize = null,
            string? displayKey = null, double? windowLeft = null, double? windowTop = null)
        {
            var positions = MergePosition(ReadExistingPositions(fullPath), displayKey, windowLeft, windowTop);
            File.WriteAllText(fullPath, SerializeDto(plainText, richJson, windowWidth, windowHeight, fontSize, positions));
        }

        /// <summary>
        /// Patches window-state fields (size/font/position for current display) without touching content.
        /// </summary>
        public static void PatchWindowState(string filePath, double windowWidth, double windowHeight,
            double fontSize, string displayKey, double windowLeft, double windowTop)
        {
            try
            {
                if (!File.Exists(filePath)) return;
                var dto = JsonSerializer.Deserialize<SavedFileDto>(File.ReadAllText(filePath)) ?? new SavedFileDto();
                dto.WindowWidth  = windowWidth;
                dto.WindowHeight = windowHeight;
                dto.FontSize     = fontSize;
                dto.Positions    = MergePosition(dto.Positions, displayKey, windowLeft, windowTop);
                File.WriteAllText(filePath, JsonSerializer.Serialize(dto, _writeOpts));
            }
            catch { }
        }

        static Dictionary<string, PositionEntry> MergePosition(
            Dictionary<string, PositionEntry>? existing, string? key, double? left, double? top)
        {
            var dict = existing != null ? new Dictionary<string, PositionEntry>(existing) : new();
            if (key != null && left.HasValue && top.HasValue)
                dict[key] = new PositionEntry { Left = left.Value, Top = top.Value };
            return dict;
        }

        static string SerializeDto(string plainText, string? richJson,
            double? windowWidth = null, double? windowHeight = null, double? fontSize = null,
            Dictionary<string, PositionEntry>? positions = null)
        {
            var dto = new SavedFileDto
            {
                PlainText    = plainText,
                RichData     = richJson != null ? JsonDocument.Parse(richJson).RootElement : null,
                WindowWidth  = windowWidth,
                WindowHeight = windowHeight,
                FontSize     = fontSize,
                Positions    = positions?.Count > 0 ? positions : null,
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
            string fp = GetDisplayFingerprint();
            Save(entry.FileName, entry.PlainText, entry.RichJson,
                entry.WindowWidth, entry.WindowHeight, entry.FontSize,
                entry.WindowLeft.HasValue ? fp : null, entry.WindowLeft, entry.WindowTop);
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

        // ── Display fingerprint ───────────────────────────────────────────────

        [DllImport("user32.dll")]
        static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, EnumMonitorsProc fn, IntPtr data);
        delegate bool EnumMonitorsProc(IntPtr mon, IntPtr dc, ref MonRect rect, IntPtr data);

        [StructLayout(LayoutKind.Sequential)]
        struct MonRect { public int Left, Top, Right, Bottom; }

        public static string GetDisplayFingerprint()
        {
            var parts = new List<string>();
            EnumMonitorsProc cb = (_, _, ref r, _) =>
            {
                parts.Add($"{r.Left},{r.Top},{r.Right - r.Left},{r.Bottom - r.Top}");
                return true;
            };
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero);
            parts.Sort();
            return string.Join("|", parts);
        }

        static Dictionary<string, PositionEntry>? ReadExistingPositions(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                return JsonSerializer.Deserialize<SavedFileDto>(File.ReadAllText(path))?.Positions;
            }
            catch { return null; }
        }

        // ── DtoToEntry ────────────────────────────────────────────────────────

        static SavedFileEntry? DtoToEntry(SavedFileDto? dto, string filePath)
        {
            if (dto == null) return null;
            var name     = Path.GetFileNameWithoutExtension(filePath);
            var modified = File.GetLastWriteTime(filePath);
            var richJson = dto.RichData?.ValueKind == JsonValueKind.Undefined ? null
                         : dto.RichData?.GetRawText();

            double? left = null, top = null;
            if (dto.Positions != null && dto.Positions.TryGetValue(GetDisplayFingerprint(), out var pos))
            {
                left = pos.Left;
                top  = pos.Top;
            }

            return new SavedFileEntry(name, dto.PlainText, richJson, modified,
                dto.WindowWidth, dto.WindowHeight, dto.FontSize, left, top);
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
            public string       PlainText    { get; set; } = "";
            public JsonElement? RichData     { get; set; }
            public double?      WindowWidth  { get; set; }
            public double?      WindowHeight { get; set; }
            public double?      FontSize     { get; set; }
            public Dictionary<string, PositionEntry>? Positions { get; set; }
        }

        class PositionEntry
        {
            public double Left { get; set; }
            public double Top  { get; set; }
        }
    }
}
