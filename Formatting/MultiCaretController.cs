using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace MinimalNotepad.Formatting
{
    /// <summary>
    /// VSCode-style multi-cursor (Alt+Click / Alt+Drag) layered on AvalonEdit's single caret.
    ///
    /// Primary caret stays AvalonEdit's own. Each extra caret is a <see cref="CaretState"/>
    /// backed by <see cref="TextAnchor"/>s that auto-track document edits.
    ///
    /// – Alt+Click       → add secondary caret at that point (Alt+Click again removes it)
    /// – Alt+Drag        → rectangular selection converted to one caret per line on mouse-up
    /// – Arrow keys      → move ALL secondary carets; primary moves naturally (not handled)
    /// – Shift+Arrow     → extend selection at ALL secondary carets; primary extends naturally
    /// – Ctrl+Arrow      → word-jump at ALL carets
    /// – Enter / Backspace / Delete / text → replayed at every caret in one undo group
    /// – Plain click / Escape → collapse back to single caret
    /// </summary>
    class MultiCaretController : IBackgroundRenderer
    {
        // ── Per-cursor state ──────────────────────────────────────────────────────
        sealed class CaretState
        {
            public required TextAnchor  Caret;   // AfterInsertion  — the blinking end
            public TextAnchor? Anchor;  // BeforeInsertion — fixed end of selection; null = no sel

            public bool HasSelection =>
                Anchor != null && !Anchor.IsDeleted && Anchor.Offset != Caret.Offset;

            public int SelectionStart  => HasSelection ? Math.Min(Caret.Offset, Anchor!.Offset) : Caret.Offset;
            public int SelectionEnd    => HasSelection ? Math.Max(Caret.Offset, Anchor!.Offset) : Caret.Offset;
            public int SelectionLength => SelectionEnd - SelectionStart;
        }

        // ── Fields ────────────────────────────────────────────────────────────────
        readonly TextEditor       _editor;
        readonly Action<int, int> _applySticky;   // NotepadWindow.ApplyStickyFormatting
        readonly List<CaretState> _carets = new();
        readonly DispatcherTimer  _blink;
        bool _blinkOn = true;

        bool   _nativeBrushHidden;
        Brush? _savedSelectionBrush;
        Brush? _savedSelectionForeground;

        bool  _altDown;
        Point _altDownPt;
        int   _altDownCaretOffset;

        bool  _ctrlSelectionMode;

        static readonly Brush SelBrush =
            new SolidColorBrush(Color.FromArgb(0x55, 0x40, 0x80, 0xFF));

        static MultiCaretController()
        {
            ((SolidColorBrush)SelBrush).Freeze();
        }

        public MultiCaretController(TextEditor editor, Action<int, int> applySticky)
        {
            _editor      = editor;
            _applySticky = applySticky;

            // Save default brushes NOW (before any Hide call) so Restore always has valid values.
            _savedSelectionBrush      = editor.TextArea.SelectionBrush;
            _savedSelectionForeground = editor.TextArea.SelectionForeground;

            editor.TextArea.TextView.BackgroundRenderers.Add(this);
            editor.PreviewMouseLeftButtonDown          += OnMouseDownPreview; // fires before TextArea handlers
            editor.TextArea.PreviewMouseLeftButtonDown += OnMouseDown;
            editor.TextArea.PreviewMouseLeftButtonUp   += OnMouseUp;
            editor.TextArea.PreviewTextInput           += OnTextInput;

            _blink = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _blink.Tick += (_, _) => { _blinkOn = !_blinkOn; Redraw(); };
        }

        TextDocument Doc   => _editor.Document;
        Caret        Caret => _editor.TextArea.Caret;

        public bool Active
        {
            get { Prune(); return _carets.Count > 0; }
        }

        void Prune()
        {
            _carets.RemoveAll(c => c.Caret.IsDeleted);
            if (_carets.Count == 0) RestoreNativeSelection();
        }

        /// <summary>
        /// Returns all non-empty selections (primary + secondaries) sorted by start offset.
        /// Used by NotepadWindow for Ctrl+C / Ctrl+X multi-caret copy.
        /// </summary>
        public IReadOnlyList<(int Start, int Length)> GetAllSelections()
        {
            Prune();
            var result = new List<(int Start, int Length)>();

            if (!_editor.TextArea.Selection.IsEmpty)
            {
                var seg = _editor.TextArea.Selection.SurroundingSegment;
                if (seg.Length > 0) result.Add((seg.Offset, seg.Length));
            }

            foreach (var cs in _carets.Where(c => !c.Caret.IsDeleted && c.HasSelection))
                result.Add((cs.SelectionStart, cs.SelectionLength));

            result.Sort((a, b) => a.Start.CompareTo(b.Start));
            return result;
        }

        // ── Mouse ─────────────────────────────────────────────────────────────────

        // Registered on TextEditor (parent of TextArea) so it fires BEFORE AvalonEdit's
        // TextArea handlers — the only way to capture the primary selection before AvalonEdit clears it.
        void OnMouseDownPreview(object? sender, MouseButtonEventArgs e)
        {
            bool alt  = Keyboard.IsKeyDown(Key.LeftAlt)  || Keyboard.IsKeyDown(Key.RightAlt);
            bool ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            if (ctrl && !alt && e.ClickCount == 1)
            {
                SavePrimarySelectionAsSecondary(); // save BEFORE AvalonEdit clears it
                _ctrlSelectionMode = true;
                if (_carets.Count > 0) EnsureBlink(); // hide native brush, start rendering secondaries
            }
        }

        void SavePrimarySelectionAsSecondary()
        {
            if (_editor.TextArea.Selection.IsEmpty) return;
            var seg = _editor.TextArea.Selection.SurroundingSegment;
            if (seg.Length == 0) return;
            int caretOff  = Caret.Offset;
            int anchorOff = (caretOff == seg.Offset) ? seg.EndOffset : seg.Offset;
            _carets.Add(new CaretState
            {
                Caret  = CaretAnchor(caretOff),
                Anchor = SelectAnchor(anchorOff),
            });
        }

        void OnMouseDown(object? sender, MouseButtonEventArgs e)
        {
            bool alt  = Keyboard.IsKeyDown(Key.LeftAlt)  || Keyboard.IsKeyDown(Key.RightAlt);
            bool ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            if (alt && e.ClickCount == 1)
            {
                _altDown            = true;
                _altDownPt          = e.GetPosition(_editor.TextArea.TextView);
                _altDownCaretOffset = Caret.Offset;
                // Don't handle — let AvalonEdit position the primary caret / start rect-select.
            }
            else if (!alt && !ctrl && Active)
            {
                Clear();
            }
        }

        void OnMouseUp(object? sender, MouseButtonEventArgs e)
        {
            if (_ctrlSelectionMode)
            {
                _ctrlSelectionMode = false;
                EnsureBlink(); // finalize: hide native if secondaries exist, restore if not
                return;
            }

            if (!_altDown) return;
            _altDown = false;

            Point up     = e.GetPosition(_editor.TextArea.TextView);
            bool wasDrag = Math.Abs(up.X - _altDownPt.X) > 3 || Math.Abs(up.Y - _altDownPt.Y) > 3;

            if (wasDrag)
            {
                if (_editor.TextArea.Selection is RectangleSelection)
                {
                    // Capture data NOW before AvalonEdit's own mouse-up handler runs.
                    // Defer conversion so AvalonEdit fully cleans up its drag state first.
                    var segs = _editor.TextArea.Selection.Segments
                        .Select(s => (Start: s.StartOffset, End: s.EndOffset))
                        .ToList();
                    int caretOff = Caret.Offset;
                    _editor.Dispatcher.BeginInvoke(DispatcherPriority.Input,
                        (Action)(() => ConvertRectSegmentsToMultiCaret(segs, caretOff)));
                }
                return;
            }

            if (!_editor.TextArea.Selection.IsEmpty) return;

            int clicked  = Caret.Offset;
            int previous = _altDownCaretOffset;
            if (clicked == previous) return;

            // Alt+click on an existing secondary → toggle off
            var hit = _carets.FirstOrDefault(c => !c.Caret.IsDeleted && c.Caret.Offset == clicked);
            if (hit != null) { _carets.Remove(hit); Redraw(); EnsureBlink(); return; }

            AddCaret(previous);
            Redraw();
            EnsureBlink();
        }

        void ConvertRectSegmentsToMultiCaret(List<(int Start, int End)> segments, int originalCaret)
        {
            if (segments.Count < 2) return;

            _carets.Clear();

            // Detect which column edge the caret was on during the drag.
            bool caretAtEnd = segments.Any(s => s.End == originalCaret)
                           || !segments.Any(s => s.Start == originalCaret);

            // First segment → primary AvalonEdit selection
            var first    = segments[0];
            int f_caret  = caretAtEnd ? first.End   : first.Start;
            int f_anchor = caretAtEnd ? first.Start : first.End;

            if (f_caret != f_anchor)
                _editor.TextArea.Selection = Selection.Create(_editor.TextArea, f_anchor, f_caret);
            else
                _editor.TextArea.ClearSelection();
            Caret.Offset = f_caret;

            // Remaining segments → secondary carets
            for (int i = 1; i < segments.Count; i++)
            {
                var seg      = segments[i];
                int caretOff  = caretAtEnd ? seg.End   : seg.Start;
                int anchorOff = caretAtEnd ? seg.Start : seg.End;

                _carets.Add(new CaretState
                {
                    Caret  = CaretAnchor(caretOff),
                    Anchor = caretOff != anchorOff ? SelectAnchor(anchorOff) : null,
                });
            }

            Redraw();
            EnsureBlink();
        }

        void AddCaret(int offset)
        {
            if (_carets.Any(c => !c.Caret.IsDeleted && c.Caret.Offset == offset)) return;
            _carets.Add(new CaretState { Caret = CaretAnchor(offset) });
        }

        /// <summary>
        /// Called by NotepadWindow immediately after Doc.BeginUpdate() in EditAll,
        /// so it runs inside the undo group — allows NotepadWindow to push a
        /// FormattingRestoreOperation paired with the multi-caret text edits.
        /// </summary>
        public Action? PreEditHook { get; set; }

        // ── Public API ────────────────────────────────────────────────────────────
        public void Clear()
        {
            if (_carets.Count == 0) return;
            _carets.Clear();
            _blink.Stop();
            RestoreNativeSelection();
            Redraw();
        }

        void EnsureBlink()
        {
            Prune();
            _blinkOn = true;
            if (_carets.Count > 0)
            {
                HideNativeSelection();
                if (!_blink.IsEnabled) _blink.Start();
            }
            else
            {
                _blink.Stop();
                RestoreNativeSelection();
            }
        }

        void HideNativeSelection()
        {
            if (_nativeBrushHidden) return;
            _editor.TextArea.SelectionBrush      = null;
            _editor.TextArea.SelectionForeground = null;
            _nativeBrushHidden = true;
        }

        void RestoreNativeSelection()
        {
            if (!_nativeBrushHidden) return;
            _editor.TextArea.SelectionBrush      = _savedSelectionBrush;
            _editor.TextArea.SelectionForeground = _savedSelectionForeground;
            _nativeBrushHidden = false;
        }

        // ── Text input ────────────────────────────────────────────────────────────
        void OnTextInput(object? sender, TextCompositionEventArgs e)
        {
            if (!Active || string.IsNullOrEmpty(e.Text)) return;
            RunInsert(e.Text);
            e.Handled = true;
        }

        public void InsertNbspAll() => RunInsert(" ");

        // ── Key routing ───────────────────────────────────────────────────────────
        /// <summary>
        /// Returns true if the key was fully handled (caller must set e.Handled).
        /// Returns false if the primary should also process the key normally.
        /// Navigation keys always return false (primary moves naturally via AvalonEdit).
        /// </summary>
        public bool HandleKey(Key key)
        {
            if (!Active) return false;

            bool shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
            bool ctrl  = Keyboard.IsKeyDown(Key.LeftCtrl)  || Keyboard.IsKeyDown(Key.RightCtrl);

            switch (key)
            {
                case Key.Left:
                    if   (shift && ctrl)  ExtendAll(cs => FindPrevSelect(cs.Caret.Offset));
                    else if (shift)       ExtendAll(cs => FindPrev(cs.Caret.Offset, false));
                    else                  MoveAll  (cs => FindPrev(cs.Caret.Offset, ctrl));
                    return false;   // primary handled by AvalonEdit / NotepadWindow

                case Key.Right:
                    if   (shift && ctrl)  ExtendAll(cs => FindNextSelect(cs.Caret.Offset));
                    else if (shift)       ExtendAll(cs => FindNext(cs.Caret.Offset, false));
                    else                  MoveAll  (cs => FindNext(cs.Caret.Offset, ctrl));
                    return false;

                case Key.Up:
                    Navigate(shift, cs => MoveVertical(cs.Caret.Offset, -1));
                    return false;

                case Key.Down:
                    Navigate(shift, cs => MoveVertical(cs.Caret.Offset, +1));
                    return false;

                case Key.Home:
                    Navigate(shift, cs => HomeOf(cs.Caret.Offset));
                    return false;

                case Key.End:
                    Navigate(shift, cs => EndOf(cs.Caret.Offset));
                    return false;

                case Key.Tab:    RunInsert("\t"); return true;
                case Key.Enter:  RunInsert("\n"); return true;
                case Key.Back:   RunBackspace();  return true;
                case Key.Delete: RunDelete();     return true;

                case Key.Escape:
                    Clear();
                    return false;
            }
            return false;
        }

        // Navigate: move or extend selection for each secondary caret.
        // Primary is NOT touched here — AvalonEdit handles it (we return false from HandleKey).
        void Navigate(bool extend, Func<CaretState, int> calcNew)
        {
            Prune();
            foreach (var cs in _carets)
            {
                int to = calcNew(cs);
                if (extend)
                {
                    if (cs.Anchor == null)
                        cs.Anchor = SelectAnchor(cs.Caret.Offset);
                    cs.Caret = CaretAnchor(to);
                }
                else
                {
                    cs.Anchor = null;
                    cs.Caret  = CaretAnchor(to);
                }
            }
            Redraw();
            EnsureBlink();
        }

        void ExtendAll(Func<CaretState, int> calc) => Navigate(true,  calc);
        void MoveAll  (Func<CaretState, int> calc) => Navigate(false, calc);

        // ── Edit helpers ──────────────────────────────────────────────────────────
        void RunInsert(string text)
        {
            EditAll((offset, hadSel) =>
            {
                Doc.Insert(offset, text);
                _applySticky(offset, text.Length);
            });
        }

        void RunBackspace() => EditAll((offset, hadSel) =>
        {
            if (!hadSel && offset > 0) Doc.Remove(offset - 1, 1);
        });

        void RunDelete() => EditAll((offset, hadSel) =>
        {
            if (!hadSel && offset < Doc.TextLength) Doc.Remove(offset, 1);
        });

        /// <summary>
        /// Core edit dispatcher. Runs <paramref name="action"/>(caretOffset, hadSelection) at
        /// every caret (all secondaries + primary) inside a single undo group.
        ///
        /// Order: descending by region-start so edits at high offsets don't shift lower ones.
        /// TextAnchors auto-adjust for all movements.
        /// </summary>
        void EditAll(Action<int, bool> action)
        {
            Prune();

            // ─ Capture primary selection ─
            TextAnchor? primSelAnchor = null;
            if (!_editor.TextArea.Selection.IsEmpty)
            {
                var seg = _editor.TextArea.Selection.SurroundingSegment;
                primSelAnchor = SelectAnchor(seg.Offset);
            }
            var primCaret = CaretAnchor(Caret.Offset);

            // ─ Build flat (caretAnchor, selAnchor?) list ─
            var all = _carets
                .Select(cs => (cs.Caret, cs.Anchor))
                .Append((primCaret, primSelAnchor))
                .OrderByDescending(t =>
                {
                    // sort by the leftmost end of each cursor's affected range (descending)
                    int caretOff = t.Item1.Offset;
                    int selOff   = (t.Item2 != null && !t.Item2.IsDeleted) ? t.Item2.Offset : caretOff;
                    return Math.Min(caretOff, selOff);
                })
                .ToList();

            Doc.BeginUpdate();
            PreEditHook?.Invoke(); // NotepadWindow pushes FormattingRestoreOperation here
            try
            {
                foreach (var (caretA, selA) in all)
                {
                    bool hadSel = selA != null && !selA.IsDeleted && selA.Offset != caretA.Offset;
                    if (hadSel)
                    {
                        int start = Math.Min(caretA.Offset, selA!.Offset);
                        int len   = Math.Abs(caretA.Offset - selA.Offset);
                        Doc.Remove(start, len);
                    }
                    action(caretA.Offset, hadSel);
                }
            }
            finally { Doc.EndUpdate(); }

            // Position primary caret and force-collapse its selection.
            // Using Selection.Create instead of ClearSelection() because AvalonEdit tracks
            // its own selection anchors (BeforeInsertion/AfterInsertion) that survive the
            // delete+insert cycle and land at different offsets — leaving a 1-char selection.
            Caret.Offset = primCaret.Offset;
            _editor.TextArea.Selection = Selection.Create(
                _editor.TextArea, primCaret.Offset, primCaret.Offset);

            // Rebuild _carets — edit operations always collapse selections to carets.
            // (BeforeInsertion anchor lands at X while AfterInsertion caret lands at X+len,
            // so we'd get a 1-char residual selection; null the anchor unconditionally.)
            _carets.Clear();
            foreach (var (caretA, selA) in all)
            {
                if (ReferenceEquals(caretA, primCaret)) continue; // skip primary
                _carets.Add(new CaretState
                {
                    Caret  = caretA,
                    Anchor = null,
                });
            }

            Redraw();
            EnsureBlink();
        }

        // ── Position calculators ──────────────────────────────────────────────────

        // Movement (Ctrl+Arrow): skip word chars then skip whitespace → land at start of next word.
        int FindNext(int off, bool word)
        {
            int len = Doc.TextLength;
            if (off >= len) return len;
            if (!word) return off + 1;
            char c = Doc.GetCharAt(off);
            bool w = IsWord(c);
            int p  = off;
            while (p < len && IsWord(Doc.GetCharAt(p)) == w) p++;
            while (p < len && char.IsWhiteSpace(Doc.GetCharAt(p))) p++;
            return p;
        }

        int FindPrev(int off, bool word)
        {
            if (off <= 0) return 0;
            if (!word) return off - 1;
            int p = off - 1;
            while (p > 0 && char.IsWhiteSpace(Doc.GetCharAt(p))) p--;
            if (p <= 0) return 0;
            bool w = IsWord(Doc.GetCharAt(p));
            while (p > 0 && IsWord(Doc.GetCharAt(p - 1)) == w) p--;
            return p;
        }

        // Selection (Ctrl+Shift+Arrow): stop at end/start of word, never eat whitespace.
        // If already in whitespace, skip it first then stop at end of next word.
        int FindNextSelect(int off)
        {
            int len = Doc.TextLength;
            if (off >= len) return len;
            int p = off;
            if (char.IsWhiteSpace(Doc.GetCharAt(p)))
            {
                while (p < len && char.IsWhiteSpace(Doc.GetCharAt(p))) p++;
                if (p < len) { bool w = IsWord(Doc.GetCharAt(p)); while (p < len && !char.IsWhiteSpace(Doc.GetCharAt(p)) && IsWord(Doc.GetCharAt(p)) == w) p++; }
            }
            else
            {
                bool w = IsWord(Doc.GetCharAt(p));
                while (p < len && !char.IsWhiteSpace(Doc.GetCharAt(p)) && IsWord(Doc.GetCharAt(p)) == w) p++;
            }
            return p;
        }

        int FindPrevSelect(int off)
        {
            if (off <= 0) return 0;
            int p = off;
            if (char.IsWhiteSpace(Doc.GetCharAt(p - 1)))
            {
                while (p > 0 && char.IsWhiteSpace(Doc.GetCharAt(p - 1))) p--;
                if (p > 0) { bool w = IsWord(Doc.GetCharAt(p - 1)); while (p > 0 && !char.IsWhiteSpace(Doc.GetCharAt(p - 1)) && IsWord(Doc.GetCharAt(p - 1)) == w) p--; }
            }
            else
            {
                bool w = IsWord(Doc.GetCharAt(p - 1));
                while (p > 0 && !char.IsWhiteSpace(Doc.GetCharAt(p - 1)) && IsWord(Doc.GetCharAt(p - 1)) == w) p--;
            }
            return p;
        }

        /// <summary>Used by NotepadWindow to extend the primary cursor's word-selection.</summary>
        public int WordBoundaryForSelect(int offset, bool forward) =>
            forward ? FindNextSelect(offset) : FindPrevSelect(offset);

        static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';

        int MoveVertical(int off, int delta)
        {
            var loc   = Doc.GetLocation(off);
            int newLn = Math.Max(1, Math.Min(loc.Line + delta, Doc.LineCount));
            var dl    = Doc.GetLineByNumber(newLn);
            int col   = Math.Min(loc.Column, dl.Length + 1);
            return Doc.GetOffset(new TextLocation(newLn, col));
        }

        int HomeOf(int off)
        {
            var line     = Doc.GetLineByOffset(off);
            int firstNWS = line.Offset;
            while (firstNWS < line.EndOffset && char.IsWhiteSpace(Doc.GetCharAt(firstNWS)))
                firstNWS++;
            return off == firstNWS ? line.Offset : firstNWS;
        }

        int EndOf(int off) => Doc.GetLineByOffset(off).EndOffset;

        // ── Anchor factories ──────────────────────────────────────────────────────
        TextAnchor CaretAnchor(int off)
        {
            var a = Doc.CreateAnchor(Clamp(off));
            a.MovementType    = AnchorMovementType.AfterInsertion;
            a.SurviveDeletion = true;
            return a;
        }

        TextAnchor SelectAnchor(int off)
        {
            var a = Doc.CreateAnchor(Clamp(off));
            a.MovementType    = AnchorMovementType.BeforeInsertion;
            a.SurviveDeletion = true;
            return a;
        }

        int Clamp(int v) => Math.Max(0, Math.Min(v, Doc.TextLength));

        // ── Rendering ─────────────────────────────────────────────────────────────
        public KnownLayer Layer => KnownLayer.Caret;

        public void Draw(TextView tv, DrawingContext dc)
        {
            Prune();
            if (_carets.Count == 0)
            {
                RestoreNativeSelection();
                return;
            }
            tv.EnsureVisualLines();
            var brush = Caret.CaretBrush ?? Brushes.Black;

            // Primary selection — drawn with our brush since native SelectionBrush is suppressed
            var primSel = _editor.TextArea.Selection;
            if (!primSel.IsEmpty)
            {
                var ss = primSel.SurroundingSegment;
                if (ss.Length > 0)
                {
                    var seg = new TextSegment { StartOffset = ss.Offset, Length = ss.Length };
                    foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(tv, seg))
                        dc.DrawRectangle(SelBrush, null, r);
                }
            }

            // Secondary carets
            foreach (var cs in _carets)
            {
                if (cs.Caret.IsDeleted) continue;

                // Selection highlight
                if (cs.HasSelection)
                {
                    var seg = new TextSegment { StartOffset = cs.SelectionStart, Length = cs.SelectionLength };
                    foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(tv, seg))
                        dc.DrawRectangle(SelBrush, null, r);
                }

                // Caret line
                if (_blinkOn)
                {
                    if (CaretRect(tv, cs.Caret.Offset) is Rect r)
                        dc.DrawRectangle(brush, null, new Rect(r.X, r.Y, 1.5, r.Height));
                }
            }
        }

        static Rect? CaretRect(TextView tv, int off)
        {
            var empty = new TextSegment { StartOffset = off, Length = 0 };
            foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(tv, empty))
                return r;

            int docLen = tv.Document?.TextLength ?? 0;
            if (off < docLen)
            {
                var next = new TextSegment { StartOffset = off, Length = 1 };
                foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(tv, next))
                    return new Rect(r.X, r.Y, 0, r.Height);
            }
            if (off > 0)
            {
                var prev = new TextSegment { StartOffset = off - 1, Length = 1 };
                foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(tv, prev))
                    return new Rect(r.Right, r.Y, 0, r.Height);
            }
            return null;
        }

        void Redraw() => _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Caret);
    }
}
