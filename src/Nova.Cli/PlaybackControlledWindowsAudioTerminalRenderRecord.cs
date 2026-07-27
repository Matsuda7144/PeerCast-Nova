using System.Text.Json;
using System.Text.Json.Serialization;
using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli;

public sealed record PlaybackControlledWindowsAudioTerminalRenderRecord(
    int SchemaVersion,
    string Milestone,
    PlaybackControlledWindowsOutputValidationKind Kind,
    string RuntimeBuildId,
    string FixtureSha256,
    PlaybackControlledWindowsPhysicalRunResult Result,
    PlaybackControlledWindowsOutputEvidenceResult EvidenceResult,
    long FixtureByteCount,
    int FixturePacketCount,
    int DecodeOperationCount,
    int DeliveryAttemptCount,
    int DeliveredCount,
    int RetainedWouldBlockCount,
    int ObservationHoldMilliseconds,
    int AudioBufferMilliseconds,
    PlaybackWindowsPhysicalRunnerState RunnerState,
    PlaybackApprovedExternalRuntimeWindowsLifecycleState LifecycleState,
    bool BorrowedWindowPreserved,
    bool BorrowedDispatcherPreserved,
    PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot PreCleanupDeliveryTrace,
    PlaybackWindowsAudioRenderTraceSnapshot PreCleanupAudioRenderTrace,
    PlaybackWindowsOutputNativeEvidenceSnapshot PreCleanupNativeEvidence,
    PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot PostStopDeliveryTrace,
    PlaybackWindowsAudioRenderTraceSnapshot PostStopAudioRenderTrace,
    PlaybackWindowsOutputNativeEvidenceSnapshot PostStopNativeEvidence,
    bool PhysicalRunStarted,
    bool PhysicalRunCompleted,
    bool CompletedRenderEvidenceEstablished,
    bool AudioAcceptanceClaimed,
    bool ExternalBinaryOrPayloadPublished);

public enum PlaybackControlledWindowsAudioTerminalRecordRejection
{
    None,
    ProjectionRejected,
    IdentityOrBoundsRejected,
    RunnerOrLifecycleStateRejected,
    TerminalClaimsRejected,
    PreCleanupCheckpointRejected,
    PostStopCheckpointRejected,
    TraceCounterConsistencyRejected,
    CleanupStabilityRejected,
    CanonicalSizeLimitRejected,
}

/// <summary>
/// Strict bounded codec for a controlled audio run that reached the system lifecycle
/// but did not establish the completed render-evidence contract. The canonical payload
/// is a fixed-model JSON value between dedicated header and footer lines. Parsing
/// succeeds only when reserialization is byte-for-byte canonical after line-ending
/// normalization.
/// </summary>
public static class PlaybackControlledWindowsAudioTerminalRenderRecordCodec
{
    public const int CurrentSchemaVersion = 1;
    public const string CurrentMilestone = "0.0.10d Phase 6";
    public const int MaximumRecordCharacterCount = 32 * 1024;

