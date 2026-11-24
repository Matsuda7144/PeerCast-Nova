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
    private readonly GenesisRecordingHistoryPersistenceCoordinator? historyPersistence;
    private readonly RecordingTransformDiagnosticFile? diagnostics;
    private readonly HashSet<Task> coordinatorReleaseTasks = [];
    private Task? disposeTask;

    public PcpRecordingRuntimeComposition(
        string recordingDirectory,
        int maximumRecordingSourceFactoryCount =
            RecordingStreamSourceLeaseRegistry.DefaultMaximumFactoryCount,
        int maximumActiveSessionCount =
            RecordingFileSink.DefaultMaximumActiveSessionCount,
        int maximumChannelCount =
            RecordingAutomationManager.DefaultMaximumChannelCount,
        TimeProvider? observationTimeProvider = null)
        : this(recordingDirectory, null, maximumRecordingSourceFactoryCount,
            maximumActiveSessionCount, maximumChannelCount, observationTimeProvider)
    {
    }

    internal PcpRecordingRuntimeComposition(
        string recordingDirectory,
        GenesisRecordingHistoryStore? historyStore,
        int maximumRecordingSourceFactoryCount = RecordingStreamSourceLeaseRegistry.DefaultMaximumFactoryCount,
        int maximumActiveSessionCount = RecordingFileSink.DefaultMaximumActiveSessionCount,
        int maximumChannelCount = RecordingAutomationManager.DefaultMaximumChannelCount,
        TimeProvider? observationTimeProvider = null,
        string? defaultRecordingDirectory = null,
        IGenesisRecordingLibraryFileSystem? libraryFileSystem = null,
        RecordingTransformDiagnosticFile? diagnostics = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordingDirectory);
        this.diagnostics = diagnostics;

        relayRuntime = new PcpRelayRuntimeComposition(
            maximumRecordingSourceFactoryCount);
        recordingRuntime = new RecordingRuntime(
            relayRuntime.RecordingSourceProvider,
            new DefaultRecordingFilePathProvider(RecordingDirectory = Path.GetFullPath(recordingDirectory)),
            maximumActiveSessionCount,
            maximumChannelCount,
            RecordingHistory,
            observationTimeProvider,
            diagnostics);
        // No session can be admitted until construction returns. Initialize
        // persistence only after runtime argument validation has succeeded.
        if (historyStore is not null)
            historyPersistence = new(RecordingHistory, historyStore, KnownRecordingRoots);
        // Restore first, then seed trusted startup values. Never inspect the
        // mutable saved-next-start presentation or access these directories.
        if (defaultRecordingDirectory is not null)
            DefaultRootRegistration = KnownRecordingRoots.Register(defaultRecordingDirectory, GenesisRecordingRootProvenance.Default);
        StartupRootRegistration = KnownRecordingRoots.Register(RecordingDirectory, GenesisRecordingRootProvenance.StartupRecordingDirectory);
        RecordingLibrary = new(KnownRecordingRoots.GetSnapshot, RecordingHistory.GetSnapshot, libraryFileSystem);
    }

    internal string RecordingDirectory { get; }

    internal GenesisRecordingHistoryReadModel RecordingHistory { get; } = new();

    internal GenesisRecordingLibraryKnownRootRegistry KnownRecordingRoots { get; } = new();

    internal GenesisRecordingLibraryDiscovery RecordingLibrary { get; }

    internal GenesisRecordingRootRegistrationResult? DefaultRootRegistration { get; }

    internal GenesisRecordingRootRegistrationResult StartupRootRegistration { get; }

    internal GenesisRecordingHistoryDurabilitySnapshot? GetHistoryDurabilitySnapshot() => historyPersistence?.GetSnapshot();

    /// <summary>
    /// Stable manual and automatic session boundary for application callers.
    /// </summary>
    public RecordingRuntime Recording => recordingRuntime;

    internal PcpRelayRuntimeComposition RelayRuntime => relayRuntime;

    /// <summary>
    /// Creates a relay owner whose fan-out is available to recording sessions.
    /// </summary>
    public PcpRelayCoordinator CreateCoordinator(
        TcpTransportConnector upstreamConnector,
        ChannelStatusTracker statusTracker,
        IPEndPoint downstreamEndPoint,
        PcpRelayCoordinatorOptions? options = null,
        bool stopWhenNoConsumers = false,
        IPcpDownstreamRelayAdmission? admission = null)
    {
        lock (lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(disposeTask is not null, this);
            return relayRuntime.CreateCoordinator(
                upstreamConnector,
                statusTracker,
                downstreamEndPoint,
                options,
                stopWhenNoConsumers,
                admission);
        }
    }

    public ValueTask ReleaseCoordinatorAsync(
        PcpRelayCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        lock (lifecycleGate)
        {
            if (disposeTask is not null)
            {
                return new ValueTask(disposeTask);
            }

            var releaseTask = relayRuntime
                .ReleaseCoordinatorAsync(coordinator)
                .AsTask();
            coordinatorReleaseTasks.Add(releaseTask);
            _ = releaseTask.ContinueWith(
                static (completed, state) =>
                {
                    var composition = (PcpRecordingRuntimeComposition)state!;
                    lock (composition.lifecycleGate)
                    {
                        composition.coordinatorReleaseTasks.Remove(completed);
                    }
                },
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return new ValueTask(releaseTask);
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

        // Recording disposal has joined terminal publication. Flush metadata
        // afterward, preserving the existing recording-before-relay ownership.
        if (historyPersistence is not null)
            await historyPersistence.ShutdownAsync().ConfigureAwait(false);

        Task[] coordinatorReleases;
        lock (lifecycleGate)
        {
            coordinatorReleases = [.. coordinatorReleaseTasks];
        }

        try
        {
            await Task.WhenAll(coordinatorReleases).ConfigureAwait(false);
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

        if (diagnostics is not null)
            await diagnostics.DisposeAsync().ConfigureAwait(false);

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
