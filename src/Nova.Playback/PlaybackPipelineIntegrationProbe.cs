namespace PeerCast.Nova.Playback;

public enum PlaybackPipelineIntegrationProbeState
{
    Idle,
    LocatingRuntime,
    VerifyingAbi,
    LoadingDiagnosticFixture,
    Running,
    Recovering,
    Stopping,
    ResetRequired,
    Faulted,
    Disposed,
}

public enum PlaybackPipelineIntegrationProbeResult
{
    Completed,
    LicenseReviewPending,
    LicenseReviewRejected,
    InvalidConfiguration,
    RuntimeRejected,
    AbiGateRejected,
    DiagnosticMediaRejected,
    RuntimeDecodeUnavailable,
    ResetRequired,
    Faulted,
    Cancelled,
    AlreadyRunning,
    Disposed,
}

public enum PlaybackPipelineIntegrationPhase
{
    RuntimeDecode,
    ManagedDelivery,
    SchedulingTiming,
    OutputLifecycle,
    RecoveryOrchestration,
}

public enum PlaybackPipelineIntegrationStage
{
    FfmpegAbiSymbolGate,
    DiagnosticFixtureAdapter,
    NativeDecoderContextLease,
    CodecParameterOwnership,
    NativeParameterTransfer,
    CodecOpen,
    PacketFrameProcessing,
    CallbackScopedNativeFrame,
    NativeFrameMetadata,
    FramePlaneSample,
    ManagedFrameCopy,
    DecodedOutputAdapter,
    ManagedFormatConversion,
    AwaitedSinkDelivery,
    PolicyClock,
    BoundedScheduler,
    RendererAudioSink,
    WindowsAdapterSelection,
    WindowsOutputActivation,
    WindowsResourceBinding,
    WindowsStreamInitialization,
    WindowsBufferProcessing,
    WindowsSchedulerAdapter,
    FiniteContinuousPump,
    AvTimingDecision,
    TimedSchedulerDispatch,
    LateItemDisposition,
    LateDispatchCoordination,
    LateReservationDisposition,
    LateAwareSchedulerDecision,
    LateAwareTimedDispatch,
    LateAwareContinuousPump,
    AudioRecovery,
    RecoveryAwarePumpOrchestrator,
    DeadlineAwareRunBudget,
}

public readonly record struct PlaybackPipelineIntegrationStageDescriptor(
    PlaybackPipelineIntegrationPhase Phase,
    PlaybackPipelineIntegrationStage Stage);

public sealed record PlaybackPipelineIntegrationProbeOptions
{
    public string RuntimeDirectory { get; init; } = string.Empty;
    public string DiagnosticMediaFile { get; init; } = string.Empty;
}

public readonly record struct PlaybackPipelineIntegrationProbeSnapshot(
    PlaybackPipelineIntegrationProbeState State,
    PlaybackPipelineIntegrationPhase? CurrentPhase,
    PlaybackPipelineIntegrationPhase? LastCompletedPhase,
    PlaybackPipelineIntegrationStage? LastStage,
    long VideoGeneration,
    long AudioGeneration,
    int ReservedItemCount,
    int RecoveryCount);

