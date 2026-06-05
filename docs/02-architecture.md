# 02 · Architecture

## Module map

```
Program.cs
  └─ single-instance guard, .mnp file association
  └─ creates NotepadWindow(s)

NotepadWindow.cs  [GOD OBJECT — 50 edges in knowledge graph]
  ├─ hosts TextEditor (AvalonEdit)
  ├─ owns FormattingManager          ← the span store
  ├─ owns FoldingManager             ← AvalonEdit fold manager (HeightTree)
  ├─ owns CodeBlockCollapseGenerator ← non-interactive ··· renderer (reads FoldingManager)
  ├─ fires ReparseCodeBlocks() on every text change (100ms debounce)
  └─ calls FindReplaceWindow.ShowFor / ClipboardHistoryWindow.ShowOrActivate / HelpWindow

FindReplaceBar.cs  [contains FindReplaceWindow + MatchHighlightRenderer + FormatPopup]
ClipboardHistoryWindow.cs
HelpWindow.cs

Config/
  AppSettings     window geometry, font size, find flags, daemon toggle
  ColorConfig     ColorEntry list — 6 typeIds × 5 keys
  ConfigLoader    load/save/BuildColorMaps → 6 Dict<int,string>

Formatting/
  FormattingManager   span store with anchors, toggle/query/snapshot API
  RichClipboard       dual-format copy/paste + .mnp serialization
  ClipboardHistory    app history (rich) persisted to JSON
  NormalClipboardHistory  system history (plain) persisted to JSON
  SavedFileStore      .mnp library under %APPDATA%
  ClipboardDaemon     hidden HWND for WM_CLIPBOARDUPDATE
  CodeBlockParser     scans document → List<CodeBlockRegion>
  [rendering stack — see 08-rendering-pipeline.md]
```

## Core data model

### TextFormatting (value object)
```
Bold | Italic | Underline | Strikethrough | ForeColorHex? | BackColorHex?
```
Stored in `FormattingSpan.Format`. ForeColor = text color; BackColor = highlighter.

### FormattingSpan
```
StartAnchor (ITextAnchor) + EndAnchor + TextFormatting
```
Anchors auto-track document edits. `IsDeleted` → true when both anchors are deleted.
`IsEmpty` → Start >= End after edits.

### CodeBlockRegion (record)
```
Language | LineNumbers | StartMinimized
FenceOpenLine | FenceCloseLine  (1-based line numbers)
ContentStart | ContentEnd       (document offsets)
```
Produced fresh by `CodeBlockParser.Parse` on every reparse.

### ColorEntry
```
KeyNumber (1-9, 0) | TypeId (1-6) | ColorHex | Name
```
typeId meanings: 1=text, 2=highlight, 3=dark-text, 4=strong-highlight, 5=code-highlight, 6=code-strong-highlight.

### ClipboardEntry / SavedFileEntry
Both carry `PlainText + RichJson?`. RichJson is the `MinimalNotepad.RichText.v1` JSON format
(array of `SpanDto` with relative offsets). See [06-clipboard.md](06-clipboard.md).

## How the main window wires everything

```
InitializeFormatting():
  1. FoldingManager.Install(textArea)         → adds FoldingElementGenerator + FoldingMargin
  2. remove FoldingMargin                     → no +/- button in left margin
  3. new CodeBlockCollapseGenerator(fm)       → insert at index 0 (wins tie vs FoldingElementGenerator)
  4. new FormattingManager(doc)
  5. new CodeSyntaxColorizer(...)
  6. add BackgroundRenderers:   CodeBlockBackgroundRenderer, CodeBlockLineNumberRenderer
  7. add ElementGenerators:     CodeBlockCollapseGenerator[0], CodeBlockLineNumberGenerator,
                                 CodeBlockPaddingGenerator, NonBreakingHyphenGenerator
  8. add LineTransformers:      RichTextColorizer, CodeSyntaxColorizer,
                                 CodeBlockFontSizeTransformer, SelectionForegroundOverride[LAST]

WireEvents():
  TextChanged         → debounce 100ms → ReparseCodeBlocks()
  ScrollOffsetChanged → copyOverlay.UpdatePositions()
  VisualLinesChanged  → copyOverlay.UpdatePositions()

ReparseCodeBlocks():
  1. CodeBlockParser.Parse → regions
  2. rebuild FoldingManager foldings for :min blocks
  3. UpdateBlocks on all renderers/transformers
  4. TextView.Redraw()
```

## Key invariants

- `FormattingManager._spans` is sorted by `Start` after every `Cleanup()`.
- Anchors survive deletion (`SurviveDeletion = true`); garbage collect via `Cleanup()`.
- `ReparseCodeBlocks` always runs on the UI thread (DispatcherTimer tick).
- No XAML. No user controls. All layout is imperative C#.
- The app is single-instance (named `Mutex`). A second launch signals via `EventWaitHandle`.
