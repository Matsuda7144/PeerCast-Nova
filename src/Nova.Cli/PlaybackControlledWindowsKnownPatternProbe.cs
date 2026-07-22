using System.Globalization;
using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli;

public enum PlaybackControlledWindowsKnownPatternProbeState
{
    Created,
    StartingRunner,
    InitializingOutput,
    Presenting,
    HoldingObservation,
    CapturingEvidence,
    CleaningUp,
    Completed,
    Faulted,
    Disposed,
}

public enum PlaybackControlledWindowsKnownPatternProbeResult
{
    None,
    Completed,
    InvalidConfiguration,
    RunnerRejected,
    OutputInitializationRejected,
    PresentRejected,
    EvidenceRejected,
    Cancelled,
    AlreadyRunning,
    Faulted,
    Disposed,
}

public sealed record PlaybackControlledWindowsKnownPatternProbeOptions
{
    public static readonly TimeSpan MaximumObservationHoldDuration = TimeSpan.FromSeconds(30);

    public TimeSpan ObservationHoldDuration { get; init; } = TimeSpan.FromSeconds(3);

    public PlaybackWindowsPhysicalRunnerOptions Runner { get; init; } = new()
    {
        ClientWidth = PlaybackControlledWindowsKnownPattern.Width,
        ClientHeight = PlaybackControlledWindowsKnownPattern.Height,
        Title = "PeerCast Nova Known Pattern Output Probe",
    };
}

public sealed record PlaybackControlledWindowsKnownPatternProbeSnapshot(
    PlaybackControlledWindowsKnownPatternProbeState State,
    PlaybackControlledWindowsKnownPatternProbeResult Result,
    string PatternId,
    int Width,
    int Height,
    int Stride,
    int NonBlackPixelCount,
    long PresentAttemptCount,
    long PresentAcknowledgementCount,
    long AcceptedByteCount,
    long WouldBlockCount,
    long ResizeCount,
    long DeviceLostCount,
    PlaybackWindowsPhysicalRunnerState RunnerState,
    bool OutputCleanupCompleted,
    bool BorrowedWindowPreserved,
    bool BorrowedDispatcherPreserved);

internal static class PlaybackControlledWindowsKnownPattern
{
    internal const string Id = "Bgra32QuadrantsV1";
    internal const int Width = 320;
    internal const int Height = 240;
    internal const int Stride = Width * 4;
    internal const int NonBlackPixelCount = Width * Height;
    internal const int ByteCount = Stride * Height;

    internal static PlaybackWindowsVideoFrameBuffer Create(long generation)
    {
        var buffer = new byte[ByteCount];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var offset = (y * Stride) + (x * 4);
                var right = x >= Width / 2;
                var bottom = y >= Height / 2;
                if (!right && !bottom)
                {
                    // Red, BGRA.
                    buffer[offset + 2] = byte.MaxValue;
                }
                else if (right && !bottom)
                {
                    // Green, BGRA.
                    buffer[offset + 1] = byte.MaxValue;
                }
                else if (!right)
                {
                    // Blue, BGRA.
                    buffer[offset] = byte.MaxValue;
                }
                else
                {
                    // White, BGRA.
                    buffer[offset] = byte.MaxValue;
                    buffer[offset + 1] = byte.MaxValue;
                    buffer[offset + 2] = byte.MaxValue;
                }

                buffer[offset + 3] = byte.MaxValue;
            }
        }

        return new(
            PlaybackDecodedVideoPixelFormat.Bgra32,
            Width,
            Height,
            Stride,
            buffer,
            generation);
    }
}

public sealed class PlaybackControlledWindowsKnownPatternProbe : IAsyncDisposable
{
    private const long Generation = 1;
    private readonly object _sync = new();
    private readonly PlaybackControlledWindowsKnownPatternProbeOptions _options;
    private readonly IPlaybackControlledWindowsKnownPatternProbeOperations _operations;
    private PlaybackControlledWindowsKnownPatternProbeState _state =
        PlaybackControlledWindowsKnownPatternProbeState.Created;
    private PlaybackControlledWindowsKnownPatternProbeResult _result;
    private long _presentAttemptCount;
    private long _presentAcknowledgementCount;
    private long _acceptedByteCount;
    private long _wouldBlockCount;
    private long _resizeCount;
    private long _deviceLostCount;
    private bool _outputCleanupCompleted;
    private bool _borrowedWindowPreserved;
    private bool _borrowedDispatcherPreserved;
    private bool _isRunning;
    private bool _isDisposed;

