using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsAudioRenderEvidenceCommandTests
{
    private const string FixtureSha256 =
        "32872B300286525F4EF97FEE3533E25419AC8E48BFD4BC2C4FD9545F071B1E15";

    private static readonly string[] ValidArguments =
    [
        PlaybackControlledWindowsAudioRenderEvidenceCommand.Argument,
        "--runtime-directory",
        "C:\\controlled-runtime",
        "--controlled-fixture",
        "C:\\controlled-fixture.flv",
        "--fixture-sha256",
        FixtureSha256,
        "--output-kind",
        "audio",
        "--max-decode-operations",
        "128",
        "--max-delivery-operations",
        "128",
        "--fixture-packet-bytes",
        "65536",
        "--observation-hold-ms",
        "1000",
    ];

    private static readonly string[] VideoArguments =
    [
        PlaybackControlledWindowsAudioRenderEvidenceCommand.Argument,
        "--runtime-directory",
        "C:\\controlled-runtime",
        "--controlled-fixture",
        "C:\\controlled-fixture.flv",
        "--fixture-sha256",
        FixtureSha256,
        "--output-kind",
        "video",
    ];

    private static readonly string[] DiagnosticArguments =
    [
        PlaybackControlledWindowsAudioRenderEvidenceCommand.Argument,
        "--runtime-directory",
        "C:\\controlled-runtime",
        "--controlled-fixture",
        "C:\\controlled-fixture.flv",
        "--fixture-sha256",
        FixtureSha256,
        "--output-kind",
        "audio",
        PlaybackControlledWindowsPhysicalRunCommand.DiagnoseAudioNativeStagesArgument,
    ];

    private static readonly string[] OldCommandArguments =
    [
        PlaybackControlledWindowsAudioRenderEvidenceCommand.Argument,
        PlaybackControlledWindowsPhysicalRunCommand.Argument,
        "--runtime-directory",
        "C:\\controlled-runtime",
        "--controlled-fixture",
        "C:\\controlled-fixture.flv",
        "--fixture-sha256",
        FixtureSha256,
        "--output-kind",
        "audio",
    ];

    [Fact]
    public void Parser_accepts_exact_audio_render_evidence_configuration()
    {
        Assert.True(
            PlaybackControlledWindowsAudioRenderEvidenceCommand.TryParse(
                ValidArguments,
                out var options));
        Assert.Equal(PlaybackControlledWindowsOutputValidationKind.Audio, options.Kind);
        Assert.Equal(TimeSpan.FromSeconds(1), options.ObservationHoldDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(100), options.AudioBufferDuration);
        Assert.False(options.DiagnoseAudioNativeStages);
    }

    [Fact]
    public void Parser_rejects_video_old_command_and_text_native_diagnostics()
    {
        Assert.False(
            PlaybackControlledWindowsAudioRenderEvidenceCommand.TryParse(
                VideoArguments,
                out _));
        Assert.False(
            PlaybackControlledWindowsAudioRenderEvidenceCommand.TryParse(
                DiagnosticArguments,
                out _));
        Assert.False(
            PlaybackControlledWindowsAudioRenderEvidenceCommand.TryParse(
                OldCommandArguments,
                out _));
    }

    [Fact]
    public void Parser_rejects_duplicate_command_or_required_option()
    {
        string[] duplicateCommand =
        [
            .. ValidArguments,
            PlaybackControlledWindowsAudioRenderEvidenceCommand.Argument,
        ];
        string[] duplicateKind = [.. ValidArguments, "--output-kind", "audio"];

        Assert.False(
            PlaybackControlledWindowsAudioRenderEvidenceCommand.TryParse(
                duplicateCommand,
                out _));
        Assert.False(
            PlaybackControlledWindowsAudioRenderEvidenceCommand.TryParse(
                duplicateKind,
                out _));
    }

    [Fact]
    public void Record_projection_marks_physical_execution_without_audio_acceptance()
    {
        Assert.True(
            PlaybackControlledWindowsAudioRenderEvidenceCommand.TryParse(
                ValidArguments,
                out var options));
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
            BorrowedDispatcherPreserved: true);

        var record = PlaybackControlledWindowsAudioRenderEvidenceCommand.CreateRecord(
            snapshot,
            options);

        Assert.True(record.PhysicalRunsExecutedByThisCommand);
        Assert.False(record.AudioAcceptanceClaimed);
        Assert.False(record.ExternalBinaryOrPayloadPublished);
        Assert.Equal(1000, record.ObservationHoldMilliseconds);
        Assert.Equal(100, record.AudioBufferMilliseconds);
    }
}
