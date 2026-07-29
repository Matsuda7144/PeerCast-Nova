using System.Globalization;
using System.Text;
using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli;

public sealed record PlaybackControlledWindowsAudioRenderEvidenceRecord(
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
    PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot HoldStartDeliveryTrace,
    PlaybackWindowsAudioRenderTraceSnapshot HoldStartAudioRenderTrace,
    PlaybackWindowsOutputNativeEvidenceSnapshot HoldStartNativeEvidence,
    PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot PreCleanupDeliveryTrace,
    PlaybackWindowsAudioRenderTraceSnapshot PreCleanupAudioRenderTrace,
    PlaybackWindowsOutputNativeEvidenceSnapshot PreCleanupNativeEvidence,
    PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot PostStopDeliveryTrace,
    PlaybackWindowsAudioRenderTraceSnapshot PostStopAudioRenderTrace,
    PlaybackWindowsOutputNativeEvidenceSnapshot PostStopNativeEvidence,
    bool PhysicalRunsExecutedByThisCommand,
    bool AudioAcceptanceClaimed,
    bool ExternalBinaryOrPayloadPublished);

/// <summary>
/// Canonical bounded codec for one controlled audio render-evidence run. The record
/// contains no local path, device identity, handle, pointer, HRESULT, wall-clock
/// timestamp, encoded packet, decoded sample, screenshot, or operator identity.
/// </summary>
public static class PlaybackControlledWindowsAudioRenderEvidenceRecordCodec
{
    public const int CurrentSchemaVersion = 1;
    public const string CurrentMilestone = "0.0.10d Phase 5";
    public const int MaximumRecordCharacterCount = 32 * 1024;

    private const int CheckpointPartCount = 110;
    private const string Header =
        "PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-EVIDENCE 1";
    private const string Footer =
        "END-PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-EVIDENCE";

    public static PlaybackControlledWindowsAudioRenderEvidenceRecord Create(
        PlaybackControlledWindowsPhysicalRunSnapshot snapshot,
        string expectedFixtureSha256,
        TimeSpan observationHoldDuration,
        TimeSpan audioBufferDuration,
        bool physicalRunsExecutedByThisCommand)
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

