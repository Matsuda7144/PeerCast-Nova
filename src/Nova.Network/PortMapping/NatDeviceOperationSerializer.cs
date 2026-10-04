using System.Collections.Concurrent;

namespace PeerCast.Nova.Network.PortMapping;

/// <summary>
/// Holds a device lane until the backend task actually ends, including after
/// the reconciler's waiting budget expires. Shared across production runtimes.
/// </summary>
public sealed class NatDeviceOperationSerializer
{
    private readonly ConcurrentDictionary<NatDeviceIdentity, SemaphoreSlim> lanes = new();

    public static NatDeviceOperationSerializer Shared { get; } = new();

    public async Task<NatBackendResult> ExecuteAsync(
        NatDeviceIdentity device,
        Func<Task<NatBackendResult>> operation)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(operation);
        var lane = lanes.GetOrAdd(device, static _ => new SemaphoreSlim(1, 1));
        await lane.WaitAsync().ConfigureAwait(false);
        try
        {
            return await operation().ConfigureAwait(false);
        }
        finally
        {
            lane.Release();
        }
    }
}
