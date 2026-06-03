using System.Collections.Generic;
using ICSharpCode.AvalonEdit.Document;

namespace MinimalNotepad.Formatting
{
    record CodeBlockRegion(
        string Language,
        bool   LineNumbers,
        int FenceOpenLine,   // 1-based
        int FenceCloseLine,  // 1-based
        int ContentStart,    // doc offset of first char after opening fence line
        int ContentEnd       // doc offset of first char of closing fence line
    );

    static class CodeBlockParser
    {
        public static List<CodeBlockRegion> Parse(TextDocument doc)
        {
            var result = new List<CodeBlockRegion>();
            int lineCount = doc.LineCount;

            string? openLang     = null;
            bool    openLineNums = false;
            int     openLine     = 0;
            int     contentStart = 0;

            for (int ln = 1; ln <= lineCount; ln++)
            {
                var    line = doc.GetLineByNumber(ln);
                string text = doc.GetText(line.Offset, line.Length);

                if (openLang == null)
                {
                    if (text.Length > 3 && text.StartsWith("```"))
                    {
                        string tag  = text.Substring(3).Trim();
                        int    ci   = tag.IndexOf(':');
                        string lang = ci >= 0 ? tag.Substring(0, ci) : tag;
                        bool   lnOn = ci >= 0 && tag.Substring(ci + 1) == "ln";
                        if (lang.Length > 0 && IsValidLangTag(lang))
                        {
                            openLang     = lang;
                            openLineNums = lnOn;
                            openLine     = ln;
                            contentStart = line.Offset + line.Length + line.DelimiterLength;
                        }
                    }
                }
                else
                {
                    if (text.Trim() == "```")
                    {
                        result.Add(new CodeBlockRegion(
                            openLang,
                            openLineNums,
                            openLine,
                            ln,
                            contentStart,
                            line.Offset));
                        openLang = null;
                    }
                }
            }

            return result;
        }

        static bool IsValidLangTag(string s)
        {
            foreach (char c in s)
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '+' && c != '#' && c != '-')
                    return false;
            return true;
        }
    }
}
