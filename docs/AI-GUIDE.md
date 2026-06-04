# AI Agent Guide — Read This First

This guide tells an AI agent how to work in this repo efficiently.
Read it once; after that, read only the targeted subsystem page you need.

## When to read which doc

| Task | Read |
|------|------|
| Adding/changing keyboard shortcuts | [03-flows.md §Keypress](03-flows.md), [10-reference.md](10-reference.md) |
| Anything touching formatting spans | [04-rich-text-formatting.md](04-rich-text-formatting.md) |
| Anything touching code blocks | **[05-code-blocks.md](05-code-blocks.md) + [08-rendering-pipeline.md](08-rendering-pipeline.md) + [09-learnings.md](09-learnings.md)** — read all three before touching |
| Clipboard / history / saved files | [06-clipboard.md](06-clipboard.md) |
| Find & Replace | [07-find-replace.md](07-find-replace.md) |
| AvalonEdit transformer ordering | [08-rendering-pipeline.md](08-rendering-pipeline.md) |
| Past bugs / crash patterns | [09-learnings.md](09-learnings.md) — check before modifying rendering code |
| Color palette / shortcuts | [10-reference.md](10-reference.md) |

## Hard rules (non-negotiable)

1. **Never remove `FoldingElementGenerator` from `ElementGenerators`.**
   It must stay installed for HeightTree line-collapse to work.
   Details: [09-learnings.md §Folding](09-learnings.md).

2. **`SelectionForegroundOverride` must remain the LAST entry in `LineTransformers`.**
   It re-applies `SelectionForeground` after all other transformers.

3. **`FormattedTextElement(FormattedText, int)` is broken in AvalonEdit 6.3.1.**
   Use the `(TextLine, int)` overload. Build the `TextLine` via
   `FormattedTextElement.PrepareText(TextFormatter.Create(), text, props)`.
   Details: [09-learnings.md §FormattedTextElement](09-learnings.md).

4. **Formatter undo: always push a `FormattingUndoOperation` after mutating spans.**
   `ApplyFormatting(action)` in `NotepadWindow` handles this for normal edits.

5. **No XAML.** All UI is built imperatively in C#. Don't add `.xaml` files.

6. **Single-instance mutex.** A second process launch signals the first via a named
   `EventWaitHandle` — don't start a second `Application`.

## How to verify a code change without UI

Build test: `dotnet build` (0 CS errors = code is correct).

Crash test for code-block minimize (`:min`): craft a `.mnp` JSON file and launch
`MyNotepad.exe <path>` — if it stays alive for 5+ seconds, the fold path is safe.
```powershell
$lines = @('```javascript:min') + (1..30 | ForEach-Object { "const v$_ = $_;" }) + @('```')
$dto = [ordered]@{ PlainText = ($lines -join "`n"); RichData = $null } | ConvertTo-Json
Set-Content "$env:TEMP\test.mnp" $dto
Start-Process "bin\Debug\net10.0-windows\MyNotepad.exe" "`"$env:TEMP\test.mnp`""
Start-Sleep 5
Get-Process MyNotepad   # must still be running
```

## Source of truth files (by subsystem)

| Subsystem | Authoritative files |
|-----------|---------------------|
| Formatting engine | `Formatting/FormattingManager.cs`, `Formatting/FormattingSpan.cs`, `Formatting/TextFormatting.cs` |
| Code block parsing | `Formatting/CodeBlockParser.cs` |
| Code block rendering | `Formatting/CodeSyntaxColorizer.cs` + the six `CodeBlock*.cs` files |
| Code block minimize | `Formatting/CodeBlockCollapseGenerator.cs`, `NotepadWindow.ReparseCodeBlocks` |
| Clipboard | `Formatting/RichClipboard.cs`, `Formatting/ClipboardHistory.cs`, `ClipboardDaemon.cs` |
| Find & Replace | `FindReplaceBar.cs` (entire file — FindReplaceWindow + MatchHighlightRenderer inside) |
| Colors | `Config/ConfigLoader.cs BuildDefaultColorConfig()`, `<exe>\colors.json` |
| Saved files | `Formatting/SavedFileStore.cs`, `NotepadWindow.SaveCurrentFile/LoadSavedFile/LoadExternalFile` |
