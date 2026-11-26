namespace PeerCast.Nova.App;

/// <summary>One window generation. Accepted saves belong to App, not the window.</summary>
internal sealed class GuiNextNetworkSettingsViewModel
{
    private static long generations;
    private readonly object gate = new();
    private readonly IGuiNextNetworkSettingsAdapter adapter;
    private readonly Action<long>? requestRefresh;
    private bool closed;
    private long latestRequest;
    private int pending;
    private NovaApplicationNetworkPersistenceResult? lastResult;

    public GuiNextNetworkSettingsViewModel(IGuiNextNetworkSettingsAdapter adapter, Action<long>? requestRefresh = null)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        this.adapter = adapter;
        this.requestRefresh = requestRefresh;
        Generation = Interlocked.Increment(ref generations);
    }

    public long Generation { get; }

    public bool CanAcceptCallback(long generation)
    {
        lock (gate) { return !closed && generation == Generation; }
    }

    public GuiNextNetworkSettingsPresentation Refresh()
    {
        lock (gate)
        {
            if (closed) return GuiNextNetworkSettingsPresentation.Unavailable("画面は閉じています。");
            try { return GuiNextNetworkSettingsPresentation.Create(adapter.ReadSnapshot(), pending, lastResult); }
            catch { return GuiNextNetworkSettingsPresentation.Unavailable("Network状態を取得できません。"); }
        }
    }

    public Task<NovaApplicationNetworkPersistenceResult> SetEnabledAsync(bool enabled)
    {
        lock (gate)
        {
            var current = Refresh();
            if (!current.CanEdit)
            {
                lastResult = new(0, enabled, NovaApplicationNetworkPersistenceStatus.Rejected,
                    closed ? NovaApplicationNetworkFailure.Closing : current.Failure);
                return Task.FromResult(lastResult);
            }

            var request = ++latestRequest;
            pending++;
            lastResult = null;
            Task<NovaApplicationNetworkPersistenceResult> operation;
            try { operation = adapter.SetAutoPortMappingAsync(enabled); }
            catch
            {
                operation = Task.FromResult(new NovaApplicationNetworkPersistenceResult(0, enabled,
                NovaApplicationNetworkPersistenceStatus.Failed, NovaApplicationNetworkFailure.PersistenceFailed));
            }
            return CompleteAsync(operation, request, enabled);
        }
    }

    public void Close()
    {
        lock (gate) { closed = true; }
    }

    private async Task<NovaApplicationNetworkPersistenceResult> CompleteAsync(
        Task<NovaApplicationNetworkPersistenceResult> operation, long request, bool enabled)
    {
        NovaApplicationNetworkPersistenceResult result;
        try { result = await operation.ConfigureAwait(false); }
        catch
        {
            result = new(0, enabled, NovaApplicationNetworkPersistenceStatus.Failed,
                NovaApplicationNetworkFailure.PersistenceFailed);
        }
        lock (gate)
        {
            pending--;
            if (!closed)
            {
                if (request == latestRequest) lastResult = result;
                // Native callback only posts a generation-tagged message. It
                // never touches controls from this completion thread.
                try { requestRefresh?.Invoke(Generation); }
                catch { /* A closed native message sink must not fault an accepted save. */ }
            }
        }
        return result;
    }
}
