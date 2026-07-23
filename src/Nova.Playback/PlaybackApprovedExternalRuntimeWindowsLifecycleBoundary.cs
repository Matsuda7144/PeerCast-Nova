using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Playback;

public enum PlaybackApprovedExternalRuntimeWindowsLifecycleState
{
    Created,
    Activated,
    WaitingForStreamDescription,
    OutputConfigurationRequired,
    Ready,
    ResizeRequired,
    DeviceLost,
    Faulted,
    Disposed,
}

public enum PlaybackApprovedExternalRuntimeWindowsLifecycleResult
{
    None,
    Initialized,
    PacketAccepted,
    EncodedInputFinalized,
    FrameTransferred,
    NeedMoreInput,
    StreamEndOfStream,
    StreamRequiresDrain,
    StreamNotFlushed,
    StreamAlreadyEnded,
    VideoDelivered,
    AudioDelivered,
    VideoDropped,
    Empty,
    RetainedWait,
    RetainedWouldBlock,
    RetainedInitializationRequired,
    RetainedBindingRequired,
    RetainedResizeRequired,
    RetainedAudioRecoveryRequired,
    ClockNotStarted,
    ResetRequired,
    Reset,
    OutputConfigurationRequired,
    OutputConfigured,
    Resized,
    PolicyRejected,
    ProbeRejected,
    ActivationRejected,
    OutputRejected,
    InvalidConfiguration,
    AlreadyRunning,
    DeviceLost,
    Cancelled,
    DispatcherShutdown,
    Faulted,
    Disposed,
}

public sealed record PlaybackApprovedExternalRuntimeWindowsLifecycleOptions
{
    internal IPlaybackWindowsAudioNativeStageReporter AudioNativeStageReporter { get; init; } =
        NullPlaybackWindowsAudioNativeStageReporter.Instance;

    public PlaybackDecodedVideoPixelFormat VideoFormat { get; init; } =
        PlaybackDecodedVideoPixelFormat.Bgra32;

    public PlaybackDecodedAudioSampleFormat AudioFormat { get; init; } =
        PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved;

    public TimeSpan AudioBufferDuration { get; init; } = TimeSpan.FromMilliseconds(100);

    public long InitialPacketSequence { get; init; }

    public PlaybackPcpFlvAwaitedDecodeOptions Production { get; init; } = new();

    public PlaybackBoundedFlvParserOptions Parser { get; init; } = new();

    public PlaybackProductionDecoderGenerationFactoryOptions Generation { get; init; } = new();
}

public sealed record PlaybackApprovedExternalRuntimeWindowsLifecycleSnapshot(
    PlaybackApprovedExternalRuntimeWindowsLifecycleState State,
    PlaybackApprovedExternalRuntimeWindowsLifecycleResult Result,
    int DecoderGeneration,
    int DiscontinuityGeneration,
    int OutputReadyGeneration,
    int VideoWidth,
    int VideoHeight,
    int AudioSampleRate,
    int AudioChannelCount,
    bool HasActiveOperation,
    PlaybackWindowsOutputProbeResult ProbeResult,
    PlaybackWindowsOutputActivationResult ActivationResult,
    PlaybackWindowsOutputBindingSnapshot Binding,
    PlaybackWindowsOutputStreamSnapshot Streams,
    PlaybackWindowsOutputBufferSnapshot Buffers);

internal enum PlaybackApprovedExternalRuntimeOutputConfigurationResult
{
    Configured,
    DeviceLost,
    Cancelled,
    DispatcherShutdown,
    Rejected,
    Faulted,
}

internal readonly record struct PlaybackApprovedExternalRuntimeOutputConfiguration(
    long Generation,
    long WindowToken,
    int Width,
    int Height,
    int SampleRate,
    int ChannelCount,
    PlaybackDecodedVideoPixelFormat VideoFormat,
    PlaybackDecodedAudioSampleFormat AudioFormat,
    TimeSpan AudioBufferDuration);

internal readonly record struct PlaybackApprovedExternalRuntimeWindowsOperationsSnapshot(
    PlaybackWindowsOutputProbeResult ProbeResult,
    PlaybackWindowsOutputActivationResult ActivationResult,
    PlaybackWindowsOutputBindingSnapshot Binding,
    PlaybackWindowsOutputStreamSnapshot Streams,
    PlaybackWindowsOutputBufferSnapshot Buffers,
    PlaybackProductionPcpFlvCompositionSnapshot? Production);

