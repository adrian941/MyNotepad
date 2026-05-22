namespace MinimalNotepad.Formatting
{
    class TextFormatting
    {
        public bool    Bold          { get; set; }
        public bool    Italic        { get; set; }
        public bool    Underline     { get; set; }
        public bool    Strikethrough { get; set; }
        public string? ForeColorHex  { get; set; }  // null = default (black)
        public string? BackColorHex  { get; set; }  // null = no highlight

        public bool IsDefault =>
            !Bold && !Italic && !Underline && !Strikethrough
            && ForeColorHex == null && BackColorHex == null;

        public TextFormatting Clone() => new()
        {
            Bold          = Bold,
            Italic        = Italic,
            Underline     = Underline,
            Strikethrough = Strikethrough,
            ForeColorHex  = ForeColorHex,
            BackColorHex  = BackColorHex
        };

        public bool SameAs(TextFormatting o) =>
            Bold == o.Bold && Italic == o.Italic && Underline == o.Underline
            && Strikethrough == o.Strikethrough
            && ForeColorHex == o.ForeColorHex && BackColorHex == o.BackColorHex;
    }
}
