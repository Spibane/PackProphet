namespace PackProphet.State;

/// <summary>
/// Persistence seam. Everything the app saves goes through here, so cloud sync later is a
/// second implementation plus conflict resolution rather than a change to any view.
/// </summary>
public interface IStateStore
{
    Task<AppState> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(AppState state, CancellationToken ct = default);
}

/// <summary>In-memory store, for tests and for a first run before storage is available.</summary>
public sealed class InMemoryStateStore : IStateStore
{
    private AppState _state;

    public InMemoryStateStore(AppState? initial = null) => _state = initial ?? AppState.Fresh();

    public Task<AppState> LoadAsync(CancellationToken ct = default) => Task.FromResult(_state);

    public Task SaveAsync(AppState state, CancellationToken ct = default)
    {
        _state = state;
        return Task.CompletedTask;
    }
}
