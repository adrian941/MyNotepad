namespace MinimalNotepad.Config
{
    class AppSettings
    {
        public double WindowLeft          { get; set; } = 100;
        public double WindowTop           { get; set; } = 100;
        public double WindowWidth         { get; set; } = 800;
        public double WindowHeight        { get; set; } = 600;
        public double FontSize               { get; set; } = 12;
        public bool   SaveGlobalClipboard   { get; set; } = false;
        public bool   FindMatchCase         { get; set; } = false;
        public bool   FindWholeWord         { get; set; } = false;
    }
}
