using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MinimalNotepad.Config
{
    class ColorEntry
    {
        // keyNumber : 1-5 = text colors, 6-9 & 0 = highlights
        // typeId    : 1 = textColor, 2 = highlight
        // colorHex  : hex color string (e.g. "#2E7D32"), null = transparent/default
        // name      : display name shown in Quick Guide

        [JsonPropertyName("keyNumber")] public int     KeyNumber { get; set; }
        [JsonPropertyName("typeId")]    public int     TypeId    { get; set; }
        [JsonPropertyName("colorHex")]  public string? ColorHex  { get; set; }
        [JsonPropertyName("name")]      public string? Name      { get; set; }
    }

    class ColorConfig
    {
        [JsonPropertyName("_legend")]
        public string Legend { get; set; } =
            "keyNumber: 1-5 = textColor (typeId=1 normal / typeId=3 dark), " +
            "6-9 & 0 = highlight (typeId=2 light / typeId=4 strong) | " +
            "Ctrl+digit = normal, Ctrl+Shift+digit = intense | same color → reverts to default";

        [JsonPropertyName("colors")]
        public List<ColorEntry> Colors { get; set; } = new();
    }
}
