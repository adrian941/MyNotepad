# 09 · Learnings & Gotchas

Hard-won knowledge from bugs, crashes, and failed fix attempts.
**An AI agent should read this before touching rendering or folding code.**

---

## Folding / minimize (`:min`)

### The crash: "Line N was skipped but not collapsed"
`InvalidOperationException: 'Line N was skipped by a VisualLineElementGenerator, but it is not collapsed.'`

**Root cause:** A `VisualLineElementGenerator` returned a `FormattedTextElement` with
`documentLength` spanning multiple document lines, but those lines were not marked as
collapsed in AvalonEdit's internal `HeightTree`.

**What updates HeightTree:** `FoldingElementGenerator` (built-in). When it is in the
`ElementGenerators` list and `FoldingSection.IsFolded = true`, it creates a
`CollapsedLineSection` in HeightTree as a side-effect of `ConstructElement`. Without it
in the list, HeightTree is never updated — even if `FoldingManager.CreateFolding` is called.

**The fix:** Keep `FoldingElementGenerator` in `ElementGenerators`. To render a custom
non-interactive `···` instead of its clickable `[···]`:
- Insert `CodeBlockCollapseGenerator` at **index 0** (before `FoldingElementGenerator`).
- AvalonEdit breaks ties by using the **first non-null** result — so `CodeBlockCollapseGenerator`
  wins visually while `FoldingElementGenerator` still runs its HeightTree side-effect.

**Don't try:**
- Removing `FoldingElementGenerator` → crash.
- Subclassing `FoldingElementGenerator` → it's `sealed`.
- Using `TextView.CollapseLines()` directly without `FoldingElementGenerator` →
  HeightTree timing issues → crash.
- `FormattedTextElement(FormattedText, int)` → null text field → `ArgumentNullException`.

### The fold end offset matters for visible closing fence
If `section.EndOffset = FenceCloseLine.Offset`, `FoldingManager` collapses through
`FenceCloseLine` (inclusive) → the closing ` ``` ` disappears and loses its gray color.

**Correct:** `section.EndOffset = GetLineByNumber(FenceCloseLine - 1).EndOffset`
This ends at the last content line; FenceCloseLine (the ` ``` `) remains visible.

---

## FormattedTextElement (AvalonEdit 6.3.1)

Three constructors. Only two work:
```
FormattedTextElement(string, int)                  ✅
FormattedTextElement(TextLine, int)                ✅
FormattedTextElement(FormattedText, int)           ❌ ArgumentNullException at render time
```
The `(FormattedText, int)` overload stores the `FormattedText` object but `CreateTextRun`
ignores it and calls `PrepareText(formatter, this.text, ...)` where `this.text` was never
set → null → crash.

**Use this pattern for custom colored text in an element generator:**
```csharp
var props = new VisualLineElementTextRunProperties(CurrentContext.GlobalTextRunProperties);
props.SetForegroundBrush(yourBrush);
var line = FormattedTextElement.PrepareText(TextFormatter.Create(), "  ···", props);
return new FormattedTextElement(line, docLen);
```

---

## SelectionForeground being overridden by syntax colorizers

**Problem:** When a code block was selected, `CodeSyntaxColorizer` ran in
`LineTransformers` and set ForeColor on text runs, overriding the `SelectionForeground`
(Black) set via `TextArea.SelectionForeground`. The selection text turned the syntax
color instead of staying black.

**Fix:** `SelectionForegroundOverride` — a `DocumentColorizingTransformer` added **last**
in `LineTransformers`. It calls `ChangeLinePart` on the selection range and re-applies
`TextArea.SelectionForeground`. Being last, it wins over all prior transformers.

---

## AA/aa and Aa/ab buttons caused unwanted navigation in Find bar

**Problem:** Calling `RunSearch()` (default `jump=true`) from a toggle button re-jumped
to the next match.

**Fix:** `RunSearch(jump: false)` in all toggle handlers (case mode, whole-word, AA, aa,
Aa toggle checked/unchecked). `jump=false` rebuilds match positions without advancing
`_currentIndex`.

---

## BackColor (highlights) invisible inside code blocks

**Problem:** `CodeSyntaxColorizer` step 1 set `SetBackgroundBrush(null)` to clear any
background on the entire line, wiping the user's highlights.

**Fix:** Step 1 only sets `SetForegroundBrush(DefaultText)`. BackColor is left untouched.
Step 5 (`ApplyCodeBlockHighlights`) then reads the spans and applies remapped highlight
colors (dark-mode variants) on top.

---

## Highlight remap direction (typeId swap)

When building `_highlightRemap` in `CodeSyntaxColorizer` constructor:
- typeId=2 (light pastel) → remaps to typeId=**6** (code-strong-highlight, darker)
- typeId=4 (vivid strong) → remaps to typeId=**5** (code-highlight, saturated)

This looks inverted but is correct: light pastels need to be replaced by darker codes;
vivid colors need slightly saturated but not the strongest variant.
If someone swaps these, colors appear with the wrong intensity.

---

## Non-breaking spaces (U+00A0)

The `Space` key inserts U+00A0 instead of U+0020 to prevent WPF word-wrap at spaces
(WPF wraps at regular spaces). `NonBreakingHyphenGenerator` handles the similar U+2011
(non-breaking hyphen). If you need to search text programmatically inside the editor,
be aware that "spaces" may be U+00A0.

---

## Single-instance: Ctrl+S after re-open

When a file is opened via file association into the running instance (second launch →
signal → `OpenOrFocusExternalFile`), `_externalPath` must be set. If it is null, the
next `Ctrl+S` writes to the library instead of the original file. Make sure every
code path that opens an external file calls `LoadExternalFile(fullPath, entry)`,
not `LoadSavedFile(entry)`.

---

## WPF DockPanel button ordering

`DockPanel` with `LastChildFill = true` — items docked `Right` are placed in
**reverse visual order**: the first added appears rightmost. The toolbar buttons
`#` / `▾` / `Copy` / `Delete` are placed right-to-left as:
`Delete` added first → rightmost; `#` added last → leftmost.
