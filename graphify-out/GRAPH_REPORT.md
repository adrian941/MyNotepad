# Graph Report - D:/Work/OtherRepos/MyNotepad  (2026-06-04)

## Corpus Check
- 63 files · ~67,826 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 550 nodes · 914 edges · 39 communities detected
- Extraction: 81% EXTRACTED · 19% INFERRED · 0% AMBIGUOUS · INFERRED: 177 edges (avg confidence: 0.81)
- Token cost: 0 input · 0 output

## Community Hubs (Navigation)
- [[_COMMUNITY_Community 0|Community 0]]
- [[_COMMUNITY_Community 1|Community 1]]
- [[_COMMUNITY_Community 2|Community 2]]
- [[_COMMUNITY_Community 3|Community 3]]
- [[_COMMUNITY_Community 4|Community 4]]
- [[_COMMUNITY_Community 5|Community 5]]
- [[_COMMUNITY_Community 6|Community 6]]
- [[_COMMUNITY_Community 7|Community 7]]
- [[_COMMUNITY_Community 8|Community 8]]
- [[_COMMUNITY_Community 9|Community 9]]
- [[_COMMUNITY_Community 10|Community 10]]
- [[_COMMUNITY_Community 11|Community 11]]
- [[_COMMUNITY_Community 12|Community 12]]
- [[_COMMUNITY_Community 13|Community 13]]
- [[_COMMUNITY_Community 14|Community 14]]
- [[_COMMUNITY_Community 15|Community 15]]
- [[_COMMUNITY_Community 16|Community 16]]
- [[_COMMUNITY_Community 17|Community 17]]
- [[_COMMUNITY_Community 18|Community 18]]
- [[_COMMUNITY_Community 19|Community 19]]
- [[_COMMUNITY_Community 20|Community 20]]
- [[_COMMUNITY_Community 21|Community 21]]
- [[_COMMUNITY_Community 22|Community 22]]
- [[_COMMUNITY_Community 23|Community 23]]
- [[_COMMUNITY_Community 24|Community 24]]
- [[_COMMUNITY_Community 25|Community 25]]
- [[_COMMUNITY_Community 26|Community 26]]
- [[_COMMUNITY_Community 27|Community 27]]
- [[_COMMUNITY_Community 28|Community 28]]
- [[_COMMUNITY_Community 29|Community 29]]
- [[_COMMUNITY_Community 30|Community 30]]
- [[_COMMUNITY_Community 31|Community 31]]
- [[_COMMUNITY_Community 32|Community 32]]
- [[_COMMUNITY_Community 33|Community 33]]
- [[_COMMUNITY_Community 34|Community 34]]
- [[_COMMUNITY_Community 35|Community 35]]
- [[_COMMUNITY_Community 36|Community 36]]
- [[_COMMUNITY_Community 37|Community 37]]
- [[_COMMUNITY_Community 38|Community 38]]

## God Nodes (most connected - your core abstractions)
1. `NotepadWindow` - 43 edges
2. `FindReplaceWindow` - 40 edges
3. `ClipboardHistoryWindow` - 34 edges
4. `FormattingManager` - 25 edges
5. `HelpWindow` - 15 edges
6. `SavedFileStore` - 14 edges
7. `CodeBlockCopyOverlay` - 13 edges
8. `CodeSyntaxColorizer` - 13 edges
9. `NotepadWindow` - 12 edges
10. `FormattingManager` - 9 edges

## Surprising Connections (you probably didn't know these)
- `Over-Colorized Code Block (Exagerare)` --semantically_similar_to--> `Colorization Bug Screenshot`  [INFERRED] [semantically similar]
  exagerarecolorizare.png → ProblemaColorizare.png
- `Problems Overview Screenshot` --semantically_similar_to--> `All Problems Combined Screenshot`  [INFERRED] [semantically similar]
  Probleme.png → ToateProblemele.png
- `VS Code Dark+ Palette Applied to C#` --implements--> `CodeSyntaxColorizer Component`  [INFERRED]
  colorizareOptimaCsharp.png → colorizarecsharp.png
- `Exaggerated Colorization Issue` --conceptually_related_to--> `Comprehensive Bug Collection View`  [INFERRED]
  exagerarecolorizare.png → ToateProblemele.png
- `SQL Syntax Highlighting Bug` --references--> `Code Block Background Renderer`  [INFERRED]
  ProblemaSql.png → colorizarecsharp.png

## Hyperedges (group relationships)
- **AvalonEdit Rendering Extension Point Stack** — render_backgroundrenderers, render_elementgenerators, render_linetransformers [EXTRACTED 1.00]
- **Code Block Rendering Components** — overview_codeblockbackgroundrenderer, overview_codeblockcollapsegenerator, overview_codesyntaxcolorizer, overview_codeblockpaddingtransformer, overview_codeblocklngenerator, overview_codeblockfonttransformer [EXTRACTED 1.00]
- **Formatting Engine Core (Manager + Span + Value Object)** — overview_formattingmanager, overview_formattingspan, overview_textformatting [EXTRACTED 1.00]
- **Code Block Rendering Pipeline** —  [INFERRED 0.95]
- **Find & Replace Feature Components** —  [EXTRACTED 0.95]

