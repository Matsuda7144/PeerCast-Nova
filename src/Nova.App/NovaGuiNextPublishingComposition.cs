namespace PeerCast.Nova.App;

/// <summary>
/// Application lifetime owns one publisher through its lifecycle boundary. The
/// GUI receives only borrowed status, never a runtime or Start/Stop command.
/// </summary>
internal sealed class NovaGuiNextPublishingComposition : IAsyncDisposable
{
    private NovaGuiNextPublishingComposition(INovaObsPublishingRuntime? owner)
    {
        Lifecycle = new NovaObsPublishingLifecycleController(owner);
    }

    internal INovaObsPublishingLifecycleController Lifecycle { get; }
    internal INovaObsPublishingReadOnlyStatus Status => Lifecycle;

    internal static NovaGuiNextPublishingComposition Create(INovaObsPublishingRuntimeFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        try { return new(factory.Create()); }
        catch { return new(null); } // Fixed Unavailable presentation; no exception text.
    }

    public ValueTask DisposeAsync() => Lifecycle.DisposeAsync();
}
