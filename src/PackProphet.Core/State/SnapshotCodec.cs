namespace PackProphet.State;

using System.IO.Compression;
using System.Text.Json;

/// <summary>
/// What a shared link carries: a collection and the plan it is measured against, nothing else.
/// Decks, wishlists, logs, resources and the trade board are left out — they are either
/// irrelevant to a read-only progress view or more private than a card count, and every one of
/// them would make the link longer for no benefit to the person opening it.
/// </summary>
public sealed record SharedSnapshot(string Name, Dictionary<string, int> Collection, TargetSettings Targets);

/// <summary>
/// Packs a snapshot into a URL-safe string and back, entirely client-side.
///
/// There is no server to host a short link behind, so the whole payload has to travel in the
/// link itself. A collection is mostly repeated small integers over card-name keys once
/// serialized, which deflate compresses well — Brotli would do better, but its native codec is
/// not linked into the Blazor WebAssembly runtime and throws PlatformNotSupportedException
/// there, which is the only runtime this ever has to work in. The result is base64url so it
/// survives being pasted into any query string without escaping.
///
/// The caller is expected to put the code after a `#`, not in the query string: a full
/// collection can run past what a server or proxy accepts on the request line (a 414), and a
/// fragment is never sent to a server at all.
/// </summary>
public static class SnapshotCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Encode(Profile profile)
    {
        var snapshot = new SharedSnapshot(profile.Name, profile.Collection, profile.Targets);
        var json = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);

        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            deflate.Write(json);

        return ToBase64Url(output.ToArray());
    }

    /// <summary>Null for anything that does not decode — a hand-edited or truncated link, most likely.</summary>
    public static SharedSnapshot? TryDecode(string? code)
    {
        if (string.IsNullOrEmpty(code)) return null;

        try
        {
            using var input = new MemoryStream(FromBase64Url(code));
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            deflate.CopyTo(output);

            return JsonSerializer.Deserialize<SharedSnapshot>(output.ToArray(), JsonOptions);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidDataException)
        {
            return null;
        }
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        var pad = s.Length % 4;
        if (pad != 0) s = s.PadRight(s.Length + (4 - pad), '=');
        return Convert.FromBase64String(s);
    }
}
