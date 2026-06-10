using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MinimalNotepad.Config;

namespace MinimalNotepad.Formatting
{
    /// <summary>
    /// Persists the "folder chips" shown above the Files view and which one is active.
    /// Chips are relative subfolder paths (e.g. "Recall", "Recall/Details") under the
    /// Saved library root (<see cref="SavedFileStore.SavedFolder"/>). An active chip filters
    /// the card list to the .mnp files inside that subfolder; no active chip = base folder.
    /// </summary>
    static class FolderChipsStore
    {
        public static string Root => SavedFileStore.SavedFolder;

        static readonly string StatePath = Path.Combine(AppDataPath.Root, "folder_chips.json");

        sealed class State
        {
            public List<string> Chips  { get; set; } = new();
            public string?      Active { get; set; }
        }

        static readonly State _state = Load();

        public static IReadOnlyList<string> Chips => _state.Chips;

        public static string? Active
        {
            get => _state.Active;
            set
            {
                _state.Active = string.IsNullOrEmpty(value) ? null : Normalize(value);
                Save();
            }
        }

        // ── Load / save ──────────────────────────────────────────────────────
        static State Load()
        {
            try
            {
                if (File.Exists(StatePath))
                {
                    var s = JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath));
                    if (s != null)
                    {
                        // Drop chips whose folder no longer exists on disk.
                        s.Chips = s.Chips.Select(Normalize)
                                         .Where(c => c.Length > 0 && FolderExists(c))
                                         .Distinct(StringComparer.OrdinalIgnoreCase)
                                         .ToList();
                        if (s.Active != null &&
                            !s.Chips.Contains(s.Active, StringComparer.OrdinalIgnoreCase))
                            s.Active = null;
                        return s;
                    }
                }
            }
            catch { }
            return new State();
        }

        static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
                File.WriteAllText(StatePath,
                    JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        // ── Chip mutations ───────────────────────────────────────────────────
        public static void AddChip(string rel)
        {
            rel = Normalize(rel);
            if (rel.Length == 0) return;
            if (!_state.Chips.Contains(rel, StringComparer.OrdinalIgnoreCase))
            {
                _state.Chips.Add(rel);
                Save();
            }
        }

        public static void RemoveChip(string rel)
        {
            rel = Normalize(rel);
            _state.Chips.RemoveAll(c => string.Equals(c, rel, StringComparison.OrdinalIgnoreCase));
            if (string.Equals(_state.Active, rel, StringComparison.OrdinalIgnoreCase))
                _state.Active = null;
            Save();
        }

        // ── Filesystem helpers ───────────────────────────────────────────────
        static string Normalize(string rel) =>
            (rel ?? "").Replace('\\', '/').Trim().Trim('/');

        public static string FullPath(string rel) =>
            Path.Combine(Root, Normalize(rel).Replace('/', Path.DirectorySeparatorChar));

        public static bool FolderExists(string rel)
        {
            try { return Directory.Exists(FullPath(rel)); } catch { return false; }
        }

        /// <summary>All subfolders under Root as relative '/'-separated paths, sorted.</summary>
        public static List<string> EnumerateSubfolders()
        {
            var result = new List<string>();
            try
            {
                string root = Path.GetFullPath(Root);
                foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
                    result.Add(Path.GetRelativePath(root, dir).Replace('\\', '/'));
            }
            catch { }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        /// <summary>Loads .mnp entries from a subfolder; returns (entry, full path) newest first.</summary>
        public static List<(SavedFileEntry Entry, string FullPath)> LoadFolder(string rel)
        {
            var list = new List<(SavedFileEntry, string)>();
            try
            {
                string dir = FullPath(rel);
                foreach (var path in Directory.GetFiles(dir, "*.mnp"))
                {
                    var e = SavedFileStore.LoadFromPath(path);
                    if (e != null) list.Add((e, path));
                }
            }
            catch { }
            return list.OrderByDescending(t => t.Item1.LastModified).ToList();
        }
    }
}
