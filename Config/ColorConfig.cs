using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MinimalNotepad.Config
{
    class ColorEntry
    {
        // keyNumber : 1-5 = text colors, 6-9 & 0 = highlights
        // typeId    : 1 = textColor, 2 = highlight
        // colorHex  : hex color string (e.g. "#2E7D32"), null = transparent/default

        [JsonPropertyName("keyNumber")] public int     KeyNumber { get; set; }
        [JsonPropertyName("typeId")]    public int     TypeId    { get; set; }
        [JsonPropertyName("colorHex")]  public string? ColorHex  { get; set; }
    }

    class ColorConfig
    {
        [JsonPropertyName("_legend")]
        public string Legend { get; set; } =
            "keyNumber: 1=Green 2=Yellow 3=Red 4=Blue 5=Violet (textColor, typeId=1) | " +
            "6=Green 7=Blue 8=Orange 9=Red 0=Violet (highlight, typeId=2) | " +
            "same color → reverts to black/transparent";

        [JsonPropertyName("colors")]
        public List<ColorEntry> Colors { get; set; } = new();
    }
}
