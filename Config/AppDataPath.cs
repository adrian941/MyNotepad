using System;
using System.IO;

namespace MinimalNotepad.Config
{
    /// <summary>
    /// Single source of truth for the app's data root directory.
    /// By default this is %AppData%\MinimalNotepad.  The user can relocate it via
    /// ChangeDataFolderDialog; the new path is stored in a small pointer file that
    /// always lives at the DEFAULT location so the app can find it after any move.
    /// </summary>
    static class AppDataPath
    {
        static readonly string _defaultRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MinimalNotepad");

        // Pointer file always stays in the default root.
        internal static readonly string PointerFile = Path.Combine(_defaultRoot, ".datapath");

        /// <summary>Effective data root for this session (read once at startup).</summary>
        public static readonly string Root;

        public static string DefaultRoot => _defaultRoot;

        static AppDataPath()
        {
            Root = ResolveRoot();
        }

        static string ResolveRoot()
        {
            try
            {
                if (File.Exists(PointerFile))
                {
                    var p = File.ReadAllText(PointerFile).Trim();
                    if (!string.IsNullOrEmpty(p) && Directory.Exists(p))
                        return p;
                }
            }
            catch { }
            return _defaultRoot;
        }

        /// <summary>Writes (or removes) the pointer file so the NEXT startup uses <paramref name="newPath"/>.</summary>
        public static void SetRoot(string newPath)
        {
            Directory.CreateDirectory(_defaultRoot);
            if (string.Equals(Path.GetFullPath(newPath), Path.GetFullPath(_defaultRoot),
                    StringComparison.OrdinalIgnoreCase))
                try { File.Delete(PointerFile); } catch { }
            else
                File.WriteAllText(PointerFile, newPath);
        }
    }
}
