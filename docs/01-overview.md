# 01 · Overview

## What MyNotepad is

A minimal-but-rich single-instance notepad for Windows. Beyond plain text it offers:

- **Inline rich-text formatting** — bold / italic / underline / strikethrough, 5 text
  colors and 5 highlighters (each with a normal and an intense variant).
- **Fenced code blocks** (` ```csharp `) rendered as dark VS-Code-style panels with
  syntax highlighting, optional line numbers (`:ln`) and visual minimize (`:min`).
- **A clipboard with memory** — every copy is kept in an app history; optionally a
  background daemon records the *system* clipboard too.
- **Find & Replace** that can also apply formatting to **all** matches at once.
- **Saved files** (`.mnp`) that persist text + formatting as JSON, with a Files browser.

## Tech stack

| Thing | Choice |
|-------|--------|
| Runtime | .NET 10, `net10.0-windows`, `WinExe` |
| UI | WPF (`UseWPF`), code-only (no XAML files — windows are built in C#) |
| Editor control | [AvalonEdit](https://github.com/icsharpcode/AvalonEdit) 6.3.1.120 |
| Serialization | `System.Text.Json` |
| Nullable | enabled. `ImplicitUsings` enabled. |

There is **no XAML**. Every window and control is constructed imperatively in C#
(`new DockPanel()`, templates built with `FrameworkElementFactory`, etc.). When you look
for UI, look in the `*.cs` window files, not in `.xaml`.

## Project layout

```
MyNotepad/
├─ Program.cs                  App entry, single-instance guard, .mnp file association
├─ NotepadWindow.cs            The main editor window (god object — ~1300 lines)
├─ FindReplaceBar.cs           FindReplaceWindow + match renderer + bulk-format popup
├─ ClipboardHistoryWindow.cs   Clipboard/Files browser popup
├─ HelpWindow.cs               Ctrl+H quick-guide window
├─ ClipboardDaemon.cs          Hidden HWND listening for WM_CLIPBOARDUPDATE
├─ GlobalClipboardMonitor.cs   Enable/disable toggle + event for global capture
├─ AssemblyInfo.cs
├─ Config/
│  ├─ AppSettings.cs           Window geometry, font size, find flags, daemon toggle
│  ├─ ColorConfig.cs           ColorEntry / ColorConfig DTOs (colors.json schema)
│  └─ ConfigLoader.cs          Load/save settings + colors, build fast color maps
├─ Formatting/
│  ├─ TextFormatting.cs        The style value object (bold/italic/.../fore/back hex)
│  ├─ FormattingSpan.cs        An anchored [start,end) region carrying a TextFormatting
│  ├─ FormattingManager.cs     The formatting engine (spans, toggles, snapshots)
│  ├─ FormattingUndoOperation.cs  Wires formatting snapshots into AvalonEdit's undo stack
│  ├─ RichTextColorizer.cs     Paints user formatting onto normal (non-code) text
│  ├─ CodeBlockParser.cs       Finds ```fenced``` regions → CodeBlockRegion list
│  ├─ CodeSyntaxColorizer.cs   Syntax-colors code-block content (VS Code Dark+ palette)
│  ├─ CodeBlockBackgroundRenderer.cs   Dark rounded panel behind blocks
│  ├─ CodeBlockLineNumberRenderer.cs   Background for the line-number gutter
│  ├─ CodeBlockLineNumberGenerator.cs  Inserts line-number elements (`:ln`)
│  ├─ CodeBlockPaddingTransformer.cs   (a.k.a. padding generator) left padding inside blocks
│  ├─ CodeBlockFontSizeTransformer.cs  Slightly different font sizing in blocks
│  ├─ CodeBlockCollapseGenerator.cs    Non-interactive "···" for minimized (:min) blocks
│  ├─ CodeBlockCopyOverlay.cs  The #/▾/Copy/Delete buttons floating over each block
│  ├─ NonBreakingHyphenGenerator.cs    Renders ‑ without line-breaking
│  ├─ SelectionForegroundOverride.cs   Forces selection text color (runs last)
│  ├─ RichClipboard.cs         Dual-format copy/paste + document (de)serialization
│  ├─ ClipboardHistory.cs      App clipboard history (rich) + persistence
│  ├─ NormalClipboardHistory.cs  System clipboard history (plain) + persistence
│  └─ SavedFileStore.cs        .mnp save/load, folder watching
├─ docs/                       ← this wiki
└─ graphify-out/               machine-generated knowledge graph (regenerate via /graphify)
```

> **Naming caveat:** the file `CodeBlockPaddingTransformer.cs` contains the type
> `CodeBlockPaddingGenerator`. Search by *type name* when in doubt.

## Build & run

```powershell
dotnet build                 # Debug
dotnet build -c Release
dotnet run                   # launches the single-instance app
```

The app registers itself (HKCU, no admin) as the handler for `.mnp` files, so
`MyNotepad.exe path\to\file.mnp` opens that file. See [03-flows.md](03-flows.md).

## On-disk state (per-user)

| Path | Contents |
|------|----------|
| `%APPDATA%\MinimalNotepad\Saved\*.mnp` | Saved documents (JSON: text + rich spans) |
| `%APPDATA%\MinimalNotepad\clipboard_history.json` | App (rich) clipboard history |
| `%APPDATA%\MinimalNotepad\open_request.txt` | Transient: file hand-off to running instance |
| `<exe dir>\settings.json` | Window geometry, font size, find flags, daemon toggle |
| `<exe dir>\colors.json` | The color palette (see [10-reference.md](10-reference.md)) |
