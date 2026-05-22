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
                        FillMissingNames(loaded, defaults);
                        return loaded;
                    }
                }
            }
            catch { }

            File.WriteAllText(path, JsonSerializer.Serialize(defaults,
                new JsonSerializerOptions { WriteIndented = true }));
            return defaults;
        }

        // Fills Name on any entry that was loaded without it (e.g. older colors.json)
        static void FillMissingNames(ColorConfig config, ColorConfig defaults)
        {
            var defaultMap = defaults.Colors.ToDictionary(e => (e.KeyNumber, e.TypeId));
            foreach (var entry in config.Colors)
            {
                if (string.IsNullOrEmpty(entry.Name) &&
                    defaultMap.TryGetValue((entry.KeyNumber, entry.TypeId), out var def))
                    entry.Name = def.Name;
            }
        }

        // Derives the fast-lookup hex dictionaries from any list of ColorEntry.
        public static (Dictionary<int, string> TextColors, Dictionary<int, string> Highlights)
            BuildColorMaps(IEnumerable<ColorEntry> entries)
        {
            var textColors = new Dictionary<int, string>();
            var highlights = new Dictionary<int, string>();
            foreach (var entry in entries)
            {
                if (entry.ColorHex == null) continue;
                if (entry.TypeId == 1) textColors[entry.KeyNumber] = entry.ColorHex;
                if (entry.TypeId == 2) highlights[entry.KeyNumber] = entry.ColorHex;
            }
            return (textColors, highlights);
        }

        // Overload kept for callers that still hold a ColorConfig.
        public static (Dictionary<int, string> TextColors, Dictionary<int, string> Highlights)
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

                // ── Highlights (typeId = 2) ── very light pastels, Material 50 palette ──
                new() { KeyNumber = 6, TypeId = 2, ColorHex = "#E8F5E9", Name = "Green"  },
                new() { KeyNumber = 7, TypeId = 2, ColorHex = "#BBDEFB", Name = "Blue"   },
                new() { KeyNumber = 8, TypeId = 2, ColorHex = "#FFF3E0", Name = "Orange" },
                new() { KeyNumber = 9, TypeId = 2, ColorHex = "#FFCDD2", Name = "Red"    },
                new() { KeyNumber = 0, TypeId = 2, ColorHex = "#F3E5F5", Name = "Violet" },
            }
        };
    }
}
