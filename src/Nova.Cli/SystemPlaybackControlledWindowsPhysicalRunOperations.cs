using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli;

internal sealed class SystemPlaybackControlledWindowsPhysicalRunOperations :
    IPlaybackControlledWindowsPhysicalRunOperations
{
    private const int MaximumControlledAudioTimingResumeCount = 2;
    private static readonly TimeSpan ControlledAudioFrameCadence =
        TimeSpan.FromTicks(213_334);
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
    private readonly bool _captureAudioRenderTrace;
    private readonly bool _capturePreStopPadding;
    private readonly bool _captureAudioNativeStages;
    private PlaybackApprovedExternalRuntimeWindowsLifecycleBoundary? _lifecycle;
    private long _borrowedWindowToken;
    private bool _cleanupValidated;
    private bool _isDisposed;

    internal SystemPlaybackControlledWindowsPhysicalRunOperations(
        PlaybackControlledWindowsPhysicalRunOptions options)
        : this(
            options,
            captureAudioRenderTrace: false,
            capturePreStopPadding: false,
            captureAudioNativeStages: false)
    {
    }

    internal SystemPlaybackControlledWindowsPhysicalRunOperations(
        PlaybackControlledWindowsPhysicalRunOptions options,
        bool captureAudioRenderTrace,
        bool capturePreStopPadding,
        bool captureAudioNativeStages = false)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (capturePreStopPadding && !captureAudioRenderTrace)
        {
            throw new ArgumentException(
                "Pre-Stop padding capture requires the bounded audio render trace.",
                nameof(capturePreStopPadding));
        }

        _options = options with { Runner = options.Runner with { } };
        _captureAudioRenderTrace = captureAudioRenderTrace;
        _capturePreStopPadding = capturePreStopPadding;
        _captureAudioNativeStages = captureAudioNativeStages;
        _runner = new(_options.Runner);
    }

    public PlaybackControlledWindowsPhysicalRunOwnedSnapshot Snapshot => new(
        _runner.Snapshot,
        _lifecycle?.Snapshot ?? EmptyLifecycle);

    internal PlaybackControlledWindowsAudioRenderEvidenceSnapshot AudioRenderEvidence =>
        PlaybackControlledWindowsAudioRenderEvidenceProjection.Capture(
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
            CreateLifecycleOptions(
                _options.AudioBufferDuration,
                _options.Kind,
                _options.DiagnoseAudioNativeStages,
                captureAudioNativeStages: _captureAudioNativeStages,
                captureAudioRenderTrace: _captureAudioRenderTrace,
                capturePreStopPadding: _capturePreStopPadding));
        return await _lifecycle.InitializeAsync(token).ConfigureAwait(false);
    }

    internal static PlaybackApprovedExternalRuntimeWindowsLifecycleOptions CreateLifecycleOptions(
        TimeSpan audioBufferDuration,
        PlaybackControlledWindowsOutputValidationKind kind,
        bool diagnoseAudioNativeStages = false,
        TextWriter? diagnosticOutput = null,
        bool captureAudioNativeStages = false,
        bool captureAudioRenderTrace = false,
        bool capturePreStopPadding = false)
    {
        if (capturePreStopPadding && !captureAudioRenderTrace)
        {
            throw new ArgumentException(
                "Pre-Stop padding capture requires the bounded audio render trace.",
                nameof(capturePreStopPadding));
        }

        IPlaybackWindowsAudioNativeStageReporter reporter =
            diagnoseAudioNativeStages || captureAudioNativeStages
            ? new TextWriterPlaybackWindowsAudioNativeStageReporter(
                diagnosticOutput ?? Console.Error)
            : NullPlaybackWindowsAudioNativeStageReporter.Instance;
        if (captureAudioRenderTrace)
        {
            reporter = new PlaybackWindowsAudioRenderTraceConfiguredReporter(
                reporter,
                capturePreStopPadding);
        }

        return new()
        {
            AudioNativeStageReporter = reporter,
            AudioBufferDuration = audioBufferDuration,
            Production = new()
            {
                MediaSelection = kind switch
                {
                    PlaybackControlledWindowsOutputValidationKind.Video =>
                        PlaybackPcpFlvAwaitedDecodeMediaSelection.AudioAndVideo,
                    PlaybackControlledWindowsOutputValidationKind.Audio =>
                        PlaybackPcpFlvAwaitedDecodeMediaSelection.AudioOnly,
                    _ => throw new ArgumentOutOfRangeException(nameof(kind)),
                },
            },
            Generation = new()
            {
                CaptureDeliveryObservations = captureAudioRenderTrace,
                Scheduler = new()
                {
                    Capacity = PlaybackSchedulerOptions.DefaultCapacity,
                    MaximumOwnedBytes = PlaybackSchedulerOptions.DefaultMaximumOwnedBytes,
                    FullPolicy = PlaybackSchedulerFullPolicy.DropOldest,
                    InitialOutputKind = kind switch
                    {
                        PlaybackControlledWindowsOutputValidationKind.Video =>
                            PlaybackSchedulerInitialOutputKind.Video,
                        PlaybackControlledWindowsOutputValidationKind.Audio =>
                            PlaybackSchedulerInitialOutputKind.Audio,
                        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
                    },
                    RestrictOutputToInitialKind =
                        kind == PlaybackControlledWindowsOutputValidationKind.Audio,
                },
            },
        };
    }

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

    public async ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> DeliverOneAsync(
        CancellationToken token)
    {
        var lifecycle = GetLifecycle();
        if (_options.Kind != PlaybackControlledWindowsOutputValidationKind.Audio)
        {
            return await lifecycle.DeliverOneAsync(token).ConfigureAwait(false);
        }

        return await ResumeControlledAudioTimingAsync(
            cancellationToken => lifecycle.DeliverOneAsync(cancellationToken),
            static (delay, cancellationToken) => Task.Delay(delay, cancellationToken),
            ControlledAudioFrameCadence,
            MaximumControlledAudioTimingResumeCount,
            token).ConfigureAwait(false);
    }

    internal static async ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult>
        ResumeControlledAudioTimingAsync(
            Func<CancellationToken,
                ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult>> deliverAsync,
            Func<TimeSpan, CancellationToken, Task> delayAsync,
            TimeSpan frameCadence,
            int maximumResumeCount,
            CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(deliverAsync);
        ArgumentNullException.ThrowIfNull(delayAsync);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(frameCadence, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumResumeCount, 1);

        var result = await deliverAsync(token).ConfigureAwait(false);
        for (var resumeCount = 0;
            result == PlaybackApprovedExternalRuntimeWindowsLifecycleResult.RetainedWait &&
            resumeCount < maximumResumeCount;
            resumeCount++)
        {
            await delayAsync(frameCadence, token).ConfigureAwait(false);
            result = await deliverAsync(token).ConfigureAwait(false);
        }

        return result;
    }

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
