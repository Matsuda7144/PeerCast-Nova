namespace PeerCast.Nova.App;

/// <summary>
/// Application lifetime owns one publisher through its lifecycle boundary. The
/// GUI receives borrowed status and application commands, never the runtime.
/// </summary>
internal sealed class NovaGuiNextPublishingComposition : IAsyncDisposable
{
    private NovaGuiNextPublishingComposition(INovaObsPublishingRuntime? owner)
    {
        Lifecycle = new NovaObsPublishingLifecycleController(owner);
        Commands = new NovaGuiNextPublishingCommands(Lifecycle);
    }

    internal INovaObsPublishingLifecycleController Lifecycle { get; }
    internal INovaObsPublishingReadOnlyStatus Status => Lifecycle;
    internal NovaGuiNextPublishingCommands Commands { get; }

    internal static NovaGuiNextPublishingComposition Create(INovaObsPublishingRuntimeFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        try { return new(factory.Create()); }
        catch { return new(null); } // Fixed Unavailable presentation; no exception text.
    }

    public ValueTask DisposeAsync() => Commands.DisposeAsync();
}
