# Graph Report - C:/Developer/repos-utils/MyNotepad  (2026-06-03)

## Corpus Check
- Corpus is ~22,108 words - fits in a single context window. You may not need a graph.

## Summary
- 357 nodes · 646 edges · 18 communities detected
- Extraction: 86% EXTRACTED · 14% INFERRED · 0% AMBIGUOUS · INFERRED: 91 edges (avg confidence: 0.8)
- Token cost: 0 input · 0 output

## Community Hubs (Navigation)
- [[_COMMUNITY_Configuration & Settings|Configuration & Settings]]
- [[_COMMUNITY_Clipboard History UI|Clipboard History UI]]
- [[_COMMUNITY_Find & Replace|Find & Replace]]
- [[_COMMUNITY_Text Formatting Engine|Text Formatting Engine]]
- [[_COMMUNITY_App Entry & Block Lifecycle|App Entry & Block Lifecycle]]
- [[_COMMUNITY_Clipboard & Code Actions|Clipboard & Code Actions]]
- [[_COMMUNITY_Rich Text Rendering|Rich Text Rendering]]
- [[_COMMUNITY_Visual Line Generators|Visual Line Generators]]
- [[_COMMUNITY_Help Window|Help Window]]
- [[_COMMUNITY_Clipboard Data Model|Clipboard Data Model]]
- [[_COMMUNITY_File Save & Store|File Save & Store]]
- [[_COMMUNITY_Config Loader & Colors|Config Loader & Colors]]
- [[_COMMUNITY_Code Block Background|Code Block Background]]
- [[_COMMUNITY_Clipboard Win32 Daemon|Clipboard Win32 Daemon]]
- [[_COMMUNITY_Color Config Entries|Color Config Entries]]
- [[_COMMUNITY_Global Clipboard Monitor|Global Clipboard Monitor]]
- [[_COMMUNITY_Formatting Spans|Formatting Spans]]
- [[_COMMUNITY_Font Size Transformer|Font Size Transformer]]

## God Nodes (most connected - your core abstractions)
1. `NotepadWindow` - 50 edges
2. `ClipboardHistoryWindow` - 35 edges
3. `FindReplaceWindow` - 34 edges
4. `FormattingManager` - 21 edges
5. `HelpWindow` - 16 edges
6. `SavedFileStore` - 13 edges
7. `CodeBlockCopyOverlay` - 11 edges
8. `CodeSyntaxColorizer` - 11 edges
9. `MatchHighlightRenderer` - 8 edges
10. `Program` - 8 edges

## Surprising Connections (you probably didn't know these)
- `Problema.png - VB/SQL Code Screenshot` --conceptually_related_to--> `CodeSyntaxColorizer`  [INFERRED]
  Problema.png → Formatting/CodeSyntaxColorizer.cs
- `AppSettings` --shares_data_with--> `FindReplaceWindow`  [INFERRED]
  C:\Developer\repos-utils\MyNotepad\Config\AppSettings.cs → C:\Developer\repos-utils\MyNotepad\FindReplaceBar.cs
- `NotepadWindow` --calls--> `FindReplaceWindow`  [EXTRACTED]
  C:\Developer\repos-utils\MyNotepad\NotepadWindow.cs → C:\Developer\repos-utils\MyNotepad\FindReplaceBar.cs
- `MatchHighlightRenderer` --calls--> `TextSpan (ISegment)`  [EXTRACTED]
  C:\Developer\repos-utils\MyNotepad\FindReplaceBar.cs → FindReplaceBar.cs
- `NotepadWindow` --calls--> `HelpWindow`  [EXTRACTED]
  C:\Developer\repos-utils\MyNotepad\NotepadWindow.cs → C:\Developer\repos-utils\MyNotepad\HelpWindow.cs

