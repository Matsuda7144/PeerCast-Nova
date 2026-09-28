using System.Net;
using System.Runtime.ExceptionServices;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.App;

/// <summary>
/// Production composition for PCP relay-owned recording streams and file
/// recording. Recording sessions stop before relay owners release their
/// shared fan-out readers.
/// </summary>
public sealed class PcpRecordingRuntimeComposition : IAsyncDisposable
{
    private readonly object lifecycleGate = new();
    private readonly PcpRelayRuntimeComposition relayRuntime;
    private readonly RecordingRuntime recordingRuntime;
    private Task? disposeTask;

    public PcpRecordingRuntimeComposition(
        string recordingDirectory,
        int maximumRecordingSourceFactoryCount =
            RecordingStreamSourceLeaseRegistry.DefaultMaximumFactoryCount,
        int maximumActiveSessionCount =
            RecordingFileSink.DefaultMaximumActiveSessionCount,
        int maximumChannelCount =
            RecordingAutomationManager.DefaultMaximumChannelCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordingDirectory);

        relayRuntime = new PcpRelayRuntimeComposition(
            maximumRecordingSourceFactoryCount);
        recordingRuntime = new RecordingRuntime(
            relayRuntime.RecordingSourceProvider,
            new DefaultRecordingFilePathProvider(recordingDirectory),
            maximumActiveSessionCount,
            maximumChannelCount);
    }

    /// <summary>
    /// Stable manual and automatic session boundary for application callers.
    /// </summary>
    public RecordingRuntime Recording => recordingRuntime;

    /// <summary>
    /// Creates a relay owner whose fan-out is available to recording sessions.
    /// </summary>
    public PcpRelayCoordinator CreateCoordinator(
        TcpTransportConnector upstreamConnector,
        ChannelStatusTracker statusTracker,
        IPEndPoint downstreamEndPoint,
        PcpRelayCoordinatorOptions? options = null)
    {
        lock (lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(disposeTask is not null, this);
            return relayRuntime.CreateCoordinator(
                upstreamConnector,
                statusTracker,
                downstreamEndPoint,
                options);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (lifecycleGate)
        {
            disposeTask ??= DisposeCoreAsync();
            return new ValueTask(disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        List<Exception>? failures = null;

        try
        {
            await recordingRuntime.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        try
        {
            await relayRuntime.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        if (failures is { Count: 1 })
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures is { Count: > 1 })
        {
            throw new AggregateException(
                "Recording and PCP relay runtimes failed during shutdown.",
                failures);
        }

    }
}
