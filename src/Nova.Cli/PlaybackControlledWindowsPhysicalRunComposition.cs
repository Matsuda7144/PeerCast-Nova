using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli;

public enum PlaybackControlledWindowsPhysicalRunState
{
    Created,
    StartingRunner,
    InitializingLifecycle,
    ReplayingFixture,
    Decoding,
    HoldingObservation,
    CapturingEvidence,
    CleaningUp,
    Completed,
    Faulted,
    Disposed,
}

public enum PlaybackControlledWindowsPhysicalRunResult
{
    None,
    Completed,
    InvalidConfiguration,
    RunnerRejected,
    LifecycleRejected,
    FixtureRejected,
    DecodeRejected,
    DeliveryRejected,
    EvidenceRejected,
    Cancelled,
    AlreadyRunning,
    Faulted,
    Disposed,
}

public sealed record PlaybackControlledWindowsPhysicalRunOptions
{
    public static readonly TimeSpan MaximumObservationHoldDuration = TimeSpan.FromSeconds(30);

    public PlaybackControlledWindowsOutputValidationKind Kind { get; init; }
    public string RuntimeDirectory { get; init; } = string.Empty;
    public string FixturePath { get; init; } = string.Empty;
    public string FixtureSha256 { get; init; } = string.Empty;
    public int MaximumFixtureBytes { get; init; } = 64 * 1024 * 1024;
    public int PacketPayloadBytes { get; init; } = 64 * 1024;
    public int MaximumDecodeOperations { get; init; } = 128;
    public int MaximumDeliveryOperations { get; init; } = 128;
    public TimeSpan ObservationHoldDuration { get; init; }
    public TimeSpan AudioBufferDuration { get; init; } = TimeSpan.FromMilliseconds(100);
    public PlaybackWindowsPhysicalRunnerOptions Runner { get; init; } = new();
}

public sealed record PlaybackControlledWindowsPhysicalRunSnapshot(
    PlaybackControlledWindowsPhysicalRunState State,
    PlaybackControlledWindowsPhysicalRunResult Result,
    PlaybackControlledWindowsOutputValidationKind Kind,
    string FixtureSha256,
    long FixtureByteCount,
    int FixturePacketCount,
    int DecodeOperationCount,
    int DeliveryAttemptCount,
    int DeliveredCount,
    int RetainedWouldBlockCount,
    PlaybackWindowsPhysicalRunnerState RunnerState,
    PlaybackApprovedExternalRuntimeWindowsLifecycleState LifecycleState,
    PlaybackControlledWindowsOutputEvidenceResult EvidenceResult,
    bool BorrowedWindowPreserved,
    bool BorrowedDispatcherPreserved);

