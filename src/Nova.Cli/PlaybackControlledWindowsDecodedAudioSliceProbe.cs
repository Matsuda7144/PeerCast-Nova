using System.Globalization;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli;

public enum PlaybackControlledWindowsDecodedAudioSliceProbeState
{
    Created,
    Decoding,
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

public enum PlaybackControlledWindowsDecodedAudioSliceProbeResult
{
    None,
    Completed,
    InvalidConfiguration,
    DecodeRejected,
    RunnerRejected,
    OutputInitializationRejected,
    WriteRejected,
    EvidenceRejected,
    Cancelled,
    AlreadyRunning,
    Faulted,
    Disposed,
}

public enum PlaybackControlledWindowsDecodedAudioSliceProbeEvidence
{
    Rejected,
    Accepted,
}

public sealed record PlaybackControlledWindowsDecodedAudioSliceProbeOptions
{
    public const string RuntimeBuildId =
        "btbn.ffmpeg.20260721.win64.lgpl-shared";
    public const string FixtureSha256 =
        "32872B300286525F4EF97FEE3533E25419AC8E48BFD4BC2C4FD9545F071B1E15";
    public const int FixtureBytes = 66_939;
    public const int DefaultMaximumDecodeOperations = 128;
    public const int DefaultFixturePacketBytes = 65_536;
    public static readonly TimeSpan MaximumObservationHoldDuration =
        TimeSpan.FromSeconds(30);

    public string RuntimeDirectory { get; init; } = string.Empty;

    public string FixturePath { get; init; } = string.Empty;

    public int MaximumDecodeOperations { get; init; } =
        DefaultMaximumDecodeOperations;

    public int FixturePacketBytes { get; init; } = DefaultFixturePacketBytes;

    public TimeSpan ObservationHoldDuration { get; init; } = TimeSpan.FromSeconds(1);

    public PlaybackWindowsPhysicalRunnerOptions Runner { get; init; } = new()
    {
        ClientWidth = 320,
        ClientHeight = 240,
        Title = "PeerCast Nova Decoded Audio Slice Probe",
    };
}

public sealed record PlaybackControlledWindowsDecodedAudioSliceProbeSnapshot(
    PlaybackControlledWindowsDecodedAudioSliceProbeState State,
    PlaybackControlledWindowsDecodedAudioSliceProbeResult Result,
    PlaybackControlledWindowsDecodedAudioSliceProbeEvidence Evidence,
    PlaybackDecodedAudioSliceCollectorSnapshot Slice,
    long WriteAttempts,
    long WritesAcknowledged,
    long StartAcknowledged,
    long RetainedWouldBlock,
    long VideoPresentAttempts,
    PlaybackWindowsPhysicalRunnerState RunnerState,
    bool OutputCleanupCompleted,
    bool BorrowedWindowPreserved,
    bool BorrowedDispatcherPreserved);

internal readonly record struct PlaybackControlledWindowsDecodedAudioSliceDecode(
    bool Completed,
    PlaybackWindowsAudioSampleBuffer Buffer,
    PlaybackDecodedAudioSliceCollectorSnapshot Snapshot);

public sealed class PlaybackControlledWindowsDecodedAudioSliceProbe : IAsyncDisposable
{
    private const long Generation = 1;
    private readonly object _sync = new();
    private readonly PlaybackControlledWindowsDecodedAudioSliceProbeOptions _options;
    private readonly IPlaybackControlledWindowsDecodedAudioSliceProbeOperations _operations;
    private PlaybackControlledWindowsDecodedAudioSliceProbeState _state =
        PlaybackControlledWindowsDecodedAudioSliceProbeState.Created;
    private PlaybackControlledWindowsDecodedAudioSliceProbeResult _result;
    private PlaybackControlledWindowsDecodedAudioSliceProbeEvidence _evidence;
    private PlaybackDecodedAudioSliceCollectorSnapshot _slice;
    private long _writeAttempts;
    private long _writesAcknowledged;
    private long _startAcknowledged;
    private long _retainedWouldBlock;
    private long _videoPresentAttempts;
    private bool _outputCleanupCompleted;
    private bool _borrowedWindowPreserved;
    private bool _borrowedDispatcherPreserved;
    private bool _isRunning;
    private bool _isDisposed;

    public PlaybackControlledWindowsDecodedAudioSliceProbe(
        PlaybackControlledWindowsDecodedAudioSliceProbeOptions options)
        : this(
            CloneOptions(options),
            new SystemPlaybackControlledWindowsDecodedAudioSliceProbeOperations(
                CloneOptions(options)))
    {
    }

