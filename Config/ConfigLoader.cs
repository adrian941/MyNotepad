using System;
using System.Collections.Generic;
using System.IO;
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
                    if (loaded?.Colors?.Count > 0) return loaded;
                }
            }
            catch { }

            File.WriteAllText(path, JsonSerializer.Serialize(defaults,
                new JsonSerializerOptions { WriteIndented = true }));
            return defaults;
        }

        public static (Dictionary<int, string> TextColors, Dictionary<int, string> Highlights)
            BuildColorMaps(ColorConfig config)
        {
            var textColors = new Dictionary<int, string>();
            var highlights = new Dictionary<int, string>();
            foreach (var entry in config.Colors)
            {
                if (entry.ColorHex == null) continue;
                if (entry.TypeId == 1) textColors[entry.KeyNumber] = entry.ColorHex;
                if (entry.TypeId == 2) highlights[entry.KeyNumber] = entry.ColorHex;
            }
            return (textColors, highlights);
        }

        static ColorConfig BuildDefaultColorConfig() => new()
        {
            Colors = new List<ColorEntry>
            {
                // ── Text colors (typeId = 1) ── VS Code Light style, easy on the eyes ──
                new() { KeyNumber = 1, TypeId = 1, ColorHex = "#2E7D32" }, // Green
                new() { KeyNumber = 2, TypeId = 1, ColorHex = "#C17A00" }, // Amber (dark, readable on white)
                new() { KeyNumber = 3, TypeId = 1, ColorHex = "#D32F2F" }, // Red
                new() { KeyNumber = 4, TypeId = 1, ColorHex = "#1565C0" }, // Blue (deep, not link-like)
                new() { KeyNumber = 5, TypeId = 1, ColorHex = "#7B22AC" }, // Violet

                // ── Highlights (typeId = 2) ── very light pastels, Material 50 palette ──
                new() { KeyNumber = 6, TypeId = 2, ColorHex = "#E8F5E9" }, // Green highlight
                new() { KeyNumber = 7, TypeId = 2, ColorHex = "#BBDEFB" }, // Blue highlight
                new() { KeyNumber = 8, TypeId = 2, ColorHex = "#FFF3E0" }, // Orange highlight
                new() { KeyNumber = 9, TypeId = 2, ColorHex = "#FFCDD2" }, // Red highlight
                new() { KeyNumber = 0, TypeId = 2, ColorHex = "#F3E5F5" }, // Violet highlight
            }
        };
    }
}
