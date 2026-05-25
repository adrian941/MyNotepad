using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MinimalNotepad.Config
{
    static class ConfigLoader
    {
        public static AppSettings LoadSettings(string path)
        {
            try
            {
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
            }
            catch { }
            return new AppSettings();
        }

        public static void SaveSettings(AppSettings settings, string path)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(settings,
                new JsonSerializerOptions { WriteIndented = true }));
        }

        public static ColorConfig LoadOrCreateColorConfig(string path)
        {
            var defaults = BuildDefaultColorConfig();

            try
            {
                if (File.Exists(path))
                {
                    var loaded = JsonSerializer.Deserialize<ColorConfig>(File.ReadAllText(path));
                    if (loaded?.Colors?.Count > 0)
                    {
                        FillMissingEntries(loaded, defaults);
                        // Re-save so colors.json stays up to date with any new entries added
                        File.WriteAllText(path, JsonSerializer.Serialize(loaded,
                            new JsonSerializerOptions { WriteIndented = true }));
                        return loaded;
                    }
                }
            }
            catch { }

            File.WriteAllText(path, JsonSerializer.Serialize(defaults,
                new JsonSerializerOptions { WriteIndented = true }));
            return defaults;
        }

        // Adds any entries present in defaults but missing from loaded config (e.g. new typeIds added in updates).
        // Also fills missing Name fields.
        static void FillMissingEntries(ColorConfig config, ColorConfig defaults)
        {
            var existing = config.Colors.ToHashSet(ColorEntryKeyComparer.Instance);
            foreach (var def in defaults.Colors)
            {
                if (!existing.Contains(def))
                    config.Colors.Add(def);
                else
                {
                    // Fill missing Name if needed
                    var match = config.Colors.First(e => e.KeyNumber == def.KeyNumber && e.TypeId == def.TypeId);
                    if (string.IsNullOrEmpty(match.Name)) match.Name = def.Name;
                }
            }
        }

        private sealed class ColorEntryKeyComparer : IEqualityComparer<ColorEntry>
        {
            public static readonly ColorEntryKeyComparer Instance = new();
            public bool Equals(ColorEntry? x, ColorEntry? y) =>
                x != null && y != null && x.KeyNumber == y.KeyNumber && x.TypeId == y.TypeId;
            public int GetHashCode(ColorEntry e) => HashCode.Combine(e.KeyNumber, e.TypeId);
        }

        // Derives the fast-lookup hex dictionaries from any list of ColorEntry.
        public static (Dictionary<int, string> TextColors, Dictionary<int, string> Highlights,
                        Dictionary<int, string> DarkTextColors, Dictionary<int, string> StrongHighlights)
            BuildColorMaps(IEnumerable<ColorEntry> entries)
        {
            var textColors       = new Dictionary<int, string>();
            var highlights       = new Dictionary<int, string>();
            var darkTextColors   = new Dictionary<int, string>();
            var strongHighlights = new Dictionary<int, string>();
            foreach (var entry in entries)
            {
                if (entry.ColorHex == null) continue;
                if (entry.TypeId == 1) textColors[entry.KeyNumber]       = entry.ColorHex;
                if (entry.TypeId == 2) highlights[entry.KeyNumber]       = entry.ColorHex;
                if (entry.TypeId == 3) darkTextColors[entry.KeyNumber]   = entry.ColorHex;
                if (entry.TypeId == 4) strongHighlights[entry.KeyNumber] = entry.ColorHex;
            }
            return (textColors, highlights, darkTextColors, strongHighlights);
        }

        // Overload kept for callers that still hold a ColorConfig.
        public static (Dictionary<int, string> TextColors, Dictionary<int, string> Highlights,
                        Dictionary<int, string> DarkTextColors, Dictionary<int, string> StrongHighlights)
            BuildColorMaps(ColorConfig config) => BuildColorMaps(config.Colors);

        static ColorConfig BuildDefaultColorConfig() => new()
        {
            Colors = new List<ColorEntry>
            {
                // ── Text colors (typeId = 1) ── VS Code Light style, easy on the eyes ──
                new() { KeyNumber = 1, TypeId = 1, ColorHex = "#2E7D32", Name = "Green"  },
                new() { KeyNumber = 2, TypeId = 1, ColorHex = "#FFFFFF", Name = "White"  },
                new() { KeyNumber = 3, TypeId = 1, ColorHex = "#D32F2F", Name = "Red"    },
                new() { KeyNumber = 4, TypeId = 1, ColorHex = "#1565C0", Name = "Blue"   },
                new() { KeyNumber = 5, TypeId = 1, ColorHex = "#7B22AC", Name = "Violet" },

                // ── Dark text colors (typeId = 3) ── Ctrl+Shift+1…5, deeper shades ──
                new() { KeyNumber = 1, TypeId = 3, ColorHex = "#1B5E20", Name = "Dark Green"  },
                new() { KeyNumber = 2, TypeId = 3, ColorHex = "#FFFFFF", Name = "White"       }, // white stays white
                new() { KeyNumber = 3, TypeId = 3, ColorHex = "#B71C1C", Name = "Dark Red"    },
                new() { KeyNumber = 4, TypeId = 3, ColorHex = "#0D47A1", Name = "Dark Blue"   },
                new() { KeyNumber = 5, TypeId = 3, ColorHex = "#4A148C", Name = "Dark Violet" },

                // ── Highlights (typeId = 2) ── very light pastels, Material 50 palette ──
                new() { KeyNumber = 6, TypeId = 2, ColorHex = "#E8F5E9", Name = "Green"  },
                new() { KeyNumber = 7, TypeId = 2, ColorHex = "#BBDEFB", Name = "Blue"   },
                new() { KeyNumber = 8, TypeId = 2, ColorHex = "#FFF3E0", Name = "Orange" },
                new() { KeyNumber = 9, TypeId = 2, ColorHex = "#FFCDD2", Name = "Red"    },
                new() { KeyNumber = 0, TypeId = 2, ColorHex = "#F3E5F5", Name = "Violet" },

                // ── Strong highlights (typeId = 4) ── Ctrl+Shift+6…9,0, vivid colors ──
                new() { KeyNumber = 6, TypeId = 4, ColorHex = "#95C11F", Name = "Strong Green"  },
                new() { KeyNumber = 7, TypeId = 4, ColorHex = "#90CAF9", Name = "Strong Blue"   },
                new() { KeyNumber = 8, TypeId = 4, ColorHex = "#FFCC80", Name = "Strong Orange" },
                new() { KeyNumber = 9, TypeId = 4, ColorHex = "#E30613", Name = "Strong Red"    },
                new() { KeyNumber = 0, TypeId = 4, ColorHex = "#CE93D8", Name = "Strong Violet" },
            }
        };
    }
}
