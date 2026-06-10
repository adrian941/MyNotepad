using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MinimalNotepad.Config;

namespace MinimalNotepad.Formatting
{
    record ClipboardEntry(string PlainText, string? RichJson, DateTime CopiedAt);

    // DTO used only for JSON serialisation — plain properties, no ctor tricks
    class ClipboardEntryDto
    {
        public string   PlainText { get; set; } = "";
        public string?  RichJson  { get; set; }
        public DateTime CopiedAt  { get; set; }
    }

    static class ClipboardHistory
    {
        const int MaxEntries = 2000;

        public static readonly string SavePath = Path.Combine(AppDataPath.Root, "clipboard_history.json");

        static readonly List<ClipboardEntry> _entries = new();

        public static IReadOnlyList<ClipboardEntry> Entries => _entries;

        /// <summary>Fired on the UI thread each time the list changes.</summary>
        public static event Action? HistoryChanged;

        // ── Persistence ───────────────────────────────────────────────────────

        public static void Load()
        {
            try
            {
                if (!File.Exists(SavePath)) return;
                var json = File.ReadAllText(SavePath);
                var dtos = JsonSerializer.Deserialize<List<ClipboardEntryDto>>(json);
                if (dtos == null) return;
                foreach (var dto in dtos)
                    _entries.Add(new ClipboardEntry(dto.PlainText, dto.RichJson, dto.CopiedAt));
            }
            catch { /* ignore corrupt / missing file */ }
        }

        static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SavePath)!);
                var dtos = _entries
                    .Select(e => new ClipboardEntryDto
                    {
                        PlainText = e.PlainText,
                        RichJson  = e.RichJson,
                        CopiedAt  = e.CopiedAt
                    })
                    .ToList();
                File.WriteAllText(SavePath,
                    JsonSerializer.Serialize(dtos, new JsonSerializerOptions { WriteIndented = false }));
            }
            catch { /* ignore write errors (read-only drive, etc.) */ }
        }

        // ── Mutation ──────────────────────────────────────────────────────────

        public static void Push(string plainText, string? richJson)
        {
            if (string.IsNullOrEmpty(plainText)) return;

            // Trim leading/trailing whitespace (including styled spaces) before any check
            (plainText, richJson) = RichClipboard.TrimRich(plainText, richJson);
            if (string.IsNullOrEmpty(plainText)) return;

            if (_entries.Count > 0 && _entries[0].PlainText == plainText)
            {
                // Same plain text — update only if formatting changed
                if (_entries[0].RichJson == richJson) return;

                _entries[0] = new ClipboardEntry(plainText, richJson, DateTime.Now);
                Save();
                HistoryChanged?.Invoke();
                return;
            }

            _entries.Insert(0, new ClipboardEntry(plainText, richJson, DateTime.Now));

            if (_entries.Count > MaxEntries)
                _entries.RemoveAt(MaxEntries); // drop oldest

            Save();
            HistoryChanged?.Invoke();
        }

        public static void ClearAll()
        {
            if (_entries.Count == 0) return;
            _entries.Clear();
            Save();
            HistoryChanged?.Invoke();
        }

        public static void Remove(ClipboardEntry entry)
        {
            if (!_entries.Remove(entry)) return;
            Save();
            HistoryChanged?.Invoke();
        }

        public static void InsertAt(int index, ClipboardEntry entry)
        {
            index = Math.Clamp(index, 0, _entries.Count);
            _entries.Insert(index, entry);
            if (_entries.Count > MaxEntries) _entries.RemoveAt(MaxEntries);
            Save();
            HistoryChanged?.Invoke();
        }
    }
}
