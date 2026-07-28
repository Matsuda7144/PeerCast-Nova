using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli;

public readonly record struct PlaybackControlledWindowsAudioRenderEvidenceCommandResult(
    int ExitCode,
    PlaybackControlledWindowsPhysicalRunResult Result);

internal enum PlaybackControlledWindowsAudioRenderEvidenceManagedFailurePhase
{
    None,
    OperationCreation,
    CompositionRun,
    SnapshotCapture,
    TerminalProjection,
    CompletedProjection,
    OutcomeWrite,
    CompositionDispose,
}

/// <summary>
/// Runs one controlled audio composition with the bounded generation and WASAPI
/// render traces enabled. It uses the system operations directly and does not add
/// an outer delivery decorator between delivery calls.
/// </summary>
public static class PlaybackControlledWindowsAudioRenderEvidenceCommand
{
    public const string Argument = "--controlled-windows-audio-render-evidence";
    public const int CompletedExitCode = 0;
    public const int RunFailureExitCode = 1;
    public const int InvalidConfigurationExitCode = 2;

    private const string ManagedFailurePhasePrefix =
        "PCN-AUDIO-MANAGED-FAILURE-PHASE: ";
    private const string TerminalRecordRejectionPrefix =
        "PCN-AUDIO-TERMINAL-RECORD-REJECTION: ";

    public static ValueTask<PlaybackControlledWindowsAudioRenderEvidenceCommandResult>
        RunAsync(
            string[] arguments,
            TextWriter output,
            CancellationToken cancellationToken = default) =>
        RunAsync(arguments, output, diagnosticOutput: null, cancellationToken);