## Communities

### Community 0 - "Community 0"
Cohesion: 0.04
Nodes (61): AI Agent Hard Rules (Non-Negotiable), ClipboardEntry / SavedFileEntry, CodeBlockRegion Record, FoldingManager (AvalonEdit), ReparseCodeBlocks() method, WireEvents() method, App Clipboard History (Rich), Normal (System) Clipboard History (+53 more)

### Community 1 - "Community 1"
Cohesion: 0.06
Nodes (52): AppSettings, BlockHighlighter, ClipboardDaemon, ClipboardHistoryWindow, CodeBlockBackgroundRenderer, CodeBlockCopyOverlay, CodeBlockFontSizeTransformer, CodeBlockLineNumberGenerator (+44 more)

### Community 2 - "Community 2"
Cohesion: 0.07
Nodes (6): CodeBlockParser, MinimalNotepad.Formatting, MinimalNotepad, NotepadWindow, MinimalNotepad.Formatting, RichClipboard

### Community 3 - "Community 3"
Cohesion: 0.11
Nodes (3): FindReplaceWindow, MatchHighlightRenderer, MinimalNotepad

### Community 4 - "Community 4"
Cohesion: 0.09
Nodes (8): ClipboardDaemon, DaemonWindow, MinimalNotepad, CodeBlockCopyOverlay, MinimalNotepad.Formatting, MinimalNotepad.Formatting, NormalClipboardHistory, Window

### Community 5 - "Community 5"
Cohesion: 0.12
Nodes (7): FormattingManager, MinimalNotepad.Formatting, FormattingUndoOperation, MinimalNotepad.Formatting, IUndoableOperation, MinimalNotepad.Formatting, TextFormatting

### Community 6 - "Community 6"
Cohesion: 0.11
Nodes (1): ClipboardHistoryWindow

### Community 7 - "Community 7"
Cohesion: 0.09
Nodes (10): CodeBlockFontSizeTransformer, MinimalNotepad.Formatting, BlockHighlighter, CodeSyntaxColorizer, MinimalNotepad.Formatting, DocumentColorizingTransformer, MinimalNotepad.Formatting, RichTextColorizer (+2 more)

### Community 8 - "Community 8"
Cohesion: 0.12
Nodes (30): Desired UI Appearance (AsaVreauSaArate), Notepad UI Target State, Code Block Background Renderer, CodeSyntaxColorizer Component, Code Block C# Colorization, C# Syntax Highlighting Screenshot, Optimal C# Colorization Screenshot, VS Code Dark+ Palette Applied to C# (+22 more)

### Community 9 - "Community 9"
Cohesion: 0.11
Nodes (6): ColorEntryKeyComparer, ConfigLoader, MinimalNotepad.Config, IEqualityComparer, MinimalNotepad, Program

### Community 10 - "Community 10"
Cohesion: 0.08
Nodes (9): CodeBlockCollapseGenerator, MinimalNotepad.Formatting, CodeBlockLineNumberGenerator, MinimalNotepad.Formatting, CodeBlockPaddingGenerator, MinimalNotepad.Formatting, MinimalNotepad.Formatting, NonBreakingHyphenGenerator (+1 more)

### Community 11 - "Community 11"
Cohesion: 0.12
Nodes (20): InitializeFormatting() method, Code Block :ln Line Numbers, Syntax Coloring Pipeline (5 Steps), BackColor Highlight Remap (Dark Mode Code Blocks), Rationale: RichTextColorizer Before CodeSyntaxColorizer, MatchHighlightRenderer, Gotcha: BackColor Invisible Inside Code Blocks, Gotcha: Highlight Remap Direction (typeId swap looks inverted) (+12 more)

### Community 12 - "Community 12"
Cohesion: 0.2
Nodes (3): MinimalNotepad.Formatting, SavedFileDto, SavedFileStore

### Community 13 - "Community 13"
Cohesion: 0.26
Nodes (2): HelpWindow, MinimalNotepad

### Community 14 - "Community 14"
Cohesion: 0.18
Nodes (5): CodeBlockBackgroundRenderer, MinimalNotepad.Formatting, CodeBlockLineNumberRenderer, MinimalNotepad.Formatting, IBackgroundRenderer

### Community 15 - "Community 15"
Cohesion: 0.31
Nodes (3): ClipboardEntryDto, ClipboardHistory, MinimalNotepad.Formatting

### Community 16 - "Community 16"
Cohesion: 0.5
Nodes (3): ColorConfig, ColorEntry, MinimalNotepad.Config

### Community 17 - "Community 17"
Cohesion: 0.5
Nodes (4): ColorEntry, ColorConfig, Color System (6 TypeIds), Keyboard Shortcuts Reference

