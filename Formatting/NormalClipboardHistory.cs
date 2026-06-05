using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MinimalNotepad.Formatting
{
    static class NormalClipboardHistory
    {
        const int MaxEntries = 2000;

        public static readonly string SavePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MinimalNotepad", "clipboard_history_normal.json");

        static readonly List<ClipboardEntry> _entries = new();

        public static IReadOnlyList<ClipboardEntry> Entries => _entries;

        /// <summary>Fired each time the list changes.</summary>
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
                    _entries.Add(new ClipboardEntry(dto.PlainText, null, dto.CopiedAt));
            }
            catch { }
        }

        static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SavePath)!);
                var dtos = _entries
                    .Select(e => new ClipboardEntryDto { PlainText = e.PlainText, CopiedAt = e.CopiedAt })
                    .ToList();
                File.WriteAllText(SavePath,
                    JsonSerializer.Serialize(dtos, new JsonSerializerOptions { WriteIndented = false }));
            }
            catch { }
        }

        // ── Mutation ──────────────────────────────────────────────────────────

        public static void Push(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return;
            if (_entries.Count > 0 && _entries[0].PlainText == plainText) return;

            _entries.Insert(0, new ClipboardEntry(plainText, null, DateTime.Now));

            if (_entries.Count > MaxEntries)
                _entries.RemoveAt(MaxEntries);

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