    public static async ValueTask<PlaybackControlledWindowsAudioRenderEvidenceCommandResult>
        RunAsync(
            string[] arguments,
            TextWriter output,
            TextWriter? diagnosticOutput,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        if (!TryParse(arguments, out var options))
        {
            await output.WriteLineAsync("Result: InvalidConfiguration").ConfigureAwait(false);
            return new(
                InvalidConfigurationExitCode,
                PlaybackControlledWindowsPhysicalRunResult.InvalidConfiguration);
        }

        var phase = PlaybackControlledWindowsAudioRenderEvidenceManagedFailurePhase
            .OperationCreation;
        var outcomeWritten = false;
        try
        {
            var operations = CreateOperations(options);
            await using var composition = new PlaybackControlledWindowsPhysicalRunComposition(
                options,
                operations);

            phase = PlaybackControlledWindowsAudioRenderEvidenceManagedFailurePhase.CompositionRun;
            var result = await composition.RunAsync(cancellationToken).ConfigureAwait(false);

            phase = PlaybackControlledWindowsAudioRenderEvidenceManagedFailurePhase.SnapshotCapture;
            var snapshot = composition.Snapshot;
            if (result != PlaybackControlledWindowsPhysicalRunResult.Completed)
            {
                phase = PlaybackControlledWindowsAudioRenderEvidenceManagedFailurePhase
                    .TerminalProjection;
                if (TryCreateTerminalRecord(
                        snapshot,
                        options,
                        out var terminalRecord,
                        out var rejection))
                {
                    phase = PlaybackControlledWindowsAudioRenderEvidenceManagedFailurePhase
                        .OutcomeWrite;
                    await PlaybackControlledWindowsAudioTerminalRenderRecordCodec.WriteAsync(
                            output,
                            terminalRecord,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    outcomeWritten = true;
                }
                else
                {
                    if (diagnosticOutput is not null)
                    {
                        await diagnosticOutput.WriteLineAsync(
                                TerminalRecordRejectionPrefix + rejection)
                            .ConfigureAwait(false);
                        await diagnosticOutput.FlushAsync(CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    phase = PlaybackControlledWindowsAudioRenderEvidenceManagedFailurePhase
                        .OutcomeWrite;
                    await output.WriteLineAsync($"Result: {result}").ConfigureAwait(false);
                    outcomeWritten = true;
                }

                phase = PlaybackControlledWindowsAudioRenderEvidenceManagedFailurePhase
                    .CompositionDispose;
                return new(RunFailureExitCode, result);
            }

            phase = PlaybackControlledWindowsAudioRenderEvidenceManagedFailurePhase
                .CompletedProjection;
            var record = CreateRecord(snapshot, options);
            phase = PlaybackControlledWindowsAudioRenderEvidenceManagedFailurePhase.OutcomeWrite;
            await PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.WriteAsync(
                    output,
                    record,
                    cancellationToken)
                .ConfigureAwait(false);
            outcomeWritten = true;
            phase = PlaybackControlledWindowsAudioRenderEvidenceManagedFailurePhase
                .CompositionDispose;
            return new(CompletedExitCode, result);
        }
        catch
        {
            await WriteManagedFailureDiagnosticAsync(diagnosticOutput, phase).ConfigureAwait(false);
            if (!outcomeWritten)
            {
                await TryWriteFaultedResultAsync(output).ConfigureAwait(false);
            }

            return new(
                RunFailureExitCode,
                PlaybackControlledWindowsPhysicalRunResult.Faulted);
        }
    }

    internal static async ValueTask WriteManagedFailureDiagnosticAsync(
        TextWriter? diagnosticOutput,
        PlaybackControlledWindowsAudioRenderEvidenceManagedFailurePhase phase)
    {
        if (diagnosticOutput is null)
        {
            return;
        }

        try
        {
            await diagnosticOutput.WriteLineAsync(ManagedFailurePhasePrefix + phase)
                .ConfigureAwait(false);
            if (phase is PlaybackControlledWindowsAudioRenderEvidenceManagedFailurePhase
                    .SnapshotCapture or
                PlaybackControlledWindowsAudioRenderEvidenceManagedFailurePhase
                    .TerminalProjection)
            {
                await diagnosticOutput.WriteLineAsync(
                        TerminalRecordRejectionPrefix +
                        PlaybackControlledWindowsAudioTerminalRecordRejection.ProjectionRejected)
                    .ConfigureAwait(false);
            }
            await diagnosticOutput.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Diagnostics are bounded and must not replace the selected failure result.
        }
    }

    internal static async ValueTask TryWriteFaultedResultAsync(TextWriter output)
    {
        try
        {
            await output.WriteLineAsync(
                    $"Result: {PlaybackControlledWindowsPhysicalRunResult.Faulted}")
                .ConfigureAwait(false);
        }
        catch
        {
            // The child-capture boundary still retains byte counts and fixed stderr markers.
        }
    }

    internal static bool TryParse(
        string[] arguments,
        out PlaybackControlledWindowsPhysicalRunOptions options)
    {
        options = new();
        if (Count(arguments, Argument) != 1 ||
            Count(arguments, PlaybackControlledWindowsPhysicalRunCommand.Argument) != 0 ||
            Count(
                arguments,
                PlaybackControlledWindowsPhysicalRunCommand
                    .DiagnoseAudioNativeStagesArgument) != 0 ||
            Count(arguments, "--runtime-directory") != 1 ||
            Count(arguments, "--controlled-fixture") != 1 ||
            Count(arguments, "--fixture-sha256") != 1 ||
            Count(arguments, "--output-kind") != 1 ||
            Count(arguments, "--max-decode-operations") > 1 ||
            Count(arguments, "--max-delivery-operations") > 1 ||
            Count(arguments, "--fixture-packet-bytes") > 1 ||
            Count(arguments, "--observation-hold-ms") > 1 ||
            !PlaybackControlledWindowsPhysicalRunCommand.TryParse(arguments, out options) ||
            options.Kind != PlaybackControlledWindowsOutputValidationKind.Audio ||
            options.DiagnoseAudioNativeStages)
        {
            options = new();
            return false;
        }

        return true;
    }

    internal static SystemPlaybackControlledWindowsPhysicalRunOperations CreateOperations(
        PlaybackControlledWindowsPhysicalRunOptions options) =>
        new(
            options,
            captureAudioRenderTrace: true,
            capturePreStopPadding: true,
            captureAudioNativeStages: true);

    internal static PlaybackControlledWindowsAudioRenderEvidenceRecord CreateRecord(
        PlaybackControlledWindowsPhysicalRunSnapshot snapshot,
        PlaybackControlledWindowsPhysicalRunOptions options) =>
        PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.Create(
            snapshot,
            options.FixtureSha256,
            options.ObservationHoldDuration,
            options.AudioBufferDuration,
            physicalRunsExecutedByThisCommand: true);

    internal static bool TryCreateTerminalRecord(
        PlaybackControlledWindowsPhysicalRunSnapshot snapshot,
        PlaybackControlledWindowsPhysicalRunOptions options,
        out PlaybackControlledWindowsAudioTerminalRenderRecord terminalRecord) =>
        TryCreateTerminalRecord(snapshot, options, out terminalRecord, out _);

    internal static bool TryCreateTerminalRecord(
        PlaybackControlledWindowsPhysicalRunSnapshot snapshot,
        PlaybackControlledWindowsPhysicalRunOptions options,
        out PlaybackControlledWindowsAudioTerminalRenderRecord terminalRecord,
        out PlaybackControlledWindowsAudioTerminalRecordRejection rejection)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(options);
        try
        {
            var candidate = PlaybackControlledWindowsAudioTerminalRenderRecordCodec.Create(
                snapshot,
                options.FixtureSha256,
                options.ObservationHoldDuration,
                options.AudioBufferDuration,
                physicalRunStarted: true);
            rejection =
                PlaybackControlledWindowsAudioTerminalRenderRecordCodec
                    .GetValidationFailure(candidate);
            if (rejection != PlaybackControlledWindowsAudioTerminalRecordRejection.None)
            {
                terminalRecord = null!;
                return false;
            }
            _ = PlaybackControlledWindowsAudioTerminalRenderRecordCodec.Serialize(candidate);
            terminalRecord = candidate;
            rejection = PlaybackControlledWindowsAudioTerminalRecordRejection.None;
            return true;
        }
        catch (ArgumentException)
        {
            rejection =
                PlaybackControlledWindowsAudioTerminalRecordRejection.ProjectionRejected;
        }
        catch (InvalidOperationException)
        {
            rejection =
                PlaybackControlledWindowsAudioTerminalRecordRejection
                    .CanonicalSizeLimitRejected;
        }

        terminalRecord = null!;
        return false;
    }

    private static int Count(string[] arguments, string value) =>
        arguments.Count(
            argument => string.Equals(argument, value, StringComparison.OrdinalIgnoreCase));
}