    private const string Header =
        "PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-TERMINAL-RENDER 1";
    private const string PayloadPrefix = "Record: ";
    private const string Footer =
        "END-PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-TERMINAL-RENDER";

    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public static PlaybackControlledWindowsAudioTerminalRenderRecord Create(
        PlaybackControlledWindowsPhysicalRunSnapshot snapshot,
        string expectedFixtureSha256,
        TimeSpan observationHoldDuration,
        TimeSpan audioBufferDuration,
        bool physicalRunStarted)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!PlaybackControlledWindowsPhysicalRunRecordCodec.TryNormalizeSha256(
                expectedFixtureSha256,
                out var normalizedDigest))
        {
            throw new ArgumentException(
                "The expected fixture digest must be a 64-character SHA-256 value.",
                nameof(expectedFixtureSha256));
        }

        var preCleanup = snapshot.PreCleanupAudioRenderEvidence;
        var postStop = snapshot.PostStopAudioRenderEvidence;
        return new(
            CurrentSchemaVersion,
            CurrentMilestone,
            snapshot.Kind,
            PlaybackFfmpegBackendCandidate.BuildId,
            normalizedDigest,
            snapshot.Result,
            snapshot.EvidenceResult,
            snapshot.FixtureByteCount,
            snapshot.FixturePacketCount,
            snapshot.DecodeOperationCount,
            snapshot.DeliveryAttemptCount,
            snapshot.DeliveredCount,
            snapshot.RetainedWouldBlockCount,
            ExactMilliseconds(
                observationHoldDuration,
                allowZero: true,
                nameof(observationHoldDuration)),
            ExactMilliseconds(
                audioBufferDuration,
                allowZero: false,
                nameof(audioBufferDuration)),
            snapshot.RunnerState,
            snapshot.LifecycleState,
            snapshot.BorrowedWindowPreserved,
            snapshot.BorrowedDispatcherPreserved,
            preCleanup.DeliveryTrace,
            preCleanup.AudioRenderTrace,
            NormalizeNativeAudio(preCleanup.NativeEvidence, preCleanup.AudioRenderTrace),
            postStop.DeliveryTrace,
            postStop.AudioRenderTrace,
            NormalizeNativeAudio(postStop.NativeEvidence, postStop.AudioRenderTrace),
            physicalRunStarted,
            PhysicalRunCompleted: false,
            CompletedRenderEvidenceEstablished: false,
            AudioAcceptanceClaimed: false,
            ExternalBinaryOrPayloadPublished: false);
    }

    public static async ValueTask WriteAsync(
        TextWriter output,
        PlaybackControlledWindowsAudioTerminalRenderRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();
        await output.WriteAsync(Serialize(record).AsMemory(), cancellationToken)
            .ConfigureAwait(false);
    }

    public static string Serialize(PlaybackControlledWindowsAudioTerminalRenderRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!IsSyntacticallyValid(record, out var normalizedDigest))
        {
            throw new ArgumentException(
                "The controlled audio terminal render record is invalid.",
                nameof(record));
        }

        var normalized = record with { FixtureSha256 = normalizedDigest };
        var payload = JsonSerializer.Serialize(normalized, SerializerOptions);
        var text = $"{Header}\n{PayloadPrefix}{payload}\n{Footer}\n";
        if (text.Length > MaximumRecordCharacterCount)
        {
            throw new InvalidOperationException(
                "The controlled audio terminal render record exceeds its fixed size limit.");
        }

        return text;
    }

    public static bool TryParse(
        string? text,
        out PlaybackControlledWindowsAudioTerminalRenderRecord? record)
    {
        record = null;
        if (string.IsNullOrEmpty(text) || text.Length > MaximumRecordCharacterCount)
        {
            return false;
        }

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        if (normalized.EndsWith('\n'))
        {
            normalized = normalized[..^1];
        }

        var lines = normalized.Split('\n');
        if (lines.Length != 3 ||
            !string.Equals(lines[0], Header, StringComparison.Ordinal) ||
            !lines[1].StartsWith(PayloadPrefix, StringComparison.Ordinal) ||
            !string.Equals(lines[2], Footer, StringComparison.Ordinal))
        {
            return false;
        }

        PlaybackControlledWindowsAudioTerminalRenderRecord? candidate;
        try
        {
            candidate = JsonSerializer.Deserialize<PlaybackControlledWindowsAudioTerminalRenderRecord>(
                lines[1][PayloadPrefix.Length..],
                SerializerOptions);
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }

        if (candidate is null || !IsSyntacticallyValid(candidate, out _))
        {
            return false;
        }

        var canonical = Serialize(candidate);
        if (!string.Equals(canonical, normalized + "\n", StringComparison.Ordinal))
        {
            return false;
        }

        record = candidate;
        return true;
    }

    private static bool IsSyntacticallyValid(
        PlaybackControlledWindowsAudioTerminalRenderRecord record,
        out string normalizedDigest) =>
        GetValidationFailure(record, out normalizedDigest) ==
            PlaybackControlledWindowsAudioTerminalRecordRejection.None;

    internal static PlaybackControlledWindowsAudioTerminalRecordRejection
        GetValidationFailure(
            PlaybackControlledWindowsAudioTerminalRenderRecord record) =>
        GetValidationFailure(record, out _);

    private static PlaybackControlledWindowsAudioTerminalRecordRejection
        GetValidationFailure(
            PlaybackControlledWindowsAudioTerminalRenderRecord record,
            out string normalizedDigest)
    {
        normalizedDigest = string.Empty;
        if (record.SchemaVersion != CurrentSchemaVersion ||
            !string.Equals(record.Milestone, CurrentMilestone, StringComparison.Ordinal) ||
            record.Kind != PlaybackControlledWindowsOutputValidationKind.Audio ||
            !string.Equals(
                record.RuntimeBuildId,
                PlaybackFfmpegBackendCandidate.BuildId,
                StringComparison.Ordinal) ||
            !PlaybackControlledWindowsPhysicalRunRecordCodec.TryNormalizeSha256(
                record.FixtureSha256,
                out normalizedDigest) ||
            !IsReviewedTerminalResult(record.Result) ||
            !Enum.IsDefined(record.EvidenceResult) ||
            record.EvidenceResult == PlaybackControlledWindowsOutputEvidenceResult.Accepted ||
            record.FixtureByteCount < 0 ||
            record.FixturePacketCount < 0 ||
            (record.FixtureByteCount == 0) != (record.FixturePacketCount == 0) ||
            record.DecodeOperationCount is < 0 or > 4096 ||
            record.DeliveryAttemptCount is < 0 or > 4096 ||
            record.DeliveredCount < 0 ||
            record.DeliveredCount > record.DeliveryAttemptCount ||
            record.RetainedWouldBlockCount < 0 ||
            record.RetainedWouldBlockCount >
                record.DeliveryAttemptCount - record.DeliveredCount ||
            record.DeliveryAttemptCount > 0 && record.FixtureByteCount == 0 ||
            record.ObservationHoldMilliseconds is < 0 or > 30_000 ||
            record.AudioBufferMilliseconds < 1 ||
            record.AudioBufferMilliseconds >
                PlaybackWindowsOutputStreamInitializationBoundary.MaximumAudioBufferDuration
                    .TotalMilliseconds)
        {
            return PlaybackControlledWindowsAudioTerminalRecordRejection
                .IdentityOrBoundsRejected;
        }

        if (
            record.RunnerState != PlaybackWindowsPhysicalRunnerState.Stopped ||
            record.LifecycleState !=
                PlaybackApprovedExternalRuntimeWindowsLifecycleState.Disposed)
        {
            return PlaybackControlledWindowsAudioTerminalRecordRejection
                .RunnerOrLifecycleStateRejected;
        }

        if (
            !record.PhysicalRunStarted ||
            record.PhysicalRunCompleted ||
            record.CompletedRenderEvidenceEstablished ||
            record.AudioAcceptanceClaimed ||
            record.ExternalBinaryOrPayloadPublished ||
            (record.Result != PlaybackControlledWindowsPhysicalRunResult.EvidenceRejected &&
                record.EvidenceResult != default))
        {
            return PlaybackControlledWindowsAudioTerminalRecordRejection
                .TerminalClaimsRejected;
        }

        if (!IsValidPreCleanupCheckpoint(
                record.PreCleanupDeliveryTrace,
                record.PreCleanupAudioRenderTrace,
                record.PreCleanupNativeEvidence,
                out var deliveredObservations,
                out var wouldBlockObservations))
        {
            return PlaybackControlledWindowsAudioTerminalRecordRejection
                .PreCleanupCheckpointRejected;
        }

        if (!IsValidPostStopCheckpoint(
                record.PostStopDeliveryTrace,
                record.PostStopAudioRenderTrace,
                record.PostStopNativeEvidence))
        {
            return PlaybackControlledWindowsAudioTerminalRecordRejection
                .PostStopCheckpointRejected;
        }

        if (
            deliveredObservations != record.DeliveredCount ||
            wouldBlockObservations != record.RetainedWouldBlockCount)
        {
            return PlaybackControlledWindowsAudioTerminalRecordRejection
                .TraceCounterConsistencyRejected;
        }

        if (!SameAudioWrites(
                record.PreCleanupAudioRenderTrace,
                record.PostStopAudioRenderTrace) ||
            !SameAudioAggregate(
                record.PreCleanupNativeEvidence,
                record.PostStopNativeEvidence))
        {
            return PlaybackControlledWindowsAudioTerminalRecordRejection
                .CleanupStabilityRejected;
        }

        return PlaybackControlledWindowsAudioTerminalRecordRejection.None;
    }

    private static bool IsReviewedTerminalResult(
        PlaybackControlledWindowsPhysicalRunResult result) =>
        result is PlaybackControlledWindowsPhysicalRunResult.DecodeRejected or
            PlaybackControlledWindowsPhysicalRunResult.DeliveryRejected or
            PlaybackControlledWindowsPhysicalRunResult.EvidenceRejected or
            PlaybackControlledWindowsPhysicalRunResult.Cancelled or
            PlaybackControlledWindowsPhysicalRunResult.Faulted;

    private static bool IsValidPreCleanupCheckpoint(
        PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot delivery,
        PlaybackWindowsAudioRenderTraceSnapshot audio,
        PlaybackWindowsOutputNativeEvidenceSnapshot native,
        out int deliveredObservations,
        out int wouldBlockObservations)
    {
        deliveredObservations = 0;
        wouldBlockObservations = 0;
        if (!IsValidDeliveryTrace(
                delivery,
                out deliveredObservations,
                out wouldBlockObservations) ||
            !IsValidAudioTrace(audio, requirePreStop: false, native) ||
            audio.Count > delivery.Count)
        {
            return false;
        }

        return CountAudioResult(audio, PlaybackWindowsAudioRenderWriteResult.Processed) ==
                deliveredObservations &&
            CountAudioResult(audio, PlaybackWindowsAudioRenderWriteResult.WouldBlock) ==
                wouldBlockObservations;
    }

    private static bool IsValidPostStopCheckpoint(
        PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot delivery,
        PlaybackWindowsAudioRenderTraceSnapshot audio,
        PlaybackWindowsOutputNativeEvidenceSnapshot native) =>
        delivery == default && IsValidAudioTrace(audio, requirePreStop: true, native);

    private static bool IsValidDeliveryTrace(
        PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot value,
        out int delivered,
        out int wouldBlock)
    {
        delivered = 0;
        wouldBlock = 0;
        if (value.Count is < 0 or > 4 || value.Overflowed ||
            !UnusedDeliverySlotsAreDefault(value))
        {
            return false;
        }

        PlaybackGenerationMatchedSchedulerDeliveryObservation? previous = null;
        for (var index = 0; index < value.Count; index++)
        {
            var observation = value.GetObservation(index);
            if (!IsValidDelivery(observation))
            {
                return false;
            }

            if (previous is { } prior)
            {
                if (observation.Sequence < prior.Sequence ||
                    observation.PresentationTimestampOffsetTicks <
                        prior.PresentationTimestampOffsetTicks)
                {
                    return false;
                }

                if (observation.Sequence == prior.Sequence)
                {
                    if (prior.Result !=
                            PlaybackGenerationMatchedSchedulerDeliveryResult.RetainedWait ||
                        prior.Completion !=
                            PlaybackGenerationMatchedSchedulerReservationCompletion.Retained ||
                        observation.PresentationTimestampOffsetTicks !=
                            prior.PresentationTimestampOffsetTicks)
                    {
                        return false;
                    }
                }
                else if (prior.Completion ==
                    PlaybackGenerationMatchedSchedulerReservationCompletion.Retained)
                {
                    return false;
                }
            }

            if (observation.Result ==
                PlaybackGenerationMatchedSchedulerDeliveryResult.AudioDelivered)
            {
                delivered++;
            }
            else if (observation.Result ==
                PlaybackGenerationMatchedSchedulerDeliveryResult.RetainedWouldBlock)
            {
                wouldBlock++;
            }

            previous = observation;
        }

        return true;
    }

    private static bool IsValidDelivery(
        PlaybackGenerationMatchedSchedulerDeliveryObservation value)
    {
        if (value.Sequence < 0 ||
            value.MediaKind != PlaybackPolicyMediaKind.Audio ||
            !value.HasPresentationTimestampOffset ||
            value.PresentationTimestampOffsetTicks < 0 ||
            !Enum.IsDefined(value.Result) ||
            value.Result == PlaybackGenerationMatchedSchedulerDeliveryResult.None ||
            !Enum.IsDefined(value.Completion) ||
            value.Completion is not (
                PlaybackGenerationMatchedSchedulerReservationCompletion.Retained or
                PlaybackGenerationMatchedSchedulerReservationCompletion.Delivered))
        {
            return false;
        }

        if (value.HasTimingDecision)
        {
            if (!Enum.IsDefined(value.TimingResult) || value.TimingWaitTicks < 0)
            {
                return false;
            }
        }
        else if (value.TimingResult != default || value.TimingWaitTicks != 0)
        {
            return false;
        }

        return value.Completion ==
                PlaybackGenerationMatchedSchedulerReservationCompletion.Delivered
            ? value.Result == PlaybackGenerationMatchedSchedulerDeliveryResult.AudioDelivered
            : value.Result != PlaybackGenerationMatchedSchedulerDeliveryResult.AudioDelivered &&
                value.Result != PlaybackGenerationMatchedSchedulerDeliveryResult.VideoDelivered &&
                value.Result != PlaybackGenerationMatchedSchedulerDeliveryResult.VideoDropped;
    }

    private static bool IsValidAudioTrace(
        PlaybackWindowsAudioRenderTraceSnapshot value,
        bool requirePreStop,
        PlaybackWindowsOutputNativeEvidenceSnapshot native)
    {
        if (value.Count is < 0 or > 4 || value.Overflowed ||
            !UnusedAudioSlotsAreDefault(value) ||
            !IsSanitizedNativeAudio(native) ||
            native.CounterOverflowed ||
            native.AudioConfigurationConflict ||
            native.AudioRenderTrace != value ||
            native.Source != PlaybackControlledWindowsOutputEvidenceSource.SystemWindows ||
            native.AudioWriteAttemptCount != value.Count)
        {
            return false;
        }

        long acceptedFrames = 0;
        long acceptedBytes = 0;
        long acknowledgements = 0;
        long wouldBlock = 0;
        long starts = 0;
        long deviceLost = 0;
        var streamStarted = false;
        var terminalWriteSeen = false;
        for (var index = 0; index < value.Count; index++)
        {
            if (terminalWriteSeen)
            {
                return false;
            }

            var observation = value.GetObservation(index);
            if (!IsValidAudio(observation, streamStarted))
            {
                return false;
            }

            switch (observation.Result)
            {
                case PlaybackWindowsAudioRenderWriteResult.Processed:
                    acknowledgements++;
                    acceptedFrames += observation.RequestedFrameCount;
                    acceptedBytes += observation.RequestedByteCount;
                    if (observation.StartRequested)
                    {
                        starts++;
                        streamStarted = true;
                    }
                    break;
                case PlaybackWindowsAudioRenderWriteResult.WouldBlock:
                    wouldBlock++;
                    terminalWriteSeen = true;
                    break;
                case PlaybackWindowsAudioRenderWriteResult.DeviceLost:
                    deviceLost++;
                    terminalWriteSeen = true;
                    break;
                default:
                    terminalWriteSeen = true;
                    break;
            }
        }

        if (native.AudioWriteAcknowledgementCount != acknowledgements ||
            native.AudioAcceptedFrameCount != acceptedFrames ||
            native.AudioAcceptedByteCount != acceptedBytes ||
            native.AudioWouldBlockCount != wouldBlock ||
            native.AudioStartAcknowledgementCount != starts ||
            native.AudioDeviceLostCount != deviceLost)
        {
            return false;
        }

        if (acknowledgements == 0)
        {
            if (native.AudioSampleFormat != default ||
                native.AudioSampleRate != 0 ||
                native.AudioChannelCount != 0 ||
                native.AudioGeneration != -1)
            {
                return false;
            }
        }
        else if (native.AudioSampleFormat is not (
                    PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved or
                    PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved) ||
            native.AudioSampleRate <= 0 ||
            native.AudioChannelCount <= 0 ||
            native.AudioGeneration < 0)
        {
            return false;
        }

        var preStop = value.PreStop;
        return requirePreStop
            ? preStop.QueryCount == 1 &&
                !preStop.Overflowed &&
                preStop.PaddingFrameCount >= 0 &&
                (preStop.Available || preStop.PaddingFrameCount == 0)
            : preStop == default;
    }

    private static bool IsValidAudio(
        PlaybackWindowsAudioRenderWriteObservation value,
        bool streamStarted)
    {
        if (value.RequestedFrameCount <= 0 ||
            value.RequestedByteCount <= 0 ||
            !IsOptionalNonNegative(value.HasCapacityFrameCount, value.CapacityFrameCount) ||
            !IsOptionalNonNegative(value.HasPaddingFrameCount, value.PaddingFrameCount) ||
            !IsOptionalNonNegative(value.HasAvailableFrameCount, value.AvailableFrameCount) ||
            value.HasAvailableFrameCount &&
                (!value.HasCapacityFrameCount ||
                    !value.HasPaddingFrameCount ||
                    value.PaddingFrameCount > value.CapacityFrameCount ||
                    value.AvailableFrameCount !=
                        value.CapacityFrameCount - value.PaddingFrameCount) ||
            !Enum.IsDefined(value.Result) ||
            value.Result == PlaybackWindowsAudioRenderWriteResult.None ||
            value.StartRequested == streamStarted ||
            value.StartAcknowledged && !value.StartRequested ||
            value.ReleaseBufferAcknowledged && !value.GetBufferAcknowledged)
        {
            return false;
        }

        if (value.Result == PlaybackWindowsAudioRenderWriteResult.Processed)
        {
            return value.GetBufferAcknowledged &&
                value.ReleaseBufferAcknowledged &&
                value.StartAcknowledged == value.StartRequested;
        }

        return !value.StartAcknowledged;
    }

    private static bool IsOptionalNonNegative(bool available, int value) =>
        available ? value >= 0 : value == 0;

    private static int CountAudioResult(
        PlaybackWindowsAudioRenderTraceSnapshot value,
        PlaybackWindowsAudioRenderWriteResult result)
    {
        var count = 0;
        for (var index = 0; index < value.Count; index++)
        {
            if (value.GetObservation(index).Result == result)
            {
                count++;
            }
        }

        return count;
    }

    private static bool UnusedDeliverySlotsAreDefault(
        PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot value)
    {
        for (var index = value.Count; index < 4; index++)
        {
            if (DeliveryAt(value, index) != default)
            {
                return false;
            }
        }

        return true;
    }

    private static bool UnusedAudioSlotsAreDefault(
        PlaybackWindowsAudioRenderTraceSnapshot value)
    {
        for (var index = value.Count; index < 4; index++)
        {
            if (AudioAt(value, index) != default)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSanitizedNativeAudio(
        PlaybackWindowsOutputNativeEvidenceSnapshot value) =>
        !value.VideoConfigurationConflict &&
        value.VideoPixelFormat == PlaybackDecodedVideoPixelFormat.Bgra32 &&
        value.VideoWidth == 0 &&
        value.VideoHeight == 0 &&
        value.VideoStride == 0 &&
        value.VideoGeneration == -1 &&
        value.VideoPresentAttemptCount == 0 &&
        value.VideoPresentAcknowledgementCount == 0 &&
        value.VideoAcceptedByteCount == 0 &&
        value.VideoWouldBlockCount == 0 &&
        value.VideoResizeCount == 0 &&
        value.VideoDeviceLostCount == 0;

    private static bool SameAudioWrites(
        PlaybackWindowsAudioRenderTraceSnapshot left,
        PlaybackWindowsAudioRenderTraceSnapshot right) =>
        left.Count == right.Count &&
        left.Overflowed == right.Overflowed &&
        left.First == right.First &&
        left.Second == right.Second &&
        left.Third == right.Third &&
        left.Fourth == right.Fourth;

    private static bool SameAudioAggregate(
        PlaybackWindowsOutputNativeEvidenceSnapshot left,
        PlaybackWindowsOutputNativeEvidenceSnapshot right) =>
        left.Source == right.Source &&
        left.CounterOverflowed == right.CounterOverflowed &&
        left.AudioConfigurationConflict == right.AudioConfigurationConflict &&
        left.AudioSampleFormat == right.AudioSampleFormat &&
        left.AudioSampleRate == right.AudioSampleRate &&
        left.AudioChannelCount == right.AudioChannelCount &&
        left.AudioGeneration == right.AudioGeneration &&
        left.AudioWriteAttemptCount == right.AudioWriteAttemptCount &&
        left.AudioWriteAcknowledgementCount == right.AudioWriteAcknowledgementCount &&
        left.AudioAcceptedFrameCount == right.AudioAcceptedFrameCount &&
        left.AudioAcceptedByteCount == right.AudioAcceptedByteCount &&
        left.AudioWouldBlockCount == right.AudioWouldBlockCount &&
        left.AudioStartAcknowledgementCount == right.AudioStartAcknowledgementCount &&
        left.AudioDeviceLostCount == right.AudioDeviceLostCount;

    private static PlaybackWindowsOutputNativeEvidenceSnapshot NormalizeNativeAudio(
        PlaybackWindowsOutputNativeEvidenceSnapshot value,
        PlaybackWindowsAudioRenderTraceSnapshot audioTrace) =>
        new(
            value.Source,
            value.CounterOverflowed,
            VideoConfigurationConflict: false,
            PlaybackDecodedVideoPixelFormat.Bgra32,
            VideoWidth: 0,
            VideoHeight: 0,
            VideoStride: 0,
            VideoGeneration: -1,
            VideoPresentAttemptCount: 0,
            VideoPresentAcknowledgementCount: 0,
            VideoAcceptedByteCount: 0,
            VideoWouldBlockCount: 0,
            VideoResizeCount: 0,
            VideoDeviceLostCount: 0,
            value.AudioConfigurationConflict,
            value.AudioSampleFormat,
            value.AudioSampleRate,
            value.AudioChannelCount,
            value.AudioGeneration,
            value.AudioWriteAttemptCount,
            value.AudioWriteAcknowledgementCount,
            value.AudioAcceptedFrameCount,
            value.AudioAcceptedByteCount,
            value.AudioWouldBlockCount,
            value.AudioStartAcknowledgementCount,
            value.AudioDeviceLostCount)
        {
            AudioRenderTrace = audioTrace,
        };

    private static PlaybackGenerationMatchedSchedulerDeliveryObservation DeliveryAt(
        PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot value,
        int index) => index switch
        {
            0 => value.First,
            1 => value.Second,
            2 => value.Third,
            3 => value.Fourth,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };

    private static PlaybackWindowsAudioRenderWriteObservation AudioAt(
        PlaybackWindowsAudioRenderTraceSnapshot value,
        int index) => index switch
        {
            0 => value.First,
            1 => value.Second,
            2 => value.Third,
            3 => value.Fourth,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };

    private static int ExactMilliseconds(
        TimeSpan value,
        bool allowZero,
        string parameterName)
    {
        if (value.Ticks % TimeSpan.TicksPerMillisecond != 0 ||
            value < TimeSpan.Zero ||
            !allowZero && value == TimeSpan.Zero ||
            value.TotalMilliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return checked((int)(value.Ticks / TimeSpan.TicksPerMillisecond));
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            WriteIndented = false,
        };
        options.Converters.Add(new JsonStringEnumConverter(
            namingPolicy: null,
            allowIntegerValues: false));
        return options;
    }
}
