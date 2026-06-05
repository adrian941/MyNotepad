# 10 · Reference

## Keyboard shortcuts

| Shortcut | Action | Handler |
|----------|--------|---------|
| Ctrl+B | Bold | `HandleCtrlShortcut` |
| Ctrl+I | Italic | |
| Ctrl+U | Underline | |
| Ctrl+F5 | Strikethrough | |
| Ctrl+1..5 | Text color (normal) | textColorMap[1..5] |
| Ctrl+Shift+1..5 | Text color (dark) | darkTextColorMap[1..5] |
| Ctrl+6..9,0 | Highlight (light) | highlightColorMap[6..9,0] |
| Ctrl+Shift+6..9,0 | Highlight (vivid) | strongHighlightMap[6..9,0] |
| Ctrl+F | Find | `FindReplaceWindow.ShowFor(replaceMode:false)` |
| Ctrl+R | Replace | `FindReplaceWindow.ShowFor(replaceMode:true)` |
| Ctrl+Shift+R | Rename saved file | `ShowRenameDialog` |
| Ctrl+S | Save | `SaveCurrentFile` |
| Ctrl+Shift+S | Save As | `ShowSaveFileDialog` |
| Ctrl+O | Files browser | `ClipboardHistoryWindow.ShowOrActivateFiles` |
| Ctrl+N | New window | `new NotepadWindow(...)` |
| Ctrl+H | Help | `HelpWindow.ShowOrActivate` |
| Ctrl+, or Ctrl+. | Wrap selection in code block | `ShowLanguagePicker` |
| Ctrl+Alt+V | Clipboard history | `ClipboardHistoryWindow.ShowOrActivateClipboard` |
| Ctrl+C | Copy (rich) | `RichClipboard.Copy` |
| Ctrl+X | Cut (rich) | |
| Ctrl+V | Paste (rich or plain) | `RichClipboard.Paste` |
| Ctrl+Mouse wheel | Zoom font | |
| Ctrl++ / Ctrl+- | Zoom font | |
| Alt+Up/Down | Move line(s) | `MoveLines` |
| Alt+U | Lowercase selection | |
| Alt+Shift+U | Uppercase selection | |
| F3 / Shift+F3 | Find next / prev | `FindReplaceWindow.FindNextStatic/FindPrevStatic` |
| Space | Insert non-breaking space (U+00A0) | `OnPreviewKeyDown` |

## Color system

6 TypeIds in `colors.json` (`Config/ConfigLoader.BuildDefaultColorConfig`):

| TypeId | Name | Keys | Trigger |
|--------|------|------|---------|
| 1 | Text colors (normal) | 1-5 | Ctrl+digit |
| 3 | Text colors (dark) | 1-5 | Ctrl+Shift+digit |
| 2 | Highlights (light pastel) | 6-9, 0 | Ctrl+digit |
| 4 | Highlights (vivid/strong) | 6-9, 0 | Ctrl+Shift+digit |
| 5 | Code block highlights (saturated) | 6-9, 0 | remap of typeId=4 inside code blocks |
| 6 | Code block strong highlights (darker) | 6-9, 0 | remap of typeId=2 inside code blocks |

Default color palette:

| Key | typeId=1 | typeId=3 | typeId=2 | typeId=4 |
|-----|----------|----------|----------|----------|
| 1 | #2E7D32 Green | #1B5E20 Dark Green | — | — |
| 2 | #FFFFFF White | #FFFFFF White | — | — |
| 3 | #D32F2F Red | #B71C1C Dark Red | — | — |
| 4 | #1565C0 Blue | #0D47A1 Dark Blue | — | — |
| 5 | #7B22AC Violet | #4A148C Dark Violet | — | — |
| 6 | — | — | #E8F5E9 Green pastel | #95C11F Strong Green |
| 7 | — | — | #BBDEFB Blue pastel | #90CAF9 Strong Blue |
| 8 | — | — | #FFF3E0 Orange pastel | #FFCC80 Strong Orange |
| 9 | — | — | #FFCDD2 Red pastel | #E30613 Strong Red |
| 0 | — | — | #F3E5F5 Violet pastel | #CE93D8 Strong Violet |

Remap for code blocks: typeId=2 hex → typeId=6 hex; typeId=4 hex → typeId=5 hex.

## On-disk file locations

| Path | Contents | Format |
|------|----------|--------|
| `<exe dir>\settings.json` | `AppSettings` | JSON |
| `<exe dir>\colors.json` | `ColorConfig` | JSON |
| `%APPDATA%\MinimalNotepad\Saved\*.mnp` | Saved documents | JSON: `{PlainText, RichData}` |
| `%APPDATA%\MinimalNotepad\clipboard_history.json` | App (rich) clipboard history | JSON array of `ClipboardEntryDto` |
| `%APPDATA%\MinimalNotepad\normal_clipboard_history.json` | System clipboard history | JSON array |
| `%APPDATA%\MinimalNotepad\open_request.txt` | Transient: path passed between instances | plain text, deleted on read |

## AppSettings fields

```csharp
double WindowLeft, WindowTop, WindowWidth, WindowHeight   // geometry
double FontSize            // default 12
bool   SaveGlobalClipboard // default false — runs Win32 clipboard daemon
bool   FindMatchCase       // default false
bool   FindWholeWord       // default false
```

## Code block VS Code Dark+ colors

| Token | Hex | Brush field |
|-------|-----|------------|
| Default text | #D4D4D4 | `DefaultText` |
| Fence lines | #CCCCCC | `FenceText` |
| Keywords | #569CD6 | `KeywordBrush` |
| Control flow | #C586C0 | `CtrlFlowBrush` |
| Strings | #CE9178 | `StringBrush` |
| Comments | #6A9955 | `CommentBrush` |
| Numbers | #B5CEA8 | `NumberBrush` |
| Types (PascalCase) | #4EC9B0 | `TypeBrush` |
| Methods (PascalCase+`(`) | #DCDCAA | `MethodBrush` |
| Preprocessor | #C586C0 | `PreprocBrush` |
| Regex | #D16D6D | `RegexBrush` |
| HTML/XML attributes | #9CDCFE | `AttrBrush` |
| HTML/XML tags | #569CD6 | `TagBrush` |
| Highlighted code text | #0F0F0F | `HighlightedCodeText` |
| Fold indicator (···) | #8599AA | `CodeBlockCollapseGenerator.MarkerBrush` |

## Important type/file name mismatches

| Type name | File name |
|-----------|-----------|
| `CodeBlockPaddingGenerator` | `CodeBlockPaddingTransformer.cs` |
| `FindReplaceWindow` | `FindReplaceBar.cs` |
| `MatchHighlightRenderer` | `FindReplaceBar.cs` (nested class) |
