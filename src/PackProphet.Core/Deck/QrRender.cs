using System.Text;
using QRCoder;

namespace PackProphet.Deck;

/// <summary>
/// Renders a deck share code as a scannable QR image.
///
/// The SVG is built from QRCoder's raw module matrix rather than with its own SVG helper, which
/// pulls in a drawing dependency. The output is a handful of rects that stay crisp at any zoom,
/// since the user holds a phone up to it for the game's scanner.
///
/// Colours are hardcoded black on white and do not follow the app theme: a scanner needs the
/// contrast and the standard polarity, so a dark-mode inversion would stop the code being readable.
/// </summary>
public static class QrRender
{
    /// <summary>
    /// An SVG document for <paramref name="text"/>, or null if it cannot be encoded.
    ///
    /// Error correction is Q (~25%) rather than the usual M: this gets photographed off a
    /// screen at an angle, and the extra redundancy costs only a slightly denser code.
    /// </summary>
    public static string? Svg(string text, int quietZone = 2)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        try
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
            return Render(data, quietZone);
        }
        catch (Exception)
        {
            // An over-long payload is the only realistic failure, and the base64 code is shown
            // beside the image regardless.
            return null;
        }
    }

    private static string Render(QRCodeData data, int quietZone)
    {
        var modules = data.ModuleMatrix.Count;
        var size = modules + (quietZone * 2);

        // One <rect> per run of dark modules, not per module: a version-6 code is 41x41, and
        // 1,681 individual rects is an order of magnitude more markup than the runs need.
        var sb = new StringBuilder();
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 ")
          .Append(size).Append(' ').Append(size)
          .Append("\" shape-rendering=\"crispEdges\" role=\"img\" aria-label=\"Deck share code\">")
          .Append("<rect width=\"100%\" height=\"100%\" fill=\"#fff\"/>")
          .Append("<g fill=\"#000\">");

        for (var y = 0; y < modules; y++)
        {
            var row = data.ModuleMatrix[y];
            var x = 0;
            while (x < modules)
            {
                if (!row[x]) { x++; continue; }

                var start = x;
                while (x < modules && row[x]) x++;

                sb.Append("<rect x=\"").Append(start + quietZone)
                  .Append("\" y=\"").Append(y + quietZone)
                  .Append("\" width=\"").Append(x - start)
                  .Append("\" height=\"1\"/>");
            }
        }

        return sb.Append("</g></svg>").ToString();
    }
}