/// <summary>
/// Diagnostic-only entry point. It is not referenced by the decoder backend
/// factory or the normal playback activation path.
/// </summary>
public sealed class PlaybackPipelineIntegrationProbe : IDisposable
{
    private static readonly PlaybackPipelineIntegrationStageDescriptor[] OrderedPlan =
    [
        new(PlaybackPipelineIntegrationPhase.RuntimeDecode, PlaybackPipelineIntegrationStage.FfmpegAbiSymbolGate),
        new(PlaybackPipelineIntegrationPhase.RuntimeDecode, PlaybackPipelineIntegrationStage.DiagnosticFixtureAdapter),
        new(PlaybackPipelineIntegrationPhase.RuntimeDecode, PlaybackPipelineIntegrationStage.NativeDecoderContextLease),
        new(PlaybackPipelineIntegrationPhase.RuntimeDecode, PlaybackPipelineIntegrationStage.CodecParameterOwnership),
        new(PlaybackPipelineIntegrationPhase.RuntimeDecode, PlaybackPipelineIntegrationStage.NativeParameterTransfer),
        new(PlaybackPipelineIntegrationPhase.RuntimeDecode, PlaybackPipelineIntegrationStage.CodecOpen),
        new(PlaybackPipelineIntegrationPhase.RuntimeDecode, PlaybackPipelineIntegrationStage.PacketFrameProcessing),
        new(PlaybackPipelineIntegrationPhase.RuntimeDecode, PlaybackPipelineIntegrationStage.CallbackScopedNativeFrame),
        new(PlaybackPipelineIntegrationPhase.RuntimeDecode, PlaybackPipelineIntegrationStage.NativeFrameMetadata),
        new(PlaybackPipelineIntegrationPhase.RuntimeDecode, PlaybackPipelineIntegrationStage.FramePlaneSample),
        new(PlaybackPipelineIntegrationPhase.RuntimeDecode, PlaybackPipelineIntegrationStage.ManagedFrameCopy),
        new(PlaybackPipelineIntegrationPhase.ManagedDelivery, PlaybackPipelineIntegrationStage.DecodedOutputAdapter),
        new(PlaybackPipelineIntegrationPhase.ManagedDelivery, PlaybackPipelineIntegrationStage.ManagedFormatConversion),
        new(PlaybackPipelineIntegrationPhase.ManagedDelivery, PlaybackPipelineIntegrationStage.AwaitedSinkDelivery),
        new(PlaybackPipelineIntegrationPhase.ManagedDelivery, PlaybackPipelineIntegrationStage.PolicyClock),
        new(PlaybackPipelineIntegrationPhase.ManagedDelivery, PlaybackPipelineIntegrationStage.BoundedScheduler),
        new(PlaybackPipelineIntegrationPhase.SchedulingTiming, PlaybackPipelineIntegrationStage.AvTimingDecision),
        new(PlaybackPipelineIntegrationPhase.SchedulingTiming, PlaybackPipelineIntegrationStage.TimedSchedulerDispatch),
        new(PlaybackPipelineIntegrationPhase.SchedulingTiming, PlaybackPipelineIntegrationStage.LateItemDisposition),
        new(PlaybackPipelineIntegrationPhase.SchedulingTiming, PlaybackPipelineIntegrationStage.LateDispatchCoordination),
        new(PlaybackPipelineIntegrationPhase.SchedulingTiming, PlaybackPipelineIntegrationStage.LateReservationDisposition),
        new(PlaybackPipelineIntegrationPhase.SchedulingTiming, PlaybackPipelineIntegrationStage.LateAwareSchedulerDecision),
        new(PlaybackPipelineIntegrationPhase.SchedulingTiming, PlaybackPipelineIntegrationStage.LateAwareTimedDispatch),
        new(PlaybackPipelineIntegrationPhase.OutputLifecycle, PlaybackPipelineIntegrationStage.RendererAudioSink),
        new(PlaybackPipelineIntegrationPhase.OutputLifecycle, PlaybackPipelineIntegrationStage.WindowsAdapterSelection),
        new(PlaybackPipelineIntegrationPhase.OutputLifecycle, PlaybackPipelineIntegrationStage.WindowsOutputActivation),
        new(PlaybackPipelineIntegrationPhase.OutputLifecycle, PlaybackPipelineIntegrationStage.WindowsResourceBinding),
        new(PlaybackPipelineIntegrationPhase.OutputLifecycle, PlaybackPipelineIntegrationStage.WindowsStreamInitialization),
        new(PlaybackPipelineIntegrationPhase.OutputLifecycle, PlaybackPipelineIntegrationStage.WindowsBufferProcessing),
        new(PlaybackPipelineIntegrationPhase.OutputLifecycle, PlaybackPipelineIntegrationStage.WindowsSchedulerAdapter),
        new(PlaybackPipelineIntegrationPhase.RecoveryOrchestration, PlaybackPipelineIntegrationStage.FiniteContinuousPump),
        new(PlaybackPipelineIntegrationPhase.RecoveryOrchestration, PlaybackPipelineIntegrationStage.LateAwareContinuousPump),
        new(PlaybackPipelineIntegrationPhase.RecoveryOrchestration, PlaybackPipelineIntegrationStage.AudioRecovery),
        new(PlaybackPipelineIntegrationPhase.RecoveryOrchestration, PlaybackPipelineIntegrationStage.RecoveryAwarePumpOrchestrator),
        new(PlaybackPipelineIntegrationPhase.RecoveryOrchestration, PlaybackPipelineIntegrationStage.DeadlineAwareRunBudget),
    ];

    private readonly object _sync = new();
    private CancellationTokenSource? _runCancellation;
    private PlaybackPipelineIntegrationProbeState _state;
    private PlaybackPipelineIntegrationPhase? _currentPhase;
    private PlaybackPipelineIntegrationPhase? _lastCompletedPhase;
    private PlaybackPipelineIntegrationStage? _lastStage;
    private bool _isRunning;
    private bool _isDisposed;

    public static IReadOnlyList<PlaybackPipelineIntegrationStageDescriptor> PhasePlan =>
        Array.AsReadOnly(OrderedPlan);

    public static IReadOnlyList<PlaybackPipelineIntegrationStage> WiringOrder =>
        Array.AsReadOnly(OrderedPlan.Select(static item => item.Stage).ToArray());

    public PlaybackPipelineIntegrationProbeSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return new(_state, _currentPhase, _lastCompletedPhase, _lastStage, 0, 0, 0, 0);
            }
        }
    }

    public async ValueTask<PlaybackPipelineIntegrationProbeResult> RunAsync(
        PlaybackPipelineIntegrationProbeOptions? options,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_isDisposed)
                return PlaybackPipelineIntegrationProbeResult.Disposed;
            if (_isRunning)
                return PlaybackPipelineIntegrationProbeResult.AlreadyRunning;
            _isRunning = true;
            _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _currentPhase = null;
            _lastCompletedPhase = null;
            _lastStage = null;
        }

        try
        {
            var review = PlaybackFfmpegBackendCandidate.Provenance.LicenseReview;
            if (!Enum.IsDefined(review))
                return SetTerminal(PlaybackPipelineIntegrationProbeState.Faulted, PlaybackPipelineIntegrationProbeResult.Faulted);
            if (review == PlaybackDecoderBackendLicenseReview.Pending)
                return SetTerminal(PlaybackPipelineIntegrationProbeState.Idle, PlaybackPipelineIntegrationProbeResult.LicenseReviewPending);
            if (review != PlaybackDecoderBackendLicenseReview.Approved)
                return SetTerminal(PlaybackPipelineIntegrationProbeState.Idle, PlaybackPipelineIntegrationProbeResult.LicenseReviewRejected);

            if (options is null ||
                string.IsNullOrWhiteSpace(options.RuntimeDirectory) ||
                string.IsNullOrWhiteSpace(options.DiagnosticMediaFile))
            {
                return SetTerminal(PlaybackPipelineIntegrationProbeState.Idle, PlaybackPipelineIntegrationProbeResult.InvalidConfiguration);
            }

            BeginPhase(PlaybackPipelineIntegrationPhase.RuntimeDecode, PlaybackPipelineIntegrationProbeState.LocatingRuntime, null);
            var location = await PlaybackDecoderBackendInstalledRuntimeLocator.LocateAndVerifyAsync(
                options.RuntimeDirectory,
                PlaybackFfmpegBackendRuntimeInventory.PinnedCandidate,
                _runCancellation.Token).ConfigureAwait(false);
            if (location.Location != PlaybackDecoderBackendInstalledRuntimeLocation.Verified || location.Lease is null)
                return SetTerminal(PlaybackPipelineIntegrationProbeState.Idle, PlaybackPipelineIntegrationProbeResult.RuntimeRejected);

            await using var runtimeLease = location.Lease;
            BeginPhase(
                PlaybackPipelineIntegrationPhase.RuntimeDecode,
                PlaybackPipelineIntegrationProbeState.VerifyingAbi,
                PlaybackPipelineIntegrationStage.FfmpegAbiSymbolGate);
            var gate = new PlaybackDecoderFfmpegAbiSymbolGate();
            using var gateLease = gate.Acquire(runtimeLease).Lease;
            if (gateLease is null)
                return SetTerminal(PlaybackPipelineIntegrationProbeState.Idle, PlaybackPipelineIntegrationProbeResult.AbiGateRejected);

            BeginPhase(
                PlaybackPipelineIntegrationPhase.RuntimeDecode,
                PlaybackPipelineIntegrationProbeState.LoadingDiagnosticFixture,
                PlaybackPipelineIntegrationStage.DiagnosticFixtureAdapter);
            var fixtureResult = PlaybackDiagnosticFixtureAdapter.TryLoad(
                () => File.OpenRead(options.DiagnosticMediaFile),
                _runCancellation.Token,
                out var encodedInput);
            if (fixtureResult == PlaybackDiagnosticFixtureAdapterResult.Cancelled)
                return SetTerminal(PlaybackPipelineIntegrationProbeState.Idle, PlaybackPipelineIntegrationProbeResult.Cancelled);
            if (fixtureResult != PlaybackDiagnosticFixtureAdapterResult.Completed || encodedInput is null)
                return SetTerminal(PlaybackPipelineIntegrationProbeState.Idle, PlaybackPipelineIntegrationProbeResult.DiagnosticMediaRejected);

            // The bounded encoded input is now available. Native context creation,
            // parameter transfer, codec open, and packet processing remain a later
            // reviewed diagnostic-only connection.
            return SetTerminal(
                PlaybackPipelineIntegrationProbeState.Idle,
                PlaybackPipelineIntegrationProbeResult.RuntimeDecodeUnavailable);
        }
        catch (OperationCanceledException)
        {
            return SetTerminal(PlaybackPipelineIntegrationProbeState.Idle, PlaybackPipelineIntegrationProbeResult.Cancelled);
        }
        catch
        {
            return SetTerminal(PlaybackPipelineIntegrationProbeState.Faulted, PlaybackPipelineIntegrationProbeResult.Faulted);
        }
        finally
        {
            lock (_sync)
            {
                _isRunning = false;
                _runCancellation?.Dispose();
                _runCancellation = null;
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_isDisposed)
                return;
            _isDisposed = true;
            _runCancellation?.Cancel();
            _state = PlaybackPipelineIntegrationProbeState.Disposed;
            _currentPhase = null;
        }
    }

    private void BeginPhase(
        PlaybackPipelineIntegrationPhase phase,
        PlaybackPipelineIntegrationProbeState state,
        PlaybackPipelineIntegrationStage? stage)
    {
        lock (_sync)
        {
            if (_isDisposed)
                return;
            _currentPhase = phase;
            _state = state;
            _lastStage = stage;
        }
    }

    private PlaybackPipelineIntegrationProbeResult SetTerminal(
        PlaybackPipelineIntegrationProbeState state,
        PlaybackPipelineIntegrationProbeResult result)
    {
        lock (_sync)
        {
            if (!_isDisposed)
                _state = state;
        }

        return result;
    }
}
