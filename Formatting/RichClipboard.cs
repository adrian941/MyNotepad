using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace MinimalNotepad.Formatting
{
    /// <summary>
    /// Dual-format clipboard: puts plain text (for external apps) AND a rich JSON payload
    /// (MinimalNotepad.RichText.v1) on the same clipboard operation.
    /// Paste reads the rich payload when available; falls back to plain text otherwise.
    /// </summary>
    static class RichClipboard
    {
        const string FormatName = "MinimalNotepad.RichText.v1";

        // ── DTO for spans (flat record, easy JSON round-trip) ─────────────────

        record SpanDto(
            int     Start,
            int     End,
            bool    Bold,
            bool    Italic,
            bool    Underline,
            bool    Strikethrough,
            string? ForeColorHex,
            string? BackColorHex);

        record ClipData(string Text, List<SpanDto> Spans);

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Places the selected text on the clipboard as both plain-text and rich format.
        /// Returns the serialized JSON payload (can be stored in <see cref="ClipboardHistory"/>).
        /// </summary>
        public static string Copy(
            string selectedText,
            IEnumerable<FormattingManager.SpanRecord> allSpans,
            int selectionStart)
        {
            int selEnd = selectionStart + selectedText.Length;

            var dtos = allSpans
                .Where(s => s.Start < selEnd && s.End > selectionStart)
                .Select(s => new SpanDto(
                    Math.Max(0, s.Start - selectionStart),
                    Math.Min(selectedText.Length, s.End - selectionStart),
                    s.Format.Bold, s.Format.Italic,
                    s.Format.Underline, s.Format.Strikethrough,
                    s.Format.ForeColorHex, s.Format.BackColorHex))
                .Where(s => s.Start < s.End)
                .ToList();

            var json = JsonSerializer.Serialize(new ClipData(selectedText, dtos));

            var dataObj = new DataObject();
            dataObj.SetText(selectedText);          // plain text for any app
            dataObj.SetData(FormatName, json);      // rich payload for MinimalNotepad
            Clipboard.SetDataObject(dataObj);
            return json;
        }

        /// <summary>
        /// Returns (text, spans) from clipboard.
        /// Spans are non-null only when the source was MinimalNotepad itself.
        /// Start/End are relative to the pasted text (0 = first char of paste).
        /// </summary>
        public static (string? Text, List<FormattingManager.SpanRecord>? Spans) Paste()
        {
            var dataObj = Clipboard.GetDataObject();
            if (dataObj == null) return (null, null);

            // Try rich payload first
            if (dataObj.GetDataPresent(FormatName))
            {
                var json = dataObj.GetData(FormatName) as string;
                if (json != null)
                {
                    try
                    {
                        var clip = JsonSerializer.Deserialize<ClipData>(json);
                        if (clip != null)
                        {
                            var spans = clip.Spans.Select(s =>
                                new FormattingManager.SpanRecord(
                                    s.Start, s.End,
                                    new TextFormatting
                                    {
                                        Bold          = s.Bold,
                                        Italic        = s.Italic,
                                        Underline     = s.Underline,
                                        Strikethrough = s.Strikethrough,
                                        ForeColorHex  = s.ForeColorHex,
                                        BackColorHex  = s.BackColorHex
                                    })).ToList();
                            return (clip.Text, spans);
                        }
                    }
                    catch { }
                }
            }

            // Fallback: plain text from any external app
            if (dataObj.GetDataPresent(DataFormats.UnicodeText))
                return (dataObj.GetData(DataFormats.UnicodeText) as string, null);
            if (dataObj.GetDataPresent(DataFormats.Text))
                return (dataObj.GetData(DataFormats.Text) as string, null);

            return (null, null);
        }

        /// <summary>
        /// Deserializes spans from a previously captured rich JSON payload.
        /// Returns null on failure or if <paramref name="richJson"/> is null.
        /// </summary>
        public static List<FormattingManager.SpanRecord>? DeserializeSpans(string? richJson)
        {
            if (richJson == null) return null;
            try
            {
                var clip = JsonSerializer.Deserialize<ClipData>(richJson);
                if (clip == null) return null;
                return clip.Spans.Select(s =>
                    new FormattingManager.SpanRecord(
                        s.Start, s.End,
                        new TextFormatting
                        {
                            Bold          = s.Bold,
                            Italic        = s.Italic,
                            Underline     = s.Underline,
                            Strikethrough = s.Strikethrough,
                            ForeColorHex  = s.ForeColorHex,
                            BackColorHex  = s.BackColorHex
                        })).ToList();
            }
            catch { return null; }
        }

        /// <summary>
        /// Trims leading/trailing whitespace (and any spans covering only that whitespace)
        /// from a (text, richJson) pair. Returns the trimmed text and re-serialized JSON.
        /// If text is all whitespace, returns ("", null).
        /// </summary>
        public static (string TrimmedText, string? TrimmedJson) TrimRich(string text, string? richJson)
        {
            int trimStart = text.Length - text.TrimStart().Length;
            int trimEnd   = text.Length - text.TrimEnd().Length;
            string trimmed = text.Trim();

            if (trimmed.Length == 0) return ("", null);
            if (trimStart == 0 && trimEnd == 0) return (text, richJson);  // nothing to do

            if (richJson == null) return (trimmed, null);

            try
            {
                var clip = JsonSerializer.Deserialize<ClipData>(richJson);
                if (clip == null) return (trimmed, null);

                int newLen = trimmed.Length;
                var shifted = clip.Spans
                    .Select(s => s with
                    {
                        Start = Math.Max(0, s.Start - trimStart),
                        End   = Math.Min(newLen, s.End - trimStart)
                    })
                    .Where(s => s.Start < s.End)   // drop spans fully in trimmed region
                    .ToList();

                var newJson = JsonSerializer.Serialize(new ClipData(trimmed, shifted));
                return (trimmed, newJson);
            }
            catch { return (trimmed, null); }
        }
    }
}

