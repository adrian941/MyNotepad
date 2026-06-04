# 04 · Rich-Text Formatting Engine

Source: `Formatting/FormattingManager.cs`, `FormattingSpan.cs`, `TextFormatting.cs`,
`RichTextColorizer.cs`, `FormattingUndoOperation.cs`

## Data model

```
TextFormatting          ← value object, one per span
  Bold | Italic | Underline | Strikethrough  (bool)
  ForeColorHex?  (null = default black)
  BackColorHex?  (null = no highlight)
  IsDefault → all false + both nulls
  Clone(), SameAs()

FormattingSpan
  StartAnchor (ITextAnchor, AfterInsertion)
  EndAnchor   (ITextAnchor, BeforeInsertion)
  Format      (TextFormatting)
  IsDeleted   → either anchor deleted
  Start/End   → anchor offsets (move with document edits)

FormattingManager
  _spans: List<FormattingSpan>   (always sorted by Start after Cleanup)
  _doc: TextDocument
```

## Span lifecycle

### Creating a span
`ModifyRange(start, end, modify)` is the only mutator:
1. `SplitAt(start)` + `SplitAt(end)` — splits any existing span that crosses a boundary.
2. `CoveragePoints(start, end)` — all unique offsets in [start,end] from existing span edges.
3. For each sub-segment: find or create a `FormattingSpan`, call `modify(format)`.
4. `Cleanup()` — remove empty/default spans, sort, merge adjacent identical spans.

### Why anchors, not offsets
Anchors (`ITextAnchor`) live in AvalonEdit's `TextDocument` and automatically shift
when text is inserted/deleted before them. No manual offset management needed.
`SurviveDeletion = true` keeps them alive even if their character is deleted —
`IsDeleted` becomes true only when the anchor itself is explicitly deleted.

## Public API

```csharp
// Toggles: off if ALL characters in range have the property, else on
ToggleBold/Italic/Underline/Strikethrough(start, end)
ToggleForeColor(start, end, targetHex)    // null → remove color
ToggleBackColor(start, end, targetHex)    // null → remove highlight

// Queries (for Find & Replace format popup ToggleButton state)
IsRangeBold/Italic/Underline/Strikethrough(start, end) → bool

// Sticky typing: inherit style of char to the left (BackColor excluded)
GetInlineFormattingBefore(offset) → TextFormatting?
ApplyInlineFormatting(start, end, style)   // does NOT toggle, just sets

// Undo/redo
TakeSnapshot() → List<SpanRecord>
RestoreSnapshot(snapshot, textView)

// Paste
ApplyRelativeSpans(pasteOffset, relativeSpans)

// Find & Replace: clear everything
ClearFormatting(start, end)
```

## Undo

`FormattingUndoOperation` wraps a before/after snapshot pair.
Pushed onto `_editor.Document.UndoStack` via `NotepadWindow.ApplyFormatting(action)`.
AvalonEdit's undo merges it with the immediately preceding text-edit operation when
both happen in the same undo group — giving clean "undo both text and format" behaviour.

## Rendering (RichTextColorizer)

`RichTextColorizer` is a `DocumentColorizingTransformer` added first in `LineTransformers`.
For each line it:
1. Collects all span boundaries crossing the line.
2. For each sub-segment, ORs Bold/Italic/Under/Strike, picks last ForeColor and BackColor.
3. Calls `ChangeLinePart(segStart, segEnd, el => ...)` to set typeface / brushes /
   text decorations on `el.TextRunProperties`.

**Key ordering rule:** `RichTextColorizer` runs BEFORE `CodeSyntaxColorizer`.
Inside code blocks, `CodeSyntaxColorizer` overrides ForeColor (syntax has priority).
BackColor (highlights) survive because `CodeSyntaxColorizer` only sets BackColor via
its own step 5 (`ApplyCodeBlockHighlights`) — it does not blank it in step 1.

## BackColor in code blocks (highlight remap)

Normal pastel highlights (typeId=2) look wrong on the dark code block background.
`CodeSyntaxColorizer` remaps them to dark-mode variants at construction time:
```
typeId=2 hex → typeId=6 (code-strong-highlight) hex
typeId=4 hex → typeId=5 (code-highlight) hex
```
Remapped color + `HighlightedCodeText` (#0F0F0F) foreground applied in step 5.
**ForeColor (text color) is intentionally ignored inside code blocks** — syntax coloring
takes priority.

## Sticky formatting edge case

When the user types at the very START of a span, `AfterInsertion` anchors mean the new
characters fall OUTSIDE the span (before the span's start). `GetInlineFormattingBefore`
looks one char to the LEFT to find the applicable style — so sticky formatting works as
long as the caret is within or just after a styled region.
