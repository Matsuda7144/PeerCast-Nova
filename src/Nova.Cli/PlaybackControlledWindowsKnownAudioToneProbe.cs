using System.Buffers.Binary;
using System.Globalization;
using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli;

public enum PlaybackControlledWindowsKnownAudioToneProbeState
{
    Created,
    StartingRunner,
    InitializingOutput,
    Writing,
    HoldingObservation,
    CapturingEvidence,
    CleaningUp,
    Completed,
    Faulted,
    Disposed,
}

public enum PlaybackControlledWindowsKnownAudioToneProbeResult
{
    None,
    Completed,
    InvalidConfiguration,
    RunnerRejected,
    OutputInitializationRejected,
    WriteRejected,
    EvidenceRejected,
    Cancelled,
    AlreadyRunning,
    Faulted,
    Disposed,
}

public enum PlaybackControlledWindowsKnownAudioToneProbeEvidence
{
    Rejected,
    Accepted,
}

public sealed record PlaybackControlledWindowsKnownAudioToneProbeOptions
{
    public static readonly TimeSpan MaximumObservationHoldDuration = TimeSpan.FromSeconds(30);

    public TimeSpan ObservationHoldDuration { get; init; } = TimeSpan.FromSeconds(1);

    public PlaybackWindowsPhysicalRunnerOptions Runner { get; init; } = new()
    {
        ClientWidth = 320,
        ClientHeight = 240,
        Title = "PeerCast Nova Known Audio Tone Probe",
    };
}

public sealed record PlaybackControlledWindowsKnownAudioToneProbeSnapshot(
    PlaybackControlledWindowsKnownAudioToneProbeState State,
    PlaybackControlledWindowsKnownAudioToneProbeResult Result,
    PlaybackControlledWindowsKnownAudioToneProbeEvidence Evidence,
    PlaybackDecodedAudioSampleFormat SampleFormat,
    int SampleRate,
    int Channels,
    int ToneHz,
    int DurationMilliseconds,
    int SampleFrames,
    int PayloadBytes,
    long WriteAttempts,
    long WritesAcknowledged,
    long StartAcknowledged,
    long RetainedWouldBlock,
    PlaybackWindowsPhysicalRunnerState RunnerState,
    bool OutputCleanupCompleted,
    bool BorrowedWindowPreserved,
    bool BorrowedDispatcherPreserved);

internal static class PlaybackControlledWindowsKnownAudioTone
{
    internal const PlaybackDecodedAudioSampleFormat SampleFormat =
        PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved;
    internal const int SampleRate = 48_000;
    internal const int Channels = 2;
    internal const int ToneHz = 1_000;
    internal const int DurationMilliseconds = 80;
    internal const int SampleFrames = 3_840;
    internal const int FadeFrames = 240;
    internal const float PeakAmplitude = 0.20F;
    internal const int PayloadBytes = SampleFrames * Channels * sizeof(float);

    internal static PlaybackWindowsAudioSampleBuffer Create(long generation)
    {
        var payload = new byte[PayloadBytes];
        for (var frame = 0; frame < SampleFrames; frame++)
        {
            var fade = 1.0;
            if (frame < FadeFrames)
            {
                fade = frame / (double)(FadeFrames - 1);
            }
            else if (frame >= SampleFrames - FadeFrames)
            {
                fade = (SampleFrames - 1 - frame) / (double)(FadeFrames - 1);
            }

            var value = (float)(PeakAmplitude * fade *
                Math.Sin(2.0 * Math.PI * ToneHz * frame / SampleRate));
            for (var channel = 0; channel < Channels; channel++)
            {
                var offset = ((frame * Channels) + channel) * sizeof(float);
                BinaryPrimitives.WriteInt32LittleEndian(
                    payload.AsSpan(offset, sizeof(float)),
                    BitConverter.SingleToInt32Bits(value));
            }
        }

        return new(SampleFormat, SampleRate, Channels, SampleFrames, payload, generation);
    }
}