## Hyperedges (group relationships)
- **Code Block Rendering Pipeline** — formatting_codeblockparser, formatting_codeblockregion, formatting_codeblockbackgroundrenderer, formatting_codesyntaxcolorizer, formatting_codeblocklinenumbergenerator, formatting_codeblockpaddinggenerator, formatting_codeblockcopyoverlay [INFERRED 0.95]
- **Find & Replace Feature Components** — findreplacebar_findreplacewindow, findreplacebar_matchhighlightrenderer, findreplacebar_textspan [EXTRACTED 0.95]

## Communities

### Community 0 - "Configuration & Settings"
Cohesion: 0.08
Nodes (13): AppSettings, MinimalNotepad.Config, CodeBlockBackgroundRenderer, CodeBlockCopyOverlay, CodeBlockLineNumberGenerator, CodeBlockLineNumberRenderer, CodeBlockPaddingGenerator, CodeBlockParser (+5 more)

### Community 1 - "Clipboard History UI"
Cohesion: 0.1
Nodes (1): ClipboardHistoryWindow

### Community 2 - "Find & Replace"
Cohesion: 0.12
Nodes (4): FindReplaceWindow, MatchHighlightRenderer, MinimalNotepad, TextSpan (ISegment)

### Community 3 - "Text Formatting Engine"
Cohesion: 0.11
Nodes (7): FormattingManager, MinimalNotepad.Formatting, FormattingUndoOperation, MinimalNotepad.Formatting, IUndoableOperation, MinimalNotepad.Formatting, TextFormatting

### Community 4 - "App Entry & Block Lifecycle"
Cohesion: 0.13
Nodes (4): CodeBlockParser, MinimalNotepad.Formatting, MinimalNotepad, Program

### Community 5 - "Clipboard & Code Actions"
Cohesion: 0.14
Nodes (4): CodeBlockCopyOverlay, MinimalNotepad.Formatting, MinimalNotepad.Formatting, NormalClipboardHistory

### Community 6 - "Rich Text Rendering"
Cohesion: 0.11
Nodes (8): CodeBlockFontSizeTransformer, MinimalNotepad.Formatting, BlockHighlighter, CodeSyntaxColorizer, MinimalNotepad.Formatting, DocumentColorizingTransformer, MinimalNotepad.Formatting, RichTextColorizer

### Community 7 - "Visual Line Generators"
Cohesion: 0.11
Nodes (7): CodeBlockLineNumberGenerator, MinimalNotepad.Formatting, CodeBlockPaddingGenerator, MinimalNotepad.Formatting, MinimalNotepad.Formatting, NonBreakingHyphenGenerator, VisualLineElementGenerator

### Community 8 - "Help Window"
Cohesion: 0.24
Nodes (2): HelpWindow, MinimalNotepad

### Community 9 - "Clipboard Data Model"
Cohesion: 0.16
Nodes (5): ClipboardEntryDto, ClipboardHistory, MinimalNotepad.Formatting, MinimalNotepad.Formatting, RichClipboard

### Community 10 - "File Save & Store"
Cohesion: 0.22
Nodes (3): MinimalNotepad.Formatting, SavedFileDto, SavedFileStore

### Community 11 - "Config Loader & Colors"
Cohesion: 0.16
Nodes (4): ColorEntryKeyComparer, ConfigLoader, MinimalNotepad.Config, IEqualityComparer

### Community 12 - "Code Block Background"
Cohesion: 0.18
Nodes (5): CodeBlockBackgroundRenderer, MinimalNotepad.Formatting, CodeBlockLineNumberRenderer, MinimalNotepad.Formatting, IBackgroundRenderer

### Community 13 - "Clipboard Win32 Daemon"
Cohesion: 0.29
Nodes (4): ClipboardDaemon, DaemonWindow, MinimalNotepad, Window

### Community 14 - "Color Config Entries"
Cohesion: 0.6
Nodes (3): ColorConfig, ColorEntry, MinimalNotepad.Config

### Community 15 - "Global Clipboard Monitor"
Cohesion: 0.67
Nodes (2): GlobalClipboardMonitor, MinimalNotepad

### Community 16 - "Formatting Spans"
Cohesion: 0.67
Nodes (2): FormattingSpan, MinimalNotepad.Formatting

