# 03 · Runtime Flows

Key paths through the app. Each flow names the exact methods involved.

## 1. Startup

```
Program.Main(args)
  → mutex check (single-instance guard)
  → ConfigLoader.LoadSettings + LoadOrCreateColorConfig
  → ClipboardHistory.Load + NormalClipboardHistory.Load
  → new NotepadWindow(settings, colors)
       → InitializeWindow, InitializeEditor, InitializeFormatting, WireEvents
  → if args[0] exists → SavedFileStore.LoadFromPath → NotepadWindow.LoadExternalFile
  → app.Run()
```

A second instance launch: writes path to `open_request.txt`, signals `EventWaitHandle`,
exits. The listener thread in the first instance picks it up →
`OpenOrFocusExternalFile` (external file) or `OpenOrFocusSavedFile` (library file).

## 2. Keystroke → formatting applied

```
PreviewKeyDown → HandleCtrlShortcut
  → ApplyFormatting(action)
       1. snapshot = _fmtManager.TakeSnapshot()  (before)
       2. action(start, end)                      (mutates spans)
       3. after = _fmtManager.TakeSnapshot()
       4. UndoStack.Push(new FormattingUndoOperation(before, after))
       5. TextView.Redraw()
```

**Keyboard map for formatting:**

| Keys | Action |
|------|--------|
| Ctrl+B | ToggleBold |
| Ctrl+I | ToggleItalic |
| Ctrl+U | ToggleUnderline |
| Ctrl+F5 | ToggleStrikethrough |
| Ctrl+1..5 | ToggleForeColor (textColorMap) |
| Ctrl+Shift+1..5 | ToggleForeColor (darkTextColorMap) |
| Ctrl+6..9,0 | ToggleBackColor (highlightColorMap) |
| Ctrl+Shift+6..9,0 | ToggleBackColor (strongHighlightMap) |
| Alt+Up/Down | MoveLines |
| Alt+U | lowercase selection |
| Alt+Shift+U | uppercase selection |
| Ctrl+F | Find |
| Ctrl+R | Replace (Shift: rename file) |
| Ctrl+S | Save (Shift: Save As) |
| Ctrl+O | Open Files browser |
| Ctrl+N | New window |
| Ctrl+H | Help |
| Ctrl+,/. | Wrap selection in code block (shows lang picker) |
| Ctrl+Alt+V | Clipboard history |
| F3/Shift+F3 | Find next/prev (when FindBar open) |
| Space | Inserts non-breaking space (U+00A0) |

## 3. Typing (sticky formatting)

```
TextArea.TextEntered → OnTextEntered(offset, text)
  → ApplyStickyFormatting(insertStart, insertedLen)
       → _fmtManager.GetInlineFormattingBefore(insertStart)
            (reads format of the char immediately to the left)
       → _fmtManager.ApplyInlineFormatting(start, end, style)
```

BackColorHex (highlighter) is **not** sticky — only Bold/Italic/Under/Strike/ForeColor.

## 4. Copy / Paste

**Copy (Ctrl+C):**
```
SpansForCopy(selStart, selLen)
  → if selection inside one code block → empty spans (plain text only)
  → else → _fmtManager.TakeSnapshot()
RichClipboard.Copy(text, spans, selStart)
  → SetDataObject({plain text, "MinimalNotepad.RichText.v1": richJson})
ClipboardHistory.Push(text, richJson)
```

**Paste (Ctrl+V):**
```
TryGetCodeBlockFormatFromClipboard()     → if "application/x-mynotepad-codeblock" present
  → PasteContent(codeBlockText, null)
else
  → RichClipboard.Paste()               → reads rich format or falls back to plain text
  → PasteContent(text, spans?)
```

## 5. Save / Load

**Save (Ctrl+S):**
```
SaveCurrentFile(name)
  if _externalPath != null → SavedFileStore.SaveToPath(_externalPath, text, richJson)
  else                     → SavedFileStore.Save(name, text, richJson)
                              → writes %APPDATA%\MinimalNotepad\Saved\<name>.mnp
```

**Load from library (ClipboardHistoryWindow → Files tab):**
```
OpenOrFocusSavedFile(entry, callerWindow)
  → if already open → Activate
  → else NotepadWindow.LoadSavedFile(entry)
       → _externalPath = null (saves by name)
       → LoadEntryContent(entry)
```

**Load external file (file association / args[0]):**
```
OpenOrFocusExternalFile(fullPath, entry, callerWindow)
  → if already open (matched by _externalPath) → Activate
  → else NotepadWindow.LoadExternalFile(fullPath, entry)
       → _externalPath = fullPath (if not inside Saved folder)
       → LoadEntryContent(entry)
```

## 6. Text change → code block reparse

```
OnTextChanged → _reParseTimer.Stop(); _reParseTimer.Start()   (100ms debounce)
  → ReparseCodeBlocks()
       1. CodeBlockParser.Parse(doc) → regions
       2. remove old FoldingManager foldings
       3. for each :min block (contentLines > 20):
            CreateFolding(line21.Offset, lastContentLine.EndOffset)
            section.IsFolded = true
       4. UpdateBlocks, UpdateRegions on all renderers
       5. TextView.Redraw()
```

## 7. Find & Replace

```
Ctrl+F → FindReplaceWindow.ShowFor(editor, window, replaceMode:false, ...)
  → RunSearch() → collects TextSpan[] matches
  → MatchHighlightRenderer paints all matches orange (KnownLayer.Background)
  → current match: white 1.8px border + black dashed [3,3] 1.8px border
  → JumpToCurrent() → editor.Select(start, len)

Format popup (the colored-bars button):
  → ApplyFormattingToAllMatches(action) → foreach match: _fmtManager.Toggle*(start,end)
     all under one compound undo entry
```