    public PlaybackControlledWindowsKnownPatternProbe(
        PlaybackControlledWindowsKnownPatternProbeOptions? options = null)
        : this(
            CloneOptions(options),
            new SystemPlaybackControlledWindowsKnownPatternProbeOperations(CloneOptions(options)))
    {
    }

    internal PlaybackControlledWindowsKnownPatternProbe(
        PlaybackControlledWindowsKnownPatternProbeOptions options,
        IPlaybackControlledWindowsKnownPatternProbeOperations operations)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(operations);
        _options = CloneOptions(options);
        _operations = operations;
    }

    public PlaybackControlledWindowsKnownPatternProbeSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return new(
                    _state,
                    _result,
                    PlaybackControlledWindowsKnownPattern.Id,
                    PlaybackControlledWindowsKnownPattern.Width,
                    PlaybackControlledWindowsKnownPattern.Height,
                    PlaybackControlledWindowsKnownPattern.Stride,
                    PlaybackControlledWindowsKnownPattern.NonBlackPixelCount,
                    _presentAttemptCount,
                    _presentAcknowledgementCount,
                    _acceptedByteCount,
                    _wouldBlockCount,
                    _resizeCount,
                    _deviceLostCount,
                    _operations.Snapshot.Runner.State,
                    _outputCleanupCompleted,
                    _borrowedWindowPreserved,
                    _borrowedDispatcherPreserved);
            }
        }
    }

    public async ValueTask<PlaybackControlledWindowsKnownPatternProbeResult> RunAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_isDisposed)
            {
                return SetResult(PlaybackControlledWindowsKnownPatternProbeResult.Disposed);
            }

            if (_isRunning || _state != PlaybackControlledWindowsKnownPatternProbeState.Created)
            {
                return SetResult(PlaybackControlledWindowsKnownPatternProbeResult.AlreadyRunning);
            }

            if (!ValidateOptions(_options))
            {
                _state = PlaybackControlledWindowsKnownPatternProbeState.Faulted;
                return SetResult(
                    PlaybackControlledWindowsKnownPatternProbeResult.InvalidConfiguration);
            }

            _isRunning = true;
        }

        var runnerStarted = false;
        var outputInitialized = false;
        try
        {
            SetState(PlaybackControlledWindowsKnownPatternProbeState.StartingRunner);
            var runner = await _operations.StartRunnerAsync(cancellationToken).ConfigureAwait(false);
            if (runner != PlaybackWindowsPhysicalRunnerResult.Started)
            {
                return Terminal(runner == PlaybackWindowsPhysicalRunnerResult.Cancelled
                    ? PlaybackControlledWindowsKnownPatternProbeResult.Cancelled
                    : PlaybackControlledWindowsKnownPatternProbeResult.RunnerRejected);
            }

            runnerStarted = true;
            SetState(PlaybackControlledWindowsKnownPatternProbeState.InitializingOutput);
            var initialized = await _operations.InitializeOutputAsync(
                Generation,
                cancellationToken).ConfigureAwait(false);
            if (initialized != PlaybackControlledWindowsKnownPatternOutputResult.Initialized)
            {
                return Terminal(initialized == PlaybackControlledWindowsKnownPatternOutputResult.Cancelled
                    ? PlaybackControlledWindowsKnownPatternProbeResult.Cancelled
                    : PlaybackControlledWindowsKnownPatternProbeResult.OutputInitializationRejected);
            }

            outputInitialized = true;
            SetState(PlaybackControlledWindowsKnownPatternProbeState.Presenting);
            var presented = await _operations.PresentAsync(
                PlaybackControlledWindowsKnownPattern.Create(Generation),
                cancellationToken).ConfigureAwait(false);
            if (presented != PlaybackWindowsOutputBufferProcessingResult.Processed)
            {
                return Terminal(presented == PlaybackWindowsOutputBufferProcessingResult.Cancelled
                    ? PlaybackControlledWindowsKnownPatternProbeResult.Cancelled
                    : PlaybackControlledWindowsKnownPatternProbeResult.PresentRejected);
            }

            if (_options.ObservationHoldDuration > TimeSpan.Zero)
            {
                SetState(PlaybackControlledWindowsKnownPatternProbeState.HoldingObservation);
                await Task.Delay(_options.ObservationHoldDuration, cancellationToken)
                    .ConfigureAwait(false);
            }

            SetState(PlaybackControlledWindowsKnownPatternProbeState.CapturingEvidence);
            var native = _operations.Snapshot.Buffers.NativeEvidence;
            RecordEvidence(native);
            if (!IsAcceptedEvidence(native))
            {
                return Terminal(PlaybackControlledWindowsKnownPatternProbeResult.EvidenceRejected);
            }

            SetState(PlaybackControlledWindowsKnownPatternProbeState.CleaningUp);
            await _operations.CleanupOutputAsync().ConfigureAwait(false);
            outputInitialized = false;
            var preservation = await _operations.VerifyBorrowedOwnersAsync(cancellationToken)
                .ConfigureAwait(false);
            lock (_sync)
            {
                _outputCleanupCompleted = preservation.OutputCleanupCompleted;
                _borrowedWindowPreserved = preservation.WindowPreserved;
                _borrowedDispatcherPreserved = preservation.DispatcherPreserved;
            }

            if (!preservation.OutputCleanupCompleted || !preservation.WindowPreserved ||
                !preservation.DispatcherPreserved)
            {
                return Terminal(PlaybackControlledWindowsKnownPatternProbeResult.EvidenceRejected);
            }

            return Complete();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Terminal(PlaybackControlledWindowsKnownPatternProbeResult.Cancelled);
        }
        catch
        {
            return Terminal(PlaybackControlledWindowsKnownPatternProbeResult.Faulted);
        }
        finally
        {
            if (outputInitialized)
            {
                try
                {
                    await _operations.CleanupOutputAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Cleanup remains fail-closed and best effort after a terminal result.
                }
            }

            if (runnerStarted)
            {
                try
                {
                    await _operations.StopRunnerAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Runner shutdown remains best effort after a terminal result.
                }
            }

            lock (_sync)
            {
                _isRunning = false;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
        }

        await _operations.DisposeAsync().ConfigureAwait(false);
        lock (_sync)
        {
            _state = PlaybackControlledWindowsKnownPatternProbeState.Disposed;
            _result = PlaybackControlledWindowsKnownPatternProbeResult.Disposed;
        }
    }

    private void RecordEvidence(PlaybackWindowsOutputNativeEvidenceSnapshot native)
    {
        lock (_sync)
        {
            _presentAttemptCount = native.VideoPresentAttemptCount;
            _presentAcknowledgementCount = native.VideoPresentAcknowledgementCount;
            _acceptedByteCount = native.VideoAcceptedByteCount;
            _wouldBlockCount = native.VideoWouldBlockCount;
            _resizeCount = native.VideoResizeCount;
            _deviceLostCount = native.VideoDeviceLostCount;
        }
    }

    private static bool IsAcceptedEvidence(PlaybackWindowsOutputNativeEvidenceSnapshot native) =>
        native.Source == PlaybackControlledWindowsOutputEvidenceSource.SystemWindows &&
        !native.CounterOverflowed &&
        !native.VideoConfigurationConflict &&
        native.VideoPixelFormat == PlaybackDecodedVideoPixelFormat.Bgra32 &&
        native.VideoWidth == PlaybackControlledWindowsKnownPattern.Width &&
        native.VideoHeight == PlaybackControlledWindowsKnownPattern.Height &&
        native.VideoStride == PlaybackControlledWindowsKnownPattern.Stride &&
        native.VideoGeneration == Generation &&
        native.VideoPresentAttemptCount == 1 &&
        native.VideoPresentAcknowledgementCount == 1 &&
        native.VideoAcceptedByteCount == PlaybackControlledWindowsKnownPattern.ByteCount &&
        native.VideoWouldBlockCount == 0 &&
        native.VideoResizeCount == 0 &&
        native.VideoDeviceLostCount == 0;

    private PlaybackControlledWindowsKnownPatternProbeResult Complete()
    {
        lock (_sync)
        {
            _state = PlaybackControlledWindowsKnownPatternProbeState.Completed;
            return SetResult(PlaybackControlledWindowsKnownPatternProbeResult.Completed);
        }
    }

    private PlaybackControlledWindowsKnownPatternProbeResult Terminal(
        PlaybackControlledWindowsKnownPatternProbeResult result)
    {
        lock (_sync)
        {
            _state = PlaybackControlledWindowsKnownPatternProbeState.Faulted;
            return SetResult(result);
        }
    }

    private void SetState(PlaybackControlledWindowsKnownPatternProbeState state)
    {
        lock (_sync)
        {
            _state = state;
        }
    }

    private PlaybackControlledWindowsKnownPatternProbeResult SetResult(
        PlaybackControlledWindowsKnownPatternProbeResult result)
    {
        _result = result;
        return result;
    }

    private static bool ValidateOptions(
        PlaybackControlledWindowsKnownPatternProbeOptions options) =>
        options.ObservationHoldDuration >= TimeSpan.Zero &&
        options.ObservationHoldDuration <=
            PlaybackControlledWindowsKnownPatternProbeOptions.MaximumObservationHoldDuration &&
        options.Runner is not null;

    private static PlaybackControlledWindowsKnownPatternProbeOptions CloneOptions(
        PlaybackControlledWindowsKnownPatternProbeOptions? options)
    {
        options ??= new();
        ArgumentNullException.ThrowIfNull(options.Runner);
        return options with { Runner = options.Runner with { } };
    }
}