### Community 25 - "Font Size Transformer"
Cohesion: 1.0
Nodes (1): CodeBlockFontSizeTransformer

## Knowledge Gaps
- **16 isolated node(s):** `MinimalNotepad`, `MinimalNotepad`, `MinimalNotepad`, `MinimalNotepad.Config`, `MinimalNotepad.Formatting` (+11 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **Thin community `Clipboard History UI`** (38 nodes): `ClipboardHistoryWindow.cs`, `ClipboardHistoryWindow`, `.AttachFollowMouseBubble()`, `.BuildCard()`, `.BuildFormattedBlock()`, `.BuildSavedFileCard()`, `.CaptureExternalHwnd()`, `.GetWindowThreadProcessId()`, `.HexBrush()`, `.LoadWindowState()`, `.MakeDot()`, `.MakeFade()`, `.MakeFooterLink()`, `.MakeTabButton()`, `.OnForegroundWindowChanged()`, `.OnHistoryChanged()`, `.OpenSavedFile()`, `.PasteEntry()`, `.PositionPopup()`, `.RefreshCards()`, `.SaveWindowState()`, `.SendInput()`, `.SetForegroundWindow()`, `.SetTabActive()`, `.SetWinEventHook()`, `.ShowEmptyMessage()`, `.ShowOrActivate()`, `.ShowOrActivateClipboard()`, `.ShowOrActivateFiles()`, `.SwitchToAppMode()`, `.SwitchToFilesMode()`, `.ToScreenDiu()`, `.UnhookWinEvent()`, `.UpdateActiveTab()`, `ClipboardHistoryWindow.cs`, `.ApplyThinScrollBarStyle()`, `.InitializeWindow()`, `.DeserializeSpans()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Help Window`** (17 nodes): `HelpWindow.cs`, `HelpWindow`, `.Badge()`, `.Brush()`, `.BuildContent()`, `.Dot()`, `.FindWindow()`, `.Label()`, `.Note()`, `.Row()`, `.Section()`, `.SetForegroundWindow()`, `.ShowOrActivate()`, `.ShowWindow()`, `.Swatch()`, `MinimalNotepad`, `.ShowHelpWindow()`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Global Clipboard Monitor`** (4 nodes): `GlobalClipboardMonitor.cs`, `GlobalClipboardMonitor.cs`, `GlobalClipboardMonitor`, `MinimalNotepad`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Formatting Spans`** (4 nodes): `FormattingSpan.cs`, `FormattingSpan.cs`, `FormattingSpan`, `MinimalNotepad.Formatting`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Font Size Transformer`** (1 nodes): `CodeBlockFontSizeTransformer`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `NotepadWindow` connect `Configuration & Settings` to `Clipboard History UI`, `Find & Replace`, `Text Formatting Engine`, `App Entry & Block Lifecycle`, `Clipboard & Code Actions`, `Help Window`, `File Save & Store`, `Config Loader & Colors`, `Clipboard Win32 Daemon`?**
  _High betweenness centrality (0.419) - this node is a cross-community bridge._
- **Why does `FindReplaceWindow` connect `Find & Replace` to `Configuration & Settings`, `Config Loader & Colors`, `Clipboard Win32 Daemon`?**
  _High betweenness centrality (0.208) - this node is a cross-community bridge._
- **Why does `ClipboardHistoryWindow` connect `Clipboard History UI` to `Clipboard Win32 Daemon`?**
  _High betweenness centrality (0.124) - this node is a cross-community bridge._
- **What connects `MinimalNotepad`, `MinimalNotepad`, `MinimalNotepad` to the rest of the system?**
  _16 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Configuration & Settings` be split into smaller, more focused modules?**
  _Cohesion score 0.08 - nodes in this community are weakly interconnected._
- **Should `Clipboard History UI` be split into smaller, more focused modules?**
  _Cohesion score 0.1 - nodes in this community are weakly interconnected._
- **Should `Find & Replace` be split into smaller, more focused modules?**
  _Cohesion score 0.12 - nodes in this community are weakly interconnected._