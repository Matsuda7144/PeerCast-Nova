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
    private readonly DefaultRecordingFilePathProvider filePathProvider;
    private readonly RecordingRuntime recordingRuntime;
    private readonly GenesisRecordingHistoryPersistenceCoordinator? historyPersistence;
    private readonly RecordingTransformDiagnosticFile? diagnostics;
    private readonly GenesisRecordingOutputDirectorySettings? outputDirectorySettings;
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
        : this(
            ResolveStaticDirectoryProvider(recordingDirectory),
            historyStore,
            maximumRecordingSourceFactoryCount,
            maximumActiveSessionCount,
            maximumChannelCount,
            observationTimeProvider,
            defaultRecordingDirectory,
            libraryFileSystem,
            diagnostics,
            outputDirectorySettings: null)
    {
    }

    internal PcpRecordingRuntimeComposition(
        Func<string> recordingDirectoryProvider,
        GenesisRecordingHistoryStore? historyStore,
        int maximumRecordingSourceFactoryCount = RecordingStreamSourceLeaseRegistry.DefaultMaximumFactoryCount,
        int maximumActiveSessionCount = RecordingFileSink.DefaultMaximumActiveSessionCount,
        int maximumChannelCount = RecordingAutomationManager.DefaultMaximumChannelCount,
        TimeProvider? observationTimeProvider = null,
        string? defaultRecordingDirectory = null,
        IGenesisRecordingLibraryFileSystem? libraryFileSystem = null,
        RecordingTransformDiagnosticFile? diagnostics = null,
        GenesisRecordingOutputDirectorySettings? outputDirectorySettings = null)
    {
        ArgumentNullException.ThrowIfNull(recordingDirectoryProvider);
        this.diagnostics = diagnostics;
        this.outputDirectorySettings = outputDirectorySettings;

        filePathProvider = new DefaultRecordingFilePathProvider(recordingDirectoryProvider);
        var startupDirectory = filePathProvider.BaseDirectory;
        relayRuntime = new PcpRelayRuntimeComposition(
            maximumRecordingSourceFactoryCount);
        recordingRuntime = new RecordingRuntime(
            relayRuntime.RecordingSourceProvider,
            filePathProvider,
            maximumActiveSessionCount,
            maximumChannelCount,
            RecordingHistory,
            observationTimeProvider,
            diagnostics);
        // No session can be admitted until construction returns. Initialize
        // persistence only after runtime argument validation has succeeded.
        if (historyStore is not null)
            historyPersistence = new(RecordingHistory, historyStore, KnownRecordingRoots);
        if (defaultRecordingDirectory is not null)
            DefaultRootRegistration = KnownRecordingRoots.Register(defaultRecordingDirectory, GenesisRecordingRootProvenance.Default);
        StartupRootRegistration = KnownRecordingRoots.Register(startupDirectory, GenesisRecordingRootProvenance.StartupRecordingDirectory);
        if (this.outputDirectorySettings is not null)
        {
            this.outputDirectorySettings.EffectiveDirectoryChanged += OnEffectiveRecordingDirectoryChanged;
        }
        RecordingLibrary = new(KnownRecordingRoots.GetSnapshot, RecordingHistory.GetSnapshot, libraryFileSystem);
    }

    private static Func<string> ResolveStaticDirectoryProvider(string recordingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordingDirectory);
        var full = Path.GetFullPath(recordingDirectory);
        return () => full;
    }

    private void OnEffectiveRecordingDirectoryChanged(string updatedDirectory) =>
        _ = KnownRecordingRoots.Register(updatedDirectory, GenesisRecordingRootProvenance.StartupRecordingDirectory);

    internal string RecordingDirectory => filePathProvider.BaseDirectory;

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
        if (outputDirectorySettings is not null)
        {
            outputDirectorySettings.EffectiveDirectoryChanged -= OnEffectiveRecordingDirectoryChanged;
        }

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