public sealed class PlaybackControlledWindowsKnownAudioToneProbe : IAsyncDisposable
{
    private const long Generation = 1;
    private readonly object _sync = new();
    private readonly PlaybackControlledWindowsKnownAudioToneProbeOptions _options;
    private readonly IPlaybackControlledWindowsKnownAudioToneProbeOperations _operations;
    private PlaybackControlledWindowsKnownAudioToneProbeState _state =
        PlaybackControlledWindowsKnownAudioToneProbeState.Created;
    private PlaybackControlledWindowsKnownAudioToneProbeResult _result;
    private PlaybackControlledWindowsKnownAudioToneProbeEvidence _evidence;
    private long _writeAttempts;
    private long _writesAcknowledged;
    private long _startAcknowledged;
    private long _retainedWouldBlock;
    private bool _outputCleanupCompleted;
    private bool _borrowedWindowPreserved;
    private bool _borrowedDispatcherPreserved;
    private bool _isRunning;
    private bool _isDisposed;

    public PlaybackControlledWindowsKnownAudioToneProbe(
        PlaybackControlledWindowsKnownAudioToneProbeOptions? options = null)
        : this(
            CloneOptions(options),
            new SystemPlaybackControlledWindowsKnownAudioToneProbeOperations(
                CloneOptions(options)))
    {
    }

