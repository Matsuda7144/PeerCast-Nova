using PeerCast.Nova.Core.Recording;
using PeerCast.Nova.Network.PortMapping;

namespace PeerCast.Nova.App;

// These injected boundaries cannot send packets, bind a listener, or claim
// router success. The ordinary network owner/manager still owns desired state.
internal sealed class NovaValidationNatRuntime : INovaApplicationNatRuntime
{
    private readonly NatPortMappingRuntime runtime;
    internal NovaValidationNatRuntime(NatPortMappingOptions options) =>
        runtime = new(options, new NovaValidationMappingBackend(), new NovaValidationNetworkSource());
    public NatPortMappingManager Manager => runtime.Manager;
    public NatShutdownOutcome? ShutdownOutcome => runtime.ShutdownOutcome;
    public ValueTask DisposeAsync() => runtime.DisposeAsync();
}

internal sealed class NovaValidationNetworkSource : INatNetworkSource
{
    public NatNetworkIdentity? GetCurrentNetwork() => null;
}

internal sealed class NovaValidationMappingBackend : INatMappingBackend
{
    public Task<NatBackendResult> ExecuteAsync(NatMappingRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new NatBackendResult(NatBackendOutcome.NotSent, DetailCode: "ValidationRouterBlocked"));
}

internal sealed class NovaValidationPublicRelayDriver : INovaPublicRelayDriver
{
    public Task<NovaPublicRelayEnd> Completion { get; } = Task.FromResult(new NovaPublicRelayEnd(true));
    public Task<NovaPublicRelayStartResult> StartAsync(NovaPublicRelayIdentity identity, CancellationToken cancellationToken) =>
        Task.FromResult(new NovaPublicRelayStartResult(null, NovaPublicRelayFailure.ListenFailed));
    public Task<NovaPublicRelayFailure> StopAsync() => Task.FromResult(NovaPublicRelayFailure.None);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class NovaValidationLibraryFileSystem(NovaValidationProfilePaths paths) : IGenesisRecordingLibraryFileSystem
{
    private readonly SystemGenesisRecordingLibraryFileSystem inner = new();
    public IEnumerable<GenesisRecordingLibraryDirectoryEntry> EnumerateDirectEntries(string root, CancellationToken token)
    {
        paths.Check(root);
        foreach (var entry in inner.EnumerateDirectEntries(root, token))
        {
            paths.Check(entry.Path);
            yield return entry;
        }
    }
    public GenesisRecordingLibraryFileMetadata ReadFileMetadata(string path, CancellationToken token)
    {
        paths.Check(path);
        return inner.ReadFileMetadata(path, token);
    }
}

internal sealed class NovaValidationRuleProvider(NovaValidationProfilePaths paths) : IGenesisRecordingRuleSetProvider
{
    public ValueTask<RecordingRuleSet> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        paths.CheckAll();
        return new GenesisRecordingRuleFileProvider(paths.Rules).GetSnapshotAsync(cancellationToken);
    }
}

/// <summary>
/// Network-settings validation admission gate. No media command is forwarded,
/// no manual/automatic recording capability is exposed, and no player launched.
/// Read-only History/Library use the existing isolated daily foundation.
/// </summary>
internal sealed class NovaValidationPlaybackController(GuiNextDailyPlaybackSessionController inner) :
    IPlaybackSessionController, IGenesisRecordingHistoryController, IGenesisRecordingLibraryController, IAsyncDisposable
{
    public PlaybackSessionSnapshot Snapshot => PlaybackSessionSnapshot.Stopped;
    public ValueTask<PlaybackSessionCommandOutcome> PlayAsync(string source, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new PlaybackSessionCommandOutcome(
            cancellationToken.IsCancellationRequested ? PlaybackSessionCommandResult.Cancelled : PlaybackSessionCommandResult.Rejected,
            new(PlaybackSessionState.Stopped, null, "検証専用：録画・再生は利用できません。")));
    public ValueTask<PlaybackSessionCommandOutcome> StopAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new PlaybackSessionCommandOutcome(PlaybackSessionCommandResult.Completed, Snapshot));
    public GenesisRecordingHistorySnapshot GetRecordingHistorySnapshot() =>
        ((IGenesisRecordingHistoryController)inner).GetRecordingHistorySnapshot();
    public GenesisRecordingHistoryDurabilitySnapshot? GetRecordingHistoryDurabilitySnapshot() =>
        ((IGenesisRecordingHistoryController)inner).GetRecordingHistoryDurabilitySnapshot();
    public GenesisRecordingLibrarySnapshot GetRecordingLibrarySnapshot() =>
        ((IGenesisRecordingHistoryController)inner).GetRecordingLibrarySnapshot();
    public GenesisRecordingLibrarySnapshot GetSnapshot() => ((IGenesisRecordingLibraryController)inner).GetSnapshot();
    public Task<GenesisRecordingLibraryRefreshResult> RefreshAsync(CancellationToken cancellationToken) =>
        ((IGenesisRecordingLibraryController)inner).RefreshAsync(cancellationToken);
    public ValueTask DisposeAsync() => inner.DisposeAsync();
}
