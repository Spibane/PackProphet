namespace PackProphet.Import;

/// <summary>
/// A minimal RFC 4180 reader, enough for the collection exports other trackers produce.
///
/// Hand-rolled rather than taken as a dependency because the input is one shape — a header row and
/// a few thousand short rows — and because the App project runs in WebAssembly, where every added
/// package is payload the user downloads before the first card is drawn.
///
/// It tolerates what real exports contain rather than what the RFC requires: a UTF-8 BOM (Excel
/// writes one), CRLF or LF line endings mixed in one file, a trailing newline, and blank lines.
/// </summary>
internal static class CsvReader
{
    /// <summary>
    /// Rows in file order, each already split into fields. Blank lines are dropped rather than
    /// yielded as a one-empty-field row, so a trailing newline does not become a phantom record
    /// the caller has to report as a bad row.
    /// </summary>
    public static List<string[]> Parse(string text)
    {
        var rows = new List<string[]>();
        if (string.IsNullOrEmpty(text)) return rows;

        // The BOM survives File/fetch reads as U+FEFF at index 0, and would otherwise become part
        // of the first header's name — which is the ID column, so detection would silently fail.
        if (text[0] == '﻿') text = text[1..];

        var fields = new List<string>();
        var field = new System.Text.StringBuilder();
        var quoted = false;
        var i = 0;

        void EndField() { fields.Add(field.ToString()); field.Clear(); }

        void EndRow()
        {
            EndField();
            if (fields.Count > 1 || fields[0].Length > 0) rows.Add(fields.ToArray());
            fields.Clear();
        }

        while (i < text.Length)
        {
            var c = text[i];

            if (quoted)
            {
                if (c == '"')
                {
                    // A doubled quote inside a quoted field is one literal quote; a single one ends
                    // the field.
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i += 2; continue; }
                    quoted = false; i++; continue;
                }
                field.Append(c); i++; continue;
            }

            switch (c)
            {
                case '"' when field.Length == 0:
                    quoted = true; i++; break;
                case ',':
                    EndField(); i++; break;
                case '\r':
                    // Consume CRLF as one terminator, and a lone CR as one too.
                    EndRow(); i += i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1; break;
                case '\n':
                    EndRow(); i++; break;
                default:
                    field.Append(c); i++; break;
            }
        }

        // A file not ending in a newline still has a final row to flush.
        if (field.Length > 0 || fields.Count > 0) EndRow();

        return rows;
    }
}
