# 05 · Code Blocks

Source: `Formatting/CodeBlockParser.cs`, `Formatting/CodeSyntaxColorizer.cs`,
`Formatting/CodeBlockBackgroundRenderer.cs`, `Formatting/CodeBlockLineNumberRenderer.cs`,
`Formatting/CodeBlockLineNumberGenerator.cs`, `Formatting/CodeBlockPaddingTransformer.cs`
(type name: `CodeBlockPaddingGenerator`), `Formatting/CodeBlockFontSizeTransformer.cs`,
`Formatting/CodeBlockCollapseGenerator.cs`, `Formatting/CodeBlockCopyOverlay.cs`

**Before touching code blocks also read [08-rendering-pipeline.md](08-rendering-pipeline.md)
and [09-learnings.md](09-learnings.md).**

## Fence syntax

```
```lang               → basic block
```lang:ln            → with line numbers
```lang:ln:min        → with line numbers + starts minimized
```lang:min           → minimized, no line numbers
```

Rules enforced by `CodeBlockParser`:
- Opening line must be ```` ``` ```` + at least one char of `lang` (must be alphanumeric/`_+#-`).
- Closing line must be exactly ```` ``` ```` (after `.Trim()`).
- Flags are colon-separated after `lang`.
- Nesting is not supported.

## CodeBlockRegion record

```csharp
record CodeBlockRegion(
  string Language,
  bool   LineNumbers,
  bool   StartMinimized,
  int    FenceOpenLine,   // 1-based
  int    FenceCloseLine,  // 1-based
  int    ContentStart,    // doc offset of first char after opening fence newline
  int    ContentEnd       // doc offset of first char of closing fence line
)
```

Produced by `CodeBlockParser.Parse(doc)` on every reparse. Never stored — always recomputed.

## The rendering stack (what draws what)

| Layer | Class | What it draws |
|-------|-------|--------------|
| Background | `CodeBlockBackgroundRenderer` | Dark rounded panel (#282828 content, #484848 fence lines) |
| Background | `CodeBlockLineNumberRenderer` | Gutter background for line numbers (`:ln`) |
| ElementGenerator[0] | `CodeBlockCollapseGenerator` | Non-interactive `···` for `:min` folded lines |
| ElementGenerator | `FoldingElementGenerator` (built-in, kept) | HeightTree collapse (MUST stay) |
| ElementGenerator | `CodeBlockLineNumberGenerator` | Line-number elements for `:ln` |
| ElementGenerator | `CodeBlockPaddingGenerator` | Left padding at line start |
| LineTransformer | `RichTextColorizer` | User spans (bold/color/highlight) |
| LineTransformer | `CodeSyntaxColorizer` | VS Code Dark+ syntax coloring |
| LineTransformer | `CodeBlockFontSizeTransformer` | Font size inside blocks |
| LineTransformer[LAST] | `SelectionForegroundOverride` | Forces selection text color |
| Canvas overlay | `CodeBlockCopyOverlay` | #/▾/Copy/Delete floating buttons |

## Syntax coloring pipeline (inside CodeSyntaxColorizer.ColorizeLine)

For each content line inside a block, 5 steps run in order (last writer wins):

1. **DefaultText foreground** — sets #D4D4D4 on entire line.
2. **PascalCase identifiers** (C-family only) — `TypeBrush` (#4EC9B0) or `MethodBrush` (#DCDCAA).
3. **AvalonEdit tokenizer sections** — uses `DocumentHighlighter` on a per-block sub-document.
4. **Control-flow keywords purple** (C-family only) — `CtrlFlowBrush` (#C586C0).
5. **Remap user highlights** — BackColor hex → dark-mode hex + `HighlightedCodeText` (#0F0F0F) fg.

Fence lines (opening/closing ` ``` `) get `FenceText` (#CCCCCC) only.

**ForeColor (user text color) is ignored in code blocks** — syntax coloring has priority.
Only BackColor (highlights) are shown, remapped to dark-mode-friendly colors.

## Language → AvalonEdit definition mapping

Handled in `CodeSyntaxColorizer.ResolveDefinition(lang)`. Notable aliases:

| Fence tag | AvalonEdit definition |
|-----------|----------------------|
| csharp, cs, c# | C# |
| sql, mssql, tsql | TSQL |
| js, javascript | JavaScript |
| json | JavaScript |
| ts, typescript | TypeScript |
| py, python | Python |
| xml, xaml | XML |
| ps, powershell | PowerShell |

C-family heuristics (PascalCase + purple control-flow) enabled for:
`csharp/cs/c#`, `java`, `javascript/js`, `typescript/ts`, `cpp/c++/c`, `kotlin/scala/swift`, `go/rust`.

## :min (minimize / fold)

### How it works

`ReparseCodeBlocks()` in `NotepadWindow` for each `:min` block with > 20 content lines:

```
line21 = FenceOpenLine + 21  (first line to hide — shown as ···)
endOffset = GetLineByNumber(FenceCloseLine - 1).EndOffset
           (= FenceCloseLine.Offset — ends at last content line, NOT at closing fence)
section = _foldingManager.CreateFolding(line21.Offset, endOffset)
section.IsFolded = true
```

`CodeBlockCollapseGenerator` (at index 0 in `ElementGenerators`) wins the offset tie
vs the built-in `FoldingElementGenerator` and renders `"  ···"` in gray (#8599AA).
The built-in generator still runs and handles HeightTree collapse.

### Toggling minimize (the ▾/▸ button)

`CodeBlockCopyOverlay.HandleToggleMinimize(region)`:
1. Reads opening fence line text.
2. Calls `ToggleFlag(text, "min")` — adds/removes `:min` in the fence line.
3. `_doc.Replace(...)` → text change → debounce → `ReparseCodeBlocks()` → fold rebuilt.

### Button label shows state
- `▾` = currently expanded (click to minimize)
- `▸` = currently minimized (click to expand)

Button hidden if block has ≤ 20 content lines (no point minimizing a short block).

## :ln (line numbers)

`CodeBlockLineNumberGenerator` inserts a zero-width element at the start of each
content line. The element draws the line number gutter.
`CodeBlockLineNumberRenderer` draws the gutter background.

Same toggle mechanism as `:min` — `ToggleFlag(text, "ln")` via the `#` button.

## Overlay buttons

`CodeBlockCopyOverlay` positions `#` / `▾` / `Copy` / `Delete` buttons on a `Canvas`
overlaid on the editor. Positions are recalculated in `UpdatePositions()` on every
scroll and visual-line change.

Button order (right-to-left): Delete | Copy | Min(▾) | #

`HandleCopy` also sets `"application/x-mynotepad-codeblock"` clipboard format
(full block with ``` markers) so code-block paste preserves the fence.
