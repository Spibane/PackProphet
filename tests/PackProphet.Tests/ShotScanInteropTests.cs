using System.Text.Json;
using PackProphet.Vision;

namespace PackProphet.Tests;

/// <summary>
/// The wire between wwwroot/js/cardshot.js and <see cref="ShotScan"/>.
///
/// Worth its own test because of how this boundary fails. A field name the deserialiser does not
/// recognise does not throw — it leaves a zero. A scan whose detail came back as 0 and whose
/// saturation came back as 0 reads as a screenshot full of blank slots, which is a plausible
/// outcome and a silent one: every card would be reported missing. So the JSON below is real output
/// from the module, pasted verbatim, and the assertions are that every number arrived.
/// </summary>
public class ShotScanInteropTests
{
    /// <summary>
    /// What Blazor's JS interop uses. Named here rather than assumed, since the whole point is that
    /// the C# side reads what JavaScript actually sends.
    /// </summary>
    private static readonly JsonSerializerOptions Interop = new(JsonSerializerDefaults.Web);

    /// <summary>One cell of genuine output, including the fields C# has no property for.</summary>
    private const string Json = """
    {
      "ok": true,
      "width": 848,
      "height": 1402,
      "lattice": {
        "rows": 4,
        "cols": 4,
        "cellWidth": 200,
        "cellHeight": 273,
        "originX": 15,
        "originY": 141,
        "confidence": 0.49201898902899527,
        "relativeCellWidth": 0.2361275088547816
      },
      "cells": [
        {
          "row": 0,
          "col": 1,
          "box": [232, 157, 183, 257],
          "hash": "270d350d2d331153b000ffc50130fcff",
          "luma": 0.4123,
          "saturation": 0.4791,
          "detail": 0.0774
        }
      ]
    }
    """;

    [Fact]
    public void EveryFieldTheReaderDependsOnSurvivesTheCrossing()
    {
        var scan = JsonSerializer.Deserialize<ShotScan>(Json, Interop);

        Assert.NotNull(scan);
        Assert.True(scan.Ok);
        Assert.Null(scan.Error);
        Assert.Equal(848, scan.Width);
        Assert.Equal(1402, scan.Height);

        var lattice = Assert.IsType<ShotLattice>(scan.Lattice);
        Assert.Equal(4, lattice.Rows);
        Assert.Equal(4, lattice.Cols);
        Assert.Equal(200, lattice.CellWidth);
        Assert.Equal(273, lattice.CellHeight);
        Assert.Equal(0.492, lattice.Confidence, 3);
        Assert.Equal(0.236, lattice.RelativeCellWidth, 3);

        var cell = Assert.Single(scan.Cells);
        Assert.Equal(0, cell.Row);
        Assert.Equal(1, cell.Col);
        Assert.Equal("270d350d2d331153b000ffc50130fcff", cell.Hash);
        Assert.Equal(0.4123, cell.Luma, 4);
        Assert.Equal(0.4791, cell.Saturation, 4);
        Assert.Equal(0.0774, cell.Detail, 4);
    }

    [Fact]
    public void TheDiagnosticFieldsAreIgnoredRatherThanRejected()
    {
        // originX, originY and the per-cell box exist so a misplaced card crop can be seen from
        // outside the module — the one bug in that file that produced confident wrong answers. C#
        // has no use for them, and must not choke on them either.
        var scan = JsonSerializer.Deserialize<ShotScan>(Json, Interop);

        Assert.NotNull(scan?.Lattice);
        Assert.Single(scan.Cells);
    }

    [Fact]
    public void AFailureCrossesAsASentenceRatherThanAnEmptyScan()
    {
        var scan = JsonSerializer.Deserialize<ShotScan>(
            """{"ok":false,"error":"That file could not be read as an image.","lattice":null,"cells":[]}""",
            Interop);

        Assert.NotNull(scan);
        Assert.False(scan.Ok);
        Assert.Equal("That file could not be read as an image.", scan.Error);
        Assert.Null(scan.Lattice);
    }

    [Fact]
    public void AHashThatCrossedAsAnEmptyStringIsNotSilentlyMatched()
    {
        // The shape of a botched crossing: a cell arrives with its numbers intact and its hash lost.
        // It has to read as unnameable, not as whatever art happens to be nearest zero.
        Assert.False(ArtHash.TryParse("", out _));
    }
}
