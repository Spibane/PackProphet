using PackProphet.Vision;

namespace PackProphet.Services;

/// <summary>
/// Fetches the card type table, once, and only if something asks for a type the card detail does
/// not carry.
///
/// Deferred for the same reason <see cref="ArtHashSource"/> is, and the deferral matters more here
/// because the common case never needs it at all: the detail table carries the type for every card
/// released so far, so this is only reached in the window between a set appearing in the card data
/// and its detail being published upstream. On any ordinary visit the request is never made.
///
/// 50 KB, and precached with everything else under data/, so a first visit pays for it whether or
/// not it is read. That is the trade the committed-table approach makes; see
/// <see cref="TypeBadgeTable"/> for why it was preferred to reading the badge in the browser.
/// </summary>
public sealed class TypeBadgeSource
{
    private const string Path = "data/card-types.txt";

    private readonly HttpClient _http;
    private Task<TypeBadgeTable>? _load;

    public TypeBadgeSource(HttpClient http) => _http = http;

    /// <summary>
    /// The table as far as it has loaded, without waiting. Null until the first successful load,
    /// which is what lets a synchronous render path consult it: a row cannot await, and a type that
    /// appears a moment after the grid first paints is the same behaviour every other detail column
    /// already has.
    /// </summary>
    public TypeBadgeTable? Loaded { get; private set; }

    /// <summary>
    /// Start loading, or return the load already in flight. Memoised on the task, so a screenful of
    /// rows asking at once shares one request and a failure is remembered rather than retried per
    /// row.
    /// </summary>
    public Task<TypeBadgeTable> GetAsync(CancellationToken ct = default) => _load ??= LoadAsync(ct);

    private async Task<TypeBadgeTable> LoadAsync(CancellationToken ct)
    {
        try
        {
            var table = TypeBadgeTable.Parse(await _http.GetStringAsync(Path, ct));
            Loaded = table;
            return table;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            // Empty rather than thrown. A missing table means the type column stays blank, which is
            // exactly what it did before this existed.
            Loaded = TypeBadgeTable.Empty;
            return TypeBadgeTable.Empty;
        }
    }
}