public static class PlaybackControlledWindowsKnownPatternProbeRecordCodec
{
    public const string Header = "PEERCAST-NOVA-CONTROLLED-WINDOWS-KNOWN-PATTERN-PROBE 1";
    public const string Footer = "END-PEERCAST-NOVA-CONTROLLED-WINDOWS-KNOWN-PATTERN-PROBE";

    public static IReadOnlyList<string> Create(
        PlaybackControlledWindowsKnownPatternProbeSnapshot snapshot) =>
    [
        Header,
        "Milestone: 0.0.10d Phase 5",
        $"Result: {snapshot.Result}",
        $"Pattern: {snapshot.PatternId}",
        $"Width: {snapshot.Width.ToString(CultureInfo.InvariantCulture)}",
        $"Height: {snapshot.Height.ToString(CultureInfo.InvariantCulture)}",
        $"Stride: {snapshot.Stride.ToString(CultureInfo.InvariantCulture)}",
        $"Non-Black-Pixels: {snapshot.NonBlackPixelCount.ToString(CultureInfo.InvariantCulture)}",
        $"Present-Attempts: {snapshot.PresentAttemptCount.ToString(CultureInfo.InvariantCulture)}",
        $"Present-Acknowledgements: {snapshot.PresentAcknowledgementCount.ToString(CultureInfo.InvariantCulture)}",
        $"Accepted-Bytes: {snapshot.AcceptedByteCount.ToString(CultureInfo.InvariantCulture)}",
        $"Present-WouldBlock: {snapshot.WouldBlockCount.ToString(CultureInfo.InvariantCulture)}",
        $"Resize-Count: {snapshot.ResizeCount.ToString(CultureInfo.InvariantCulture)}",
        $"Device-Lost-Count: {snapshot.DeviceLostCount.ToString(CultureInfo.InvariantCulture)}",
        $"Runner-State: {snapshot.RunnerState}",
        $"Output-Cleanup-Completed: {snapshot.OutputCleanupCompleted}",
        $"Borrowed-Window-Preserved: {snapshot.BorrowedWindowPreserved}",
        $"Borrowed-Dispatcher-Preserved: {snapshot.BorrowedDispatcherPreserved}",
        "FFmpeg-Runtime-Used: False",
        "External-Media-Or-Payload-Published: False",
        Footer,
    ];

