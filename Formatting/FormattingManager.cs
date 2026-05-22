using System;
using System.Collections.Generic;
using System.Linq;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace MinimalNotepad.Formatting
{
    class FormattingManager
    {
        private readonly List<FormattingSpan> _spans = new();
        private readonly TextDocument         _doc;

        public FormattingManager(TextDocument doc) => _doc = doc;

        public IReadOnlyList<FormattingSpan> Spans => _spans;

        // ── Anchor helpers ────────────────────────────────────────────────────

        ITextAnchor MakeAnchor(int offset, AnchorMovementType movement)
        {
            var a = _doc.CreateAnchor(offset);
            a.MovementType    = movement;
            a.SurviveDeletion = true;
            return a;
        }

        // ── Split a span at the given offset ──────────────────────────────────

        void SplitAt(int offset)
        {
            if (offset <= 0 || offset >= _doc.TextLength) return;
            for (int i = _spans.Count - 1; i >= 0; i--)
            {
                var s = _spans[i];
                if (s.IsDeleted || s.IsEmpty) { _spans.RemoveAt(i); continue; }
                if (s.Start < offset && s.End > offset)
                {
                    var leftEnd    = MakeAnchor(offset, AnchorMovementType.BeforeInsertion);
                    var rightStart = MakeAnchor(offset, AnchorMovementType.AfterInsertion);
                    _spans[i] = new FormattingSpan(s.StartAnchor, leftEnd,    s.Format.Clone());
                    _spans.Insert(i + 1,
                                  new FormattingSpan(rightStart,  s.EndAnchor, s.Format.Clone()));
                }
            }
        }

        // ── Coverage helpers ──────────────────────────────────────────────────

        List<int> CoveragePoints(int start, int end)
        {
            var pts = new SortedSet<int> { start, end };
            foreach (var s in _spans)
            {
                if (s.IsDeleted || s.IsEmpty) continue;
                if (s.Start >= start && s.Start <= end) pts.Add(s.Start);
                if (s.End   >= start && s.End   <= end) pts.Add(s.End);
            }
            return pts.ToList();
        }

        FormattingSpan? SpanFor(int segStart, int segEnd) =>
            _spans.FirstOrDefault(s =>
                !s.IsDeleted && !s.IsEmpty && s.Start == segStart && s.End == segEnd);

        // ── Apply a mutation to all sub-segments in [start, end] ─────────────

        void ModifyRange(int start, int end, Action<TextFormatting> modify)
        {
            if (start >= end) return;
            SplitAt(start);
            SplitAt(end);
            var points = CoveragePoints(start, end);
            for (int i = 0; i < points.Count - 1; i++)
            {
                int segStart = points[i], segEnd = points[i + 1];
                var existing = SpanFor(segStart, segEnd);
                if (existing != null)
                {
                    modify(existing.Format);
                }
                else
                {
                    var fmt = new TextFormatting();
                    modify(fmt);
                    if (!fmt.IsDefault)
                        _spans.Add(new FormattingSpan(
                            MakeAnchor(segStart, AnchorMovementType.AfterInsertion),
                            MakeAnchor(segEnd,   AnchorMovementType.BeforeInsertion),
                            fmt));
                }
            }
            Cleanup();
        }

        // ── Remove empty/default spans and merge adjacent identical ones ──────

        void Cleanup()
        {
            for (int i = _spans.Count - 1; i >= 0; i--)
            {
                var s = _spans[i];
                if (s.IsDeleted || s.IsEmpty || s.Format.IsDefault)
                    _spans.RemoveAt(i);
            }
            _spans.Sort((a, b) => a.Start.CompareTo(b.Start));
            for (int i = _spans.Count - 2; i >= 0; i--)
            {
                var a = _spans[i]; var b = _spans[i + 1];
                if (a.End == b.Start && a.Format.SameAs(b.Format))
                {
                    _spans[i] = new FormattingSpan(a.StartAnchor, b.EndAnchor, a.Format.Clone());
                    _spans.RemoveAt(i + 1);
                }
            }
        }

        // ── Query helpers ─────────────────────────────────────────────────────

        bool IsUniform(int start, int end, Func<TextFormatting, bool> pred)
        {
            SplitAt(start); SplitAt(end);
            var points = CoveragePoints(start, end);
            for (int i = 0; i < points.Count - 1; i++)
            {
                var s = SpanFor(points[i], points[i + 1]);
                if (s == null || !pred(s.Format)) return false;
            }
            return true;
        }

        // Returns the uniform hex color, null if all default, "MIXED" if not uniform.
        string? GetUniformColor(int start, int end, Func<TextFormatting, string?> getter)
        {
            SplitAt(start); SplitAt(end);
            var points = CoveragePoints(start, end);
            const string Unset = "__UNSET__";
            string? uniform = Unset;
            for (int i = 0; i < points.Count - 1; i++)
            {
                var s = SpanFor(points[i], points[i + 1]);
                string? c = s != null ? getter(s.Format) : null;
                if (uniform == Unset) uniform = c;
                else if (uniform != c) return "MIXED";
            }
            return uniform == Unset ? null : uniform;
        }

        // ── Public formatting actions ─────────────────────────────────────────

        public void ToggleBold(int start, int end)
        {
            bool allBold = IsUniform(start, end, f => f.Bold);
            ModifyRange(start, end, f => f.Bold = !allBold);
        }

        public void ToggleItalic(int start, int end)
        {
            bool allItalic = IsUniform(start, end, f => f.Italic);
            ModifyRange(start, end, f => f.Italic = !allItalic);
        }

        public void ToggleUnderline(int start, int end)
        {
            bool allUnder = IsUniform(start, end, f => f.Underline);
            ModifyRange(start, end, f => f.Underline = !allUnder);
        }

        public void ToggleStrikethrough(int start, int end)
        {
            bool allStrike = IsUniform(start, end, f => f.Strikethrough);
            ModifyRange(start, end, f => f.Strikethrough = !allStrike);
        }

        public void ToggleForeColor(int start, int end, string targetHex)
        {
            string? current = GetUniformColor(start, end, f => f.ForeColorHex);
            string? newColor = string.Equals(current, targetHex, StringComparison.OrdinalIgnoreCase)
                ? null : targetHex;
            ModifyRange(start, end, f => f.ForeColorHex = newColor);
        }

        public void ToggleBackColor(int start, int end, string targetHex)
        {
            string? current = GetUniformColor(start, end, f => f.BackColorHex);
            string? newColor = string.Equals(current, targetHex, StringComparison.OrdinalIgnoreCase)
                ? null : targetHex;
            ModifyRange(start, end, f => f.BackColorHex = newColor);
        }

        /// <summary>
        /// Returns the inline formatting (Bold/Italic/Underline/Strikethrough/ForeColorHex)
        /// of the character immediately to the left of <paramref name="offset"/>.
        /// BackColorHex (highlighter) is intentionally excluded — sticky color only inherits
        /// character style, not background highlight.
        /// Returns null when offset == 0 or no span covers that position (default style).
        /// </summary>
        public TextFormatting? GetInlineFormattingBefore(int offset)
        {
            if (offset <= 0) return null;
            int pos = offset - 1;
            foreach (var s in _spans)
            {
                if (s.IsDeleted || s.IsEmpty) continue;
                if (s.Start <= pos && s.End > pos)
                {
                    var f = s.Format;
                    // Only worth inheriting if there is something set (ignoring highlighter)
                    if (!f.Bold && !f.Italic && !f.Underline && !f.Strikethrough && f.ForeColorHex == null)
                        return null;
                    return new TextFormatting
                    {
                        Bold          = f.Bold,
                        Italic        = f.Italic,
                        Underline     = f.Underline,
                        Strikethrough = f.Strikethrough,
                        ForeColorHex  = f.ForeColorHex,
                        // BackColorHex intentionally omitted
                    };
                }
            }
            return null;
        }

        /// <summary>
        /// Unconditionally applies inline formatting on [start, end) without toggling.
        /// Used for sticky-style typing — preserves existing BackColorHex.
        /// </summary>
        public void ApplyInlineFormatting(int start, int end, TextFormatting style)
            => ModifyRange(start, end, f =>
            {
                f.Bold          = style.Bold;
                f.Italic        = style.Italic;
                f.Underline     = style.Underline;
                f.Strikethrough = style.Strikethrough;
                f.ForeColorHex  = style.ForeColorHex;
                // BackColorHex untouched — don't inherit highlighter
            });

        // ── Paste support: apply spans relative to a paste offset ────────────

        /// <summary>
        /// Adds formatting spans from a clipboard payload, shifted by <paramref name="pasteOffset"/>.
        /// Called after the paste text has already been inserted into the document.
        /// </summary>
        public void ApplyRelativeSpans(int pasteOffset, List<SpanRecord> relativeSpans)
        {
            int docLen = _doc.TextLength;
            foreach (var r in relativeSpans)
            {
                int s = Math.Max(0, Math.Min(pasteOffset + r.Start, docLen));
                int e = Math.Max(s,  Math.Min(pasteOffset + r.End,   docLen));
                if (s >= e) continue;
                _spans.Add(new FormattingSpan(
                    MakeAnchor(s, AnchorMovementType.AfterInsertion),
                    MakeAnchor(e, AnchorMovementType.BeforeInsertion),
                    r.Format.Clone()));
            }
            Cleanup();
        }

        // ── Snapshot support for undo/redo ────────────────────────────────────

        public record SpanRecord(int Start, int End, TextFormatting Format);

        public List<SpanRecord> TakeSnapshot() =>
            _spans
                .Where(s => !s.IsDeleted && !s.IsEmpty)
                .Select(s => new SpanRecord(s.Start, s.End, s.Format.Clone()))
                .ToList();

        public void RestoreSnapshot(List<SpanRecord> snapshot, TextView textView)
        {
            _spans.Clear();
            int docLen = _doc.TextLength;
            foreach (var r in snapshot)
            {
                int s = Math.Max(0, Math.Min(r.Start, docLen));
                int e = Math.Max(s,  Math.Min(r.End,   docLen));
                if (s >= e) continue;
                _spans.Add(new FormattingSpan(
                    MakeAnchor(s, AnchorMovementType.AfterInsertion),
                    MakeAnchor(e, AnchorMovementType.BeforeInsertion),
                    r.Format.Clone()));
            }
            textView.Redraw();
        }
    }
}
