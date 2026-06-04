# 07 · Find & Replace

Source: `FindReplaceBar.cs` — the entire file contains three nested classes:
`FindReplaceWindow` (the main window), `MatchHighlightRenderer` (AvalonEdit background
renderer for match highlights), and the format popup logic.

## FindReplaceWindow

Singleton pattern: `FindReplaceWindow.ShowFor(editor, owner, ...)` creates/reuses the
window. Static fields `_staticEditor`, `_staticFmtManager`, `_staticColorEntries` share
the current editor state across invocations without re-creating.

### Search

`RunSearch(jump: bool)` does:
1. Gets text + flags (match-case, whole-word, case-toggle state).
2. Collects `TextSpan[]` matches via regex or literal search.
3. Advances/stays at `_currentIndex` if `jump=true`; otherwise stays at current.
4. Updates `MatchHighlightRenderer` and calls `JumpToCurrent()`.

`jump=false` is used by: Aa/ab (case-toggle) buttons, case/word option toggles.
This prevents unwanted navigation when just toggling an option.

### Replace

`HandleReplace()` replaces current match, calls `RunSearch(jump:true)`.
`HandleReplaceAll()` replaces all matches in a single undo group.

## Match rendering (MatchHighlightRenderer)

`IBackgroundRenderer` on `KnownLayer.Background`.

All matches: orange fill `rgba(255, 165, 0, 0.59)`.

Current match (in addition):
- White solid border 1.8px.
- Black dashed border [3px dash, 3px gap] 1.8px — drawn using `DrawingContext.DrawGeometry`
  with a custom `DashStyle`.

Selection override for the current match:
- `editor.TextArea.SelectionBrush` = orange (alpha=150) — overrides default blue.
- `editor.TextArea.SelectionForeground` = Black.
- Restored to original values when the find bar closes.

`SelectionForegroundOverride` transformer (last in LineTransformers) then forces the
selection text black even when a syntax colorizer or highlight colorizer runs after.

## Format popup (bulk formatting of all matches)

Opened via the colored-bars button in the find bar toolbar.
Built by `BuildFormatPopup()`, stays open (no auto-close after click).

Contains:
- **STYLE** section: B/I/U/S as `ToggleButton`s; AA/aa as `Button`s.
- **TEXT COLOR** section: dots for typeId=1 (normal) and typeId=3 (dark).
- **HIGHLIGHT** section: squares for typeId=2 (light) and typeId=4 (strong).
- **Clear** button.

`ToggleButton` state is synced with `UpdateFormatToggles()` which calls
`_staticFmtManager.IsRangeBold/Italic/Underline/Strikethrough` across all matches.

**Bulk apply:**
`ApplyFormattingToAllMatches(action)`:
1. Snapshots formatting before.
2. Calls `action` on each match range.
3. Snapshots after.
4. Pushes one `FormattingUndoOperation` covering all matches.

**Text transform (AA/aa):**
`ApplyTextTransformToAllMatches(toUpper)`:
1. For each match, replaces the text with `.ToUpper()` or `.ToLower()`.
2. Calls `RunSearch(jump: false)` — rebuilds match positions without navigating.

## Static state management

The find bar uses static fields so it can be shown/hidden without losing state:
- `_staticFmtManager` — updated each time `ShowFor` is called with a new window.
- `_staticColorEntries` — for the color dot/square buttons.
- `FindReplaceWindow.IsOpen` — queried by `NotepadWindow.OnPreviewKeyDown` for F3/Shift+F3.
- `FindReplaceWindow.FindNextStatic()` / `FindPrevStatic()` — called by F3.
- `FindReplaceWindow.CloseIfTargeting(editor)` — called when the editor window closes.
