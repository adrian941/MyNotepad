# 06 · Clipboard & History

Source: `Formatting/RichClipboard.cs`, `Formatting/ClipboardHistory.cs`,
`Formatting/NormalClipboardHistory.cs`, `ClipboardDaemon.cs`,
`GlobalClipboardMonitor.cs`, `Formatting/SavedFileStore.cs`

## The two clipboard histories

| History | Class | What it captures | Storage |
|---------|-------|-----------------|---------|
| App history | `ClipboardHistory` | MinimalNotepad copies only, with rich JSON | `%APPDATA%\MinimalNotepad\clipboard_history.json` |
| Normal history | `NormalClipboardHistory` | System clipboard text (from any app, if daemon running) | `%APPDATA%\MinimalNotepad\normal_clipboard_history.json` |

Both cap at 2000 entries. Oldest entry is dropped when full.
Both de-duplicate: same plain text → update timestamp if richJson changed, else skip.

## RichClipboard format

Custom clipboard format name: `"MinimalNotepad.RichText.v1"`.
Payload: JSON `{ "Text": "...", "Spans": [...] }` where spans have relative offsets
(0 = first char of the copied text).

**Paste priority:** custom format first → plain text fallback (from any app).

API:
```csharp
// Sets both plain text + rich format on clipboard, returns richJson string
RichClipboard.Copy(selectedText, allSpans, selectionStart) → string

// Reads clipboard: (text, spans?) — spans null if from external app
RichClipboard.Paste() → (string? text, List<SpanRecord>? spans)

// For file save (no clipboard involved)
RichClipboard.SerializeDocument(text, spans) → string
RichClipboard.DeserializeSpans(richJson) → List<SpanRecord>?
RichClipboard.TrimRich(text, richJson) → (trimmedText, trimmedJson)
```

## Code-block clipboard format

Name: `"application/x-mynotepad-codeblock"`.
Payload: the full block text with ` ``` ` markers. Set by `Copy` button on a code block
and by `Ctrl+C` when selection is entirely inside a code block.
On paste, this format is tried first — pasting a code block preserves its ``` fences.

## The Win32 clipboard daemon

`ClipboardDaemon` owns a hidden `DaemonWindow` — a WPF window with opacity=0, no chrome,
no taskbar entry, positioned at (-32000, -32000). Its HWND is registered with
`AddClipboardFormatListener`. When `WM_CLIPBOARDUPDATE` fires:
- If the clipboard does NOT contain the app's rich format (i.e., came from outside) AND
  does contain text → push to `NormalClipboardHistory`.

Started by `ClipboardDaemon.Start()`, stopped by `Stop()`.
`GlobalClipboardMonitor.IsEnabled` controls whether it runs (persisted in `AppSettings.SaveGlobalClipboard`).
The daemon survives after all editor windows are closed if `IsEnabled = true`;
`TryShutdownIfIdle()` only shuts the app if both windows=0 AND daemon is not running.

## Saved files (.mnp)

Format: same `RichClipboard` JSON, wrapped in:
```json
{ "PlainText": "...", "RichData": { "Text": "...", "Spans": [...] } }
```

`SavedFileStore.Save(name, text, richJson)` → `%APPDATA%\MinimalNotepad\Saved\<name>.mnp`
`SavedFileStore.SaveToPath(fullPath, text, richJson)` → arbitrary path (in-place save for external files)

A `FileSystemWatcher` on the `Saved` folder fires `SavedFilesChanged` whenever any `.mnp`
file is created/deleted/changed — `ClipboardHistoryWindow` subscribes to refresh its list.

`SavedFileEntry` record: `FileName | PlainText | RichJson? | LastModified`.
`FileName` is the name without `.mnp` extension. No full path — use `SavedFileStore.GetFilePath(name)` to get path.
External files track their path in `NotepadWindow._externalPath` (not in `SavedFileEntry`).

## ClipboardHistoryWindow

Two tabs: **Clipboard** (app/normal history) and **Files** (saved files library).
Uses Win32 `SetForegroundWindow` + `SendInput` to paste back into the previously active
window without requiring focus. Opened via `Ctrl+Alt+V`.