public sealed class PlaybackControlledWindowsPhysicalRunComposition : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly PlaybackControlledWindowsPhysicalRunOptions _options;
    private readonly IPlaybackControlledWindowsPhysicalRunOperations _operations;
    private PlaybackControlledWindowsPhysicalRunState _state =
        PlaybackControlledWindowsPhysicalRunState.Created;
    private PlaybackControlledWindowsPhysicalRunResult _result;
    private string _fixtureSha256 = string.Empty;
    private long _fixtureByteCount;
    private int _fixturePacketCount;
    private int _decodeOperationCount;
    private int _deliveryAttemptCount;
    private int _deliveredCount;
    private int _retainedWouldBlockCount;
    private PlaybackControlledWindowsOutputEvidenceResult _evidenceResult;
    private bool _borrowedWindowPreserved;
    private bool _borrowedDispatcherPreserved;
    private bool _isRunning;
    private bool _isDisposed;

    public PlaybackControlledWindowsPhysicalRunComposition(
        PlaybackControlledWindowsPhysicalRunOptions options)
        : this(options, new SystemPlaybackControlledWindowsPhysicalRunOperations(options))
    {
    }

    internal PlaybackControlledWindowsPhysicalRunComposition(
        PlaybackControlledWindowsPhysicalRunOptions options,
        IPlaybackControlledWindowsPhysicalRunOperations operations)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(operations);
        _options = options with { Runner = options.Runner with { } };
        _operations = operations;
    }

    public PlaybackControlledWindowsPhysicalRunSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                var owned = _operations.Snapshot;
                return new(
                    _state,
                    _result,
                    _options.Kind,
                    _fixtureSha256,
                    _fixtureByteCount,
                    _fixturePacketCount,
                    _decodeOperationCount,
                    _deliveryAttemptCount,
                    _deliveredCount,
                    _retainedWouldBlockCount,
                    owned.Runner.State,
                    owned.Lifecycle.State,
                    _evidenceResult,
                    _borrowedWindowPreserved,
                    _borrowedDispatcherPreserved);
            }
        }
    }

    public async ValueTask<PlaybackControlledWindowsPhysicalRunResult> RunAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_isDisposed)
                return SetResult(PlaybackControlledWindowsPhysicalRunResult.Disposed);
            if (_isRunning || _state != PlaybackControlledWindowsPhysicalRunState.Created)
                return SetResult(PlaybackControlledWindowsPhysicalRunResult.AlreadyRunning);
            if (!ValidateOptions(_options))
            {
                _state = PlaybackControlledWindowsPhysicalRunState.Faulted;
                return SetResult(PlaybackControlledWindowsPhysicalRunResult.InvalidConfiguration);
            }
            _isRunning = true;
        }

        var runnerStarted = false;
        var lifecycleStarted = false;
        try
        {
            SetState(PlaybackControlledWindowsPhysicalRunState.StartingRunner);
            var runner = await _operations.StartRunnerAsync(cancellationToken).ConfigureAwait(false);
            if (runner != PlaybackWindowsPhysicalRunnerResult.Started)
                return Terminal(runner == PlaybackWindowsPhysicalRunnerResult.Cancelled
                    ? PlaybackControlledWindowsPhysicalRunResult.Cancelled
                    : PlaybackControlledWindowsPhysicalRunResult.RunnerRejected);
            runnerStarted = true;

            SetState(PlaybackControlledWindowsPhysicalRunState.InitializingLifecycle);
            var lifecycle = await _operations.InitializeLifecycleAsync(cancellationToken)
                .ConfigureAwait(false);
            if (lifecycle != PlaybackApprovedExternalRuntimeWindowsLifecycleResult.Initialized)
                return Terminal(lifecycle == PlaybackApprovedExternalRuntimeWindowsLifecycleResult.Cancelled
                    ? PlaybackControlledWindowsPhysicalRunResult.Cancelled
                    : PlaybackControlledWindowsPhysicalRunResult.LifecycleRejected);
            lifecycleStarted = true;

            SetState(PlaybackControlledWindowsPhysicalRunState.ReplayingFixture);
            var replay = await _operations.ReplayFixtureAsync(cancellationToken)
                .ConfigureAwait(false);
            RecordReplay(replay);
            if (replay.Result != PcpControlledFlvFixtureReplayResult.Completed)
                return Terminal(replay.Result == PcpControlledFlvFixtureReplayResult.Cancelled
                    ? PlaybackControlledWindowsPhysicalRunResult.Cancelled
                    : PlaybackControlledWindowsPhysicalRunResult.FixtureRejected);

            var input = await _operations.CompleteInputAsync(cancellationToken)
                .ConfigureAwait(false);
            if (input != PlaybackApprovedExternalRuntimeWindowsLifecycleResult.EncodedInputFinalized)
                return Terminal(input == PlaybackApprovedExternalRuntimeWindowsLifecycleResult.Cancelled
                    ? PlaybackControlledWindowsPhysicalRunResult.Cancelled
                    : PlaybackControlledWindowsPhysicalRunResult.DecodeRejected);

            SetState(PlaybackControlledWindowsPhysicalRunState.Decoding);
            var delivery = await RunFiniteDecodeAsync(cancellationToken).ConfigureAwait(false);
            if (delivery != PlaybackControlledWindowsPhysicalRunResult.Completed)
                return Terminal(delivery);

            if (_options.ObservationHoldDuration > TimeSpan.Zero)
            {
                SetState(PlaybackControlledWindowsPhysicalRunState.HoldingObservation);
                await Task.Delay(_options.ObservationHoldDuration, cancellationToken)
                    .ConfigureAwait(false);
            }

            SetState(PlaybackControlledWindowsPhysicalRunState.CapturingEvidence);
            var preCleanup = _operations.Snapshot;
            if (preCleanup.Lifecycle.OutputReadyGeneration < 0 ||
                preCleanup.Lifecycle.DecoderGeneration < 0)
                return Terminal(PlaybackControlledWindowsPhysicalRunResult.EvidenceRejected);

            SetState(PlaybackControlledWindowsPhysicalRunState.CleaningUp);
            await _operations.StopLifecycleAsync().ConfigureAwait(false);
            lifecycleStarted = false;
            var owners = await _operations.VerifyBorrowedOwnersAsync(cancellationToken)
                .ConfigureAwait(false);
            lock (_sync)
            {
                _borrowedWindowPreserved = owners.WindowPreserved;
                _borrowedDispatcherPreserved = owners.DispatcherPreserved;
            }

            using var capture = new PlaybackControlledWindowsOutputCaptureBoundary(
                _options.Kind,
                _options.FixtureSha256);
            var evidence = capture.CaptureAndConsume(new(
                PlaybackFfmpegBackendCandidate.BuildId,
                _fixtureSha256,
                preCleanup.Lifecycle.DecoderGeneration,
                preCleanup.Lifecycle.OutputReadyGeneration,
                _deliveryAttemptCount,
                _deliveredCount,
                _retainedWouldBlockCount,
                HiddenRetryCount: 0,
                DeviceFallbackCount: 0,
                DeviceRecreationCount: 0,
                _options.AudioBufferDuration,
                preCleanup.Lifecycle.Buffers,
                new(
                    ProductionStopOrder: 1,
                    BufferDisposeOrder: 2,
                    StreamDisposeOrder: 3,
                    BindingDisposeOrder: 4,
                    ActivationDisposeOrder: 5,
                    OwnerThreadTeardown: owners.OwnerThreadTeardown,
                    BorrowedWindowPreserved: owners.WindowPreserved,
                    BorrowedDispatcherPreserved: owners.DispatcherPreserved)));
            lock (_sync)
                _evidenceResult = evidence;
            return evidence == PlaybackControlledWindowsOutputEvidenceResult.Accepted
                ? Complete()
                : Terminal(PlaybackControlledWindowsPhysicalRunResult.EvidenceRejected);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Terminal(PlaybackControlledWindowsPhysicalRunResult.Cancelled);
        }
        catch
        {
            return Terminal(PlaybackControlledWindowsPhysicalRunResult.Faulted);
        }
        finally
        {
            if (lifecycleStarted)
            {
                try { await _operations.StopLifecycleAsync().ConfigureAwait(false); }
                catch { }
            }
            if (runnerStarted)
            {
                try { await _operations.StopRunnerAsync().ConfigureAwait(false); }
                catch { }
            }
            lock (_sync)
                _isRunning = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_isDisposed) return;
            _isDisposed = true;
        }
        await _operations.DisposeAsync().ConfigureAwait(false);
        lock (_sync)
        {
            _state = PlaybackControlledWindowsPhysicalRunState.Disposed;
            _result = PlaybackControlledWindowsPhysicalRunResult.Disposed;
        }
    }

    private async ValueTask<PlaybackControlledWindowsPhysicalRunResult> RunFiniteDecodeAsync(
        CancellationToken cancellationToken)
    {
        var kind = _options.Kind == PlaybackControlledWindowsOutputValidationKind.Video
            ? PlaybackBoundedDemuxMediaKind.Video
            : PlaybackBoundedDemuxMediaKind.Audio;
        var draining = false;
        for (var index = 0; index < _options.MaximumDecodeOperations; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Increment(ref _decodeOperationCount);
            var result = draining
                ? await _operations.DrainDecodedAsync(kind, cancellationToken).ConfigureAwait(false)
                : await _operations.ReceiveDecodedAsync(kind, cancellationToken).ConfigureAwait(false);
            switch (result)
            {
                case PlaybackApprovedExternalRuntimeWindowsLifecycleResult.FrameTransferred:
                    return await DeliverOneAsync(cancellationToken).ConfigureAwait(false);
                case PlaybackApprovedExternalRuntimeWindowsLifecycleResult.NeedMoreInput:
                case PlaybackApprovedExternalRuntimeWindowsLifecycleResult.StreamRequiresDrain:
                    draining = true;
                    break;
                case PlaybackApprovedExternalRuntimeWindowsLifecycleResult.Cancelled:
                    return PlaybackControlledWindowsPhysicalRunResult.Cancelled;
                case PlaybackApprovedExternalRuntimeWindowsLifecycleResult.StreamEndOfStream:
                case PlaybackApprovedExternalRuntimeWindowsLifecycleResult.StreamAlreadyEnded:
                    return PlaybackControlledWindowsPhysicalRunResult.DeliveryRejected;
                default:
                    return PlaybackControlledWindowsPhysicalRunResult.DecodeRejected;
            }
        }
        return PlaybackControlledWindowsPhysicalRunResult.DecodeRejected;
    }

    private async ValueTask<PlaybackControlledWindowsPhysicalRunResult> DeliverOneAsync(
        CancellationToken cancellationToken)
    {
        if (_deliveryAttemptCount >= _options.MaximumDeliveryOperations)
            return PlaybackControlledWindowsPhysicalRunResult.DeliveryRejected;
        Increment(ref _deliveryAttemptCount);
        var result = await _operations.DeliverOneAsync(cancellationToken).ConfigureAwait(false);
        var expected = _options.Kind == PlaybackControlledWindowsOutputValidationKind.Video
            ? PlaybackApprovedExternalRuntimeWindowsLifecycleResult.VideoDelivered
            : PlaybackApprovedExternalRuntimeWindowsLifecycleResult.AudioDelivered;
        if (result == expected)
        {
            Increment(ref _deliveredCount);
            return PlaybackControlledWindowsPhysicalRunResult.Completed;
        }
        if (result == PlaybackApprovedExternalRuntimeWindowsLifecycleResult.RetainedWouldBlock)
            Increment(ref _retainedWouldBlockCount);
        return result == PlaybackApprovedExternalRuntimeWindowsLifecycleResult.Cancelled
            ? PlaybackControlledWindowsPhysicalRunResult.Cancelled
            : PlaybackControlledWindowsPhysicalRunResult.DeliveryRejected;
    }

    private void RecordReplay(PcpControlledFlvFixtureReplayReport report)
    {
        lock (_sync)
        {
            _fixtureSha256 = report.FixtureSha256;
            _fixtureByteCount = report.PayloadByteCount;
            _fixturePacketCount = report.PacketCount;
        }
    }

    private PlaybackControlledWindowsPhysicalRunResult Complete()
    {
        lock (_sync)
        {
            _state = PlaybackControlledWindowsPhysicalRunState.Completed;
            return SetResult(PlaybackControlledWindowsPhysicalRunResult.Completed);
        }
    }

    private PlaybackControlledWindowsPhysicalRunResult Terminal(
        PlaybackControlledWindowsPhysicalRunResult result)
    {
        lock (_sync)
        {
            _state = PlaybackControlledWindowsPhysicalRunState.Faulted;
            return SetResult(result);
        }
    }

    private void SetState(PlaybackControlledWindowsPhysicalRunState state)
    {
        lock (_sync) _state = state;
    }

    private PlaybackControlledWindowsPhysicalRunResult SetResult(
        PlaybackControlledWindowsPhysicalRunResult result)
    {
        _result = result;
        return result;
    }

    private void Increment(ref int value)
    {
        lock (_sync) value = checked(value + 1);
    }

    private static bool ValidateOptions(PlaybackControlledWindowsPhysicalRunOptions options) =>
        (options.Kind is PlaybackControlledWindowsOutputValidationKind.Video or
            PlaybackControlledWindowsOutputValidationKind.Audio) &&
        IsWindowsPath(options.RuntimeDirectory) && IsWindowsPath(options.FixturePath) &&
        IsSha256(options.FixtureSha256) &&
        options.MaximumFixtureBytes is >= 1 and <= 256 * 1024 * 1024 &&
        options.PacketPayloadBytes is >= 1 and <= 1024 * 1024 &&
        options.PacketPayloadBytes <= options.MaximumFixtureBytes &&
        options.MaximumDecodeOperations is >= 1 and <= 4096 &&
        options.MaximumDeliveryOperations is >= 1 and <= 4096 &&
        options.ObservationHoldDuration >= TimeSpan.Zero &&
        options.ObservationHoldDuration <=
            PlaybackControlledWindowsPhysicalRunOptions.MaximumObservationHoldDuration &&
        options.AudioBufferDuration >= TimeSpan.FromMilliseconds(1) &&
        options.AudioBufferDuration <=
            PlaybackWindowsOutputStreamInitializationBoundary.MaximumAudioBufferDuration &&
        options.Runner is not null;

    private static bool IsWindowsPath(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length is >= 3 and <= 32_767 &&
        char.IsAsciiLetter(value[0]) && value[1] == ':' &&
        value[2] is '\\' or '/' && value.IndexOf('\0') < 0;

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(char.IsAsciiHexDigit);
}

