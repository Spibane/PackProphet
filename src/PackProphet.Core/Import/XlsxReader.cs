namespace PackProphet.Import;

using System.IO.Compression;
using System.Xml.Linq;

/// <summary>
/// Reads the first worksheet of an .xlsx file into rows of strings, so the same import pipeline
/// serves a workbook and a CSV.
///
/// It exists because PTCGP Tracker exports a workbook rather than a CSV, and because a file that
/// has been opened in Excel to be looked at usually comes back as one whichever way it started.
/// Telling someone to convert their file first is asking them to do the one step most likely to
/// go wrong.
///
/// Only what a collection export contains is supported: a single sheet of shared strings, inline
/// strings and numbers. Formulas are read as their cached result, and everything about styling —
/// including number formats, so a date would arrive as its serial number — is ignored. Nothing
/// here writes .xlsx.
///
/// Hand-rolled for the same reason <see cref="CsvReader"/> is: the App project ships to a browser,
/// and a spreadsheet library is a payload every user downloads to read a file most of them never
/// have. The parts of the format actually needed are two XML documents and a zip.
/// </summary>
internal static class XlsxReader
{
    private static readonly XNamespace Main =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly XNamespace Rels =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    private static readonly XNamespace DocRels =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>
    /// Rows of the first worksheet, each padded to the width of its widest cell reference so a
    /// column index means the same thing on every row.
    ///
    /// Returns an empty list for anything that is not a readable workbook, matching
    /// <see cref="CsvReader"/>: the caller reports "nothing here looks like a collection" rather
    /// than surfacing a zip or XML error nobody can act on.
    /// </summary>
    public static List<string[]> Parse(byte[] bytes)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

            var shared = SharedStrings(zip);
            var sheet = Entry(zip, FirstSheetPath(zip));
            if (sheet is null) return [];

            return Rows(XDocument.Load(sheet.Open()), shared);
        }
        catch (Exception e) when (e is InvalidDataException or System.Xml.XmlException
                                      or ArgumentException or NotSupportedException)
        {
            return [];
        }
    }

    private static ZipArchiveEntry? Entry(ZipArchive zip, string? path) =>
        path is null ? null : zip.Entries.FirstOrDefault(
            e => string.Equals(e.FullName, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Where the first sheet lives, followed through the workbook's relationships rather than
    /// assumed to be "xl/worksheets/sheet1.xml".
    ///
    /// The guess is right for most writers and wrong for enough of them to matter: sheet order in
    /// the workbook is not the order of the files, so a workbook whose first tab is sheet2.xml
    /// would silently import a different sheet. The fallback below is only reached when the
    /// relationship cannot be followed at all.
    /// </summary>
    private static string? FirstSheetPath(ZipArchive zip)
    {
        var workbook = Entry(zip, "xl/workbook.xml");
        var relsPart = Entry(zip, "xl/_rels/workbook.xml.rels");

        if (workbook is not null && relsPart is not null)
        {
            var id = XDocument.Load(workbook.Open())
                .Descendants(Main + "sheet").FirstOrDefault()
                ?.Attribute(DocRels + "id")?.Value;

            var target = id is null ? null : XDocument.Load(relsPart.Open())
                .Descendants(Rels + "Relationship")
                .FirstOrDefault(r => r.Attribute("Id")?.Value == id)
                ?.Attribute("Target")?.Value;

            if (target is { Length: > 0 })
            {
                // Targets are relative to the xl/ part that declared them, and may say so.
                target = target.TrimStart('/');
                return target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)
                    ? target
                    : "xl/" + target;
            }
        }

        return zip.Entries
            .Select(e => e.FullName)
            .FirstOrDefault(n => n.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)
                                 && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The shared string table. Text is stored once here and referenced by index from the cells,
    /// so a sheet read without it is a sheet of numbers.
    /// </summary>
    private static string[] SharedStrings(ZipArchive zip)
    {
        var part = Entry(zip, "xl/sharedStrings.xml");
        if (part is null) return [];

        return XDocument.Load(part.Open())
            .Descendants(Main + "si")
            .Select(TextOf)
            .ToArray();
    }

    /// <summary>
    /// The text of a string item. Concatenated across its runs: a cell whose text was edited in
    /// pieces, or which carries mixed formatting, is split into several &lt;r&gt; elements, and
    /// reading only the first would truncate it. Phonetic hints are dropped, since they are a
    /// reading aid rather than the value.
    /// </summary>
    private static string TextOf(XElement si) =>
        string.Concat(si.Descendants(Main + "t")
            .Where(t => t.Parent?.Name != Main + "rPh")
            .Select(t => t.Value));

    private static List<string[]> Rows(XDocument sheet, string[] shared)
    {
        var rows = new List<string[]>();

        foreach (var row in sheet.Descendants(Main + "row"))
        {
            var cells = new List<string>();

            foreach (var cell in row.Elements(Main + "c"))
            {
                // Cells are sparse: an empty cell is usually absent rather than present and blank,
                // so the column has to come from the reference ("C7") and the gap be filled in.
                // Reading them in document order instead would shift every value left of a gap.
                var column = ColumnOf(cell.Attribute("r")?.Value) ?? cells.Count;
                while (cells.Count < column) cells.Add("");

                cells.Add(ValueOf(cell, shared));
            }

            if (cells.Any(c => c.Length > 0)) rows.Add(cells.ToArray());
        }

        // A short row would otherwise make Field() read past its end and report a missing value
        // where the file has a gap. Padding once here keeps that out of every caller.
        var width = rows.Count == 0 ? 0 : rows.Max(r => r.Length);
        for (var i = 0; i < rows.Count; i++)
            if (rows[i].Length < width)
            {
                var padded = new string[width];
                rows[i].CopyTo(padded, 0);
                for (var c = rows[i].Length; c < width; c++) padded[c] = "";
                rows[i] = padded;
            }

        return rows;
    }

    /// <summary>Zero-based column from a cell reference: "A1" to 0, "AB12" to 27.</summary>
    private static int? ColumnOf(string? reference)
    {
        if (string.IsNullOrEmpty(reference)) return null;

        var column = 0;
        var letters = 0;

        foreach (var c in reference)
        {
            if (char.IsAsciiDigit(c)) break;
            if (!char.IsAsciiLetter(c)) return null;

            column = column * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
            letters++;
        }

        return letters == 0 ? null : column - 1;
    }

    private static string ValueOf(XElement cell, string[] shared)
    {
        var type = cell.Attribute("t")?.Value;

        // An inline string keeps its text in the cell instead of the shared table.
        if (type == "inlineStr")
            return cell.Element(Main + "is") is { } inline ? TextOf(inline) : "";

        var raw = cell.Element(Main + "v")?.Value ?? "";

        if (type == "s")
            return int.TryParse(raw, System.Globalization.NumberStyles.Integer,
                       System.Globalization.CultureInfo.InvariantCulture, out var i)
                   && i >= 0 && i < shared.Length
                ? shared[i]
                : "";

        // "b" is a boolean stored as 0 or 1. Spelled out, because the count column reads a
        // collected flag as text and "1" would be read as one copy either way — but a file
        // marking a card FALSE must not import as a copy.
        if (type == "b") return raw == "1" ? "true" : "false";

        return raw;
    }
}
