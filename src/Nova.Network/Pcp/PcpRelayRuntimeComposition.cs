using System.Net;
using System.Runtime.ExceptionServices;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;
using PeerCast.Nova.Network.PortMapping;
using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Minimal runtime composition for PCP relay owners and recording readers.
/// It owns one recording source registry and one provider for its lifetime;
/// it does not define recording rules, paths, sinks, or network connections.
/// </summary>
public sealed class PcpRelayRuntimeComposition : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly List<PcpRelayCoordinator> coordinators = [];
    private readonly NatPortMappingRuntime? portMappingRuntime;
    private int disposed;

    public PcpRelayRuntimeComposition(
        int maximumRecordingSourceFactoryCount =
            RecordingStreamSourceLeaseRegistry.DefaultMaximumFactoryCount,
        NatPortMappingOptions? portMappingOptions = null)
    {
        RecordingStreamSourceLeaseRegistry =
            new RecordingStreamSourceLeaseRegistry(
                maximumRecordingSourceFactoryCount);
        RecordingSourceProvider = new LeasedRecordingStreamSourceProvider(
            RecordingStreamSourceLeaseRegistry);
        if (portMappingOptions is not null)
        {
            portMappingRuntime = new NatPortMappingRuntime(portMappingOptions);
        }
    }

    public NatPortMappingManager? PortMapping => portMappingRuntime?.Manager;

    /// <summary>
    /// The one registry shared by every coordinator and the recording reader
    /// provider created by this runtime.
    /// </summary>
    public RecordingStreamSourceLeaseRegistry RecordingStreamSourceLeaseRegistry
    {
        get;
    }

    /// <summary>
    /// Recording-side consumer over the shared registry. It only acquires
    /// leases from existing relay fan-out owners.
    /// </summary>
    public LeasedRecordingStreamSourceProvider RecordingSourceProvider
    {
        get;
    }

    /// <summary>
    /// Creates a relay owner bound to this runtime's shared registry.
    /// The runtime retains ownership until it is disposed.
    /// </summary>
    public PcpRelayCoordinator CreateCoordinator(
        TcpTransportConnector upstreamConnector,
        ChannelStatusTracker statusTracker,
        IPEndPoint downstreamEndPoint,
        PcpRelayCoordinatorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(upstreamConnector);
        ArgumentNullException.ThrowIfNull(statusTracker);
        ArgumentNullException.ThrowIfNull(downstreamEndPoint);

        lock (gate)
        {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref disposed) != 0,
                this);

            var coordinator = new PcpRelayCoordinator(
                upstreamConnector,
                statusTracker,
                downstreamEndPoint,
                options,
                RecordingStreamSourceLeaseRegistry,
                PortMapping);
            coordinators.Add(coordinator);
            return coordinator;
        }
    }

    /// <summary>
    /// Stops and disposes every relay owner before the runtime lifetime ends.
    /// Owner cleanup therefore removes registrations before the shared
    /// registry becomes unreachable. Cleanup continues for all owners so a
    /// single owner failure cannot leave a stale registration in another one.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        PcpRelayCoordinator[] owned;

        lock (gate)
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            owned = [.. coordinators];
            coordinators.Clear();
        }

        List<Exception>? failures = null;

        for (var index = owned.Length - 1; index >= 0; index--)
        {
            try
            {
                await owned[index].DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (portMappingRuntime is not null)
        {
            try
            {
                await portMappingRuntime.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is { Count: 1 })
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures is { Count: > 1 })
        {
            throw new AggregateException(
                "One or more PCP relay owners failed during runtime shutdown.",
                failures);
        }

        GC.SuppressFinalize(this);
    }
}
