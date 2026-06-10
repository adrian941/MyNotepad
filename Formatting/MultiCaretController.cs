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
        readonly Action<int, int> _applySticky;
        readonly List<CaretState> _carets = new();
        readonly DispatcherTimer  _blink;
        bool _blinkOn = true;


        bool  _altDown;
        Point _altDownPt;
        int   _altDownCaretOffset;

        // Ctrl+drag selection mode
        bool       _ctrlSelectionMode;
        int        _ctrlDragStartOff;

        // Tracks the user's explicitly-dragged primary selection (set after Ctrl+drag / Alt+drag).
        // Persists through Find navigation so we draw/scope the RIGHT region, not the Find match.
        CaretState? _userPrimaryCS;

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


            editor.TextArea.TextView.BackgroundRenderers.Add(this);
            editor.PreviewMouseLeftButtonDown          += OnMouseDownPreview; // fires BEFORE TextArea handlers
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
            get { Prune(); return _ctrlSelectionMode || _carets.Count > 0 || _userPrimaryCS != null; }
        }

        void Prune()
        {
            _carets.RemoveAll(c => c.Caret.IsDeleted);
            if (_userPrimaryCS?.Caret.IsDeleted == true) _userPrimaryCS = null;
            SyncNativeSelectionBrush();
        }

        /// <summary>
        /// Returns all non-empty selections (primary + secondaries) sorted by start offset.
        /// Used for Ctrl+C / Ctrl+X multi-caret copy/cut.
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

        /// <summary>
        /// Returns only the user-explicitly-dragged selections (Ctrl+drag / Alt+drag),
        /// independent of Find navigation that may have replaced the primary AvalonEdit selection.
        /// Used for "Find in Selection" / "Style in Selection" scope.
        /// </summary>
        public IReadOnlyList<(int Start, int Length)> GetUserMultiSelections()
        {
            Prune();
            var result = new List<(int Start, int Length)>();
            if (_userPrimaryCS != null && !_userPrimaryCS.Caret.IsDeleted && _userPrimaryCS.HasSelection)
                result.Add((_userPrimaryCS.SelectionStart, _userPrimaryCS.SelectionLength));
            foreach (var cs in _carets.Where(c => !c.Caret.IsDeleted && c.HasSelection))
                result.Add((cs.SelectionStart, cs.SelectionLength));
            result.Sort((a, b) => a.Start.CompareTo(b.Start));
            return result;
        }

        // ── Mouse ─────────────────────────────────────────────────────────────────

        // On TextEditor (parent) — fires BEFORE AvalonEdit's TextArea handlers.
        // Only Ctrl+drag entry point: we need to intercept BEFORE AvalonEdit does word-select.
        void OnMouseDownPreview(object? sender, MouseButtonEventArgs e)
        {
            bool alt  = Keyboard.IsKeyDown(Key.LeftAlt)  || Keyboard.IsKeyDown(Key.RightAlt);
            bool ctrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            if (ctrl && !alt && e.ClickCount == 1)
            {
                SavePrimarySelectionAsSecondary(); // capture BEFORE AvalonEdit clears it
                e.Handled = true;                 // prevent AvalonEdit's Ctrl+click word-select

                var tv  = _editor.TextArea.TextView;
                int off = OffsetFromPoint(tv, e.GetPosition(tv));
                _ctrlDragStartOff  = off;
                _ctrlSelectionMode = true;

                _editor.TextArea.Caret.Offset = off;
                _editor.TextArea.Selection    = Selection.Create(_editor.TextArea, off, off);
                _editor.TextArea.CaptureMouse();
                _editor.TextArea.MouseMove   += OnCtrlMouseMove;
                SyncNativeSelectionBrush(); // hide now — Draw() renders the in-progress drag instead
            }
        }

        void OnCtrlMouseMove(object? sender, MouseEventArgs e)
        {
            if (!_ctrlSelectionMode) return;
            var tv  = _editor.TextArea.TextView;
            int off = OffsetFromPoint(tv, e.GetPosition(tv));
            _editor.TextArea.Selection    = Selection.Create(_editor.TextArea, _ctrlDragStartOff, off);
            _editor.TextArea.Caret.Offset = off;
            Redraw();
        }

        int OffsetFromPoint(TextView tv, Point pt)
        {
            tv.EnsureVisualLines();
            var pos = tv.GetPosition(pt + tv.ScrollOffset);
            if (pos.HasValue)
                return _editor.Document.GetOffset(pos.Value.Line, pos.Value.Column);
            return pt.Y < 0 ? 0 : _editor.Document.TextLength;
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
                _editor.TextArea.MouseMove -= OnCtrlMouseMove;
                _editor.TextArea.ReleaseMouseCapture();
                _ctrlSelectionMode = false;

                // Save the drag result as the user's explicit primary selection.
                // This persists through Find navigation (which replaces _editor.TextArea.Selection).
                var sel = _editor.TextArea.Selection;
                if (!sel.IsEmpty && sel.SurroundingSegment.Length > 0)
                {
                    var ss = sel.SurroundingSegment;
                    int caretOff  = Caret.Offset;
                    int anchorOff = (caretOff == ss.Offset) ? ss.EndOffset : ss.Offset;
                    _userPrimaryCS = new CaretState
                    {
                        Caret  = CaretAnchor(caretOff),
                        Anchor = SelectAnchor(anchorOff),
                    };
                }
                EnsureBlink();
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
            _userPrimaryCS = null;

            bool caretAtEnd = segments.Any(s => s.End == originalCaret)
                           || !segments.Any(s => s.Start == originalCaret);

            // First segment → primary AvalonEdit selection + save as _userPrimaryCS
            var first    = segments[0];
            int f_caret  = caretAtEnd ? first.End   : first.Start;
            int f_anchor = caretAtEnd ? first.Start : first.End;

            if (f_caret != f_anchor)
            {
                _editor.TextArea.Selection = Selection.Create(_editor.TextArea, f_anchor, f_caret);
                _userPrimaryCS = new CaretState
                {
                    Caret  = CaretAnchor(f_caret),
                    Anchor = SelectAnchor(f_anchor),
                };
            }
            else
            {
                _editor.TextArea.ClearSelection();
            }
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

        public Action? PreEditHook { get; set; }

        // ── Public API ────────────────────────────────────────────────────────────
        public void Clear()
        {
            _carets.Clear();
            _userPrimaryCS = null;
            _blink.Stop();
            SyncNativeSelectionBrush();
            Redraw();
        }

        void EnsureBlink()
        {
            _carets.RemoveAll(c => c.Caret.IsDeleted);
            if (_userPrimaryCS?.Caret.IsDeleted == true) _userPrimaryCS = null;
            _blinkOn = true;
            bool anyActive = _ctrlSelectionMode || _carets.Count > 0 || _userPrimaryCS != null;
            if (anyActive)
            {
                if (!_blink.IsEnabled) _blink.Start();
            }
            else
            {
                _blink.Stop();
            }
            SyncNativeSelectionBrush();
        }

        /// <summary>
        /// The single authoritative point that decides whether AvalonEdit's native selection
        /// brush should be suppressed. No boolean flag to desync — the decision is recomputed
        /// from live state every call. Called after every state change AND from Draw(), so even
        /// if another subsystem (e.g. Find) leaves a stale value, the next render self-heals.
        ///
        /// KEY: AvalonEdit's default blue comes from the control template's STYLE setter, not a
        /// local value (a fresh TextArea.SelectionBrush is null). To hide we set a LOCAL
        /// Transparent (local value wins over the style setter). To restore we must ClearValue()
        /// — NOT set null — so the property falls back to the style setter's blue again. Setting
        /// null locally would permanently override the style and lose the selection color.
        ///
        /// Sentinel = Brushes.Transparent. We only ever clear when the brush is STILL our
        /// sentinel — so we never clobber a brush another owner (Find's orange) legitimately set.
        /// </summary>
        void SyncNativeSelectionBrush()
        {
            bool needHidden = _ctrlSelectionMode || _carets.Count > 0 || _userPrimaryCS != null;
            var cur = _editor.TextArea.SelectionBrush;

            if (needHidden)
            {
                if (!ReferenceEquals(cur, Brushes.Transparent))
                    _editor.TextArea.SelectionBrush = Brushes.Transparent;
                // Null foreground so SelectionForegroundOverride skips — text keeps its own color.
                if (_editor.TextArea.SelectionForeground != null)
                    _editor.TextArea.SelectionForeground = null;
            }
            else
            {
                // Revert to the style/template defaults ONLY if it's still our sentinel.
                if (ReferenceEquals(cur, Brushes.Transparent))
                {
                    _editor.TextArea.ClearValue(TextArea.SelectionBrushProperty);
                    _editor.TextArea.ClearValue(TextArea.SelectionForegroundProperty);
                }
            }
        }

        // ── Text input ────────────────────────────────────────────────────────────
        void OnTextInput(object? sender, TextCompositionEventArgs e)
        {
            if (!Active || string.IsNullOrEmpty(e.Text)) return;
            RunInsert(e.Text);
            e.Handled = true;
        }

        public void InsertNbspAll() => RunInsert(" ");

        // ── Key routing ───────────────────────────────────────────────────────────
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
                    return false;

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

        void EditAll(Action<int, bool> action)
        {
            Prune();

            TextAnchor? primSelAnchor = null;
            if (!_editor.TextArea.Selection.IsEmpty)
            {
                var seg = _editor.TextArea.Selection.SurroundingSegment;
                primSelAnchor = SelectAnchor(seg.Offset);
            }
            var primCaret = CaretAnchor(Caret.Offset);

            var all = _carets
                .Select(cs => (cs.Caret, cs.Anchor))
                .Append((primCaret, primSelAnchor))
                .OrderByDescending(t =>
                {
                    int caretOff = t.Item1.Offset;
                    int selOff   = (t.Item2 != null && !t.Item2.IsDeleted) ? t.Item2.Offset : caretOff;
                    return Math.Min(caretOff, selOff);
                })
                .ToList();

            Doc.BeginUpdate();
            PreEditHook?.Invoke();
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

            Caret.Offset = primCaret.Offset;
            _editor.TextArea.Selection = Selection.Create(
                _editor.TextArea, primCaret.Offset, primCaret.Offset);

            // After any edit, selections collapse to plain carets — clear the user primary too.
            _userPrimaryCS = null;

            _carets.Clear();
            foreach (var (caretA, selA) in all)
            {
                if (ReferenceEquals(caretA, primCaret)) continue;
                _carets.Add(new CaretState { Caret = caretA, Anchor = null });
            }

            Redraw();
            EnsureBlink();
        }

        // ── Position calculators ──────────────────────────────────────────────────

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
            Prune(); // also runs SyncNativeSelectionBrush() — self-heals a stale native brush
            bool anyActive = _ctrlSelectionMode || _carets.Count > 0 || _userPrimaryCS != null;
            if (!anyActive) return;
            tv.EnsureVisualLines();
            var brush = Caret.CaretBrush ?? Brushes.Black;

            // Primary selection highlight.
            // During active Ctrl+drag: draw the live in-progress selection.
            // After a drag: draw _userPrimaryCS (persists through Find navigation).
            // Plain Alt+drag multi-caret (no _userPrimaryCS): draw AvalonEdit's selection.
            if (_ctrlSelectionMode)
            {
                DrawAvalonSel(tv, dc);
            }
            else if (_userPrimaryCS != null && !_userPrimaryCS.Caret.IsDeleted && _userPrimaryCS.HasSelection)
            {
                var seg = new TextSegment { StartOffset = _userPrimaryCS.SelectionStart, Length = _userPrimaryCS.SelectionLength };
                foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(tv, seg))
                    dc.DrawRectangle(SelBrush, null, r);
            }
            else
            {
                DrawAvalonSel(tv, dc);
            }

            // Secondary carets
            foreach (var cs in _carets)
            {
                if (cs.Caret.IsDeleted) continue;

                if (cs.HasSelection)
                {
                    var seg = new TextSegment { StartOffset = cs.SelectionStart, Length = cs.SelectionLength };
                    foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(tv, seg))
                        dc.DrawRectangle(SelBrush, null, r);
                }

                if (_blinkOn)
                {
                    if (CaretRect(tv, cs.Caret.Offset) is Rect r)
                        dc.DrawRectangle(brush, null, new Rect(r.X, r.Y, 1.5, r.Height));
                }
            }
        }

        void DrawAvalonSel(TextView tv, DrawingContext dc)
        {
            var primSel = _editor.TextArea.Selection;
            if (primSel.IsEmpty) return;
            var ss = primSel.SurroundingSegment;
            if (ss.Length == 0) return;
            var seg = new TextSegment { StartOffset = ss.Offset, Length = ss.Length };
            foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(tv, seg))
                dc.DrawRectangle(SelBrush, null, r);
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
