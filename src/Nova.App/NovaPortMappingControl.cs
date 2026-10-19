using PeerCast.Nova.Foundation.Configuration;
using PeerCast.Nova.Network.PortMapping;

namespace PeerCast.Nova.App;

internal interface INovaDailyPortMappingControl
{
    NovaPortMappingControl? PortMappingControl { get; }
}

/// <summary>Settings/desired-state adapter. It never sends router operations.</summary>
internal sealed class NovaPortMappingControl : IDisposable
{
    private readonly NatPortMappingManager manager;
    private readonly NovaSettingsStore store;
    private readonly SemaphoreSlim writes = new(1, 1);

    public NovaPortMappingControl(NatPortMappingManager manager, NovaSettingsStore store,
        NovaOptions savedSettings, bool startupEnvironmentOverride)
    {
        this.manager = manager;
        this.store = store;
        // Preserve the construction contract without retaining a stale write snapshot.
        _ = savedSettings;
        StartupEnvironmentOverride = startupEnvironmentOverride;
    }

    public bool StartupEnvironmentOverride { get; }

    public void Dispose() => writes.Dispose();

    public NatPortMappingDisplay ReadStatus() => NatPortMappingStatusProjection.Create(manager.ReadState());

    public async Task SetEnabledAsync(bool enabled)
    {
        // This synchronous portion runs in event order and performs no I/O or
        // router operation. The runtime independently pumps the reconciler.
        manager.SetEnabled(enabled);
        await writes.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() => store.UpdateAsync(current => current with { AutoPortMapping = enabled }))
                .ConfigureAwait(false);
        }
        finally
        {
            writes.Release();
        }
    }
}
