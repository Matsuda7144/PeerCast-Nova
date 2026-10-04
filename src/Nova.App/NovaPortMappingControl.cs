using PeerCast.Nova.Foundation.Configuration;
using PeerCast.Nova.Network.PortMapping;

namespace PeerCast.Nova.App;

internal interface INovaDailyPortMappingControl
{
    NovaPortMappingControl? PortMappingControl { get; }
}

/// <summary>Settings/desired-state adapter. It never sends router operations.</summary>
internal sealed class NovaPortMappingControl(
    NatPortMappingManager manager,
    NovaSettingsStore store,
    NovaOptions savedSettings,
    bool startupEnvironmentOverride) : IDisposable
{
    private readonly SemaphoreSlim writes = new(1, 1);
    private NovaOptions settings = savedSettings;

    public bool StartupEnvironmentOverride { get; } = startupEnvironmentOverride;

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
            var updated = settings with { AutoPortMapping = enabled };
            await Task.Run(() => store.SaveAsync(updated)).ConfigureAwait(false);
            settings = updated;
        }
        finally
        {
            writes.Release();
        }
    }
}