    public static async ValueTask WriteAsync(
        TextWriter output,
        PlaybackControlledWindowsKnownPatternProbeSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        foreach (var line in Create(snapshot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await output.WriteLineAsync(line).ConfigureAwait(false);
        }
    }
}

internal enum PlaybackControlledWindowsKnownPatternOutputResult
{
    Initialized,
    Cancelled,
    Rejected,
}

internal readonly record struct PlaybackControlledWindowsKnownPatternProbeOwnedSnapshot(
    PlaybackWindowsPhysicalRunnerSnapshot Runner,
    PlaybackWindowsOutputBufferSnapshot Buffers);

internal readonly record struct PlaybackControlledWindowsKnownPatternProbePreservation(
    bool OutputCleanupCompleted,
    bool WindowPreserved,
    bool DispatcherPreserved);

internal interface IPlaybackControlledWindowsKnownPatternProbeOperations : IAsyncDisposable
{
    PlaybackControlledWindowsKnownPatternProbeOwnedSnapshot Snapshot { get; }

    ValueTask<PlaybackWindowsPhysicalRunnerResult> StartRunnerAsync(CancellationToken token);

    ValueTask<PlaybackControlledWindowsKnownPatternOutputResult> InitializeOutputAsync(
        long generation,
        CancellationToken token);

