namespace PackProphet.State;

using System.IO.Compression;
using System.Text.Json;

/// <summary>
/// What a shared wishlist link carries: who wants it, what the list is called, and the wanted
/// printings — nothing about what the owner already has. A wishlist is a want-list, not a
/// collection: the person opening the link needs to know what to offer, not what the owner's
/// shelf looks like, and a bare want-list is both smaller and more private than a full
/// collection would be.
/// </summary>
public sealed record SharedWishlist(string OwnerName, string ListName, Dictionary<string, int> Wanted);

/// <summary>
/// Packs a wishlist into a URL-safe string and back, entirely client-side.
///
/// There is no server to host a short link behind, so the whole payload has to travel in the
/// link itself. A wishlist is a handful to a few dozen entries, which keeps the link short even
/// uncompressed, but it still deflates and base64url-encodes the same way a larger payload would
/// so the format has one code path regardless of size.
/// </summary>
public static class WishlistCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Encode(string ownerName, Wishlist list)
    {
        var shared = new SharedWishlist(ownerName, list.Name, list.Wanted);
        var json = JsonSerializer.SerializeToUtf8Bytes(shared, JsonOptions);

        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            deflate.Write(json);

        return ToBase64Url(output.ToArray());
    }

    /// <summary>Null for anything that does not decode — a hand-edited or truncated link, most likely.</summary>
    public static SharedWishlist? TryDecode(string? code)
    {
        if (string.IsNullOrEmpty(code)) return null;

        try
        {
            using var input = new MemoryStream(FromBase64Url(code));
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            deflate.CopyTo(output);

            return JsonSerializer.Deserialize<SharedWishlist>(output.ToArray(), JsonOptions);
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
