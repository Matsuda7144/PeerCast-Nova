namespace PeerCast.Nova.App;

/// <summary>
/// Application lifetime owns one existing, unstarted publisher. The GUI receives
/// only PR270's borrowed status interface, never a runtime or Start/Stop command.
/// </summary>
internal sealed class NovaGuiNextPublishingComposition : IAsyncDisposable
{
    private readonly INovaObsPublishingRuntime? _owner;
    private readonly object _gate = new();
    private Task? _disposeTask;

    private NovaGuiNextPublishingComposition(INovaObsPublishingRuntime? owner)
    {
        _owner = owner;
        Status = new NovaObsPublishingReadOnlyStatus(owner);
    }

    internal INovaObsPublishingReadOnlyStatus Status { get; }

    internal static NovaGuiNextPublishingComposition Create(INovaObsPublishingRuntimeFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        try { return new(factory.Create()); }
        catch { return new(null); } // Fixed Unavailable presentation; no exception text.
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        if (_owner is not null) await _owner.DisposeAsync().ConfigureAwait(false);
    }
}
