using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsPhysicalRunRecordPreflightCommandTests
{
    private const string FixtureSha256 =
        "32872B300286525F4EF97FEE3533E25419AC8E48BFD4BC2C4FD9545F071B1E15";

    [Theory]
    [InlineData(PlaybackControlledWindowsOutputValidationKind.Video, "video",
        PlaybackControlledWindowsIndependentRunSetResult.VideoAccepted)]
    [InlineData(PlaybackControlledWindowsOutputValidationKind.Audio, "audio",
        PlaybackControlledWindowsIndependentRunSetResult.AudioAccepted)]
    public async Task Valid_single_record_is_accepted_without_physical_output(
        PlaybackControlledWindowsOutputValidationKind kind,
        string expectedKind,
        PlaybackControlledWindowsIndependentRunSetResult expectedResult)
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(
                path,
                PlaybackControlledWindowsPhysicalRunRecordCodec.Serialize(ValidRecord(kind)));
            using var output = new StringWriter();

            var result = await PlaybackControlledWindowsPhysicalRunRecordPreflightCommand.RunAsync(
                Arguments(path, expectedKind),
                output);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(expectedResult, result.Result);
            Assert.True(result.Accepted);
            var report = output.ToString();
            Assert.Contains($"Result: {expectedResult}", report);
            Assert.Contains($"Expected-Kind: {kind}", report);
            Assert.Contains("Record-Accepted: True", report);
            Assert.Contains("Physical-Runs-Executed-By-This-Command: False", report);
            Assert.DoesNotContain(path, report);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Wrong_expected_kind_fails_closed_without_printing_path()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(
                path,
                PlaybackControlledWindowsPhysicalRunRecordCodec.Serialize(
                    ValidRecord(PlaybackControlledWindowsOutputValidationKind.Video)));
            using var output = new StringWriter();

            var result = await PlaybackControlledWindowsPhysicalRunRecordPreflightCommand.RunAsync(
                Arguments(path, "audio"),
                output);

            Assert.Equal(2, result.ExitCode);
            Assert.Equal(
                PlaybackControlledWindowsIndependentRunSetResult.InvalidRecord,
                result.Result);
            Assert.False(result.Accepted);
            Assert.Contains("Record-Accepted: False", output.ToString());
            Assert.DoesNotContain(path, output.ToString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Rejected_evidence_returns_semantic_failure()
    {
        var path = Path.GetTempFileName();
        try
        {
            var record = ValidRecord(PlaybackControlledWindowsOutputValidationKind.Video) with
            {
                Result = PlaybackControlledWindowsPhysicalRunResult.DeliveryRejected,
                EvidenceResult = PlaybackControlledWindowsOutputEvidenceResult.None,
                DeliveredCount = 0,
            };
            await File.WriteAllTextAsync(
                path,
                PlaybackControlledWindowsPhysicalRunRecordCodec.Serialize(record));
            using var output = new StringWriter();

            var result = await PlaybackControlledWindowsPhysicalRunRecordPreflightCommand.RunAsync(
                Arguments(path, "video"),
                output);

            Assert.Equal(1, result.ExitCode);
            Assert.Equal(
                PlaybackControlledWindowsIndependentRunSetResult.EvidenceRejected,
                result.Result);
            Assert.False(result.Accepted);
            Assert.Contains("Record-Accepted: False", output.ToString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Missing_arguments_fail_before_file_access()
    {
        using var output = new StringWriter();

        var result = await PlaybackControlledWindowsPhysicalRunRecordPreflightCommand.RunAsync(
            [PlaybackControlledWindowsPhysicalRunRecordPreflightCommand.Argument],
            output);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(
            PlaybackControlledWindowsIndependentRunSetResult.InvalidRecord,
            result.Result);
        Assert.False(result.Accepted);
        Assert.Contains("Result: InvalidConfiguration", output.ToString());
    }

    private static string[] Arguments(string path, string kind) =>
    [
        PlaybackControlledWindowsPhysicalRunRecordPreflightCommand.Argument,
        "--record", path,
        "--expected-kind", kind,
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
