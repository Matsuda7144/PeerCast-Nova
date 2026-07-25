using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsAudioRenderEvidenceRecordPreflightCommandTests
{
    private const string FixtureSha256 =
        "32872B300286525F4EF97FEE3533E25419AC8E48BFD4BC2C4FD9545F071B1E15";

    [Fact]
    public async Task Valid_completed_record_is_revalidated_without_physical_output()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, EvidenceRecord());
            using var output = new StringWriter();

            var result =
                await PlaybackControlledWindowsAudioRenderEvidenceRecordPreflightCommand
                    .RunAsync(Arguments(path), output);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(
                PlaybackControlledWindowsAudioRenderEvidenceRecordPreflightResult
                    .RenderEvidenceRecordValidated,
                result.Result);
            Assert.Equal(
                PlaybackControlledWindowsPhysicalRunResult.Completed,
                result.RecordResult);
            Assert.Equal(
                PlaybackControlledWindowsOutputEvidenceResult.Accepted,
                result.EvidenceResult);
            Assert.True(result.Validated);
            var report = output.ToString();
            Assert.Contains("Result: RenderEvidenceRecordValidated", report);
            Assert.Contains("Record-Result: Completed", report);
            Assert.Contains("Evidence: Accepted", report);
            Assert.Contains("Render-Evidence-Record-Validated: True", report);
            Assert.Contains("Captured-Physical-Run-Completion-Established: True", report);
            Assert.Contains("Completed-Render-Evidence-Established: True", report);
            Assert.Contains("Audio-Acceptance-Claimed: False", report);
            Assert.Contains("Physical-Runs-Executed-By-This-Command: False", report);
            Assert.DoesNotContain(path, report, StringComparison.Ordinal);
            Assert.DoesNotContain("Hold-Start-Checkpoint:", report, StringComparison.Ordinal);
            Assert.DoesNotContain("Pre-Cleanup-Checkpoint:", report, StringComparison.Ordinal);
            Assert.DoesNotContain("Post-Stop-Checkpoint:", report, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", 1000, 100)]
    [InlineData(FixtureSha256, 999, 100)]
    [InlineData(FixtureSha256, 1000, 99)]
    public async Task Identity_mismatch_fails_closed(
        string expectedFixtureSha256,
        int observationHoldMilliseconds,
        int audioBufferMilliseconds)
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, EvidenceRecord());
            using var output = new StringWriter();

            var result =
                await PlaybackControlledWindowsAudioRenderEvidenceRecordPreflightCommand
                    .RunAsync(
                        Arguments(
                            path,
                            expectedFixtureSha256,
                            observationHoldMilliseconds,
                            audioBufferMilliseconds),
                        output);

            Assert.Equal(1, result.ExitCode);
            Assert.Equal(
                PlaybackControlledWindowsAudioRenderEvidenceRecordPreflightResult
                    .IdentityMismatch,
                result.Result);
            Assert.Equal(PlaybackControlledWindowsPhysicalRunResult.None, result.RecordResult);
            Assert.Equal(
                PlaybackControlledWindowsOutputEvidenceResult.None,
                result.EvidenceResult);
            Assert.False(result.Validated);
            Assert.Contains("Render-Evidence-Record-Validated: False", output.ToString());
            Assert.Contains(
                "Captured-Physical-Run-Completion-Established: False",
                output.ToString());
            Assert.DoesNotContain(path, output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Valid_terminal_record_is_rejected_as_not_completed_evidence()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, TerminalRecord());
            using var output = new StringWriter();

            var result =
                await PlaybackControlledWindowsAudioRenderEvidenceRecordPreflightCommand
                    .RunAsync(Arguments(path), output);

            Assert.Equal(1, result.ExitCode);
            Assert.Equal(
                PlaybackControlledWindowsAudioRenderEvidenceRecordPreflightResult.InvalidRecord,
                result.Result);
            Assert.False(result.Validated);
            Assert.Contains("Completed-Render-Evidence-Established: False", output.ToString());
            Assert.DoesNotContain("Record: {", output.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(path, output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Duplicate_option_is_invalid_configuration_before_file_access()
    {
        using var output = new StringWriter();
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var arguments = Arguments(missingPath).Concat(
            ["--record", missingPath]).ToArray();

        var result =
            await PlaybackControlledWindowsAudioRenderEvidenceRecordPreflightCommand.RunAsync(
                arguments,
                output);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(
            PlaybackControlledWindowsAudioRenderEvidenceRecordPreflightResult
                .InvalidConfiguration,
            result.Result);
        Assert.False(result.Validated);
        Assert.Equal("Result: InvalidConfiguration" + Environment.NewLine, output.ToString());
    }

    [Fact]
    public async Task Missing_arguments_are_invalid_configuration()
    {
        using var output = new StringWriter();

        var result =
            await PlaybackControlledWindowsAudioRenderEvidenceRecordPreflightCommand.RunAsync(
                [PlaybackControlledWindowsAudioRenderEvidenceRecordPreflightCommand.Argument],
                output);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(
            PlaybackControlledWindowsAudioRenderEvidenceRecordPreflightResult
                .InvalidConfiguration,
            result.Result);
        Assert.False(result.Validated);
        Assert.Equal("Result: InvalidConfiguration" + Environment.NewLine, output.ToString());
    }

    private static string[] Arguments(
        string path,
        string fixtureSha256 = FixtureSha256,
        int observationHoldMilliseconds = 1000,
        int audioBufferMilliseconds = 100) =>
    [
        PlaybackControlledWindowsAudioRenderEvidenceRecordPreflightCommand.Argument,
        "--record", path,
        "--fixture-sha256", fixtureSha256,
        "--observation-hold-ms", observationHoldMilliseconds.ToString(
            System.Globalization.CultureInfo.InvariantCulture),
        "--audio-buffer-ms", audioBufferMilliseconds.ToString(
            System.Globalization.CultureInfo.InvariantCulture),
    ];

    private static string EvidenceRecord()
    {
        var holdStart = Checkpoint(includePreStop: false);
        var preCleanup = Checkpoint(includePreStop: false);
        var postStop = Checkpoint(includePreStop: true);
        var snapshot = new PlaybackControlledWindowsPhysicalRunSnapshot(
            PlaybackControlledWindowsPhysicalRunState.Completed,
            PlaybackControlledWindowsPhysicalRunResult.Completed,
            PlaybackControlledWindowsOutputValidationKind.Audio,
            FixtureSha256,
            FixtureByteCount: 66_939,
            FixturePacketCount: 2,
            DecodeOperationCount: 4,
            DeliveryAttemptCount: 4,
            DeliveredCount: 4,
            RetainedWouldBlockCount: 0,
            PlaybackWindowsPhysicalRunnerState.Stopped,
            PlaybackApprovedExternalRuntimeWindowsLifecycleState.Disposed,
            PlaybackControlledWindowsOutputEvidenceResult.Accepted,
            BorrowedWindowPreserved: true,
            BorrowedDispatcherPreserved: true)
        {
            HoldStartAudioRenderEvidence = holdStart,
            PreCleanupAudioRenderEvidence = preCleanup,
            PostStopAudioRenderEvidence = postStop,
        };
        return PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.Serialize(
            PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.Create(
                snapshot,
                FixtureSha256,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(100),
                physicalRunsExecutedByThisCommand: true));
    }

    private static string TerminalRecord()
    {
        var preCleanupAudio = default(PlaybackWindowsAudioRenderTraceSnapshot);
        var postStopAudio = preCleanupAudio with
        {
            PreStop = new(
                QueryCount: 1,
                Available: false,
                PaddingFrameCount: 0,
                Overflowed: false),
        };
        var snapshot = new PlaybackControlledWindowsPhysicalRunSnapshot(
            PlaybackControlledWindowsPhysicalRunState.Faulted,
            PlaybackControlledWindowsPhysicalRunResult.DecodeRejected,
            PlaybackControlledWindowsOutputValidationKind.Audio,
            FixtureSha256,
            FixtureByteCount: 66_939,
            FixturePacketCount: 2,
            DecodeOperationCount: 1,
            DeliveryAttemptCount: 0,
            DeliveredCount: 0,
            RetainedWouldBlockCount: 0,
            PlaybackWindowsPhysicalRunnerState.Stopped,
            PlaybackApprovedExternalRuntimeWindowsLifecycleState.Disposed,
            EvidenceResult: default,
            BorrowedWindowPreserved: false,
            BorrowedDispatcherPreserved: false)
        {
            PreCleanupAudioRenderEvidence = new(
                DeliveryTrace: default,
                preCleanupAudio,
                EmptyNativeEvidence(preCleanupAudio)),
            PostStopAudioRenderEvidence = new(
                DeliveryTrace: default,
                postStopAudio,
                EmptyNativeEvidence(postStopAudio)),
        };
        return PlaybackControlledWindowsAudioTerminalRenderRecordCodec.Serialize(
            PlaybackControlledWindowsAudioTerminalRenderRecordCodec.Create(
                snapshot,
                FixtureSha256,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(100),
                physicalRunStarted: true));
    }

    private static PlaybackControlledWindowsAudioRenderEvidenceSnapshot Checkpoint(
        bool includePreStop)
    {
        PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot delivery = includePreStop
            ? default
            : new(
                Count: 4,
                Overflowed: false,
                First: Delivery(0, 0),
                Second: Delivery(1, 21),
                Third: Delivery(2, 42),
                Fourth: Delivery(3, 64));
        var audio = new PlaybackWindowsAudioRenderTraceSnapshot(
            Count: 4,
            Overflowed: false,
            First: Write(0, startRequested: true),
            Second: Write(1024, startRequested: false),
            Third: Write(2048, startRequested: false),
            Fourth: Write(3072, startRequested: false),
            PreStop: includePreStop
                ? new(
                    QueryCount: 1,
                    Available: true,
                    PaddingFrameCount: 0,
                    Overflowed: false)
                : default);
        return new(delivery, audio, NativeEvidence(audio));
    }

    private static PlaybackGenerationMatchedSchedulerDeliveryObservation Delivery(
        long sequence,
        int presentationMilliseconds) =>
        new(
            sequence,
            PlaybackPolicyMediaKind.Audio,
            HasPresentationTimestampOffset: true,
            TimeSpan.FromMilliseconds(presentationMilliseconds).Ticks,
            HasTimingDecision: true,
            PlaybackAvTimingDecisionResult.ProcessNow,
            TimingWaitTicks: 0,
            PlaybackGenerationMatchedSchedulerDeliveryResult.AudioDelivered,
            PlaybackGenerationMatchedSchedulerReservationCompletion.Delivered);

    private static PlaybackWindowsAudioRenderWriteObservation Write(
        int paddingFrames,
        bool startRequested) =>
        new(
            RequestedFrameCount: 1024,
            RequestedByteCount: 4096,
            HasCapacityFrameCount: true,
            CapacityFrameCount: 4800,
            HasPaddingFrameCount: true,
            PaddingFrameCount: paddingFrames,
            HasAvailableFrameCount: true,
            AvailableFrameCount: 4800 - paddingFrames,
            StartRequested: startRequested,
            GetBufferAcknowledged: true,
            ReleaseBufferAcknowledged: true,
            StartAcknowledged: startRequested,
            PlaybackWindowsAudioRenderWriteResult.Processed);

    private static PlaybackWindowsOutputNativeEvidenceSnapshot NativeEvidence(
        PlaybackWindowsAudioRenderTraceSnapshot audioTrace) =>
        new(
            PlaybackControlledWindowsOutputEvidenceSource.SystemWindows,
            CounterOverflowed: false,
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
            AudioConfigurationConflict: false,
            PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved,
            AudioSampleRate: 48_000,
            AudioChannelCount: 2,
            AudioGeneration: 7,
            AudioWriteAttemptCount: 4,
            AudioWriteAcknowledgementCount: 4,
            AudioAcceptedFrameCount: 4_096,
            AudioAcceptedByteCount: 16_384,
            AudioWouldBlockCount: 0,
            AudioStartAcknowledgementCount: 1,
            AudioDeviceLostCount: 0)
        {
            AudioRenderTrace = audioTrace,
        };

    private static PlaybackWindowsOutputNativeEvidenceSnapshot EmptyNativeEvidence(
        PlaybackWindowsAudioRenderTraceSnapshot audioTrace) =>
        new(
            PlaybackControlledWindowsOutputEvidenceSource.SystemWindows,
            CounterOverflowed: false,
            VideoConfigurationConflict: false,
            VideoPixelFormat: default,
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
            AudioConfigurationConflict: false,
            AudioSampleFormat: default,
            AudioSampleRate: 0,
            AudioChannelCount: 0,
            AudioGeneration: -1,
            AudioWriteAttemptCount: 0,
            AudioWriteAcknowledgementCount: 0,
            AudioAcceptedFrameCount: 0,
            AudioAcceptedByteCount: 0,
            AudioWouldBlockCount: 0,
            AudioStartAcknowledgementCount: 0,
            AudioDeviceLostCount: 0)
        {
            AudioRenderTrace = audioTrace,
        };
}
