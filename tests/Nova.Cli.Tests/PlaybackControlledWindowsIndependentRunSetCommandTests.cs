using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsIndependentRunSetCommandTests
{
    private const string FixtureSha256 =
        "32872B300286525F4EF97FEE3533E25419AC8E48BFD4BC2C4FD9545F071B1E15";

    [Fact]
    public async Task Missing_record_paths_fail_before_file_access()
    {
        using var output = new StringWriter();

        var result = await PlaybackControlledWindowsIndependentRunSetCommand.RunAsync(
            [PlaybackControlledWindowsIndependentRunSetCommand.Argument],
            output);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(
            PlaybackControlledWindowsIndependentRunSetResult.InvalidRecord,
            result.Result);
        Assert.Contains("Result: InvalidConfiguration", output.ToString());
    }

    [Fact]
    public async Task Independent_video_and_audio_files_complete_verification()
    {
        var videoPath = Path.GetTempFileName();
        var audioPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(
                videoPath,
                PlaybackControlledWindowsPhysicalRunRecordCodec.Serialize(
                    ValidRecord(PlaybackControlledWindowsOutputValidationKind.Video)));
            await File.WriteAllTextAsync(
                audioPath,
                PlaybackControlledWindowsPhysicalRunRecordCodec.Serialize(
                    ValidRecord(PlaybackControlledWindowsOutputValidationKind.Audio)));
            using var output = new StringWriter();

            var result = await PlaybackControlledWindowsIndependentRunSetCommand.RunAsync(
                Arguments(videoPath, audioPath),
                output);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(
                PlaybackControlledWindowsIndependentRunSetResult.Completed,
                result.Result);
            var report = output.ToString();
            Assert.Contains("Video-Record-Accepted: True", report);
            Assert.Contains("Audio-Record-Accepted: True", report);
            Assert.Contains("Physical-Runs-Executed-By-This-Command: False", report);
            Assert.DoesNotContain(videoPath, report);
            Assert.DoesNotContain(audioPath, report);
        }
        finally
        {
            File.Delete(videoPath);
            File.Delete(audioPath);
        }
    }

    [Fact]
    public async Task Swapped_record_kinds_fail_closed()
    {
        var videoPath = Path.GetTempFileName();
        var audioPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(
                videoPath,
                PlaybackControlledWindowsPhysicalRunRecordCodec.Serialize(
                    ValidRecord(PlaybackControlledWindowsOutputValidationKind.Audio)));
            await File.WriteAllTextAsync(
                audioPath,
                PlaybackControlledWindowsPhysicalRunRecordCodec.Serialize(
                    ValidRecord(PlaybackControlledWindowsOutputValidationKind.Video)));
            using var output = new StringWriter();

            var result = await PlaybackControlledWindowsIndependentRunSetCommand.RunAsync(
                Arguments(videoPath, audioPath),
                output);

            Assert.Equal(2, result.ExitCode);
            Assert.Equal(
                PlaybackControlledWindowsIndependentRunSetResult.InvalidRecord,
                result.Result);
            Assert.Contains("Physical-Runs-Executed-By-This-Command: False", output.ToString());
        }
        finally
        {
            File.Delete(videoPath);
            File.Delete(audioPath);
        }
    }

    [Fact]
    public async Task Runtime_mismatch_is_reported_without_printing_paths()
    {
        var videoPath = Path.GetTempFileName();
        var audioPath = Path.GetTempFileName();
        try
        {
            var video = ValidRecord(PlaybackControlledWindowsOutputValidationKind.Video) with
            {
                RuntimeBuildId = "unexpected-runtime",
            };
            await File.WriteAllTextAsync(
                videoPath,
                PlaybackControlledWindowsPhysicalRunRecordCodec.Serialize(video));
            await File.WriteAllTextAsync(
                audioPath,
                PlaybackControlledWindowsPhysicalRunRecordCodec.Serialize(
                    ValidRecord(PlaybackControlledWindowsOutputValidationKind.Audio)));
            using var output = new StringWriter();

            var result = await PlaybackControlledWindowsIndependentRunSetCommand.RunAsync(
                Arguments(videoPath, audioPath),
                output);

            Assert.Equal(1, result.ExitCode);
            Assert.Equal(
                PlaybackControlledWindowsIndependentRunSetResult.RuntimeRejected,
                result.Result);
            Assert.DoesNotContain(videoPath, output.ToString());
            Assert.DoesNotContain(audioPath, output.ToString());
        }
        finally
        {
            File.Delete(videoPath);
            File.Delete(audioPath);
        }
    }

    private static string[] Arguments(string videoPath, string audioPath) =>
    [
        PlaybackControlledWindowsIndependentRunSetCommand.Argument,
        "--video-record", videoPath,
        "--audio-record", audioPath,
        "--fixture-sha256", FixtureSha256,
    ];

    private static PlaybackControlledWindowsPhysicalRunRecord ValidRecord(
        PlaybackControlledWindowsOutputValidationKind kind) => new(
            PlaybackControlledWindowsPhysicalRunRecordCodec.CurrentSchemaVersion,
            PlaybackControlledWindowsPhysicalRunRecordCodec.CurrentMilestone,
            kind,
            PlaybackFfmpegBackendCandidate.BuildId,
            FixtureSha256,
            PlaybackControlledWindowsPhysicalRunResult.Completed,
            PlaybackControlledWindowsOutputEvidenceResult.Accepted,
            FixtureByteCount: 66_939,
            FixturePacketCount: 2,
            DecodeOperationCount: 1,
            DeliveryAttemptCount: 1,
            DeliveredCount: 1,
            RetainedWouldBlockCount: 0,
            PlaybackWindowsPhysicalRunnerState.Stopped,
            PlaybackApprovedExternalRuntimeWindowsLifecycleState.Disposed,
            BorrowedWindowPreserved: true,
            BorrowedDispatcherPreserved: true,
            ExternalBinaryOrPayloadPublished: false);
}