    internal PlaybackControlledWindowsKnownAudioToneProbe(
        PlaybackControlledWindowsKnownAudioToneProbeOptions options,
        IPlaybackControlledWindowsKnownAudioToneProbeOperations operations)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(operations);
        _options = CloneOptions(options);
        _operations = operations;
    }

    public PlaybackControlledWindowsKnownAudioToneProbeSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return new(
                    _state,
                    _result,
                    _evidence,
                    PlaybackControlledWindowsKnownAudioTone.SampleFormat,
                    PlaybackControlledWindowsKnownAudioTone.SampleRate,
                    PlaybackControlledWindowsKnownAudioTone.Channels,
                    PlaybackControlledWindowsKnownAudioTone.ToneHz,
                    PlaybackControlledWindowsKnownAudioTone.DurationMilliseconds,
                    PlaybackControlledWindowsKnownAudioTone.SampleFrames,
                    PlaybackControlledWindowsKnownAudioTone.PayloadBytes,
                    _writeAttempts,
                    _writesAcknowledged,
                    _startAcknowledged,
                    _retainedWouldBlock,
                    _operations.Snapshot.Runner.State,
                    _outputCleanupCompleted,
                    _borrowedWindowPreserved,
                    _borrowedDispatcherPreserved);
            }
        }
    }

    public async ValueTask<PlaybackControlledWindowsKnownAudioToneProbeResult> RunAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_isDisposed)
            {
                return SetResult(PlaybackControlledWindowsKnownAudioToneProbeResult.Disposed);
            }

            if (_isRunning || _state != PlaybackControlledWindowsKnownAudioToneProbeState.Created)
            {
                return SetResult(
                    PlaybackControlledWindowsKnownAudioToneProbeResult.AlreadyRunning);
            }

            if (!ValidateOptions(_options))
            {
                _state = PlaybackControlledWindowsKnownAudioToneProbeState.Faulted;
                return SetResult(
                    PlaybackControlledWindowsKnownAudioToneProbeResult.InvalidConfiguration);
            }

            _isRunning = true;
        }

        var runnerStarted = false;
        var outputInitialized = false;
        try
        {
            SetState(PlaybackControlledWindowsKnownAudioToneProbeState.StartingRunner);
            var runner = await _operations.StartRunnerAsync(cancellationToken)
                .ConfigureAwait(false);
            if (runner != PlaybackWindowsPhysicalRunnerResult.Started)
            {
                return Terminal(runner == PlaybackWindowsPhysicalRunnerResult.Cancelled
                    ? PlaybackControlledWindowsKnownAudioToneProbeResult.Cancelled
                    : PlaybackControlledWindowsKnownAudioToneProbeResult.RunnerRejected);
            }

            runnerStarted = true;
            SetState(PlaybackControlledWindowsKnownAudioToneProbeState.InitializingOutput);
            var initialized = await _operations.InitializeOutputAsync(
                Generation,
                cancellationToken).ConfigureAwait(false);
            if (initialized != PlaybackControlledWindowsKnownAudioToneOutputResult.Initialized)
            {
                return Terminal(initialized ==
                    PlaybackControlledWindowsKnownAudioToneOutputResult.Cancelled
                    ? PlaybackControlledWindowsKnownAudioToneProbeResult.Cancelled
                    : PlaybackControlledWindowsKnownAudioToneProbeResult
                        .OutputInitializationRejected);
            }

            outputInitialized = true;
            SetState(PlaybackControlledWindowsKnownAudioToneProbeState.Writing);
            var written = await _operations.WriteAsync(
                PlaybackControlledWindowsKnownAudioTone.Create(Generation),
                cancellationToken).ConfigureAwait(false);
            if (written != PlaybackWindowsOutputBufferProcessingResult.Processed)
            {
                RecordEvidence(_operations.Snapshot.Buffers.NativeEvidence);
                return Terminal(written == PlaybackWindowsOutputBufferProcessingResult.Cancelled
                    ? PlaybackControlledWindowsKnownAudioToneProbeResult.Cancelled
                    : PlaybackControlledWindowsKnownAudioToneProbeResult.WriteRejected);
            }

            if (_options.ObservationHoldDuration > TimeSpan.Zero)
            {
                SetState(PlaybackControlledWindowsKnownAudioToneProbeState.HoldingObservation);
                await Task.Delay(_options.ObservationHoldDuration, cancellationToken)
                    .ConfigureAwait(false);
            }

            SetState(PlaybackControlledWindowsKnownAudioToneProbeState.CapturingEvidence);
            var native = _operations.Snapshot.Buffers.NativeEvidence;
            RecordEvidence(native);
            if (!IsAcceptedEvidence(native))
            {
                return Terminal(PlaybackControlledWindowsKnownAudioToneProbeResult.EvidenceRejected);
            }

            SetState(PlaybackControlledWindowsKnownAudioToneProbeState.CleaningUp);
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
                return Terminal(PlaybackControlledWindowsKnownAudioToneProbeResult.EvidenceRejected);
            }

            lock (_sync)
            {
                _evidence = PlaybackControlledWindowsKnownAudioToneProbeEvidence.Accepted;
                _state = PlaybackControlledWindowsKnownAudioToneProbeState.Completed;
                return SetResult(PlaybackControlledWindowsKnownAudioToneProbeResult.Completed);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Terminal(PlaybackControlledWindowsKnownAudioToneProbeResult.Cancelled);
        }
        catch
        {
            return Terminal(PlaybackControlledWindowsKnownAudioToneProbeResult.Faulted);
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
            _state = PlaybackControlledWindowsKnownAudioToneProbeState.Disposed;
            _result = PlaybackControlledWindowsKnownAudioToneProbeResult.Disposed;
        }
    }

    private void RecordEvidence(PlaybackWindowsOutputNativeEvidenceSnapshot native)
    {
        lock (_sync)
        {
            _writeAttempts = native.AudioWriteAttemptCount;
            _writesAcknowledged = native.AudioWriteAcknowledgementCount;
            _startAcknowledged = native.AudioStartAcknowledgementCount;
            _retainedWouldBlock = native.AudioWouldBlockCount;
        }
    }

    private static bool IsAcceptedEvidence(PlaybackWindowsOutputNativeEvidenceSnapshot native) =>
        native.Source == PlaybackControlledWindowsOutputEvidenceSource.SystemWindows &&
        !native.CounterOverflowed &&
        !native.AudioConfigurationConflict &&
        native.AudioSampleFormat == PlaybackControlledWindowsKnownAudioTone.SampleFormat &&
        native.AudioSampleRate == PlaybackControlledWindowsKnownAudioTone.SampleRate &&
        native.AudioChannelCount == PlaybackControlledWindowsKnownAudioTone.Channels &&
        native.AudioGeneration == Generation &&
        native.AudioWriteAttemptCount == 1 &&
        native.AudioWriteAcknowledgementCount == 1 &&
        native.AudioAcceptedFrameCount == PlaybackControlledWindowsKnownAudioTone.SampleFrames &&
        native.AudioAcceptedByteCount == PlaybackControlledWindowsKnownAudioTone.PayloadBytes &&
        native.AudioWouldBlockCount == 0 &&
        native.AudioStartAcknowledgementCount == 1 &&
        native.AudioDeviceLostCount == 0 &&
        native.VideoPresentAttemptCount == 0;

    private PlaybackControlledWindowsKnownAudioToneProbeResult Terminal(
        PlaybackControlledWindowsKnownAudioToneProbeResult result)
    {
        lock (_sync)
        {
            _evidence = PlaybackControlledWindowsKnownAudioToneProbeEvidence.Rejected;
            _state = PlaybackControlledWindowsKnownAudioToneProbeState.Faulted;
            return SetResult(result);
        }
    }

    private void SetState(PlaybackControlledWindowsKnownAudioToneProbeState state)
    {
        lock (_sync)
        {
            _state = state;
        }
    }

    private PlaybackControlledWindowsKnownAudioToneProbeResult SetResult(
        PlaybackControlledWindowsKnownAudioToneProbeResult result)
    {
        _result = result;
        return result;
    }

    private static bool ValidateOptions(
        PlaybackControlledWindowsKnownAudioToneProbeOptions options) =>
        options.ObservationHoldDuration >= TimeSpan.Zero &&
        options.ObservationHoldDuration <=
            PlaybackControlledWindowsKnownAudioToneProbeOptions.MaximumObservationHoldDuration &&
        options.Runner is not null;

    private static PlaybackControlledWindowsKnownAudioToneProbeOptions CloneOptions(
        PlaybackControlledWindowsKnownAudioToneProbeOptions? options)
    {
        options ??= new();
        ArgumentNullException.ThrowIfNull(options.Runner);
        return options with { Runner = options.Runner with { } };
    }
}