internal interface IPlaybackApprovedExternalRuntimeWindowsLifecycleOperations :
    IAsyncDisposable
{
    PlaybackApprovedExternalRuntimeWindowsOperationsSnapshot Snapshot { get; }

    PlaybackWindowsOutputProbeReport Probe(
        PlaybackDecodedVideoPixelFormat videoFormat,
        PlaybackDecodedAudioSampleFormat audioFormat);

    ValueTask<PlaybackWindowsOutputActivationResult> ActivateAsync(
        CancellationToken cancellationToken);

    void CreateProduction();

    ValueTask<PlaybackProductionPcpFlvCompositionResult> ConsumeAsync(
        PcpChannelPacket packet,
        CancellationToken cancellationToken);

    ValueTask<PlaybackProductionPcpFlvCompositionResult> CompleteInputAsync(
        CancellationToken cancellationToken);

    ValueTask<PlaybackProductionPcpFlvCompositionResult> ReceiveDecodedAsync(
        PlaybackBoundedDemuxMediaKind mediaKind,
        CancellationToken cancellationToken);

    ValueTask<PlaybackProductionPcpFlvCompositionResult> DrainDecodedAsync(
        PlaybackBoundedDemuxMediaKind mediaKind,
        CancellationToken cancellationToken);

    ValueTask<PlaybackProductionPcpFlvCompositionResult> DeliverOneAsync(
        CancellationToken cancellationToken);

    ValueTask<PlaybackProductionPcpFlvCompositionResult> ResetAsync(
        CancellationToken cancellationToken);

    ValueTask<PlaybackApprovedExternalRuntimeOutputConfigurationResult> ConfigureOutputAsync(
        PlaybackApprovedExternalRuntimeOutputConfiguration configuration,
        bool replacement,
        CancellationToken cancellationToken);

    ValueTask<PlaybackApprovedExternalRuntimeOutputConfigurationResult> ResizeVideoAsync(
        long windowToken,
        int width,
        int height,
        PlaybackDecodedVideoPixelFormat format,
        long generation,
        CancellationToken cancellationToken);

    ValueTask StopProductionAsync();

    ValueTask<PlaybackApprovedExternalRuntimeOutputConfigurationResult> DisposeOutputAsync();
}