internal readonly record struct PlaybackControlledWindowsPhysicalRunOwnedSnapshot(
    PlaybackWindowsPhysicalRunnerSnapshot Runner,
    PlaybackApprovedExternalRuntimeWindowsLifecycleSnapshot Lifecycle);

internal readonly record struct PlaybackControlledWindowsPhysicalRunPreservation(
    bool WindowPreserved,
    bool DispatcherPreserved,
    bool OwnerThreadTeardown);

internal interface IPlaybackControlledWindowsPhysicalRunOperations : IAsyncDisposable
{
    PlaybackControlledWindowsPhysicalRunOwnedSnapshot Snapshot { get; }
    ValueTask<PlaybackWindowsPhysicalRunnerResult> StartRunnerAsync(CancellationToken token);
    ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> InitializeLifecycleAsync(
        CancellationToken token);
    ValueTask<PcpControlledFlvFixtureReplayReport> ReplayFixtureAsync(CancellationToken token);
    ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> CompleteInputAsync(
        CancellationToken token);
    ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> ReceiveDecodedAsync(
        PlaybackBoundedDemuxMediaKind kind, CancellationToken token);
    ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> DrainDecodedAsync(
        PlaybackBoundedDemuxMediaKind kind, CancellationToken token);
    ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> DeliverOneAsync(
        CancellationToken token);
    ValueTask StopLifecycleAsync();
    ValueTask<PlaybackControlledWindowsPhysicalRunPreservation> VerifyBorrowedOwnersAsync(
        CancellationToken token);
    ValueTask<PlaybackWindowsPhysicalRunnerResult> StopRunnerAsync();
}
