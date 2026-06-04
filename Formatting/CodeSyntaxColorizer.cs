using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;

namespace MinimalNotepad.Formatting
{
    class CodeSyntaxColorizer : DocumentColorizingTransformer
    {
        private List<BlockHighlighter> _blocks = new();

        private readonly FormattingManager _fmtManager;
        // Maps normal highlight hex → code-block-friendly hex (dark-mode variants)
        private readonly Dictionary<string, string> _highlightRemap;
        // Text color applied over any active highlight inside a code block
        static readonly SolidColorBrush HighlightedCodeText = Freeze(new SolidColorBrush(Color.FromRgb(0x0F, 0x0F, 0x0F)));

        // ── VS Code Dark+ palette ─────────────────────────────────────────────
        static readonly SolidColorBrush DefaultText   = Freeze(new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4)));
        static readonly SolidColorBrush FenceText     = Freeze(new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)));
        static readonly SolidColorBrush KeywordBrush  = Freeze(new SolidColorBrush(Color.FromRgb(0x56, 0x9C, 0xD6)));
        static readonly SolidColorBrush CtrlFlowBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xC5, 0x86, 0xC0)));
        static readonly SolidColorBrush StringBrush   = Freeze(new SolidColorBrush(Color.FromRgb(0xCE, 0x91, 0x78)));
        static readonly SolidColorBrush CommentBrush  = Freeze(new SolidColorBrush(Color.FromRgb(0x6A, 0x99, 0x55)));
        static readonly SolidColorBrush NumberBrush   = Freeze(new SolidColorBrush(Color.FromRgb(0xB5, 0xCE, 0xA8)));
        static readonly SolidColorBrush TypeBrush     = Freeze(new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0xB0)));
        static readonly SolidColorBrush MethodBrush   = Freeze(new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xAA)));
        static readonly SolidColorBrush PreprocBrush  = Freeze(new SolidColorBrush(Color.FromRgb(0xC5, 0x86, 0xC0)));
        static readonly SolidColorBrush RegexBrush    = Freeze(new SolidColorBrush(Color.FromRgb(0xD1, 0x6D, 0x6D)));
        static readonly SolidColorBrush AttrBrush     = Freeze(new SolidColorBrush(Color.FromRgb(0x9C, 0xDC, 0xFE)));
        static readonly SolidColorBrush TagBrush      = Freeze(new SolidColorBrush(Color.FromRgb(0x56, 0x9C, 0xD6)));

        static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

        // PascalCase identifier — needs at least one lowercase to exclude ALL_CAPS/HTTP/SQL
        static readonly Regex IdentifierRegex = new(
            @"\b[A-Z][a-zA-Z0-9]*[a-z][a-zA-Z0-9]*\b",
            RegexOptions.Compiled);

        // After-context: modifier or "new" keyword immediately before identifier
        static readonly Regex TypePositionRegex = new(
            @"\b(static|public|private|protected|internal|override|virtual|abstract|sealed|async|partial|readonly|const|new|return|throw)\s*$",
            RegexOptions.Compiled);

        // Control-flow keywords for C-family languages — coloured purple in VS Code.
        // Applied AFTER token colors so this overrides the highlighter's blue.
        static readonly Regex ControlFlowRegex = new(
            @"\b(if|else|for|foreach|while|do|switch|case|default|break|continue|return|goto|throw|try|catch|finally|lock|yield|in)\b",
            RegexOptions.Compiled);

        // ─────────────────────────────────────────────────────────────────────

        public CodeSyntaxColorizer(
            FormattingManager fmtManager,
            IReadOnlyDictionary<int, string> highlights,
            IReadOnlyDictionary<int, string> strongHighlights,
            IReadOnlyDictionary<int, string> codeHighlights,
            IReadOnlyDictionary<int, string> codeStrongHighlights)
        {
            _fmtManager     = fmtManager;
            _highlightRemap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // Build remap (inverted): typeId=2 hex → typeId=6 hex, and typeId=4 hex → typeId=5 hex
            foreach (var kv in highlights)
                if (codeStrongHighlights.TryGetValue(kv.Key, out var codeHex))
                    _highlightRemap[kv.Value] = codeHex;
            foreach (var kv in strongHighlights)
                if (codeHighlights.TryGetValue(kv.Key, out var codeHex))
                    _highlightRemap[kv.Value] = codeHex;
        }

        static SolidColorBrush BrushFor(string hex)
        {
            var color = (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            var b = new SolidColorBrush(color);
            b.Freeze();
            return b;
        }

        sealed class BlockHighlighter
        {
            public CodeBlockRegion     Region;
            public TextDocument        SubDoc;
            public DocumentHighlighter? Highlighter;
            public bool                 IsCFamily;

            public BlockHighlighter(CodeBlockRegion r, TextDocument d, DocumentHighlighter? h, bool cFamily)
            { Region = r; SubDoc = d; Highlighter = h; IsCFamily = cFamily; }
        }

        // Languages where the C#-style heuristics (PascalCase type/method + purple
        // control-flow) make sense. SQL, HTML, CSS, Python, etc. are excluded.
        static bool IsCFamilyLanguage(string lang) =>
            lang.ToLowerInvariant() switch
            {
                "csharp" or "cs" or "c#"        => true,
                "java"                           => true,
                "javascript" or "js"             => true,
                "typescript" or "ts"             => true,
                "cpp" or "c++" or "c"            => true,
                "kotlin" or "scala" or "swift"   => true,
                "go" or "rust"                   => true,
                _                                => false
            };

        public IReadOnlyList<CodeBlockRegion> CurrentBlocks
        {
            get
            {
                var list = new List<CodeBlockRegion>(_blocks.Count);
                foreach (var b in _blocks) list.Add(b.Region);
                return list;
            }
        }

        public void UpdateBlocks(TextDocument mainDoc, List<CodeBlockRegion> regions)
        {
            _blocks.Clear();
            foreach (var region in regions)
            {
                var def  = ResolveDefinition(region.Language);
                int len  = Math.Max(0, region.ContentEnd - region.ContentStart);
                string text = len > 0 ? mainDoc.GetText(region.ContentStart, len) : "";

                var subDoc = new TextDocument(text);
                var hl     = def != null ? new DocumentHighlighter(subDoc, def) : null;
                _blocks.Add(new BlockHighlighter(region, subDoc, hl, IsCFamilyLanguage(region.Language)));
            }
        }

        protected override void ColorizeLine(DocumentLine line)
        {
            int ln = line.LineNumber;

            foreach (var block in _blocks)
            {
                var region = block.Region;

                // ── Fence lines ───────────────────────────────────────────────
                if (ln == region.FenceOpenLine || ln == region.FenceCloseLine)
                {
                    if (line.Length > 0)
                        ChangeLinePart(line.Offset, line.EndOffset, el =>
                        {
                            el.TextRunProperties.SetForegroundBrush(FenceText);
                            el.TextRunProperties.SetBackgroundBrush(null);
                        });
                    return;
                }

                int contentStart = region.FenceOpenLine + 1;
                int contentEnd   = region.FenceCloseLine - 1;
                if (ln < contentStart || ln > contentEnd) continue;

                // ── Step 1: default foreground (user highlights preserved) ───
                if (line.Length > 0)
                    ChangeLinePart(line.Offset, line.EndOffset, el =>
                    {
                        el.TextRunProperties.SetForegroundBrush(DefaultText);
                    });

                if (block.Highlighter == null) return;

                int subLineNum = ln - region.FenceOpenLine;
                if (subLineNum < 1 || subLineNum > block.SubDoc.LineCount) return;

                var subLine     = block.SubDoc.GetLineByNumber(subLineNum);
                int offsetDelta = line.Offset - subLine.Offset;
                string lineText = subLine.Length > 0
                    ? block.SubDoc.GetText(subLine.Offset, subLine.Length)
                    : "";

                // ── Step 2: PascalCase coloring — ONLY for C-family languages ─
                if (block.IsCFamily && lineText.Length > 0)
                    ApplyIdentifierColoring(line, lineText);

                // ── Step 3: AvalonEdit token sections ──────────────────────────
                HighlightedLine highlighted;
                try { highlighted = block.Highlighter.HighlightLine(subLineNum); }
                catch { return; }

                foreach (var section in highlighted.Sections)
                {
                    if (section.Length == 0) continue;
                    var brush = DarkBrushFor(section.Color?.Name);

                    int segStart = section.Offset + offsetDelta;
                    int segEnd   = segStart + section.Length;
                    if (segStart < line.Offset || segEnd > line.EndOffset) continue;

                    ChangeLinePart(segStart, segEnd, el =>
                        el.TextRunProperties.SetForegroundBrush(brush));
                }

                // ── Step 4: control-flow keywords → purple (C-family only) ────
                if (block.IsCFamily && lineText.Length > 0)
                {
                    foreach (Match m in ControlFlowRegex.Matches(lineText))
                    {
                        int s = line.Offset + m.Index;
                        int e = s + m.Length;
                        if (e > line.EndOffset) continue;
                        ChangeLinePart(s, e, el =>
                            el.TextRunProperties.SetForegroundBrush(CtrlFlowBrush));
                    }
                }

                // ── Step 5: remap user highlights for dark-mode code block ─────
                ApplyCodeBlockHighlights(line);
                return;
            }
        }

        // ── Remap user highlights to dark-mode-friendly colors inside code blocks ─

        void ApplyCodeBlockHighlights(DocumentLine line)
        {
            var spans = _fmtManager.Spans;
            if (spans.Count == 0) return;

            // Collect segment boundaries within this line from spans that have BackColor
            var pts = new SortedSet<int> { line.Offset, line.EndOffset };
            foreach (var span in spans)
            {
                if (span.IsDeleted || span.IsEmpty || span.Format.BackColorHex == null) continue;
                int s = span.Start, e = span.End;
                if (s < line.EndOffset && e > line.Offset)
                {
                    if (s > line.Offset)    pts.Add(s);
                    if (e < line.EndOffset) pts.Add(e);
                }
            }

            var points = new List<int>(pts);
            for (int i = 0; i < points.Count - 1; i++)
            {
                int segStart = points[i], segEnd = points[i + 1];
                string? backHex = null;
                foreach (var span in spans)
                {
                    if (span.IsDeleted || span.IsEmpty || span.Format.BackColorHex == null) continue;
                    if (span.Start <= segStart && span.End >= segEnd)
                    {
                        backHex = span.Format.BackColorHex;
                        break;
                    }
                }
                if (backHex == null) continue;

                string codeBackHex = _highlightRemap.TryGetValue(backHex, out var mapped) ? mapped : backHex;
                var codeBg = BrushFor(codeBackHex);

                ChangeLinePart(segStart, segEnd, el =>
                {
                    el.TextRunProperties.SetBackgroundBrush(codeBg);
                    el.TextRunProperties.SetForegroundBrush(HighlightedCodeText);
                });
            }
        }

        // ── Context-aware identifier coloring ─────────────────────────────────

        void ApplyIdentifierColoring(DocumentLine line, string lineText)
        {
            foreach (Match m in IdentifierRegex.Matches(lineText))
            {
                int idStart = m.Index;
                int idEnd   = idStart + m.Length;

                string before      = lineText.Substring(0, idStart);
                string after       = idEnd < lineText.Length ? lineText.Substring(idEnd) : "";
                string afterTrim   = after.TrimStart();

                // Skip property/variable assignments: `Identifier =` (but not `==`)
                if (afterTrim.Length > 0 && afterTrim[0] == '='
                    && (afterTrim.Length < 2 || afterTrim[1] != '='))
                    continue;

                SolidColorBrush? brush = null;

                // Method call: `Identifier(`
                if (afterTrim.Length > 0 && afterTrim[0] == '(')
                    brush = MethodBrush;
                // Generic type: `Identifier<`
                else if (afterTrim.Length > 0 && afterTrim[0] == '<')
                    brush = TypeBrush;
                // Static member access: `Identifier.Member` (member starts uppercase)
                else if (afterTrim.Length >= 2 && afterTrim[0] == '.' && char.IsUpper(afterTrim[1]))
                    brush = TypeBrush;
                // Type after modifier/new/return/throw/: keyword
                else if (TypePositionRegex.IsMatch(before)
                         || EndsWithCharThenSpace(before, ':')
                         || EndsWithCharThenSpace(before, '('))
                    brush = TypeBrush;

                if (brush == null) continue;

                int s = line.Offset + idStart;
                int e = s + m.Length;
                if (e > line.EndOffset) continue;

                ChangeLinePart(s, e, el =>
                    el.TextRunProperties.SetForegroundBrush(brush));
            }
        }

        static bool EndsWithCharThenSpace(string before, char ch)
        {
            int i = before.Length - 1;
            while (i >= 0 && (before[i] == ' ' || before[i] == '\t')) i--;
            return i >= 0 && before[i] == ch;
        }

        // ── Named color → dark theme brush ────────────────────────────────────

        static SolidColorBrush DarkBrushFor(string? name)
        {
            if (name == null) return DefaultText;
            string n = name.ToLowerInvariant();

            if (Has(n, "comment", "doc", "javadoc", "xmldoc"))     return CommentBrush;
            if (Has(n, "string", "verbatim", "interpolated"))      return StringBrush;
            if (Has(n, "char", "character"))                       return StringBrush;
            if (Has(n, "regex"))                                   return RegexBrush;
            if (Has(n, "number", "digit"))                         return NumberBrush;
            if (Has(n, "preprocessor", "directive", "pragma"))     return PreprocBrush;

            if (Has(n, "method", "call", "function", "invoke"))    return MethodBrush;

            if (Has(n, "attribute", "htmlattr", "xmlattr"))        return AttrBrush;
            if (Has(n, "tag", "htmltag", "xmltag", "element"))     return TagBrush;

            if (Has(n, "keyword", "statement", "modifier",
                       "visibility", "namespace", "access",
                       "operator", "checked", "unsafe", "query",
                       "semantic", "truefalse", "null", "value",
                       "import", "context", "parametermodifier",
                       "gotokey", "exception"))
                return KeywordBrush;

            return DefaultText;
        }

        static bool Has(string s, params string[] needles)
        {
            foreach (var n in needles)
                if (s.Contains(n)) return true;
            return false;
        }

        // ── Language name → AvalonEdit definition ─────────────────────────────

        static IHighlightingDefinition? ResolveDefinition(string lang)
        {
            string n = lang.ToLowerInvariant() switch
            {
                "csharp" or "cs" or "c#"         => "C#",
                "sql" or "mssql" or "tsql"        => "TSQL",
                "html"                             => "HTML",
                "js" or "javascript"               => "JavaScript",
                "css"                              => "CSS",
                "json"                             => "JavaScript",
                "xml" or "xaml"                    => "XML",
                "ts" or "typescript"               => "TypeScript",
                "py" or "python"                   => "Python",
                "cpp" or "c++"                     => "C++",
                "c"                                => "C",
                "vb" or "vbnet"                    => "VB",
                "php"                              => "PHP",
                "java"                             => "Java",
                "ps" or "powershell"               => "PowerShell",
                "fs" or "fsharp"                   => "F#",
                "patch" or "diff"                  => "Patch",
                _                                  => lang
            };

            return HighlightingManager.Instance.GetDefinition(n);
        }
    }
}