    internal PlaybackControlledWindowsDecodedAudioSliceProbe(
        PlaybackControlledWindowsDecodedAudioSliceProbeOptions options,
        IPlaybackControlledWindowsDecodedAudioSliceProbeOperations operations)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(operations);
        _options = CloneOptions(options);
        _operations = operations;
    }

    public PlaybackControlledWindowsDecodedAudioSliceProbeSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return new(
                    _state,
                    _result,
                    _evidence,
                    _slice,
                    _writeAttempts,
                    _writesAcknowledged,
                    _startAcknowledged,
                    _retainedWouldBlock,
                    _videoPresentAttempts,
                    _operations.Snapshot.Runner.State,
                    _outputCleanupCompleted,
                    _borrowedWindowPreserved,
                    _borrowedDispatcherPreserved);
            }
        }
    }

    public async ValueTask<PlaybackControlledWindowsDecodedAudioSliceProbeResult> RunAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_isDisposed)
            {
                return SetResult(
                    PlaybackControlledWindowsDecodedAudioSliceProbeResult.Disposed);
            }

            if (_isRunning || _state !=
                PlaybackControlledWindowsDecodedAudioSliceProbeState.Created)
            {
                return SetResult(
                    PlaybackControlledWindowsDecodedAudioSliceProbeResult.AlreadyRunning);
            }

            if (!ValidateOptions(_options))
            {
                _state = PlaybackControlledWindowsDecodedAudioSliceProbeState.Faulted;
                return SetResult(
                    PlaybackControlledWindowsDecodedAudioSliceProbeResult.InvalidConfiguration);
            }

            _isRunning = true;
        }

        var runnerStarted = false;
        var outputInitialized = false;
        try
        {
            SetState(PlaybackControlledWindowsDecodedAudioSliceProbeState.Decoding);
            var decoded = await _operations.DecodeAsync(
                _options.RuntimeDirectory,
                _options.FixturePath,
                _options.MaximumDecodeOperations,
                _options.FixturePacketBytes,
                cancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                _slice = decoded.Snapshot;
            }

            if (!decoded.Completed || !IsAcceptedSlice(decoded.Snapshot))
            {
                return Terminal(
                    PlaybackControlledWindowsDecodedAudioSliceProbeResult.DecodeRejected);
            }

            SetState(PlaybackControlledWindowsDecodedAudioSliceProbeState.StartingRunner);
            var runner = await _operations.StartRunnerAsync(cancellationToken)
                .ConfigureAwait(false);
            if (runner != PlaybackWindowsPhysicalRunnerResult.Started)
            {
                return Terminal(runner == PlaybackWindowsPhysicalRunnerResult.Cancelled
                    ? PlaybackControlledWindowsDecodedAudioSliceProbeResult.Cancelled
                    : PlaybackControlledWindowsDecodedAudioSliceProbeResult.RunnerRejected);
            }

            runnerStarted = true;
            SetState(PlaybackControlledWindowsDecodedAudioSliceProbeState.InitializingOutput);
            var initialized = await _operations.InitializeOutputAsync(
                Generation,
                cancellationToken).ConfigureAwait(false);
            if (initialized != PlaybackControlledWindowsKnownAudioToneOutputResult.Initialized)
            {
                return Terminal(initialized ==
                    PlaybackControlledWindowsKnownAudioToneOutputResult.Cancelled
                    ? PlaybackControlledWindowsDecodedAudioSliceProbeResult.Cancelled
                    : PlaybackControlledWindowsDecodedAudioSliceProbeResult
                        .OutputInitializationRejected);
            }

            outputInitialized = true;
            SetState(PlaybackControlledWindowsDecodedAudioSliceProbeState.Writing);
            var written = await _operations.WriteAsync(
                decoded.Buffer,
                cancellationToken).ConfigureAwait(false);
            if (written != PlaybackWindowsOutputBufferProcessingResult.Processed)
            {
                RecordEvidence(_operations.Snapshot.Buffers.NativeEvidence);
                return Terminal(written == PlaybackWindowsOutputBufferProcessingResult.Cancelled
                    ? PlaybackControlledWindowsDecodedAudioSliceProbeResult.Cancelled
                    : PlaybackControlledWindowsDecodedAudioSliceProbeResult.WriteRejected);
            }

            if (_options.ObservationHoldDuration > TimeSpan.Zero)
            {
                SetState(
                    PlaybackControlledWindowsDecodedAudioSliceProbeState.HoldingObservation);
                await Task.Delay(_options.ObservationHoldDuration, cancellationToken)
                    .ConfigureAwait(false);
            }

            SetState(
                PlaybackControlledWindowsDecodedAudioSliceProbeState.CapturingEvidence);
            var native = _operations.Snapshot.Buffers.NativeEvidence;
            RecordEvidence(native);
            if (!IsAcceptedNativeEvidence(native))
            {
                return Terminal(
                    PlaybackControlledWindowsDecodedAudioSliceProbeResult.EvidenceRejected);
            }

            SetState(PlaybackControlledWindowsDecodedAudioSliceProbeState.CleaningUp);
            await _operations.CleanupOutputAsync().ConfigureAwait(false);
            outputInitialized = false;
            var preservation = await _operations.VerifyBorrowedOwnersAsync(
                cancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                _outputCleanupCompleted = preservation.OutputCleanupCompleted;
                _borrowedWindowPreserved = preservation.WindowPreserved;
                _borrowedDispatcherPreserved = preservation.DispatcherPreserved;
            }

            if (!preservation.OutputCleanupCompleted ||
                !preservation.WindowPreserved ||
                !preservation.DispatcherPreserved)
            {
                return Terminal(
                    PlaybackControlledWindowsDecodedAudioSliceProbeResult.EvidenceRejected);
            }

            lock (_sync)
            {
                _evidence = PlaybackControlledWindowsDecodedAudioSliceProbeEvidence.Accepted;
                _state = PlaybackControlledWindowsDecodedAudioSliceProbeState.Completed;
                return SetResult(
                    PlaybackControlledWindowsDecodedAudioSliceProbeResult.Completed);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Terminal(
                PlaybackControlledWindowsDecodedAudioSliceProbeResult.Cancelled);
        }
        catch
        {
            return Terminal(PlaybackControlledWindowsDecodedAudioSliceProbeResult.Faulted);
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
                    // Cleanup remains fail-closed after a fixed terminal result.
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
            _state = PlaybackControlledWindowsDecodedAudioSliceProbeState.Disposed;
            _result = PlaybackControlledWindowsDecodedAudioSliceProbeResult.Disposed;
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
            _videoPresentAttempts = native.VideoPresentAttemptCount;
        }
    }

    private static bool IsAcceptedSlice(
        PlaybackDecodedAudioSliceCollectorSnapshot slice) =>
        slice.Result == PlaybackDecodedAudioSliceCollectorResult.Completed &&
        slice.Generation == 0 &&
        slice.DecodedFramesConsumed ==
            PlaybackDecodedAudioSliceCollectorOptions.ExpectedDecodedFrames &&
        slice.FullDecodedFramesCopied ==
            PlaybackDecodedAudioSliceCollectorOptions.ExpectedFullFrames &&
        slice.FinalFrameSampleFramesCopied ==
            PlaybackDecodedAudioSliceCollectorOptions.ExpectedFinalFrameSampleFrames &&
        slice.OutputSampleFrames ==
            PlaybackDecodedAudioSliceCollectorOptions.TargetSampleFrames &&
        slice.PayloadBytes == PlaybackDecodedAudioSliceCollectorOptions.PayloadBytes &&
        slice.IsComplete &&
        slice.PayloadPublished;

    private static bool IsAcceptedNativeEvidence(
        PlaybackWindowsOutputNativeEvidenceSnapshot native) =>
        native.Source == PlaybackControlledWindowsOutputEvidenceSource.SystemWindows &&
        !native.CounterOverflowed &&
        !native.AudioConfigurationConflict &&
        native.AudioSampleFormat == PlaybackDecodedAudioSliceCollector.SampleFormat &&
        native.AudioSampleRate == PlaybackDecodedAudioSliceCollector.SampleRate &&
        native.AudioChannelCount == PlaybackDecodedAudioSliceCollector.Channels &&
        native.AudioGeneration == Generation &&
        native.AudioWriteAttemptCount == 1 &&
        native.AudioWriteAcknowledgementCount == 1 &&
        native.AudioAcceptedFrameCount ==
            PlaybackDecodedAudioSliceCollectorOptions.TargetSampleFrames &&
        native.AudioAcceptedByteCount ==
            PlaybackDecodedAudioSliceCollectorOptions.PayloadBytes &&
        native.AudioWouldBlockCount == 0 &&
        native.AudioStartAcknowledgementCount == 1 &&
        native.AudioDeviceLostCount == 0 &&
        native.VideoPresentAttemptCount == 0;

    private PlaybackControlledWindowsDecodedAudioSliceProbeResult Terminal(
        PlaybackControlledWindowsDecodedAudioSliceProbeResult result)
    {
        lock (_sync)
        {
            _evidence = PlaybackControlledWindowsDecodedAudioSliceProbeEvidence.Rejected;
            _state = PlaybackControlledWindowsDecodedAudioSliceProbeState.Faulted;
            return SetResult(result);
        }
    }

    private void SetState(PlaybackControlledWindowsDecodedAudioSliceProbeState state)
    {
        lock (_sync)
        {
            _state = state;
        }
    }

    private PlaybackControlledWindowsDecodedAudioSliceProbeResult SetResult(
        PlaybackControlledWindowsDecodedAudioSliceProbeResult result)
    {
        _result = result;
        return result;
    }

    private static bool ValidateOptions(
        PlaybackControlledWindowsDecodedAudioSliceProbeOptions options) =>
        !string.IsNullOrWhiteSpace(options.RuntimeDirectory) &&
        !string.IsNullOrWhiteSpace(options.FixturePath) &&
        options.MaximumDecodeOperations is >= 1 and <=
            PlaybackPcpFlvAwaitedDecodeOptions.MaximumDecodeOperationsPerCallHardLimit &&
        options.FixturePacketBytes is >= 1 and <=
            PlaybackControlledWindowsDecodedAudioSliceProbeOptions.FixtureBytes &&
        options.ObservationHoldDuration >= TimeSpan.Zero &&
        options.ObservationHoldDuration <=
            PlaybackControlledWindowsDecodedAudioSliceProbeOptions
                .MaximumObservationHoldDuration &&
        options.Runner is not null;

    private static PlaybackControlledWindowsDecodedAudioSliceProbeOptions CloneOptions(
        PlaybackControlledWindowsDecodedAudioSliceProbeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Runner);
        return options with { Runner = options.Runner with { } };
    }
}