### Community 18 - "Community 18"
Cohesion: 0.5
Nodes (4): ClipboardEntryDto, ClipboardHistory, Community: Clipboard Data Model, RichClipboard

### Community 19 - "Community 19"
Cohesion: 0.67
Nodes (2): GlobalClipboardMonitor, MinimalNotepad

### Community 20 - "Community 20"
Cohesion: 0.67
Nodes (2): AppSettings, MinimalNotepad.Config

### Community 21 - "Community 21"
Cohesion: 0.67
Nodes (2): FormattingSpan, MinimalNotepad.Formatting

### Community 22 - "Community 22"
Cohesion: 0.67
Nodes (3): Code Block Overlay Buttons (#/▾/Copy/Delete), Gotcha: DockPanel Button Right-to-Left Order, CodeBlockCopyOverlay

### Community 23 - "Community 23"
Cohesion: 0.67
Nodes (3): ColorConfig, ColorEntry, Community: Color Config Entries

### Community 24 - "Community 24"
Cohesion: 1.0
Nodes (2): FileSystemWatcher on Saved Folder, ClipboardHistoryWindow

### Community 25 - "Community 25"
Cohesion: 1.0
Nodes (2): Community: Formatting Spans, FormattingSpan

### Community 26 - "Community 26"
Cohesion: 1.0
Nodes (2): Community: Global Clipboard Monitor, GlobalClipboardMonitor

### Community 27 - "Community 27"
Cohesion: 1.0
Nodes (0): 

### Community 28 - "Community 28"
Cohesion: 1.0
Nodes (0): 

### Community 29 - "Community 29"
Cohesion: 1.0
Nodes (0): 

### Community 30 - "Community 30"
Cohesion: 1.0
Nodes (0): 

### Community 31 - "Community 31"
Cohesion: 1.0
Nodes (0): 

### Community 32 - "Community 32"
Cohesion: 1.0
Nodes (1): FindReplaceBar.cs

### Community 33 - "Community 33"
Cohesion: 1.0
Nodes (1): HelpWindow

### Community 34 - "Community 34"
Cohesion: 1.0
Nodes (1): AppSettings

### Community 35 - "Community 35"
Cohesion: 1.0
Nodes (1): KnownLayer.Background for Match Highlights

### Community 36 - "Community 36"
Cohesion: 1.0
Nodes (1): On-Disk File Locations

### Community 37 - "Community 37"
Cohesion: 1.0
Nodes (1): How to Verify a Code Change Without UI

### Community 38 - "Community 38"
Cohesion: 1.0
Nodes (1): Graph Report

## Knowledge Gaps
- **113 isolated node(s):** `MinimalNotepad`, `MinimalNotepad`, `MinimalNotepad`, `GlobalClipboardMonitor`, `MinimalNotepad` (+108 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **Thin community `Community 24`** (2 nodes): `FileSystemWatcher on Saved Folder`, `ClipboardHistoryWindow`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 25`** (2 nodes): `Community: Formatting Spans`, `FormattingSpan`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 26`** (2 nodes): `Community: Global Clipboard Monitor`, `GlobalClipboardMonitor`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 27`** (1 nodes): `AssemblyInfo.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 28`** (1 nodes): `MyNotepad.AssemblyInfo.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 29`** (1 nodes): `MyNotepad.GlobalUsings.g.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 30`** (1 nodes): `MyNotepad.AssemblyInfo.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 31`** (1 nodes): `MyNotepad.GlobalUsings.g.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 32`** (1 nodes): `FindReplaceBar.cs`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 33`** (1 nodes): `HelpWindow`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 34`** (1 nodes): `AppSettings`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 35`** (1 nodes): `KnownLayer.Background for Match Highlights`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 36`** (1 nodes): `On-Disk File Locations`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 37`** (1 nodes): `How to Verify a Code Change Without UI`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.
- **Thin community `Community 38`** (1 nodes): `Graph Report`
  Too small to be a meaningful cluster - may be noise or needs more connections extracted.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `NotepadWindow` connect `Community 2` to `Community 9`, `Community 4`, `Community 12`, `Community 7`?**
  _High betweenness centrality (0.138) - this node is a cross-community bridge._
- **Why does `FindReplaceWindow` connect `Community 3` to `Community 9`, `Community 2`, `Community 4`, `Community 5`?**
  _High betweenness centrality (0.082) - this node is a cross-community bridge._
- **Why does `ClipboardHistoryWindow` connect `Community 6` to `Community 4`?**
  _High betweenness centrality (0.054) - this node is a cross-community bridge._
- **What connects `MinimalNotepad`, `MinimalNotepad`, `MinimalNotepad` to the rest of the system?**
  _113 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Community 0` be split into smaller, more focused modules?**
  _Cohesion score 0.04 - nodes in this community are weakly interconnected._
- **Should `Community 1` be split into smaller, more focused modules?**
  _Cohesion score 0.06 - nodes in this community are weakly interconnected._
- **Should `Community 2` be split into smaller, more focused modules?**
  _Cohesion score 0.07 - nodes in this community are weakly interconnected._