    ValueTask<PlaybackWindowsOutputBufferProcessingResult> PresentAsync(
        PlaybackWindowsVideoFrameBuffer frame,
        CancellationToken token);

    ValueTask CleanupOutputAsync();

    ValueTask<PlaybackControlledWindowsKnownPatternProbePreservation>
        VerifyBorrowedOwnersAsync(CancellationToken token);

    ValueTask<PlaybackWindowsPhysicalRunnerResult> StopRunnerAsync();
}

internal sealed class SystemPlaybackControlledWindowsKnownPatternProbeOperations :
    IPlaybackControlledWindowsKnownPatternProbeOperations
{
    private readonly object _sync = new();
    private readonly PlaybackWindowsPhysicalRunnerHost _runner;
    private PlaybackWindowsOutputActivationBoundary? _activation;
    private PlaybackWindowsOutputResourceBindingBoundary? _binding;
    private PlaybackWindowsOutputStreamInitializationBoundary? _streams;
    private PlaybackWindowsOutputBufferProcessingBoundary? _buffers;
    private PlaybackWindowsOutputBufferSnapshot _lastBuffers;
    private long _borrowedWindowToken;
    private bool _cleanupCompleted;
    private bool _isDisposed;

    internal SystemPlaybackControlledWindowsKnownPatternProbeOperations(
        PlaybackControlledWindowsKnownPatternProbeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _runner = new(options.Runner);
    }

    public PlaybackControlledWindowsKnownPatternProbeOwnedSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return new(_runner.Snapshot, _buffers?.Snapshot ?? _lastBuffers);
            }
        }
    }

    public ValueTask<PlaybackWindowsPhysicalRunnerResult> StartRunnerAsync(
        CancellationToken token) => _runner.StartAsync(token);

    public async ValueTask<PlaybackControlledWindowsKnownPatternOutputResult>
        InitializeOutputAsync(long generation, CancellationToken token)
    {
        var invocation = await _runner.InvokeAsync(
            async cancellationToken =>
            {
                if (!_runner.HasAccess || generation < 0)
                {
                    return PlaybackControlledWindowsKnownPatternOutputResult.Rejected;
                }

                var runner = _runner.Snapshot;
                if (runner.State != PlaybackWindowsPhysicalRunnerState.Running ||
                    runner.WindowToken <= 0 || runner.AdmissionClosed)
                {
                    return PlaybackControlledWindowsKnownPatternOutputResult.Rejected;
                }

                var activation = new PlaybackWindowsOutputActivationBoundary();
                if (activation.Activate() != PlaybackWindowsOutputActivationResult.Activated)
                {
                    activation.Dispose();
                    return PlaybackControlledWindowsKnownPatternOutputResult.Rejected;
                }

                var binding = new PlaybackWindowsOutputResourceBindingBoundary(activation);
                var bind = await binding.ConfigureAsync(
                    new(
                        PlaybackDecodedVideoPixelFormat.Bgra32,
                        PlaybackControlledWindowsKnownPattern.Width,
                        PlaybackControlledWindowsKnownPattern.Height,
                        PlaybackControlledWindowsKnownPattern.Stride,
                        generation),
                    new(
                        PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved,
                        48_000,
                        2,
                        generation),
                    cancellationToken).ConfigureAwait(false);
                if (bind != PlaybackWindowsOutputBindingResult.Bound)
                {
                    binding.Dispose();
                    activation.Dispose();
                    return bind == PlaybackWindowsOutputBindingResult.Cancelled
                        ? PlaybackControlledWindowsKnownPatternOutputResult.Cancelled
                        : PlaybackControlledWindowsKnownPatternOutputResult.Rejected;
                }

                var streams = new PlaybackWindowsOutputStreamInitializationBoundary(
                    activation,
                    binding);
                var initialized = await streams.InitializeAsync(
                    new(
                        runner.WindowToken,
                        PlaybackControlledWindowsKnownPattern.Width,
                        PlaybackControlledWindowsKnownPattern.Height,
                        PlaybackDecodedVideoPixelFormat.Bgra32,
                        generation),
                    new(
                        PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved,
                        48_000,
                        2,
                        TimeSpan.FromMilliseconds(100),
                        generation),
                    cancellationToken).ConfigureAwait(false);
                if (initialized != PlaybackWindowsOutputStreamInitializationResult.Initialized)
                {
                    streams.Dispose();
                    binding.Dispose();
                    activation.Dispose();
                    return initialized == PlaybackWindowsOutputStreamInitializationResult.Cancelled
                        ? PlaybackControlledWindowsKnownPatternOutputResult.Cancelled
                        : PlaybackControlledWindowsKnownPatternOutputResult.Rejected;
                }

                var buffers = new PlaybackWindowsOutputBufferProcessingBoundary(
                    activation,
                    binding,
                    streams);
                lock (_sync)
                {
                    _activation = activation;
                    _binding = binding;
                    _streams = streams;
                    _buffers = buffers;
                    _borrowedWindowToken = runner.WindowToken;
                    _cleanupCompleted = false;
                }

                return PlaybackControlledWindowsKnownPatternOutputResult.Initialized;
            },
            token).ConfigureAwait(false);

        return invocation.Result switch
        {
            PlaybackWindowsOutputDispatcherInvocationResult.Completed => invocation.Value,
            PlaybackWindowsOutputDispatcherInvocationResult.Cancelled =>
                PlaybackControlledWindowsKnownPatternOutputResult.Cancelled,
            _ => PlaybackControlledWindowsKnownPatternOutputResult.Rejected,
        };
    }

    public async ValueTask<PlaybackWindowsOutputBufferProcessingResult> PresentAsync(
        PlaybackWindowsVideoFrameBuffer frame,
        CancellationToken token)
    {
        var invocation = await _runner.InvokeAsync(
            async cancellationToken =>
            {
                PlaybackWindowsOutputBufferProcessingBoundary? buffers;
                lock (_sync)
                {
                    buffers = _buffers;
                }

                return buffers is null
                    ? PlaybackWindowsOutputBufferProcessingResult.InitializationRequired
                    : await buffers.ProcessVideoAsync(frame, cancellationToken)
                        .ConfigureAwait(false);
            },
            token).ConfigureAwait(false);

        return invocation.Result switch
        {
            PlaybackWindowsOutputDispatcherInvocationResult.Completed => invocation.Value,
            PlaybackWindowsOutputDispatcherInvocationResult.Cancelled =>
                PlaybackWindowsOutputBufferProcessingResult.Cancelled,
            PlaybackWindowsOutputDispatcherInvocationResult.Shutdown =>
                PlaybackWindowsOutputBufferProcessingResult.Disposed,
            _ => PlaybackWindowsOutputBufferProcessingResult.Failure,
        };
    }

    public async ValueTask CleanupOutputAsync()
    {
        PlaybackWindowsOutputBufferProcessingBoundary? buffers;
        PlaybackWindowsOutputStreamInitializationBoundary? streams;
        PlaybackWindowsOutputResourceBindingBoundary? binding;
        PlaybackWindowsOutputActivationBoundary? activation;
        lock (_sync)
        {
            buffers = _buffers;
            streams = _streams;
            binding = _binding;
            activation = _activation;
        }

        if (buffers is null && streams is null && binding is null && activation is null)
        {
            return;
        }

        var invocation = await _runner.InvokeAsync(
            cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_runner.HasAccess)
                {
                    return ValueTask.FromResult(false);
                }

                if (buffers is not null)
                {
                    _lastBuffers = buffers.Snapshot;
                    buffers.Dispose();
                }

                streams?.Dispose();
                binding?.Dispose();
                activation?.Dispose();
                return ValueTask.FromResult(true);
            },
            CancellationToken.None).ConfigureAwait(false);

        lock (_sync)
        {
            _buffers = null;
            _streams = null;
            _binding = null;
            _activation = null;
            _cleanupCompleted = invocation.Result ==
                PlaybackWindowsOutputDispatcherInvocationResult.Completed && invocation.Value;
        }
    }

    public async ValueTask<PlaybackControlledWindowsKnownPatternProbePreservation>
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
        bool cleanup;
        long window;
        lock (_sync)
        {
            cleanup = _cleanupCompleted;
            window = _borrowedWindowToken;
        }

        var windowPreserved = before.State == PlaybackWindowsPhysicalRunnerState.Running &&
            after.State == PlaybackWindowsPhysicalRunnerState.Running &&
            before.WindowToken == window && after.WindowToken == window && window > 0 &&
            before.HasMessageLoop && after.HasMessageLoop && !after.AdmissionClosed;
        var dispatcherPreserved = invocation.Result ==
            PlaybackWindowsOutputDispatcherInvocationResult.Completed && invocation.Value;
        return new(cleanup, windowPreserved, dispatcherPreserved);
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
        await CleanupOutputAsync().ConfigureAwait(false);
        await _runner.DisposeAsync().ConfigureAwait(false);
    }
}