/// <summary>
/// Finite application-owned lifecycle connecting one approved or explicitly
/// host-attested external FFmpeg runtime route to the existing Windows output
/// boundaries through a borrowed owner-thread dispatcher and window token.
/// It creates no window, thread, queue, timer, message pump, retry loop, runtime
/// search, download, installation, or output fallback.
/// </summary>
public sealed class PlaybackApprovedExternalRuntimeWindowsLifecycleBoundary :
    IDisposable,
    IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _stopSource = new();
    private readonly long _windowToken;
    private readonly PlaybackApprovedExternalRuntimeWindowsLifecycleOptions _options;
    private readonly IPlaybackApprovedExternalRuntimeWindowsLifecycleOperations _operations;
    private readonly PlaybackApprovedExternalRuntimeWindowsLifecycleResult _preflightResult;

    private PlaybackApprovedExternalRuntimeWindowsLifecycleState _state =
        PlaybackApprovedExternalRuntimeWindowsLifecycleState.Created;
    private PlaybackApprovedExternalRuntimeWindowsLifecycleResult _result =
        PlaybackApprovedExternalRuntimeWindowsLifecycleResult.None;
    private int _outputReadyGeneration = -1;
    private bool _hasActiveOperation;
    private bool _productionCreated;
    private int _admissionClosed;
    private int _isDisposed;
    private Task? _stopTask;

    public PlaybackApprovedExternalRuntimeWindowsLifecycleBoundary(
        ChannelId channelId,
        ChannelSessionId channelSessionId,
        string runtimeDirectory,
        PlaybackFfmpegExternalRuntimeManifest runtimeManifest,
        PlaybackDecoderBackendPolicy policy,
        IPlaybackWindowsOutputDispatcher dispatcher,
        long windowToken,
        PlaybackApprovedExternalRuntimeWindowsLifecycleOptions? options = null,
        IPlaybackMonotonicClock? clock = null)
        : this(
            channelId,
            channelSessionId,
            runtimeDirectory,
            runtimeManifest,
            policy,
            dispatcher,
            windowToken,
            CloneOptions(options),
            clock ?? new StopwatchPlaybackMonotonicClock(),
            operations: null)
    {
    }

    internal PlaybackApprovedExternalRuntimeWindowsLifecycleBoundary(
        ChannelId channelId,
        ChannelSessionId channelSessionId,
        string runtimeDirectory,
        PlaybackFfmpegExternalRuntimeManifest runtimeManifest,
        PlaybackDecoderBackendPolicy policy,
        IPlaybackWindowsOutputDispatcher dispatcher,
        long windowToken,
        PlaybackApprovedExternalRuntimeWindowsLifecycleOptions options,
        IPlaybackMonotonicClock clock,
        IPlaybackApprovedExternalRuntimeWindowsLifecycleOperations? operations)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        ArgumentNullException.ThrowIfNull(channelSessionId);
        ArgumentNullException.ThrowIfNull(runtimeManifest);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        _windowToken = windowToken;
        _options = options;
        _preflightResult = ValidatePreflight(
            runtimeDirectory,
            runtimeManifest,
            policy,
            windowToken,
            options);
        _operations = operations ??
            new PlaybackApprovedExternalRuntimeWindowsLifecycleOperations(
                channelId,
                channelSessionId,
                runtimeDirectory,
                runtimeManifest,
                policy,
                dispatcher,
                options,
                clock);
    }

    public PlaybackApprovedExternalRuntimeWindowsLifecycleSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                var owned = _operations.Snapshot;
                var production = owned.Production;
                return new(
                    _state,
                    _result,
                    production?.DecoderGeneration ?? -1,
                    production?.ParserGeneration ?? 0,
                    _outputReadyGeneration,
                    production?.VideoWidth ?? 0,
                    production?.VideoHeight ?? 0,
                    production?.AudioSampleRate ?? 0,
                    production?.AudioChannelCount ?? 0,
                    _hasActiveOperation,
                    owned.ProbeResult,
                    owned.ActivationResult,
                    owned.Binding,
                    owned.Streams,
                    owned.Buffers);
            }
        }
    }

    public ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> InitializeAsync(
        CancellationToken cancellationToken = default) =>
        RunSerializedAsync(InitializeCoreAsync, cancellationToken, requireInitialized: false);

    public ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> ConsumeAsync(
        PcpChannelPacket packet,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return RunSerializedAsync(
            token => RunProductionAsync(
                operation: inner => _operations.ConsumeAsync(packet, inner),
                synchronizeOutput: true,
                token),
            cancellationToken,
            requireInitialized: true);
    }

    public ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> CompleteInputAsync(
        CancellationToken cancellationToken = default) =>
        RunSerializedAsync(
            token => RunProductionAsync(
                _operations.CompleteInputAsync,
                synchronizeOutput: true,
                token),
            cancellationToken,
            requireInitialized: true);

    public ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> ReceiveDecodedAsync(
        PlaybackBoundedDemuxMediaKind mediaKind,
        CancellationToken cancellationToken = default) =>
        RunSerializedAsync(
            token => RunProductionAsync(
                inner => _operations.ReceiveDecodedAsync(mediaKind, inner),
                synchronizeOutput: true,
                token),
            cancellationToken,
            requireInitialized: true);

    public ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> DrainDecodedAsync(
        PlaybackBoundedDemuxMediaKind mediaKind,
        CancellationToken cancellationToken = default) =>
        RunSerializedAsync(
            token => RunProductionAsync(
                inner => _operations.DrainDecodedAsync(mediaKind, inner),
                synchronizeOutput: true,
                token),
            cancellationToken,
            requireInitialized: true);

    public ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> DeliverOneAsync(
        CancellationToken cancellationToken = default) =>
        RunSerializedAsync(DeliverOneCoreAsync, cancellationToken, requireInitialized: true);

    public ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> ResetAsync(
        CancellationToken cancellationToken = default) =>
        RunSerializedAsync(
            token => RunProductionAsync(
                _operations.ResetAsync,
                synchronizeOutput: true,
                token),
            cancellationToken,
            requireInitialized: true);

    public ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult> ResizeVideoAsync(
        int width,
        int height,
        CancellationToken cancellationToken = default) =>
        RunSerializedAsync(
            token => ResizeVideoCoreAsync(width, height, token),
            cancellationToken,
            requireInitialized: true);

    public ValueTask StopAsync()
    {
        var existing = Volatile.Read(ref _stopTask);
        if (existing is not null)
        {
            return new(existing);
        }

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        existing = Interlocked.CompareExchange(ref _stopTask, completion.Task, null);
        if (existing is not null)
        {
            return new(existing);
        }

        Interlocked.Exchange(ref _admissionClosed, 1);
        _stopSource.Cancel();
        _ = CompleteStopAsync(completion);
        return new(completion.Task);
    }

    public void Dispose()
    {
        StopAsync().AsTask().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private async ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult>
        InitializeCoreAsync(CancellationToken cancellationToken)
    {
        if (_preflightResult != PlaybackApprovedExternalRuntimeWindowsLifecycleResult.Initialized)
        {
            return SetResult(_preflightResult);
        }

        if (GetState() != PlaybackApprovedExternalRuntimeWindowsLifecycleState.Created)
        {
            return SetResult(PlaybackApprovedExternalRuntimeWindowsLifecycleResult.InvalidConfiguration);
        }

        var probe = _operations.Probe(_options.VideoFormat, _options.AudioFormat);
        if (probe.Result != PlaybackWindowsOutputProbeResult.Available)
        {
            await CleanupOwnedAsync().ConfigureAwait(false);
            return Fault(PlaybackApprovedExternalRuntimeWindowsLifecycleResult.ProbeRejected);
        }

        var activation = await _operations.ActivateAsync(cancellationToken).ConfigureAwait(false);
        if (activation != PlaybackWindowsOutputActivationResult.Activated)
        {
            await CleanupOwnedAsync().ConfigureAwait(false);
            return activation == PlaybackWindowsOutputActivationResult.Disposed
                ? SetResult(PlaybackApprovedExternalRuntimeWindowsLifecycleResult.DispatcherShutdown)
                : Fault(PlaybackApprovedExternalRuntimeWindowsLifecycleResult.ActivationRejected);
        }

        SetState(PlaybackApprovedExternalRuntimeWindowsLifecycleState.Activated);
        try
        {
            _operations.CreateProduction();
            _productionCreated = true;
        }
        catch
        {
            await CleanupOwnedAsync().ConfigureAwait(false);
            return Fault(PlaybackApprovedExternalRuntimeWindowsLifecycleResult.Faulted);
        }

        SetState(PlaybackApprovedExternalRuntimeWindowsLifecycleState.WaitingForStreamDescription);
        return SetResult(PlaybackApprovedExternalRuntimeWindowsLifecycleResult.Initialized);
    }

    private async ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult>
        RunProductionAsync(
            Func<CancellationToken, ValueTask<PlaybackProductionPcpFlvCompositionResult>> operation,
            bool synchronizeOutput,
            CancellationToken cancellationToken)
    {
        PlaybackProductionPcpFlvCompositionResult productionResult;
        try
        {
            productionResult = await operation(cancellationToken).ConfigureAwait(false);
        }
        catch (PlaybackProductionPcpFlvCompositionException exception)
        {
            productionResult = exception.Result;
        }

        if (HasDeviceLoss(_operations.Snapshot))
        {
            return await LatchDeviceLossAsync().ConfigureAwait(false);
        }

        var mapped = MapProduction(productionResult);
        if (mapped is PlaybackApprovedExternalRuntimeWindowsLifecycleResult.Faulted or
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.OutputRejected)
        {
            await CleanupOwnedAsync().ConfigureAwait(false);
            return Fault(mapped);
        }

        if (synchronizeOutput)
        {
            var synchronization = await SynchronizeOutputAsync(cancellationToken)
                .ConfigureAwait(false);
            if (synchronization is not (
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.OutputConfigured or
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.OutputConfigurationRequired))
            {
                return synchronization;
            }
        }

        return SetResult(mapped);
    }

    private async ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult>
        DeliverOneCoreAsync(CancellationToken cancellationToken)
    {
        var synchronization = await SynchronizeOutputAsync(cancellationToken)
            .ConfigureAwait(false);
        if (synchronization ==
            PlaybackApprovedExternalRuntimeWindowsLifecycleResult.OutputConfigurationRequired)
        {
            return SetResult(synchronization);
        }

        if (synchronization != PlaybackApprovedExternalRuntimeWindowsLifecycleResult.OutputConfigured ||
            GetState() != PlaybackApprovedExternalRuntimeWindowsLifecycleState.Ready)
        {
            return synchronization;
        }

        var result = await RunProductionAsync(
            _operations.DeliverOneAsync,
            synchronizeOutput: false,
            cancellationToken).ConfigureAwait(false);
        if (result == PlaybackApprovedExternalRuntimeWindowsLifecycleResult.RetainedResizeRequired)
        {
            SetState(PlaybackApprovedExternalRuntimeWindowsLifecycleState.ResizeRequired);
        }

        return result;
    }

    private async ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult>
        ResizeVideoCoreAsync(int width, int height, CancellationToken cancellationToken)
    {
        if (width <= 0 || height <= 0 || _outputReadyGeneration < 0)
        {
            return SetResult(
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.InvalidConfiguration);
        }

        var resize = await _operations.ResizeVideoAsync(
            _windowToken,
            width,
            height,
            _options.VideoFormat,
            _outputReadyGeneration,
            cancellationToken).ConfigureAwait(false);
        return resize switch
        {
            PlaybackApprovedExternalRuntimeOutputConfigurationResult.Configured =>
                Ready(PlaybackApprovedExternalRuntimeWindowsLifecycleResult.Resized),
            PlaybackApprovedExternalRuntimeOutputConfigurationResult.DeviceLost =>
                await LatchDeviceLossAsync().ConfigureAwait(false),
            PlaybackApprovedExternalRuntimeOutputConfigurationResult.Cancelled =>
                SetResult(PlaybackApprovedExternalRuntimeWindowsLifecycleResult.Cancelled),
            PlaybackApprovedExternalRuntimeOutputConfigurationResult.DispatcherShutdown =>
                Fault(PlaybackApprovedExternalRuntimeWindowsLifecycleResult.DispatcherShutdown),
            _ => Fault(PlaybackApprovedExternalRuntimeWindowsLifecycleResult.OutputRejected),
        };
    }

    private async ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult>
        SynchronizeOutputAsync(CancellationToken cancellationToken)
    {
        var snapshot = _operations.Snapshot;
        if (HasDeviceLoss(snapshot))
        {
            return await LatchDeviceLossAsync().ConfigureAwait(false);
        }

        var production = snapshot.Production;
        if (production is null || !production.HasDecoderGeneration ||
            production.VideoWidth <= 0 || production.VideoHeight <= 0 ||
            production.AudioSampleRate <= 0 || production.AudioChannelCount <= 0)
        {
            SetState(PlaybackApprovedExternalRuntimeWindowsLifecycleState.WaitingForStreamDescription);
            return SetResult(
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.OutputConfigurationRequired);
        }

        var generation = production.DecoderGeneration;
        if (_outputReadyGeneration == generation && IsOutputReady(snapshot, generation))
        {
            SetState(PlaybackApprovedExternalRuntimeWindowsLifecycleState.Ready);
            return PlaybackApprovedExternalRuntimeWindowsLifecycleResult.OutputConfigured;
        }

        SetState(PlaybackApprovedExternalRuntimeWindowsLifecycleState.OutputConfigurationRequired);
        PlaybackApprovedExternalRuntimeOutputConfiguration configuration;
        try
        {
            configuration = new(
                generation,
                _windowToken,
                production.VideoWidth,
                production.VideoHeight,
                production.AudioSampleRate,
                production.AudioChannelCount,
                _options.VideoFormat,
                _options.AudioFormat,
                _options.AudioBufferDuration);
        }
        catch
        {
            return Fault(PlaybackApprovedExternalRuntimeWindowsLifecycleResult.InvalidConfiguration);
        }

        var configured = await _operations.ConfigureOutputAsync(
            configuration,
            replacement: _outputReadyGeneration >= 0,
            cancellationToken).ConfigureAwait(false);
        switch (configured)
        {
            case PlaybackApprovedExternalRuntimeOutputConfigurationResult.Configured:
                var completed = _operations.Snapshot;
                if (!IsOutputReady(completed, generation))
                {
                    return Fault(PlaybackApprovedExternalRuntimeWindowsLifecycleResult.OutputRejected);
                }

                _outputReadyGeneration = generation;
                SetState(PlaybackApprovedExternalRuntimeWindowsLifecycleState.Ready);
                return PlaybackApprovedExternalRuntimeWindowsLifecycleResult.OutputConfigured;
            case PlaybackApprovedExternalRuntimeOutputConfigurationResult.DeviceLost:
                return await LatchDeviceLossAsync().ConfigureAwait(false);
            case PlaybackApprovedExternalRuntimeOutputConfigurationResult.Cancelled:
                return SetResult(PlaybackApprovedExternalRuntimeWindowsLifecycleResult.Cancelled);
            case PlaybackApprovedExternalRuntimeOutputConfigurationResult.DispatcherShutdown:
                return Fault(PlaybackApprovedExternalRuntimeWindowsLifecycleResult.DispatcherShutdown);
            default:
                return Fault(PlaybackApprovedExternalRuntimeWindowsLifecycleResult.OutputRejected);
        }
    }

    private async ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult>
        LatchDeviceLossAsync()
    {
        Interlocked.Exchange(ref _admissionClosed, 1);
        SetState(PlaybackApprovedExternalRuntimeWindowsLifecycleState.DeviceLost);
        await CleanupOwnedAsync().ConfigureAwait(false);
        return SetResult(PlaybackApprovedExternalRuntimeWindowsLifecycleResult.DeviceLost);
    }

    private async ValueTask CleanupOwnedAsync()
    {
        if (_productionCreated)
        {
            try
            {
                await _operations.StopProductionAsync().ConfigureAwait(false);
            }
            catch
            {
                // Mandatory output cleanup continues after production stop failure.
            }

            _productionCreated = false;
        }

        try
        {
            await _operations.DisposeOutputAsync().ConfigureAwait(false);
        }
        catch
        {
            // Fixed lifecycle results never expose cleanup exceptions.
        }
    }

    private async ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult>
        RunSerializedAsync(
            Func<CancellationToken, ValueTask<PlaybackApprovedExternalRuntimeWindowsLifecycleResult>>
                operation,
            CancellationToken cancellationToken,
            bool requireInitialized)
    {
        if (Volatile.Read(ref _isDisposed) != 0)
        {
            return SetResult(PlaybackApprovedExternalRuntimeWindowsLifecycleResult.Disposed);
        }

        if (Volatile.Read(ref _admissionClosed) != 0)
        {
            return GetState() == PlaybackApprovedExternalRuntimeWindowsLifecycleState.DeviceLost
                ? SetResult(PlaybackApprovedExternalRuntimeWindowsLifecycleResult.DeviceLost)
                : SetResult(PlaybackApprovedExternalRuntimeWindowsLifecycleResult.Disposed);
        }

        bool entered;
        try
        {
            entered = await _operationGate.WaitAsync(0, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return SetResult(PlaybackApprovedExternalRuntimeWindowsLifecycleResult.Cancelled);
        }

        if (!entered)
        {
            return SetResult(PlaybackApprovedExternalRuntimeWindowsLifecycleResult.AlreadyRunning);
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _stopSource.Token);
        SetActive(true);
        try
        {
            if (requireInitialized && !_productionCreated)
            {
                return SetResult(
                    PlaybackApprovedExternalRuntimeWindowsLifecycleResult.OutputConfigurationRequired);
            }

            return await operation(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (Volatile.Read(ref _admissionClosed) == 0)
            {
                return SetResult(PlaybackApprovedExternalRuntimeWindowsLifecycleResult.Cancelled);
            }

            return SetResult(PlaybackApprovedExternalRuntimeWindowsLifecycleResult.Disposed);
        }
        catch
        {
            await CleanupOwnedAsync().ConfigureAwait(false);
            return Fault(PlaybackApprovedExternalRuntimeWindowsLifecycleResult.Faulted);
        }
        finally
        {
            SetActive(false);
            _operationGate.Release();
        }
    }

    private async Task CompleteStopAsync(TaskCompletionSource<bool> completion)
    {
        try
        {
            await _operationGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await CleanupOwnedAsync().ConfigureAwait(false);
                try
                {
                    await _operations.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Cleanup is idempotent and fixed-result.
                }

                Interlocked.Exchange(ref _isDisposed, 1);
                SetState(PlaybackApprovedExternalRuntimeWindowsLifecycleState.Disposed);
                SetResult(PlaybackApprovedExternalRuntimeWindowsLifecycleResult.Disposed);
            }
            finally
            {
                _operationGate.Release();
            }

            completion.TrySetResult(true);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    private PlaybackApprovedExternalRuntimeWindowsLifecycleResult Ready(
        PlaybackApprovedExternalRuntimeWindowsLifecycleResult result)
    {
        SetState(PlaybackApprovedExternalRuntimeWindowsLifecycleState.Ready);
        return SetResult(result);
    }

    private PlaybackApprovedExternalRuntimeWindowsLifecycleResult Fault(
        PlaybackApprovedExternalRuntimeWindowsLifecycleResult result)
    {
        SetState(PlaybackApprovedExternalRuntimeWindowsLifecycleState.Faulted);
        return SetResult(result);
    }

    private PlaybackApprovedExternalRuntimeWindowsLifecycleResult SetResult(
        PlaybackApprovedExternalRuntimeWindowsLifecycleResult result)
    {
        lock (_sync)
        {
            _result = result;
            return result;
        }
    }

    private void SetState(PlaybackApprovedExternalRuntimeWindowsLifecycleState state)
    {
        lock (_sync)
        {
            _state = state;
        }
    }

    private PlaybackApprovedExternalRuntimeWindowsLifecycleState GetState()
    {
        lock (_sync)
        {
            return _state;
        }
    }

    private void SetActive(bool value)
    {
        lock (_sync)
        {
            _hasActiveOperation = value;
        }
    }

    private static bool IsOutputReady(
        PlaybackApprovedExternalRuntimeWindowsOperationsSnapshot snapshot,
        long generation) =>
        snapshot.Binding.VideoState == PlaybackWindowsOutputBindingState.Bound &&
        snapshot.Binding.AudioState == PlaybackWindowsOutputBindingState.Bound &&
        snapshot.Binding.VideoGeneration == generation &&
        snapshot.Binding.AudioGeneration == generation &&
        snapshot.Streams.VideoState == PlaybackWindowsOutputStreamState.Initialized &&
        snapshot.Streams.AudioState == PlaybackWindowsOutputStreamState.Initialized &&
        snapshot.Streams.VideoGeneration == generation &&
        snapshot.Streams.AudioGeneration == generation &&
        snapshot.Buffers.VideoState != PlaybackWindowsOutputBufferState.Disposed &&
        snapshot.Buffers.AudioState != PlaybackWindowsOutputBufferState.Disposed &&
        snapshot.Buffers.VideoGeneration == generation &&
        snapshot.Buffers.AudioGeneration == generation;

    private static bool HasDeviceLoss(
        PlaybackApprovedExternalRuntimeWindowsOperationsSnapshot snapshot) =>
        snapshot.Binding.VideoState == PlaybackWindowsOutputBindingState.DeviceLost ||
        snapshot.Binding.AudioState == PlaybackWindowsOutputBindingState.DeviceLost ||
        snapshot.Streams.VideoState == PlaybackWindowsOutputStreamState.DeviceLost ||
        snapshot.Streams.AudioState == PlaybackWindowsOutputStreamState.DeviceLost ||
        snapshot.Buffers.VideoState == PlaybackWindowsOutputBufferState.DeviceLost ||
        snapshot.Buffers.AudioState == PlaybackWindowsOutputBufferState.DeviceLost;

    private static PlaybackApprovedExternalRuntimeWindowsLifecycleResult MapProduction(
        PlaybackProductionPcpFlvCompositionResult result) => result switch
        {
            PlaybackProductionPcpFlvCompositionResult.PacketAccepted =>
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.PacketAccepted,
            PlaybackProductionPcpFlvCompositionResult.EncodedInputFinalized =>
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.EncodedInputFinalized,
            PlaybackProductionPcpFlvCompositionResult.FrameTransferred =>
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.FrameTransferred,
            PlaybackProductionPcpFlvCompositionResult.NeedMoreInput =>
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.NeedMoreInput,
            PlaybackProductionPcpFlvCompositionResult.StreamEndOfStream =>
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.StreamEndOfStream,
            PlaybackProductionPcpFlvCompositionResult.StreamRequiresDrain =>
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.StreamRequiresDrain,
            PlaybackProductionPcpFlvCompositionResult.StreamNotFlushed =>
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.StreamNotFlushed,
            PlaybackProductionPcpFlvCompositionResult.StreamAlreadyEnded =>
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.StreamAlreadyEnded,
            PlaybackProductionPcpFlvCompositionResult.VideoDelivered =>
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.VideoDelivered,
            PlaybackProductionPcpFlvCompositionResult.AudioDelivered =>
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.AudioDelivered,
            PlaybackProductionPcpFlvCompositionResult.VideoDropped =>
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.VideoDropped,
            PlaybackProductionPcpFlvCompositionResult.Empty =>
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.Empty,
            PlaybackProductionPcpFlvCompositionResult.RetainedWait =>
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.RetainedWait,
            PlaybackProductionPcpFlvCompositionResult.RetainedWouldBlock =>
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.RetainedWouldBlock,
            PlaybackProductionPcpFlvCompositionResult.RetainedInitializationRequired =>
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.RetainedInitializationRequired,
            PlaybackProductionPcpFlvCompositionResult.RetainedBindingRequired =>
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.RetainedBindingRequired,
            PlaybackProductionPcpFlvCompositionResult.RetainedResizeRequired =>
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.RetainedResizeRequired,
            PlaybackProductionPcpFlvCompositionResult.RetainedAudioRecoveryRequired =>
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.RetainedAudioRecoveryRequired,
            PlaybackProductionPcpFlvCompositionResult.ClockNotStarted =>
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.ClockNotStarted,
            PlaybackProductionPcpFlvCompositionResult.ResetRequired =>
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.ResetRequired,
            PlaybackProductionPcpFlvCompositionResult.Reset =>
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.Reset,
            PlaybackProductionPcpFlvCompositionResult.PolicyRejected or
            PlaybackProductionPcpFlvCompositionResult.LicenseReviewPending =>
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.PolicyRejected,
            PlaybackProductionPcpFlvCompositionResult.InvalidConfiguration =>
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.InvalidConfiguration,
            PlaybackProductionPcpFlvCompositionResult.Cancelled =>
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.Cancelled,
            PlaybackProductionPcpFlvCompositionResult.Disposed =>
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.Disposed,
            PlaybackProductionPcpFlvCompositionResult.OutputUnavailable or
            PlaybackProductionPcpFlvCompositionResult.BindingRejected or
            PlaybackProductionPcpFlvCompositionResult.RuntimeRejected or
            PlaybackProductionPcpFlvCompositionResult.AbiGateRejected or
            PlaybackProductionPcpFlvCompositionResult.OwnershipConflict or
            PlaybackProductionPcpFlvCompositionResult.NativeSessionRejected or
            PlaybackProductionPcpFlvCompositionResult.GenerationRejected or
            PlaybackProductionPcpFlvCompositionResult.DeliveryRejected or
            PlaybackProductionPcpFlvCompositionResult.ResetRejected =>
                PlaybackApprovedExternalRuntimeWindowsLifecycleResult.OutputRejected,
            _ => PlaybackApprovedExternalRuntimeWindowsLifecycleResult.Faulted,
        };

    private static PlaybackApprovedExternalRuntimeWindowsLifecycleOptions CloneOptions(
        PlaybackApprovedExternalRuntimeWindowsLifecycleOptions? options)
    {
        options ??= new();
        ArgumentNullException.ThrowIfNull(options.Production);
        ArgumentNullException.ThrowIfNull(options.Parser);
        ArgumentNullException.ThrowIfNull(options.Generation);
        return options with
        {
            Production = options.Production with { },
            Parser = options.Parser with { },
            Generation = options.Generation with { },
        };
    }

    private static PlaybackApprovedExternalRuntimeWindowsLifecycleResult ValidatePreflight(
        string runtimeDirectory,
        PlaybackFfmpegExternalRuntimeManifest manifest,
        PlaybackDecoderBackendPolicy policy,
        long windowToken,
        PlaybackApprovedExternalRuntimeWindowsLifecycleOptions options)
    {
        if (!IsExplicitWindowsRuntimeDirectory(runtimeDirectory) || windowToken <= 0 ||
            options.VideoFormat != PlaybackDecodedVideoPixelFormat.Bgra32 ||
            options.AudioFormat is not (
                PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved or
                PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved) ||
            options.AudioBufferDuration < TimeSpan.FromMilliseconds(1) ||
            options.AudioBufferDuration >
                PlaybackWindowsOutputStreamInitializationBoundary.MaximumAudioBufferDuration ||
            options.InitialPacketSequence < 0 || policy.AllowBundled)
        {
            return PlaybackApprovedExternalRuntimeWindowsLifecycleResult.InvalidConfiguration;
        }

        var rejection = manifest.IsHostSupplied
            ? policy.EvaluateHostAttestedRuntime(manifest.Provenance)
            : policy.Evaluate(manifest.Provenance);
        return rejection == PlaybackDecoderBackendPolicyRejection.None
            ? PlaybackApprovedExternalRuntimeWindowsLifecycleResult.Initialized
            : PlaybackApprovedExternalRuntimeWindowsLifecycleResult.PolicyRejected;
    }

    private static bool IsExplicitWindowsRuntimeDirectory(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 32_767 &&
        value.Length >= 3 &&
        char.IsAsciiLetter(value[0]) &&
        value[1] == ':' &&
        value[2] is '\\' or '/' &&
        value.IndexOf('\0') < 0;
}
