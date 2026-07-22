using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli;

internal sealed class SystemPlaybackControlledWindowsPhysicalRunOperations :
    IPlaybackControlledWindowsPhysicalRunOperations
{
    private static readonly ChannelId ControlledChannelId = new(
        Guid.ParseExact("71440000000000000000000000000001", "N"));
    private static readonly ChannelSessionId ControlledSessionId = new(
        Guid.ParseExact("71440000000000000000000000000002", "N"));
    private static readonly PlaybackApprovedExternalRuntimeWindowsLifecycleSnapshot EmptyLifecycle =
        new(
            PlaybackApprovedExternalRuntimeWindowsLifecycleState.Created,
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.None,
            DecoderGeneration: -1,
            DiscontinuityGeneration: 0,
            OutputReadyGeneration: -1,
            VideoWidth: 0,
            VideoHeight: 0,
            AudioSampleRate: 0,
            AudioChannelCount: 0,
            HasActiveOperation: false,
            PlaybackWindowsOutputProbeResult.Failure,
            PlaybackWindowsOutputActivationResult.Failure,
            default,
            default,
            default);

    private readonly PlaybackControlledWindowsPhysicalRunOptions _options;
    private readonly PlaybackWindowsPhysicalRunnerHost _runner;
    private PlaybackApprovedExternalRuntimeWindowsLifecycleBoundary? _lifecycle;
    private long _borrowedWindowToken;
    private bool _cleanupValidated;
    private bool _isDisposed;

    internal SystemPlaybackControlledWindowsPhysicalRunOperations(
        PlaybackControlledWindowsPhysicalRunOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options with { Runner = options.Runner with { } };
        _runner = new(_options.Runner);
    }

    public PlaybackControlledWindowsPhysicalRunOwnedSnapshot Snapshot => new(
        _runner.Snapshot,
        _lifecycle?.Snapshot ?? EmptyLifecycle);

    public ValueTask<PlaybackWindowsPhysicalRunnerResult> StartRunnerAsync(
        CancellationToken token) => _runner.StartAsync(token);

    public async ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult>
        InitializeLifecycleAsync(CancellationToken token)
    {
        var runner = _runner.Snapshot;
        if (runner.State != PlaybackWindowsPhysicalRunnerState.Running ||
            runner.WindowToken <= 0 || runner.AdmissionClosed)
        {
            return PlaybackApprovedExternalRuntimeWindowsLifecycleResult.InvalidConfiguration;
        }

        _borrowedWindowToken = runner.WindowToken;
        _lifecycle = new(
            ControlledChannelId,
            ControlledSessionId,
            _options.RuntimeDirectory,
            PlaybackFfmpegExternalRuntimeManifest.BuiltInCandidate,
            new PlaybackDecoderBackendPolicy
            {
                AllowThirdParty = true,
                AllowNativeCode = true,
                AllowBundled = false,
            },
            _runner,
            runner.WindowToken,
            CreateLifecycleOptions(_options.AudioBufferDuration));
        return await _lifecycle.InitializeAsync(token).ConfigureAwait(false);
    }

    internal static PlaybackApprovedExternalRuntimeWindowsLifecycleOptions CreateLifecycleOptions(
        TimeSpan audioBufferDuration) => new()
        {
            AudioBufferDuration = audioBufferDuration,
            Generation = new()
            {
                Scheduler = new()
                {
                    Capacity = PlaybackSchedulerOptions.DefaultCapacity,
                    MaximumOwnedBytes = PlaybackSchedulerOptions.DefaultMaximumOwnedBytes,
                    FullPolicy = PlaybackSchedulerFullPolicy.DropOldest,
                },
            },
        };

    public ValueTask<PcpControlledFlvFixtureReplayReport> ReplayFixtureAsync(
        CancellationToken token)
    {
        var lifecycle = GetLifecycle();
        return PcpControlledFlvFixtureReplayBoundary.ReplayAsync(
            new()
            {
                FixturePath = _options.FixturePath,
                ExpectedSha256 = _options.FixtureSha256,
                ChannelId = ControlledChannelId,
                SessionId = ControlledSessionId,
                MaximumFixtureBytes = _options.MaximumFixtureBytes,
                PacketPayloadBytes = _options.PacketPayloadBytes,
            },
            async (packet, cancellationToken) =>
                await lifecycle.ConsumeAsync(packet, cancellationToken).ConfigureAwait(false) ==
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.PacketAccepted,
            token);
    }

    public ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> CompleteInputAsync(
        CancellationToken token) => GetLifecycle().CompleteInputAsync(token);

    public ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> ReceiveDecodedAsync(
        PlaybackBoundedDemuxMediaKind kind,
        CancellationToken token) => GetLifecycle().ReceiveDecodedAsync(kind, token);

    public ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> DrainDecodedAsync(
        PlaybackBoundedDemuxMediaKind kind,
        CancellationToken token) => GetLifecycle().DrainDecodedAsync(kind, token);

    public ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> DeliverOneAsync(
        CancellationToken token) => GetLifecycle().DeliverOneAsync(token);

    public async ValueTask StopLifecycleAsync()
    {
        if (_lifecycle is null)
        {
            return;
        }

        await _lifecycle.StopAsync().ConfigureAwait(false);
        var snapshot = _lifecycle.Snapshot;
        _cleanupValidated =
            snapshot.State == PlaybackApprovedExternalRuntimeWindowsLifecycleState.Disposed &&
            snapshot.Buffers.VideoState == PlaybackWindowsOutputBufferState.Disposed &&
            snapshot.Buffers.AudioState == PlaybackWindowsOutputBufferState.Disposed &&
            snapshot.Streams.VideoState == PlaybackWindowsOutputStreamState.Disposed &&
            snapshot.Streams.AudioState == PlaybackWindowsOutputStreamState.Disposed &&
            snapshot.Binding.VideoState == PlaybackWindowsOutputBindingState.Disposed &&
            snapshot.Binding.AudioState == PlaybackWindowsOutputBindingState.Disposed;
        if (!_cleanupValidated)
        {
            throw new InvalidOperationException(
                "The controlled Windows lifecycle did not complete output cleanup.");
        }
    }

    public async ValueTask<PlaybackControlledWindowsPhysicalRunPreservation>
        VerifyBorrowedOwnersAsync(CancellationToken token)
    {
        var before = _runner.Snapshot;
        var invocation = await _runner.InvokeAsync(
            cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(_runner.HasAccess);
            },
            token).ConfigureAwait(false);
        var after = _runner.Snapshot;
        var windowPreserved = before.State == PlaybackWindowsPhysicalRunnerState.Running &&
            after.State == PlaybackWindowsPhysicalRunnerState.Running &&
            before.WindowToken == _borrowedWindowToken &&
            after.WindowToken == _borrowedWindowToken && _borrowedWindowToken > 0 &&
            before.HasMessageLoop && after.HasMessageLoop && !after.AdmissionClosed;
        var dispatcherPreserved = invocation.Result ==
            PlaybackWindowsOutputDispatcherInvocationResult.Completed && invocation.Value;
        return new(
            windowPreserved,
            dispatcherPreserved,
            _cleanupValidated && dispatcherPreserved);
    }

    public ValueTask<PlaybackWindowsPhysicalRunnerResult> StopRunnerAsync() =>
        _runner.StopAsync();

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        if (_lifecycle is not null)
        {
            await _lifecycle.DisposeAsync().ConfigureAwait(false);
        }

        await _runner.DisposeAsync().ConfigureAwait(false);
    }

    private PlaybackApprovedExternalRuntimeWindowsLifecycleBoundary GetLifecycle() =>
        _lifecycle ?? throw new InvalidOperationException(
            "The controlled Windows lifecycle is unavailable.");
}
