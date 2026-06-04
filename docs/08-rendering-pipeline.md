# 08 · AvalonEdit Rendering Pipeline

This page documents the exact layering rules. Violations cause visual glitches or
crashes. **Read before touching anything in the Formatting/ directory.**

## The three rendering extension points

| Type | Interface/Base | When called | Who wins on conflict |
|------|---------------|-------------|----------------------|
| `IBackgroundRenderer` | `IBackgroundRenderer` | Before text is drawn | Last renderer added (paints on top) |
| `VisualLineElementGenerator` | `VisualLineElementGenerator` | During visual line build, replaces characters | FIRST generator to return non-null at an offset wins |
| `DocumentColorizingTransformer` | `DocumentColorizingTransformer` | After visual line built, mutates TextRunProperties | LAST transformer wins (overrides earlier ones) |

## Actual registration order

```
BackgroundRenderers (KnownLayer.Background):
  [0] CodeBlockBackgroundRenderer    ← dark panel
  [1] CodeBlockLineNumberRenderer    ← gutter background

ElementGenerators:
  [0] CodeBlockCollapseGenerator     ← ··· for :min — wins tie vs [1]
  [1] FoldingElementGenerator        ← MUST STAY — HeightTree collapse
  [2] CodeBlockLineNumberGenerator   ← line number elements
  [3] CodeBlockPaddingGenerator      ← left padding
  [4] NonBreakingHyphenGenerator     ← renders U+2011

LineTransformers:
  [0] RichTextColorizer              ← user spans (bold/color/highlight)
  [1] CodeSyntaxColorizer            ← VS Code Dark+ syntax inside code blocks
  [2] CodeBlockFontSizeTransformer   ← font size adjustments
  [3] SelectionForegroundOverride    ← MUST BE LAST — forces selection text color
```

## Critical ordering constraints

### ElementGenerators: first wins
When two generators report interest at the same offset (`GetFirstInterestedOffset`
returns the same value), AvalonEdit calls `ConstructElement` on them in order and uses
the **first non-null result**. Later generators for that offset are skipped.

This is how `CodeBlockCollapseGenerator` [0] overrides `FoldingElementGenerator` [1]:
both report the fold start offset; [0] returns a `FormattedTextElement("···")` first,
so [1] never gets to render its clickable `[···]` box. But [1] is still called for its
**side-effect** (HeightTree collapse via `CollapsedLineSection`).

> **Invariant:** `CodeBlockCollapseGenerator` must be at index 0.
> `FoldingElementGenerator` must remain in the list (cannot be removed).

### LineTransformers: last wins
`ChangeLinePart(start, end, el => el.TextRunProperties.Set*())` — later transformers
overwrite earlier ones. This is why:
- `CodeSyntaxColorizer` overrides `RichTextColorizer`'s ForeColor inside code blocks
  (syntax takes priority over user text color).
- `SelectionForegroundOverride` runs last so it restores the selection foreground color
  after syntax coloring may have changed it.

> **Invariant:** `SelectionForegroundOverride` must be the last entry in LineTransformers.

### BackgroundRenderers: last is on top
Background renderers draw in order; later ones paint over earlier ones.

## AvalonEdit internal constraints (learned the hard way)

### FoldingElementGenerator is not optional
`FoldingElementGenerator` implements `ITextViewConnect`. When installed, it registers
the TextView with the FoldingManager. This is what lets `FoldingSection.IsFolded`
actually collapse lines in the HeightTree (via `CollapseLines`).
**Remove it → HeightTree not updated → any multi-line VisualLineElement crashes:**
`InvalidOperationException: "Line N was skipped by a VisualLineElementGenerator, but it is not collapsed."`

### FormattedTextElement constructor choice
AvalonEdit 6.3.1 has three constructors:
```csharp
FormattedTextElement(string text, int documentLength)       // ← OK
FormattedTextElement(FormattedText ft, int documentLength)  // ← BROKEN: text field stays null → ArgumentNullException in PrepareText
FormattedTextElement(TextLine line, int documentLength)     // ← OK
```
Use `(string, int)` or `(TextLine, int)`. To get a custom color with `(TextLine, int)`:
```csharp
var props = new VisualLineElementTextRunProperties(CurrentContext.GlobalTextRunProperties);
props.SetForegroundBrush(brush);
var line = FormattedTextElement.PrepareText(TextFormatter.Create(), "  ···", props);
return new FormattedTextElement(line, docLen);
```

### KnownLayer for match highlights
Use `KnownLayer.Background` for the match-highlight renderer — it renders behind text.
`KnownLayer.Caret` renders over text (used for caret). Don't mix them up.

### SelectionBrush / SelectionForeground
`TextArea.SelectionBrush` and `SelectionForeground` are WPF dependency properties.
Setting them to custom colors (orange, black) is safe; restoring the originals on close
is required to avoid stale state if the same editor is reused.
`SelectionForegroundOverride` (last transformer) then re-applies the foreground color
to prevent syntax colorizers from overriding it after the selection is painted.
