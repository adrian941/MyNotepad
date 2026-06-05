# MyNotepad — Developer Wiki

A WPF (.NET 10, `net10.0-windows`) rich-text notepad built on **AvalonEdit**, with a
formatting engine, fenced code blocks with syntax highlighting, a dual-format
clipboard + history, find & replace with bulk formatting, and saved files.

This wiki documents *how the app is built and why* — for humans onboarding and for AI
agents working in the repo. Read it top to bottom the first time; after that, jump to
the page you need.

## Map of the wiki

| # | Page | What it covers |
|---|------|----------------|
| 01 | [Overview](01-overview.md) | Purpose, tech stack, file/folder layout, build & run |
| 02 | [Architecture](02-architecture.md) | Module map, core abstractions, data stores, how pieces connect |
| 03 | [Runtime Flows](03-flows.md) | Startup, keypress→format, copy/paste, save/load, code-block reparse |
| 04 | [Rich-Text Formatting Engine](04-rich-text-formatting.md) | Spans, anchors, toggling, sticky typing, undo |
| 05 | [Code Blocks](05-code-blocks.md) | Fence parsing, syntax coloring, `:ln`/`:min`, the rendering pipeline |
| 06 | [Clipboard & History](06-clipboard.md) | Dual-format clipboard, app/normal history, the Win32 daemon |
| 07 | [Find & Replace](07-find-replace.md) | Search, match rendering, bulk formatting of all matches |
| 08 | [Rendering Pipeline](08-rendering-pipeline.md) | AvalonEdit transformer/generator/renderer ordering — the layering rules |
| 09 | [Learnings & Gotchas](09-learnings.md) | Hard-won AvalonEdit lessons (folding, transformers, FormattedTextElement…) |
| 10 | [Reference](10-reference.md) | Keyboard shortcuts, color config, on-disk file locations |
| — | [AI Agent Guide](AI-GUIDE.md) | How an AI should navigate, verify, and change this repo |

## The one-paragraph mental model

A single `NotepadWindow` hosts one AvalonEdit `TextEditor`. User character styling
(bold/italic/color/highlight) is **not** stored in the document text — it lives in a
parallel `FormattingManager` as anchor-based spans, and is painted onto the view by
`DocumentColorizingTransformer`s. Fenced ` ```lang ` code blocks are detected by a
re-parse pass and rendered by a separate stack of transformers, element generators and
background renderers that draw a dark VS-Code-style block. Copy/paste carries a rich
JSON sidecar so formatting survives round-trips; saved `.mnp` files use the same JSON.

> See [02-architecture.md](02-architecture.md) for the full picture, then dive into the
> subsystem pages.

## Keeping this wiki honest

This wiki describes code that changes. When you change behaviour in a subsystem, update
its page. Each page header notes the **source of truth** files — if the code and the doc
disagree, the code wins and the doc is a bug. The graphify knowledge graph in
[`../graphify-out/GRAPH_REPORT.md`](../graphify-out/GRAPH_REPORT.md) is a machine-generated
companion view (god nodes, communities) and can be regenerated with `/graphify`.
