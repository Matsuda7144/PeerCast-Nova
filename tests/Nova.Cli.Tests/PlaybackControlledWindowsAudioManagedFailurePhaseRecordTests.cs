namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsAudioManagedFailurePhaseRecordTests
{
    [Fact]
    public void Phase_parser_accepts_one_exact_allowlisted_marker()
    {
        const string stderr =
            "PCN-AUDIO-NATIVE-STAGE: ActivationCleanupCompleted\r\n" +
            "PCN-AUDIO-MANAGED-FAILURE-PHASE: SnapshotCapture\r\n";

        Assert.Equal(
            "SnapshotCapture",
            PlaybackControlledWindowsAudioManagedFailurePhaseDiagnostics
                .GetManagedFailurePhaseOrNone(stderr));
        Assert.True(
            PlaybackControlledWindowsAudioManagedFailurePhaseDiagnostics
                .IsManagedFailurePhaseOrNone("SnapshotCapture"));
    }

    [Theory]
    [InlineData("PCN-AUDIO-MANAGED-FAILURE-PHASE: PrivatePhase\n")]
    [InlineData("PCN-AUDIO-MANAGED-FAILURE-PHASE: 3\n")]
    [InlineData(
        "PCN-AUDIO-MANAGED-FAILURE-PHASE: SnapshotCapture\n" +
        "PCN-AUDIO-MANAGED-FAILURE-PHASE: SnapshotCapture\n")]
    [InlineData(
        "PCN-AUDIO-MANAGED-FAILURE-PHASE: SnapshotCapture private-detail\n")]
    public void Phase_parser_rejects_unknown_duplicate_or_extended_marker(string stderr)
    {
        Assert.Equal(
            "None",
            PlaybackControlledWindowsAudioManagedFailurePhaseDiagnostics
                .GetManagedFailurePhaseOrNone(stderr));
    }

    [Fact]
    public void V6_record_has_exact_shape_and_round_trips_phase()
    {
        var record = new PlaybackControlledWindowsAudioRenderChildFailureRecord(
            PlaybackControlledWindowsAudioRenderChildFailureRecordCodec
                .ManagedFailurePhaseSchemaVersion,
            PlaybackControlledWindowsAudioRenderChildFailureRecordCodec.CurrentMilestone,
            ChildExitCodeAvailable: true,
            ChildExitCode: 1,
            StdoutByteCount: 17,
            StderrByteCount: 211,
            LastNativeAudioStage: "ActivationCleanupCompleted",
            ChildProcessResultCaptured: true)
        {
            ControlledRunResult = "Faulted",
            TerminalRecordRejection = "ProjectionRejected",
            ManagedFailurePhase = "SnapshotCapture",
        };

        var serialized =
            PlaybackControlledWindowsAudioRenderChildFailureRecordCodec.Serialize(record);
        var lines = serialized.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(22, lines.Length);
        Assert.Equal(
            "PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-EVIDENCE-FAILURE 6",
            lines[0]);
        Assert.Equal("Managed-Failure-Phase: SnapshotCapture", lines[12]);
        Assert.True(
            PlaybackControlledWindowsAudioRenderChildFailureRecordCodec.TryParse(
                serialized,
                out var parsed));
        Assert.Equal(record, parsed);
        Assert.DoesNotContain("Exception", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("path", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void V5_record_remains_canonical_and_effective_phase_is_none()
    {
        var record = new PlaybackControlledWindowsAudioRenderChildFailureRecord(
            PlaybackControlledWindowsAudioRenderChildFailureRecordCodec.CurrentSchemaVersion,
            PlaybackControlledWindowsAudioRenderChildFailureRecordCodec.CurrentMilestone,
            ChildExitCodeAvailable: true,
            ChildExitCode: 1,
            StdoutByteCount: 0,
            StderrByteCount: 3122,
            LastNativeAudioStage: "ActivationCleanupCompleted",
            ChildProcessResultCaptured: true)
        {
            ControlledRunResult = "None",
            TerminalRecordRejection = "None",
        };

        var serialized =
            PlaybackControlledWindowsAudioRenderChildFailureRecordCodec.Serialize(record);

        Assert.DoesNotContain("Managed-Failure-Phase:", serialized, StringComparison.Ordinal);
        Assert.True(
            PlaybackControlledWindowsAudioRenderChildFailureRecordCodec.TryParse(
                serialized,
                out var parsed));
        Assert.Equal("None", parsed!.ManagedFailurePhase);
    }

    [Fact]
    public void Child_capture_serializer_uses_v6_when_phase_is_available()
    {
        var serialized =
            PlaybackControlledWindowsAudioRenderEvidenceChildCaptureComposition
                .SerializeFailureRecord(
                    childExitCode: 1,
                    stdoutByteCount: 17,
                    stderrByteCount: 211,
                    lastNativeAudioStage: "ActivationCleanupCompleted",
                    controlledRunResult: "Faulted",
                    terminalRecordRejection: "ProjectionRejected",
                    managedFailurePhase: "SnapshotCapture",
                    childProcessResultCaptured: true);

        Assert.True(
            PlaybackControlledWindowsAudioRenderChildFailureRecordCodec.TryParse(
                serialized,
                out var parsed));
        Assert.Equal(
            PlaybackControlledWindowsAudioRenderChildFailureRecordCodec
                .ManagedFailurePhaseSchemaVersion,
            parsed!.SchemaVersion);
        Assert.Equal("SnapshotCapture", parsed.ManagedFailurePhase);
    }

    [Fact]
    public async Task Preflight_surfaces_allowlisted_phase_without_private_output()
    {
        var path = Path.GetTempFileName();
        try
        {
            var serialized =
                PlaybackControlledWindowsAudioRenderEvidenceChildCaptureComposition
                    .SerializeFailureRecord(
                        childExitCode: 1,
                        stdoutByteCount: 17,
                        stderrByteCount: 211,
                        lastNativeAudioStage: "ActivationCleanupCompleted",
                        controlledRunResult: "Faulted",
                        terminalRecordRejection: "ProjectionRejected",
                        managedFailurePhase: "SnapshotCapture",
                        childProcessResultCaptured: true);
            await File.WriteAllTextAsync(path, serialized);
            using var output = new StringWriter();

            var result =
                await PlaybackControlledWindowsAudioRenderFailureRecordPreflightCommand.RunAsync(
                    [
                        PlaybackControlledWindowsAudioRenderFailureRecordPreflightCommand.Argument,
                        "--record",
                        path,
                        "--expected-source",
                        "child",
                    ],
                    output);

            Assert.Equal(0, result.ExitCode);
            Assert.True(result.Validated);
            Assert.Contains(
                "Managed-Failure-Phase: SnapshotCapture" + Environment.NewLine,
                output.ToString(),
                StringComparison.Ordinal);
            Assert.DoesNotContain(path, output.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("private", output.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