        var holdStart = snapshot.HoldStartAudioRenderEvidence;
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
            holdStart.DeliveryTrace,
            holdStart.AudioRenderTrace,
            NormalizeNativeAudio(holdStart.NativeEvidence, holdStart.AudioRenderTrace),
            preCleanup.DeliveryTrace,
            preCleanup.AudioRenderTrace,
            NormalizeNativeAudio(preCleanup.NativeEvidence, preCleanup.AudioRenderTrace),
            postStop.DeliveryTrace,
            postStop.AudioRenderTrace,
            NormalizeNativeAudio(postStop.NativeEvidence, postStop.AudioRenderTrace),
            physicalRunsExecutedByThisCommand,
            AudioAcceptanceClaimed: false,
            ExternalBinaryOrPayloadPublished: false);
    }

    public static async ValueTask WriteAsync(
        TextWriter output,
        PlaybackControlledWindowsAudioRenderEvidenceRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();
        await output.WriteAsync(Serialize(record).AsMemory(), cancellationToken)
            .ConfigureAwait(false);
    }

    public static string Serialize(
        PlaybackControlledWindowsAudioRenderEvidenceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!IsSyntacticallyValid(record, out var normalizedDigest))
        {
            throw new ArgumentException(
                "The controlled audio render evidence record is invalid.",
                nameof(record));
        }

        var builder = new StringBuilder(capacity: 8 * 1024);
        Append(builder, Header);
        Append(builder, $"Milestone: {record.Milestone}");
        Append(builder, $"Kind: {record.Kind}");
        Append(builder, $"Runtime-Build: {record.RuntimeBuildId}");
        Append(builder, $"Fixture-SHA256: {normalizedDigest}");
        Append(builder, $"Result: {record.Result}");
        Append(builder, $"Evidence: {record.EvidenceResult}");
        Append(builder, Format("Fixture-Bytes", record.FixtureByteCount));
        Append(builder, Format("Fixture-Packets", record.FixturePacketCount));
        Append(builder, Format("Decode-Operations", record.DecodeOperationCount));
        Append(builder, Format("Delivery-Attempts", record.DeliveryAttemptCount));
        Append(builder, Format("Delivered", record.DeliveredCount));
        Append(builder, Format("Retained-WouldBlock", record.RetainedWouldBlockCount));
        Append(
            builder,
            Format("Observation-Hold-Milliseconds", record.ObservationHoldMilliseconds));
        Append(builder, Format("Audio-Buffer-Milliseconds", record.AudioBufferMilliseconds));
        Append(builder, $"Runner-State: {record.RunnerState}");
        Append(builder, $"Lifecycle-State: {record.LifecycleState}");
        Append(builder, $"Borrowed-Window-Preserved: {record.BorrowedWindowPreserved}");
        Append(
            builder,
            $"Borrowed-Dispatcher-Preserved: {record.BorrowedDispatcherPreserved}");
        Append(
            builder,
            $"Physical-Runs-Executed-By-This-Command: " +
            record.PhysicalRunsExecutedByThisCommand);
        Append(builder, $"Audio-Acceptance-Claimed: {record.AudioAcceptanceClaimed}");
        Append(
            builder,
            $"External-Binary-Or-Payload-Published: " +
            record.ExternalBinaryOrPayloadPublished);
        AppendCheckpoint(
            builder,
            "Hold-Start-Checkpoint: ",
            record.HoldStartDeliveryTrace,
            record.HoldStartAudioRenderTrace,
            record.HoldStartNativeEvidence);
        AppendCheckpoint(
            builder,
            "Pre-Cleanup-Checkpoint: ",
            record.PreCleanupDeliveryTrace,
            record.PreCleanupAudioRenderTrace,
            record.PreCleanupNativeEvidence);
        AppendCheckpoint(
            builder,
            "Post-Stop-Checkpoint: ",
            record.PostStopDeliveryTrace,
            record.PostStopAudioRenderTrace,
            record.PostStopNativeEvidence);
        Append(builder, Footer);
        if (builder.Length > MaximumRecordCharacterCount)
        {
            throw new InvalidOperationException(
                "The controlled audio render evidence record exceeds its fixed size limit.");
        }

        return builder.ToString();
    }

    public static bool TryParse(
        string? text,
        out PlaybackControlledWindowsAudioRenderEvidenceRecord? record)
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

        var reader = new LineReader(normalized.Split('\n'));
        if (!reader.TryReadExact(Header) ||
            !reader.TryRead("Milestone: ", out var milestone) ||
            !reader.TryReadEnum(
                "Kind: ",
                out PlaybackControlledWindowsOutputValidationKind kind) ||
            !reader.TryRead("Runtime-Build: ", out var runtimeBuildId) ||
            !reader.TryRead("Fixture-SHA256: ", out var fixtureSha256) ||
            !reader.TryReadEnum(
                "Result: ",
                out PlaybackControlledWindowsPhysicalRunResult result) ||
            !reader.TryReadEnum(
                "Evidence: ",
                out PlaybackControlledWindowsOutputEvidenceResult evidence) ||
            !reader.TryReadInt64("Fixture-Bytes: ", out var fixtureBytes) ||
            !reader.TryReadInt32("Fixture-Packets: ", out var fixturePackets) ||
            !reader.TryReadInt32("Decode-Operations: ", out var decodeOperations) ||
            !reader.TryReadInt32("Delivery-Attempts: ", out var deliveryAttempts) ||
            !reader.TryReadInt32("Delivered: ", out var delivered) ||
            !reader.TryReadInt32("Retained-WouldBlock: ", out var retainedWouldBlock) ||
            !reader.TryReadInt32(
                "Observation-Hold-Milliseconds: ",
                out var observationHoldMilliseconds) ||
            !reader.TryReadInt32(
                "Audio-Buffer-Milliseconds: ",
                out var audioBufferMilliseconds) ||
            !reader.TryReadEnum(
                "Runner-State: ",
                out PlaybackWindowsPhysicalRunnerState runnerState) ||
            !reader.TryReadEnum(
                "Lifecycle-State: ",
                out PlaybackApprovedExternalRuntimeWindowsLifecycleState lifecycleState) ||
            !reader.TryReadBoolean(
                "Borrowed-Window-Preserved: ",
                out var windowPreserved) ||
            !reader.TryReadBoolean(
                "Borrowed-Dispatcher-Preserved: ",
                out var dispatcherPreserved) ||
            !reader.TryReadBoolean(
                "Physical-Runs-Executed-By-This-Command: ",
                out var physicalRunsExecuted) ||
            !reader.TryReadBoolean(
                "Audio-Acceptance-Claimed: ",
                out var audioAcceptanceClaimed) ||
            !reader.TryReadBoolean(
                "External-Binary-Or-Payload-Published: ",
                out var externalPublished) ||
            !TryReadCheckpoint(
                reader,
                "Hold-Start-Checkpoint: ",
                out var holdStartDelivery,
                out var holdStartAudio,
                out var holdStartNative) ||
            !TryReadCheckpoint(
                reader,
                "Pre-Cleanup-Checkpoint: ",
                out var preCleanupDelivery,
                out var preCleanupAudio,
                out var preCleanupNative) ||
            !TryReadCheckpoint(
                reader,
                "Post-Stop-Checkpoint: ",
                out var postStopDelivery,
                out var postStopAudio,
                out var postStopNative) ||
            !reader.TryReadExact(Footer) || !reader.IsComplete ||
            !PlaybackControlledWindowsPhysicalRunRecordCodec.TryNormalizeSha256(
                fixtureSha256,
                out var normalizedDigest))
        {
            return false;
        }

        var candidate = new PlaybackControlledWindowsAudioRenderEvidenceRecord(
            CurrentSchemaVersion,
            milestone,
            kind,
            runtimeBuildId,
            normalizedDigest,
            result,
            evidence,
            fixtureBytes,
            fixturePackets,
            decodeOperations,
            deliveryAttempts,
            delivered,
            retainedWouldBlock,
            observationHoldMilliseconds,
            audioBufferMilliseconds,
            runnerState,
            lifecycleState,
            windowPreserved,
            dispatcherPreserved,
            holdStartDelivery,
            holdStartAudio,
            holdStartNative,
            preCleanupDelivery,
            preCleanupAudio,
            preCleanupNative,
            postStopDelivery,
            postStopAudio,
            postStopNative,
            physicalRunsExecuted,
            audioAcceptanceClaimed,
            externalPublished);
        if (!IsSyntacticallyValid(candidate, out _))
        {
            return false;
        }

        record = candidate;
        return true;
    }

    internal static bool IsValid(
        PlaybackControlledWindowsAudioRenderEvidenceRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return IsSyntacticallyValid(record, out _);
    }

    private static void AppendCheckpoint(
        StringBuilder builder,
        string prefix,
        PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot delivery,
        PlaybackWindowsAudioRenderTraceSnapshot audio,
        PlaybackWindowsOutputNativeEvidenceSnapshot native)
    {
        var parts = new List<string>(CheckpointPartCount);
        Add(parts, delivery.Count);
        Add(parts, delivery.Overflowed);
        for (var index = 0; index < 4; index++)
        {
            AddDelivery(parts, DeliveryAt(delivery, index));
        }

        Add(parts, audio.Count);
        Add(parts, audio.Overflowed);
        for (var index = 0; index < 4; index++)
        {
            AddAudio(parts, AudioAt(audio, index));
        }

        Add(parts, audio.PreStop.QueryCount);
        Add(parts, audio.PreStop.Available);
        Add(parts, audio.PreStop.PaddingFrameCount);
        Add(parts, audio.PreStop.Overflowed);
        AddNativeAudio(parts, native);
        if (parts.Count != CheckpointPartCount)
        {
            throw new InvalidOperationException("The fixed checkpoint schema is inconsistent.");
        }

        Append(builder, prefix + string.Join('|', parts));
    }

    private static bool TryReadCheckpoint(
        LineReader reader,
        string prefix,
        out PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot delivery,
        out PlaybackWindowsAudioRenderTraceSnapshot audio,
        out PlaybackWindowsOutputNativeEvidenceSnapshot native)
    {
        delivery = default;
        audio = default;
        native = default;
        if (!reader.TryReadParts(prefix, CheckpointPartCount, out var parts))
        {
            return false;
        }

        var values = new PartReader(parts);
        if (!values.TryReadInt32(out var deliveryCount) ||
            !values.TryReadBoolean(out var deliveryOverflowed) ||
            !TryReadDelivery(values, out var firstDelivery) ||
            !TryReadDelivery(values, out var secondDelivery) ||
            !TryReadDelivery(values, out var thirdDelivery) ||
            !TryReadDelivery(values, out var fourthDelivery) ||
            !values.TryReadInt32(out var audioCount) ||
            !values.TryReadBoolean(out var audioOverflowed) ||
            !TryReadAudio(values, out var firstAudio) ||
            !TryReadAudio(values, out var secondAudio) ||
            !TryReadAudio(values, out var thirdAudio) ||
            !TryReadAudio(values, out var fourthAudio) ||
            !values.TryReadInt32(out var preStopQueryCount) ||
            !values.TryReadBoolean(out var preStopAvailable) ||
            !values.TryReadInt32(out var preStopPadding) ||
            !values.TryReadBoolean(out var preStopOverflowed))
        {
            return false;
        }

        delivery = new(
            deliveryCount,
            deliveryOverflowed,
            firstDelivery,
            secondDelivery,
            thirdDelivery,
            fourthDelivery);
        audio = new(
            audioCount,
            audioOverflowed,
            firstAudio,
            secondAudio,
            thirdAudio,
            fourthAudio,
            new(
                preStopQueryCount,
                preStopAvailable,
                preStopPadding,
                preStopOverflowed));
        if (!TryReadNativeAudio(values, audio, out native) || !values.IsComplete)
        {
            delivery = default;
            audio = default;
            native = default;
            return false;
        }

        return true;
    }

    private static bool IsSyntacticallyValid(
        PlaybackControlledWindowsAudioRenderEvidenceRecord record,
        out string normalizedDigest)
    {
        normalizedDigest = string.Empty;
        return record.SchemaVersion == CurrentSchemaVersion &&
            string.Equals(record.Milestone, CurrentMilestone, StringComparison.Ordinal) &&
            record.Kind == PlaybackControlledWindowsOutputValidationKind.Audio &&
            string.Equals(
                record.RuntimeBuildId,
                PlaybackFfmpegBackendCandidate.BuildId,
                StringComparison.Ordinal) &&
            PlaybackControlledWindowsPhysicalRunRecordCodec.TryNormalizeSha256(
                record.FixtureSha256,
                out normalizedDigest) &&
            record.Result == PlaybackControlledWindowsPhysicalRunResult.Completed &&
            record.EvidenceResult == PlaybackControlledWindowsOutputEvidenceResult.Accepted &&
            record.FixtureByteCount > 0 && record.FixturePacketCount > 0 &&
            record.DecodeOperationCount is >= 1 and <= 4096 &&
            record.DeliveryAttemptCount == 4 && record.DeliveredCount == 4 &&
            record.RetainedWouldBlockCount == 0 &&
            record.ObservationHoldMilliseconds is >= 0 and <= 30_000 &&
            record.AudioBufferMilliseconds >= 1 &&
            record.AudioBufferMilliseconds <=
                PlaybackWindowsOutputStreamInitializationBoundary.MaximumAudioBufferDuration
                    .TotalMilliseconds &&
            record.RunnerState == PlaybackWindowsPhysicalRunnerState.Stopped &&
            record.LifecycleState ==
                PlaybackApprovedExternalRuntimeWindowsLifecycleState.Disposed &&
            record.BorrowedWindowPreserved && record.BorrowedDispatcherPreserved &&
            record.PhysicalRunsExecutedByThisCommand &&
            !record.AudioAcceptanceClaimed && !record.ExternalBinaryOrPayloadPublished &&
            IsValidCheckpoint(
                record.HoldStartDeliveryTrace,
                record.HoldStartAudioRenderTrace,
                record.HoldStartNativeEvidence,
                requireDeliveryTrace: true,
                requirePreStop: false) &&
            IsValidCheckpoint(
                record.PreCleanupDeliveryTrace,
                record.PreCleanupAudioRenderTrace,
                record.PreCleanupNativeEvidence,
                requireDeliveryTrace: true,
                requirePreStop: false) &&
            IsValidPostStopCheckpoint(
                record.PostStopDeliveryTrace,
                record.PostStopAudioRenderTrace,
                record.PostStopNativeEvidence,
                record.PreCleanupDeliveryTrace) &&
            record.HoldStartDeliveryTrace == record.PreCleanupDeliveryTrace &&
            record.HoldStartAudioRenderTrace == record.PreCleanupAudioRenderTrace &&
            SameAudioAggregate(
                record.HoldStartNativeEvidence,
                record.PreCleanupNativeEvidence) &&
            SameAudioWrites(
                record.HoldStartAudioRenderTrace,
                record.PostStopAudioRenderTrace) &&
            SameAudioAggregate(
                record.HoldStartNativeEvidence,
                record.PostStopNativeEvidence);
    }

    private static bool IsValidPostStopCheckpoint(
        PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot delivery,
        PlaybackWindowsAudioRenderTraceSnapshot audio,
        PlaybackWindowsOutputNativeEvidenceSnapshot native,
        PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot preCleanupDelivery)
    {
        if (delivery == default)
        {
            return IsValidCheckpoint(
                delivery,
                audio,
                native,
                requireDeliveryTrace: false,
                requirePreStop: true);
        }

        return delivery == preCleanupDelivery &&
            IsValidCheckpoint(
                delivery,
                audio,
                native,
                requireDeliveryTrace: true,
                requirePreStop: true);
    }

    private static bool IsValidCheckpoint(
        PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot delivery,
        PlaybackWindowsAudioRenderTraceSnapshot audio,
        PlaybackWindowsOutputNativeEvidenceSnapshot native,
        bool requireDeliveryTrace,
        bool requirePreStop)
    {
        if (delivery.Count is < 0 or > 4 || audio.Count != 4 ||
            delivery.Overflowed || audio.Overflowed || native.CounterOverflowed ||
            native.AudioConfigurationConflict || native.AudioRenderTrace != audio ||
            native.Source != PlaybackControlledWindowsOutputEvidenceSource.SystemWindows ||
            native.AudioSampleFormat is not (
                PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved or
                PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved) ||
            native.AudioSampleRate <= 0 || native.AudioChannelCount <= 0 ||
            native.AudioGeneration < 0)
        {
            return false;
        }

        if (requireDeliveryTrace ? delivery.Count != 4 : delivery != default)
        {
            return false;
        }

        long acceptedFrames = 0;
        long acceptedBytes = 0;
        for (var index = 0; index < audio.Count; index++)
        {
            var observation = audio.GetObservation(index);
            if (!IsValidAudio(observation, index == 0))
            {
                return false;
            }

            acceptedFrames += observation.RequestedFrameCount;
            acceptedBytes += observation.RequestedByteCount;
        }

        if (requireDeliveryTrace)
        {
            long previousSequence = -1;
            long previousPresentationOffset = -1;
            for (var index = 0; index < delivery.Count; index++)
            {
                var observation = delivery.GetObservation(index);
                if (!IsValidDelivery(observation) ||
                    observation.Sequence <= previousSequence ||
                    observation.PresentationTimestampOffsetTicks < previousPresentationOffset)
                {
                    return false;
                }

                previousSequence = observation.Sequence;
                previousPresentationOffset = observation.PresentationTimestampOffsetTicks;
            }
        }

        if (native.AudioWriteAttemptCount != 4 ||
            native.AudioWriteAcknowledgementCount != 4 ||
            native.AudioAcceptedFrameCount != acceptedFrames ||
            native.AudioAcceptedByteCount != acceptedBytes ||
            native.AudioWouldBlockCount != 0 ||
            native.AudioStartAcknowledgementCount != 1 ||
            native.AudioDeviceLostCount != 0)
        {
            return false;
        }

        var preStop = audio.PreStop;
        return requirePreStop
            ? preStop.QueryCount == 1 && !preStop.Overflowed &&
                preStop.PaddingFrameCount >= 0 &&
                (preStop.Available || preStop.PaddingFrameCount == 0)
            : preStop == default;
    }

    private static bool IsValidDelivery(
        PlaybackGenerationMatchedSchedulerDeliveryObservation value) =>
        value.Sequence >= 0 && value.MediaKind == PlaybackPolicyMediaKind.Audio &&
        value.HasPresentationTimestampOffset && value.PresentationTimestampOffsetTicks >= 0 &&
        value.HasTimingDecision && value.TimingWaitTicks >= 0 &&
        Enum.IsDefined(value.TimingResult) &&
        value.Result == PlaybackGenerationMatchedSchedulerDeliveryResult.AudioDelivered &&
        value.Completion ==
            PlaybackGenerationMatchedSchedulerReservationCompletion.Delivered;

    private static bool IsValidAudio(
        PlaybackWindowsAudioRenderWriteObservation value,
        bool first) =>
        value.RequestedFrameCount > 0 && value.RequestedByteCount > 0 &&
        value.HasCapacityFrameCount && value.CapacityFrameCount > 0 &&
        value.HasPaddingFrameCount && value.PaddingFrameCount >= 0 &&
        value.HasAvailableFrameCount && value.AvailableFrameCount >= 0 &&
        value.PaddingFrameCount <= value.CapacityFrameCount &&
        value.AvailableFrameCount == value.CapacityFrameCount - value.PaddingFrameCount &&
        value.StartRequested == first && value.StartAcknowledged == first &&
        value.GetBufferAcknowledged && value.ReleaseBufferAcknowledged &&
        value.Result == PlaybackWindowsAudioRenderWriteResult.Processed;

    private static bool SameAudioWrites(
        PlaybackWindowsAudioRenderTraceSnapshot left,
        PlaybackWindowsAudioRenderTraceSnapshot right) =>
        left.Count == right.Count && left.Overflowed == right.Overflowed &&
        left.First == right.First && left.Second == right.Second &&
        left.Third == right.Third && left.Fourth == right.Fourth;

    private static bool SameAudioAggregate(
        PlaybackWindowsOutputNativeEvidenceSnapshot left,
        PlaybackWindowsOutputNativeEvidenceSnapshot right) =>
        left.Source == right.Source && left.CounterOverflowed == right.CounterOverflowed &&
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

    private static void AddDelivery(
        List<string> parts,
        PlaybackGenerationMatchedSchedulerDeliveryObservation value)
    {
        Add(parts, value.Sequence);
        Add(parts, value.MediaKind);
        Add(parts, value.HasPresentationTimestampOffset);
        Add(parts, value.PresentationTimestampOffsetTicks);
        Add(parts, value.HasTimingDecision);
        Add(parts, value.TimingResult);
        Add(parts, value.TimingWaitTicks);
        Add(parts, value.Result);
        Add(parts, value.Completion);
    }

    private static void AddAudio(
        List<string> parts,
        PlaybackWindowsAudioRenderWriteObservation value)
    {
        Add(parts, value.RequestedFrameCount);
        Add(parts, value.RequestedByteCount);
        Add(parts, value.HasCapacityFrameCount);
        Add(parts, value.CapacityFrameCount);
        Add(parts, value.HasPaddingFrameCount);
        Add(parts, value.PaddingFrameCount);
        Add(parts, value.HasAvailableFrameCount);
        Add(parts, value.AvailableFrameCount);
        Add(parts, value.StartRequested);
        Add(parts, value.GetBufferAcknowledged);
        Add(parts, value.ReleaseBufferAcknowledged);
        Add(parts, value.StartAcknowledged);
        Add(parts, value.Result);
    }

    private static void AddNativeAudio(
        List<string> parts,
        PlaybackWindowsOutputNativeEvidenceSnapshot value)
    {
        Add(parts, value.Source);
        Add(parts, value.CounterOverflowed);
        Add(parts, value.AudioConfigurationConflict);
        Add(parts, value.AudioSampleFormat);
        Add(parts, value.AudioSampleRate);
        Add(parts, value.AudioChannelCount);
        Add(parts, value.AudioGeneration);
        Add(parts, value.AudioWriteAttemptCount);
        Add(parts, value.AudioWriteAcknowledgementCount);
        Add(parts, value.AudioAcceptedFrameCount);
        Add(parts, value.AudioAcceptedByteCount);
        Add(parts, value.AudioWouldBlockCount);
        Add(parts, value.AudioStartAcknowledgementCount);
        Add(parts, value.AudioDeviceLostCount);
    }

    private static bool TryReadDelivery(
        PartReader reader,
        out PlaybackGenerationMatchedSchedulerDeliveryObservation value)
    {
        value = default;
        if (!reader.TryReadInt64(out var sequence) ||
            !reader.TryReadEnum(out PlaybackPolicyMediaKind mediaKind) ||
            !reader.TryReadBoolean(out var hasPresentationOffset) ||
            !reader.TryReadInt64(out var presentationOffsetTicks) ||
            !reader.TryReadBoolean(out var hasTimingDecision) ||
            !reader.TryReadEnum(out PlaybackAvTimingDecisionResult timingResult) ||
            !reader.TryReadInt64(out var timingWaitTicks) ||
            !reader.TryReadEnum(
                out PlaybackGenerationMatchedSchedulerDeliveryResult result) ||
            !reader.TryReadEnum(
                out PlaybackGenerationMatchedSchedulerReservationCompletion completion))
        {
            return false;
        }

        value = new(
            sequence,
            mediaKind,
            hasPresentationOffset,
            presentationOffsetTicks,
            hasTimingDecision,
            timingResult,
            timingWaitTicks,
            result,
            completion);
        return true;
    }

    private static bool TryReadAudio(
        PartReader reader,
        out PlaybackWindowsAudioRenderWriteObservation value)
    {
        value = default;
        if (!reader.TryReadInt32(out var requestedFrames) ||
            !reader.TryReadInt32(out var requestedBytes) ||
            !reader.TryReadBoolean(out var hasCapacity) ||
            !reader.TryReadInt32(out var capacityFrames) ||
            !reader.TryReadBoolean(out var hasPadding) ||
            !reader.TryReadInt32(out var paddingFrames) ||
            !reader.TryReadBoolean(out var hasAvailable) ||
            !reader.TryReadInt32(out var availableFrames) ||
            !reader.TryReadBoolean(out var startRequested) ||
            !reader.TryReadBoolean(out var getBufferAcknowledged) ||
            !reader.TryReadBoolean(out var releaseBufferAcknowledged) ||
            !reader.TryReadBoolean(out var startAcknowledged) ||
            !reader.TryReadEnum(out PlaybackWindowsAudioRenderWriteResult result))
        {
            return false;
        }

        value = new(
            requestedFrames,
            requestedBytes,
            hasCapacity,
            capacityFrames,
            hasPadding,
            paddingFrames,
            hasAvailable,
            availableFrames,
            startRequested,
            getBufferAcknowledged,
            releaseBufferAcknowledged,
            startAcknowledged,
            result);
        return true;
    }

    private static bool TryReadNativeAudio(
        PartReader reader,
        PlaybackWindowsAudioRenderTraceSnapshot audioTrace,
        out PlaybackWindowsOutputNativeEvidenceSnapshot value)
    {
        value = default;
        if (!reader.TryReadEnum(out PlaybackControlledWindowsOutputEvidenceSource source) ||
            !reader.TryReadBoolean(out var counterOverflowed) ||
            !reader.TryReadBoolean(out var configurationConflict) ||
            !reader.TryReadEnum(out PlaybackDecodedAudioSampleFormat sampleFormat) ||
            !reader.TryReadInt32(out var sampleRate) ||
            !reader.TryReadInt32(out var channelCount) ||
            !reader.TryReadInt64(out var generation) ||
            !reader.TryReadInt64(out var writeAttempts) ||
            !reader.TryReadInt64(out var writeAcknowledgements) ||
            !reader.TryReadInt64(out var acceptedFrames) ||
            !reader.TryReadInt64(out var acceptedBytes) ||
            !reader.TryReadInt64(out var wouldBlock) ||
            !reader.TryReadInt64(out var startAcknowledgements) ||
            !reader.TryReadInt64(out var deviceLost))
        {
            return false;
        }

        value = new(
            source,
            counterOverflowed,
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
            configurationConflict,
            sampleFormat,
            sampleRate,
            channelCount,
            generation,
            writeAttempts,
            writeAcknowledgements,
            acceptedFrames,
            acceptedBytes,
            wouldBlock,
            startAcknowledgements,
            deviceLost)
        {
            AudioRenderTrace = audioTrace,
        };
        return true;
    }

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
            value < TimeSpan.Zero || !allowZero && value == TimeSpan.Zero ||
            value.TotalMilliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return checked((int)(value.Ticks / TimeSpan.TicksPerMillisecond));
    }

    private static void Add<T>(List<string> values, T value) where T : notnull =>
        values.Add(value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : value.ToString() ?? string.Empty);

    private static bool TryParseEnum<T>(string value, out T result)
        where T : struct, Enum =>
        Enum.TryParse(value, ignoreCase: false, out result) && Enum.IsDefined(result);

    private static bool TryParseBoolean(string value, out bool result)
    {
        if (string.Equals(value, bool.TrueString, StringComparison.Ordinal))
        {
            result = true;
            return true;
        }

        if (string.Equals(value, bool.FalseString, StringComparison.Ordinal))
        {
            result = false;
            return true;
        }

        result = false;
        return false;
    }

    private static bool TryParseInt32(string value, out int result) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);

    private static bool TryParseInt64(string value, out long result) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);

    private static string Format(string name, long value) =>
        $"{name}: {value.ToString(CultureInfo.InvariantCulture)}";

    private static void Append(StringBuilder builder, string value) =>
        builder.Append(value).Append('\n');

    private sealed class LineReader(string[] lines)
    {
        private int _index;

        internal bool IsComplete => _index == lines.Length;

        internal bool TryReadExact(string expected)
        {
            if (_index >= lines.Length ||
                !string.Equals(lines[_index], expected, StringComparison.Ordinal))
            {
                return false;
            }

            _index++;
            return true;
        }

        internal bool TryRead(string prefix, out string value)
        {
            value = string.Empty;
            if (_index >= lines.Length ||
                !lines[_index].StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            value = lines[_index][prefix.Length..];
            if (value.Length == 0)
            {
                value = string.Empty;
                return false;
            }

            _index++;
            return true;
        }

        internal bool TryReadParts(string prefix, int count, out string[] parts)
        {
            parts = [];
            if (!TryRead(prefix, out var value))
            {
                return false;
            }

            parts = value.Split('|');
            return parts.Length == count && parts.All(static part => part.Length > 0);
        }

        internal bool TryReadInt32(string prefix, out int value)
        {
            value = 0;
            return TryRead(prefix, out var text) && TryParseInt32(text, out value);
        }

        internal bool TryReadInt64(string prefix, out long value)
        {
            value = 0;
            return TryRead(prefix, out var text) && TryParseInt64(text, out value);
        }

        internal bool TryReadBoolean(string prefix, out bool value)
        {
            value = false;
            return TryRead(prefix, out var text) && TryParseBoolean(text, out value);
        }

        internal bool TryReadEnum<T>(string prefix, out T value)
            where T : struct, Enum
        {
            value = default;
            return TryRead(prefix, out var text) && TryParseEnum(text, out value);
        }
    }

    private sealed class PartReader(string[] parts)
    {
        private int _index;

        internal bool IsComplete => _index == parts.Length;

        internal bool TryReadInt32(out int value)
        {
            value = 0;
            return TryRead(out var text) && TryParseInt32(text, out value);
        }

        internal bool TryReadInt64(out long value)
        {
            value = 0;
            return TryRead(out var text) && TryParseInt64(text, out value);
        }

        internal bool TryReadBoolean(out bool value)
        {
            value = false;
            return TryRead(out var text) && TryParseBoolean(text, out value);
        }

        internal bool TryReadEnum<T>(out T value)
            where T : struct, Enum
        {
            value = default;
            return TryRead(out var text) && TryParseEnum(text, out value);
        }

        private bool TryRead(out string value)
        {
            value = string.Empty;
            if (_index >= parts.Length)
            {
                return false;
            }

            value = parts[_index++];
            return value.Length > 0;
        }
    }
}