public static class PlaybackControlledWindowsDecodedAudioSliceProbeRecordCodec
{
    public const string Header =
        "PEERCAST-NOVA-CONTROLLED-WINDOWS-DECODED-AUDIO-SLICE-PROBE 1";
    public const string Footer =
        "END-PEERCAST-NOVA-CONTROLLED-WINDOWS-DECODED-AUDIO-SLICE-PROBE";

    public static IReadOnlyList<string> Create(
        PlaybackControlledWindowsDecodedAudioSliceProbeSnapshot snapshot) =>
    [
        Header,
        "Milestone: 0.0.10d Phase 5",
        $"Runtime-Build: {PlaybackControlledWindowsDecodedAudioSliceProbeOptions.RuntimeBuildId}",
        $"Fixture-SHA256: {PlaybackControlledWindowsDecodedAudioSliceProbeOptions.FixtureSha256}",
        $"Result: {snapshot.Result}",
        $"Evidence: {snapshot.Evidence}",
        $"Sample-Format: {PlaybackDecodedAudioSliceCollector.SampleFormat}",
        $"Sample-Rate: {PlaybackDecodedAudioSliceCollector.SampleRate.ToString(CultureInfo.InvariantCulture)}",
        $"Channels: {PlaybackDecodedAudioSliceCollector.Channels.ToString(CultureInfo.InvariantCulture)}",
        $"Duration-Milliseconds: {PlaybackDecodedAudioSliceCollector.DurationMilliseconds.ToString(CultureInfo.InvariantCulture)}",
        $"Decoded-Frames-Consumed: {snapshot.Slice.DecodedFramesConsumed.ToString(CultureInfo.InvariantCulture)}",
        $"Full-Decoded-Frames-Copied: {snapshot.Slice.FullDecodedFramesCopied.ToString(CultureInfo.InvariantCulture)}",
        $"Final-Frame-Sample-Frames-Copied: {snapshot.Slice.FinalFrameSampleFramesCopied.ToString(CultureInfo.InvariantCulture)}",
        $"Output-Sample-Frames: {snapshot.Slice.OutputSampleFrames.ToString(CultureInfo.InvariantCulture)}",
        $"Payload-Bytes: {snapshot.Slice.PayloadBytes.ToString(CultureInfo.InvariantCulture)}",
        $"Write-Attempts: {snapshot.WriteAttempts.ToString(CultureInfo.InvariantCulture)}",
        $"Writes-Acknowledged: {snapshot.WritesAcknowledged.ToString(CultureInfo.InvariantCulture)}",
        $"Start-Acknowledged: {snapshot.StartAcknowledged == 1}",
        $"Retained-WouldBlock: {snapshot.RetainedWouldBlock.ToString(CultureInfo.InvariantCulture)}",
        $"Video-Present-Attempts: {snapshot.VideoPresentAttempts.ToString(CultureInfo.InvariantCulture)}",
        $"Runner-State: {snapshot.RunnerState}",
        $"Lifecycle-State: {(snapshot.OutputCleanupCompleted ? "Disposed" : "Faulted")}",
        $"Borrowed-Window-Preserved: {snapshot.BorrowedWindowPreserved}",
        $"Borrowed-Dispatcher-Preserved: {snapshot.BorrowedDispatcherPreserved}",
        "External-Binary-Or-Payload-Published: False",
        Footer,
    ];