public static class PlaybackControlledWindowsKnownAudioToneProbeRecordCodec
{
    public const string Header =
        "PEERCAST-NOVA-CONTROLLED-WINDOWS-KNOWN-AUDIO-TONE-PROBE 1";
    public const string Footer =
        "END-PEERCAST-NOVA-CONTROLLED-WINDOWS-KNOWN-AUDIO-TONE-PROBE";

    public static IReadOnlyList<string> Create(
        PlaybackControlledWindowsKnownAudioToneProbeSnapshot snapshot) =>
    [
        Header,
        "Milestone: 0.0.10d Phase 5",
        $"Result: {snapshot.Result}",
        $"Evidence: {snapshot.Evidence}",
        $"Sample-Format: {snapshot.SampleFormat}",
        $"Sample-Rate: {snapshot.SampleRate.ToString(CultureInfo.InvariantCulture)}",
        $"Channels: {snapshot.Channels.ToString(CultureInfo.InvariantCulture)}",
        $"Tone-Hz: {snapshot.ToneHz.ToString(CultureInfo.InvariantCulture)}",
        $"Duration-Milliseconds: {snapshot.DurationMilliseconds.ToString(CultureInfo.InvariantCulture)}",
        $"Sample-Frames: {snapshot.SampleFrames.ToString(CultureInfo.InvariantCulture)}",
        $"Payload-Bytes: {snapshot.PayloadBytes.ToString(CultureInfo.InvariantCulture)}",
        $"Write-Attempts: {snapshot.WriteAttempts.ToString(CultureInfo.InvariantCulture)}",
        $"Writes-Acknowledged: {snapshot.WritesAcknowledged.ToString(CultureInfo.InvariantCulture)}",
        $"Start-Acknowledged: {snapshot.StartAcknowledged == 1}",
        $"Retained-WouldBlock: {snapshot.RetainedWouldBlock.ToString(CultureInfo.InvariantCulture)}",
        $"Runner-State: {snapshot.RunnerState}",
        $"Lifecycle-State: {(snapshot.OutputCleanupCompleted ? "Disposed" : "Faulted")}",
        $"Borrowed-Window-Preserved: {snapshot.BorrowedWindowPreserved}",
        $"Borrowed-Dispatcher-Preserved: {snapshot.BorrowedDispatcherPreserved}",
        "External-Binary-Or-Payload-Published: False",
        Footer,
    ];

