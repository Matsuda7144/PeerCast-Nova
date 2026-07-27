using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsAudioProcessFailureDiagnosticsTests
{
    private const string RuntimeBuild = "btbn.ffmpeg.20260721.win64.lgpl-shared";
    private const string FixtureSha256 =
        "32872B300286525F4EF97FEE3533E25419AC8E48BFD4BC2C4FD9545F071B1E15";

    [Fact]
    public void CompleteCanonicalAudioRecordIsAcceptedButIncompleteEnvelopeIsNot()
    {
        var record = PlaybackControlledWindowsPhysicalRunRecordCodec.Serialize(new(
            1,
            "0.0.10d Phase 5",
            PlaybackControlledWindowsOutputValidationKind.Audio,
            RuntimeBuild,
            FixtureSha256,
            PlaybackControlledWindowsPhysicalRunResult.Completed,
            PlaybackControlledWindowsOutputEvidenceResult.Accepted,
            66_939,
            2,
            2,
            1,
            1,
            0,
            PlaybackWindowsPhysicalRunnerState.Stopped,
            PlaybackApprovedExternalRuntimeWindowsLifecycleState.Disposed,
            true,
            true,
            false));

        Assert.True(
            PlaybackControlledWindowsAudioProcessFailureDiagnostics.IsCanonicalAudioRecord(
                record,
                RuntimeBuild,
                FixtureSha256));
        Assert.False(
            PlaybackControlledWindowsAudioProcessFailureDiagnostics.IsCanonicalAudioRecord(
                record.Replace(
                    "END-PEERCAST-NOVA-CONTROLLED-WINDOWS-RUN-RECORD\n",
                    string.Empty,
                    StringComparison.Ordinal),
                RuntimeBuild,
                FixtureSha256));
    }

    [Fact]
    public void AccessViolationExitIsRetainedAsSignedAndEightDigitHex()
    {
        var exitCode = unchecked((int)0xC0000005);
        var snapshot = PlaybackControlledWindowsAudioProcessFailureDiagnostics.Analyze(
            exitCode,
            string.Empty,
            string.Empty);

        var record =
            PlaybackControlledWindowsAudioProcessFailureDiagnostics.SerializeFailureRecord(
                snapshot);

        Assert.Equal(exitCode, snapshot.ChildExitCode);
        Assert.Equal(
            PlaybackControlledWindowsProcessStderrClassification.NativeProcessTermination,
            snapshot.StderrClassification);
        Assert.Contains("Child-Exit-Code-Signed: -1073741819\n", record);
        Assert.Contains("Child-Exit-Code-Hex: C0000005\n", record);
        Assert.Contains("Stdout-Bytes: 0\n", record);
    }

    [Fact]
    public void OnlyLastExactAllowlistedStageIsRetainedAndRawErrorDoesNotLeak()
    {
        const string privateError =
            "private-path endpoint-name pointer-value payload-value";
        var standardError = string.Join(
            '\n',
            "PCN-AUDIO-NATIVE-STAGE: GetBufferEntering",
            "PCN-AUDIO-NATIVE-STAGE: NotAStage",
            privateError,
            "PCN-AUDIO-NATIVE-STAGE: ReleaseBufferEntering",
            string.Empty);

        var snapshot = PlaybackControlledWindowsAudioProcessFailureDiagnostics.Analyze(
            1,
            string.Empty,
            standardError);
        var record =
            PlaybackControlledWindowsAudioProcessFailureDiagnostics.SerializeFailureRecord(
                snapshot);

        Assert.Equal("ReleaseBufferEntering", snapshot.LastNativeAudioStage);
        Assert.Equal(
            PlaybackControlledWindowsProcessStderrClassification.UnknownNonZeroExit,
            snapshot.StderrClassification);
        Assert.DoesNotContain(privateError, record, StringComparison.Ordinal);
        Assert.DoesNotContain("NotAStage", record, StringComparison.Ordinal);
    }

    [Fact]
    public void ManagedMarkerIsClassifiedWithoutCopyingOtherStandardError()
    {
        var standardError = string.Join(
            '\n',
            "PCN-AUDIO-NATIVE-STAGE: StartEntering",
            PlaybackControlledWindowsAudioProcessFailureDiagnostics
                .ManagedUnhandledExceptionMarker,
            "System.Exception: private details");

        var snapshot = PlaybackControlledWindowsAudioProcessFailureDiagnostics.Analyze(
            1,
            string.Empty,
            standardError);
        var record =
            PlaybackControlledWindowsAudioProcessFailureDiagnostics.SerializeFailureRecord(
                snapshot);

        Assert.Equal(
            PlaybackControlledWindowsProcessStderrClassification.ManagedUnhandledException,
            snapshot.StderrClassification);
        Assert.Equal("StartEntering", snapshot.LastNativeAudioStage);
        Assert.DoesNotContain("System.Exception", record, StringComparison.Ordinal);
        Assert.DoesNotContain("private details", record, StringComparison.Ordinal);
    }

    [Fact]
    public void ExactCapturedByteCountsAreRetainedIndependentlyOfDecodedText()
    {
        var snapshot = PlaybackControlledWindowsAudioProcessFailureDiagnostics.Analyze(
            1,
            "decoded stdout",
            17,
            "decoded stderr",
            29);

        Assert.Equal(17, snapshot.StdoutByteCount);
        Assert.Equal(29, snapshot.StderrByteCount);
    }

    [Theory]
    [InlineData("Result: DeliveryRejected\r\n", "DeliveryRejected")]
    [InlineData("Result: Faulted\n", "Faulted")]
    [InlineData("Result: deliveryRejected\r\n", "None")]
    [InlineData("Result: None\r\n", "None")]
    [InlineData("Result: Completed\r\n", "None")]
    [InlineData("Result: DeliveryRejected\r\nprivate output\r\n", "None")]
    [InlineData("private output", "None")]
    public void Only_exact_single_line_controlled_run_result_is_retained(
        string standardOutput,
        string expected)
    {
        Assert.Equal(
            expected,
            PlaybackControlledWindowsAudioProcessFailureDiagnostics
                .GetControlledRunResultOrNone(standardOutput));
    }

    [Theory]
    [InlineData(
        "PCN-AUDIO-TERMINAL-RECORD-REJECTION: TraceCounterConsistencyRejected\r\n",
        "TraceCounterConsistencyRejected")]
    [InlineData(
        "private stderr\r\nPCN-AUDIO-TERMINAL-RECORD-REJECTION: PreCleanupCheckpointRejected\r\n",
        "PreCleanupCheckpointRejected")]
    [InlineData(
        "PCN-AUDIO-TERMINAL-RECORD-REJECTION: PreCleanupAudioTraceRejected\r\n",
        "PreCleanupAudioTraceRejected")]
    [InlineData(
        "PCN-AUDIO-TERMINAL-RECORD-REJECTION: None\r\n",
        "None")]
    [InlineData(
        "PCN-AUDIO-TERMINAL-RECORD-REJECTION: private\r\n",
        "None")]
    [InlineData(
        "PCN-AUDIO-TERMINAL-RECORD-REJECTION: PreCleanupCheckpointRejected\r\n" +
        "PCN-AUDIO-TERMINAL-RECORD-REJECTION: PostStopCheckpointRejected\r\n",
        "None")]
    public void Only_one_exact_allowlisted_terminal_rejection_is_retained(
        string standardError,
        string expected)
    {
        Assert.Equal(
            expected,
            PlaybackControlledWindowsAudioProcessFailureDiagnostics
                .GetTerminalRecordRejectionOrNone(standardError));
    }
}