    public static bool TryParse(
        IReadOnlyList<string> lines,
        out PlaybackControlledWindowsDecodedAudioSliceProbeSnapshot snapshot)
    {
        snapshot = default!;
        var accepted = AcceptedSnapshot();
        var expected = Create(accepted);
        if (lines.Count != expected.Count)
        {
            return false;
        }

        for (var index = 0; index < expected.Count; index++)
        {
            if (!string.Equals(lines[index], expected[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        snapshot = accepted;
        return true;
    }

    public static async ValueTask WriteAsync(
        TextWriter output,
        PlaybackControlledWindowsDecodedAudioSliceProbeSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        foreach (var line in Create(snapshot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await output.WriteLineAsync(line).ConfigureAwait(false);
        }
    }

    private static PlaybackControlledWindowsDecodedAudioSliceProbeSnapshot
        AcceptedSnapshot() => new(
            PlaybackControlledWindowsDecodedAudioSliceProbeState.Completed,
            PlaybackControlledWindowsDecodedAudioSliceProbeResult.Completed,
            PlaybackControlledWindowsDecodedAudioSliceProbeEvidence.Accepted,
            new(
                PlaybackDecodedAudioSliceCollectorResult.Completed,
                Generation: 0,
                PlaybackDecodedAudioSliceCollectorOptions.ExpectedDecodedFrames,
                PlaybackDecodedAudioSliceCollectorOptions.ExpectedFullFrames,
                PlaybackDecodedAudioSliceCollectorOptions.ExpectedFinalFrameSampleFrames,
                PlaybackDecodedAudioSliceCollectorOptions.TargetSampleFrames,
                PlaybackDecodedAudioSliceCollectorOptions.PayloadBytes,
                IsComplete: true,
                PayloadPublished: true),
            WriteAttempts: 1,
            WritesAcknowledged: 1,
            StartAcknowledged: 1,
            RetainedWouldBlock: 0,
            VideoPresentAttempts: 0,
            PlaybackWindowsPhysicalRunnerState.Stopped,
            OutputCleanupCompleted: true,
            BorrowedWindowPreserved: true,
            BorrowedDispatcherPreserved: true);
}

internal interface IPlaybackControlledWindowsDecodedAudioSliceProbeOperations :
    IAsyncDisposable
{
    PlaybackControlledWindowsKnownAudioToneProbeOwnedSnapshot Snapshot { get; }

    ValueTask<PlaybackControlledWindowsDecodedAudioSliceDecode> DecodeAsync(
        string runtimeDirectory,
        string fixturePath,
        int maximumDecodeOperations,
        int fixturePacketBytes,
        CancellationToken token);

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

internal sealed class SystemPlaybackControlledWindowsDecodedAudioSliceProbeOperations :
    IPlaybackControlledWindowsDecodedAudioSliceProbeOperations
{
    private static readonly ChannelId FixtureChannelId =
        new(Guid.Parse("46f6edc8-53ce-44e3-9532-8d0d48cffd34"));
    private static readonly ChannelSessionId FixtureSessionId =
        new(Guid.Parse("05b54f32-d7de-4b98-819d-d0068bfc86aa"));
    private readonly SystemPlaybackControlledWindowsKnownAudioToneProbeOperations _output;

    internal SystemPlaybackControlledWindowsDecodedAudioSliceProbeOperations(
        PlaybackControlledWindowsDecodedAudioSliceProbeOptions options)
    {
        _output = new(new PlaybackControlledWindowsKnownAudioToneProbeOptions
        {
            ObservationHoldDuration = options.ObservationHoldDuration,
            Runner = options.Runner with { },
        });
    }

    public PlaybackControlledWindowsKnownAudioToneProbeOwnedSnapshot Snapshot =>
        _output.Snapshot;

    public async ValueTask<PlaybackControlledWindowsDecodedAudioSliceDecode> DecodeAsync(
        string runtimeDirectory,
        string fixturePath,
        int maximumDecodeOperations,
        int fixturePacketBytes,
        CancellationToken token)
    {
        using var collector = new PlaybackDecodedAudioSliceCollector(
            new() { Generation = 0 });
        PlaybackDecodedAudioSliceGenerationFactory? factory = null;
        PlaybackPcpFlvAwaitedDecodeCompositionBoundary? composition = null;
        try
        {
            var replay = await PcpControlledFlvFixtureReplayBoundary.ReplayAsync(
                new()
                {
                    FixturePath = fixturePath,
                    ExpectedSha256 =
                        PlaybackControlledWindowsDecodedAudioSliceProbeOptions
                            .FixtureSha256,
                    ChannelId = FixtureChannelId,
                    SessionId = FixtureSessionId,
                    MaximumFixtureBytes =
                        PlaybackControlledWindowsDecodedAudioSliceProbeOptions
                            .FixtureBytes,
                    PacketPayloadBytes = fixturePacketBytes,
                },
                async (packet, cancellationToken) =>
                {
                    if (collector.IsComplete)
                    {
                        return true;
                    }

                    factory ??=
                        await PlaybackDecodedAudioSliceGenerationFactory.PrepareAsync(
                            runtimeDirectory,
                            collector,
                            cancellationToken).ConfigureAwait(false);
                    if (factory is null)
                    {
                        return false;
                    }

                    composition ??=
                        new PlaybackPcpFlvAwaitedDecodeCompositionBoundary(
                            FixtureChannelId,
                            FixtureSessionId,
                            factory,
                            initialPacketSequence: 0,
                            new PlaybackPcpFlvAwaitedDecodeOptions
                            {
                                MaximumDecodeOperationsPerCall =
                                    maximumDecodeOperations,
                                MaximumDecodedFramesPerCall =
                                    maximumDecodeOperations,
                                MediaSelection =
                                    PlaybackPcpFlvAwaitedDecodeMediaSelection.AudioOnly,
                            },
                            parserOptions: null,
                            stopAfterSettlement: () => collector.IsComplete);

                    try
                    {
                        await composition.ConsumeAsync(packet, cancellationToken)
                            .ConfigureAwait(false);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                },
                token).ConfigureAwait(false);

            var snapshot = collector.Snapshot;
            if (replay.Result != PcpControlledFlvFixtureReplayResult.Completed ||
                replay.PayloadByteCount !=
                    PlaybackControlledWindowsDecodedAudioSliceProbeOptions.FixtureBytes ||
                !string.Equals(
                    replay.FixtureSha256,
                    PlaybackControlledWindowsDecodedAudioSliceProbeOptions.FixtureSha256,
                    StringComparison.Ordinal) ||
                !collector.TryTakeCompletedBuffer(outputGeneration: 1, out var buffer))
            {
                return new(false, default, snapshot);
            }

            snapshot = collector.Snapshot;
            return new(true, buffer, snapshot);
        }
        finally
        {
            if (composition is not null)
            {
                await composition.DisposeAsync().ConfigureAwait(false);
            }

            factory?.Dispose();
        }
    }

    public ValueTask<PlaybackWindowsPhysicalRunnerResult> StartRunnerAsync(
        CancellationToken token) => _output.StartRunnerAsync(token);

    public ValueTask<PlaybackControlledWindowsKnownAudioToneOutputResult>
        InitializeOutputAsync(long generation, CancellationToken token) =>
        _output.InitializeOutputAsync(generation, token);

    public ValueTask<PlaybackWindowsOutputBufferProcessingResult> WriteAsync(
        PlaybackWindowsAudioSampleBuffer samples,
        CancellationToken token) => _output.WriteAsync(samples, token);

    public ValueTask CleanupOutputAsync() => _output.CleanupOutputAsync();

    public ValueTask<PlaybackControlledWindowsKnownAudioToneProbePreservation>
        VerifyBorrowedOwnersAsync(CancellationToken token) =>
        _output.VerifyBorrowedOwnersAsync(token);

    public ValueTask<PlaybackWindowsPhysicalRunnerResult> StopRunnerAsync() =>
        _output.StopRunnerAsync();

    public ValueTask DisposeAsync() => _output.DisposeAsync();
}