    public static bool TryParse(
        IReadOnlyList<string> lines,
        out PlaybackControlledWindowsKnownAudioToneProbeSnapshot snapshot)
    {
        snapshot = default!;
        if (lines.Count != 21 || lines[0] != Header || lines[^1] != Footer)
        {
            return false;
        }

        var expected = Create(AcceptedSnapshot());
        for (var index = 0; index < expected.Count; index++)
        {
            if (!string.Equals(lines[index], expected[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        snapshot = AcceptedSnapshot();
        return true;
    }

    public static async ValueTask WriteAsync(
        TextWriter output,
        PlaybackControlledWindowsKnownAudioToneProbeSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        foreach (var line in Create(snapshot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await output.WriteLineAsync(line).ConfigureAwait(false);
        }
    }

    private static PlaybackControlledWindowsKnownAudioToneProbeSnapshot AcceptedSnapshot() => new(
        PlaybackControlledWindowsKnownAudioToneProbeState.Completed,
        PlaybackControlledWindowsKnownAudioToneProbeResult.Completed,
        PlaybackControlledWindowsKnownAudioToneProbeEvidence.Accepted,
        PlaybackControlledWindowsKnownAudioTone.SampleFormat,
        PlaybackControlledWindowsKnownAudioTone.SampleRate,
        PlaybackControlledWindowsKnownAudioTone.Channels,
        PlaybackControlledWindowsKnownAudioTone.ToneHz,
        PlaybackControlledWindowsKnownAudioTone.DurationMilliseconds,
        PlaybackControlledWindowsKnownAudioTone.SampleFrames,
        PlaybackControlledWindowsKnownAudioTone.PayloadBytes,
        WriteAttempts: 1,
        WritesAcknowledged: 1,
        StartAcknowledged: 1,
        RetainedWouldBlock: 0,
        PlaybackWindowsPhysicalRunnerState.Stopped,
        OutputCleanupCompleted: true,
        BorrowedWindowPreserved: true,
        BorrowedDispatcherPreserved: true);
}

internal enum PlaybackControlledWindowsKnownAudioToneOutputResult
{
    Initialized,
    Cancelled,
    Rejected,
}

internal readonly record struct PlaybackControlledWindowsKnownAudioToneProbeOwnedSnapshot(
    PlaybackWindowsPhysicalRunnerSnapshot Runner,
    PlaybackWindowsOutputBufferSnapshot Buffers);

internal readonly record struct PlaybackControlledWindowsKnownAudioToneProbePreservation(
    bool OutputCleanupCompleted,
    bool WindowPreserved,
    bool DispatcherPreserved);

internal interface IPlaybackControlledWindowsKnownAudioToneProbeOperations : IAsyncDisposable
{
    PlaybackControlledWindowsKnownAudioToneProbeOwnedSnapshot Snapshot { get; }

    ValueTask<PlaybackWindowsPhysicalRunnerResult> StartRunnerAsync(CancellationToken token);

    ValueTask<PlaybackControlledWindowsKnownAudioToneOutputResult> InitializeOutputAsync(
        long generation,
        CancellationToken token);

    ValueTask<PlaybackWindowsOutputBufferProcessingResult> WriteAsync(
        PlaybackWindowsAudioSampleBuffer samples,
        CancellationToken token);

    ValueTask CleanupOutputAsync();

    ValueTask<PlaybackControlledWindowsKnownAudioToneProbePreservation>
        VerifyBorrowedOwnersAsync(CancellationToken token);

    ValueTask<PlaybackWindowsPhysicalRunnerResult> StopRunnerAsync();
}

internal sealed class SystemPlaybackControlledWindowsKnownAudioToneProbeOperations :
    IPlaybackControlledWindowsKnownAudioToneProbeOperations
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

    internal SystemPlaybackControlledWindowsKnownAudioToneProbeOperations(
        PlaybackControlledWindowsKnownAudioToneProbeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _runner = new(options.Runner);
    }

    public PlaybackControlledWindowsKnownAudioToneProbeOwnedSnapshot Snapshot
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

    public async ValueTask<PlaybackControlledWindowsKnownAudioToneOutputResult>
        InitializeOutputAsync(long generation, CancellationToken token)
    {
        var invocation = await _runner.InvokeAsync(
            async cancellationToken =>
            {
                if (!_runner.HasAccess || generation < 0)
                {
                    return PlaybackControlledWindowsKnownAudioToneOutputResult.Rejected;
                }

                var runner = _runner.Snapshot;
                if (runner.State != PlaybackWindowsPhysicalRunnerState.Running ||
                    runner.WindowToken <= 0 || runner.AdmissionClosed)
                {
                    return PlaybackControlledWindowsKnownAudioToneOutputResult.Rejected;
                }

                var activation = new PlaybackWindowsOutputActivationBoundary();
                if (activation.Activate() != PlaybackWindowsOutputActivationResult.Activated)
                {
                    activation.Dispose();
                    return PlaybackControlledWindowsKnownAudioToneOutputResult.Rejected;
                }

                var binding = new PlaybackWindowsOutputResourceBindingBoundary(activation);
                var bind = await binding.ConfigureAsync(
                    new(PlaybackDecodedVideoPixelFormat.Bgra32, 320, 240, 1_280, generation),
                    new(
                        PlaybackControlledWindowsKnownAudioTone.SampleFormat,
                        PlaybackControlledWindowsKnownAudioTone.SampleRate,
                        PlaybackControlledWindowsKnownAudioTone.Channels,
                        generation),
                    cancellationToken).ConfigureAwait(false);
                if (bind != PlaybackWindowsOutputBindingResult.Bound)
                {
                    binding.Dispose();
                    activation.Dispose();
                    return bind == PlaybackWindowsOutputBindingResult.Cancelled
                        ? PlaybackControlledWindowsKnownAudioToneOutputResult.Cancelled
                        : PlaybackControlledWindowsKnownAudioToneOutputResult.Rejected;
                }

                var streams = new PlaybackWindowsOutputStreamInitializationBoundary(
                    activation,
                    binding);
                var initialized = await streams.InitializeAsync(
                    new(
                        runner.WindowToken,
                        320,
                        240,
                        PlaybackDecodedVideoPixelFormat.Bgra32,
                        generation),
                    new(
                        PlaybackControlledWindowsKnownAudioTone.SampleFormat,
                        PlaybackControlledWindowsKnownAudioTone.SampleRate,
                        PlaybackControlledWindowsKnownAudioTone.Channels,
                        TimeSpan.FromMilliseconds(100),
                        generation),
                    cancellationToken).ConfigureAwait(false);
                if (initialized != PlaybackWindowsOutputStreamInitializationResult.Initialized)
                {
                    streams.Dispose();
                    binding.Dispose();
                    activation.Dispose();
                    return initialized == PlaybackWindowsOutputStreamInitializationResult.Cancelled
                        ? PlaybackControlledWindowsKnownAudioToneOutputResult.Cancelled
                        : PlaybackControlledWindowsKnownAudioToneOutputResult.Rejected;
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

                return PlaybackControlledWindowsKnownAudioToneOutputResult.Initialized;
            },
            token).ConfigureAwait(false);

        return invocation.Result switch
        {
            PlaybackWindowsOutputDispatcherInvocationResult.Completed => invocation.Value,
            PlaybackWindowsOutputDispatcherInvocationResult.Cancelled =>
                PlaybackControlledWindowsKnownAudioToneOutputResult.Cancelled,
            _ => PlaybackControlledWindowsKnownAudioToneOutputResult.Rejected,
        };
    }

    public async ValueTask<PlaybackWindowsOutputBufferProcessingResult> WriteAsync(
        PlaybackWindowsAudioSampleBuffer samples,
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
                    : await buffers.ProcessAudioAsync(samples, cancellationToken)
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

    public async ValueTask<PlaybackControlledWindowsKnownAudioToneProbePreservation>
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
