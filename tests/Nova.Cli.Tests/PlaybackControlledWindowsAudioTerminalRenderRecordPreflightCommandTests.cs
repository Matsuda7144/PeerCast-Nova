using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsAudioTerminalRenderRecordPreflightCommandTests
{
    private const string FixtureSha256 =
        "32872B300286525F4EF97FEE3533E25419AC8E48BFD4BC2C4FD9545F071B1E15";

    [Fact]
    public async Task Valid_terminal_record_is_revalidated_without_physical_output()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, TerminalRecord());
            using var output = new StringWriter();

            var result =
                await PlaybackControlledWindowsAudioTerminalRenderRecordPreflightCommand
                    .RunAsync(Arguments(path), output);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(
                PlaybackControlledWindowsAudioTerminalRenderRecordPreflightResult
                    .TerminalRecordValidated,
                result.Result);
            Assert.Equal(
                PlaybackControlledWindowsPhysicalRunResult.DecodeRejected,
                result.TerminalResult);
            Assert.True(result.Validated);
            var report = output.ToString();
            Assert.Contains("Result: TerminalRecordValidated", report);
            Assert.Contains("Terminal-Result: DecodeRejected", report);
            Assert.Contains("Terminal-Record-Validated: True", report);
            Assert.Contains("Physical-Run-Started-Established: True", report);
            Assert.Contains("Physical-Run-Completion-Established: False", report);
            Assert.Contains("Completed-Render-Evidence-Established: False", report);
            Assert.Contains("Audio-Acceptance-Claimed: False", report);
            Assert.Contains("Physical-Runs-Executed-By-This-Command: False", report);
            Assert.DoesNotContain(path, report, StringComparison.Ordinal);
            Assert.DoesNotContain("Record: {", report, StringComparison.Ordinal);
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
            await File.WriteAllTextAsync(path, TerminalRecord());
            using var output = new StringWriter();

            var result =
                await PlaybackControlledWindowsAudioTerminalRenderRecordPreflightCommand
                    .RunAsync(
                        Arguments(
                            path,
                            expectedFixtureSha256,
                            observationHoldMilliseconds,
                            audioBufferMilliseconds),
                        output);

            Assert.Equal(1, result.ExitCode);
            Assert.Equal(
                PlaybackControlledWindowsAudioTerminalRenderRecordPreflightResult
                    .IdentityMismatch,
                result.Result);
            Assert.Equal(PlaybackControlledWindowsPhysicalRunResult.None, result.TerminalResult);
            Assert.False(result.Validated);
            Assert.Contains("Terminal-Record-Validated: False", output.ToString());
            Assert.Contains("Physical-Run-Started-Established: False", output.ToString());
            Assert.DoesNotContain(path, output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("not a record")]
    [InlineData("PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-EVIDENCE 1\n")]
    public async Task Non_terminal_record_is_rejected_without_echoing_content(string content)
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, content);
            using var output = new StringWriter();

            var result =
                await PlaybackControlledWindowsAudioTerminalRenderRecordPreflightCommand
                    .RunAsync(Arguments(path), output);

            Assert.Equal(1, result.ExitCode);
            Assert.Equal(
                PlaybackControlledWindowsAudioTerminalRenderRecordPreflightResult.InvalidRecord,
                result.Result);
            Assert.False(result.Validated);
            Assert.DoesNotContain(content.Trim(), output.ToString(), StringComparison.Ordinal);
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
            await PlaybackControlledWindowsAudioTerminalRenderRecordPreflightCommand.RunAsync(
                arguments,
                output);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(
            PlaybackControlledWindowsAudioTerminalRenderRecordPreflightResult
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
            await PlaybackControlledWindowsAudioTerminalRenderRecordPreflightCommand.RunAsync(
                [PlaybackControlledWindowsAudioTerminalRenderRecordPreflightCommand.Argument],
                output);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(
            PlaybackControlledWindowsAudioTerminalRenderRecordPreflightResult
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
        PlaybackControlledWindowsAudioTerminalRenderRecordPreflightCommand.Argument,
        "--record", path,
        "--fixture-sha256", fixtureSha256,
        "--observation-hold-ms", observationHoldMilliseconds.ToString(
            System.Globalization.CultureInfo.InvariantCulture),
        "--audio-buffer-ms", audioBufferMilliseconds.ToString(
            System.Globalization.CultureInfo.InvariantCulture),
    ];

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
