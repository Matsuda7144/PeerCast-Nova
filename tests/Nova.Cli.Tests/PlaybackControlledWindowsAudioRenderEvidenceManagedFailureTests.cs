namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsAudioRenderEvidenceManagedFailureTests
{
    [Fact]
    public async Task Snapshot_failure_emits_exact_phase_and_projection_rejection()
    {
        using var diagnostic = new StringWriter();

        await PlaybackControlledWindowsAudioRenderEvidenceCommand
            .WriteManagedFailureDiagnosticAsync(
                diagnostic,
                PlaybackControlledWindowsAudioRenderEvidenceManagedFailurePhase
                    .SnapshotCapture);

        Assert.Equal(
            "PCN-AUDIO-MANAGED-FAILURE-PHASE: SnapshotCapture" + Environment.NewLine +
            "PCN-AUDIO-TERMINAL-RECORD-REJECTION: ProjectionRejected" + Environment.NewLine,
            diagnostic.ToString());
        Assert.DoesNotContain("Exception", diagnostic.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("path", diagnostic.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Composition_failure_emits_only_exact_phase()
    {
        using var diagnostic = new StringWriter();

        await PlaybackControlledWindowsAudioRenderEvidenceCommand
            .WriteManagedFailureDiagnosticAsync(
                diagnostic,
                PlaybackControlledWindowsAudioRenderEvidenceManagedFailurePhase
                    .CompositionRun);

        Assert.Equal(
            "PCN-AUDIO-MANAGED-FAILURE-PHASE: CompositionRun" + Environment.NewLine,
            diagnostic.ToString());
        Assert.DoesNotContain(
            "PCN-AUDIO-TERMINAL-RECORD-REJECTION:",
            diagnostic.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Faulted_result_is_one_exact_canonical_line()
    {
        using var output = new StringWriter();

        await PlaybackControlledWindowsAudioRenderEvidenceCommand
            .TryWriteFaultedResultAsync(output);

        Assert.Equal("Result: Faulted" + Environment.NewLine, output.ToString());
        Assert.Equal(
            "Faulted",
            PlaybackControlledWindowsAudioProcessFailureDiagnostics
                .GetControlledRunResultOrNone(output.ToString()));
    }

    [Fact]
    public async Task Diagnostic_writer_failure_does_not_escape()
    {
        await PlaybackControlledWindowsAudioRenderEvidenceCommand
            .WriteManagedFailureDiagnosticAsync(
                new ThrowingTextWriter(),
                PlaybackControlledWindowsAudioRenderEvidenceManagedFailurePhase
                    .TerminalProjection);
        await PlaybackControlledWindowsAudioRenderEvidenceCommand
            .TryWriteFaultedResultAsync(new ThrowingTextWriter());
    }

    private sealed class ThrowingTextWriter : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

        public override Task WriteLineAsync(string? value) =>
            Task.FromException(new IOException("private details"));

        public override Task FlushAsync() =>
            Task.FromException(new IOException("private details"));
    }
}
