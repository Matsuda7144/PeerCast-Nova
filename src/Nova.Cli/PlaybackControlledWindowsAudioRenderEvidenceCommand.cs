using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli;

public readonly record struct PlaybackControlledWindowsAudioRenderEvidenceCommandResult(
    int ExitCode,
    PlaybackControlledWindowsPhysicalRunResult Result);

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

        var operations = CreateOperations(options);
        await using var composition = new PlaybackControlledWindowsPhysicalRunComposition(
            options,
            operations);
        var result = await composition.RunAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = composition.Snapshot;
        if (result != PlaybackControlledWindowsPhysicalRunResult.Completed)
        {
            if (TryCreateTerminalRecord(
                    snapshot,
                    options,
                    out var terminalRecord,
                    out var rejection))
            {
                await PlaybackControlledWindowsAudioTerminalRenderRecordCodec.WriteAsync(
                        output,
                        terminalRecord,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            else
            {
                if (diagnosticOutput is not null)
                {
                    await diagnosticOutput.WriteLineAsync(
                            "PCN-AUDIO-TERMINAL-RECORD-REJECTION: " + rejection)
                        .ConfigureAwait(false);
                    await diagnosticOutput.FlushAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                }
                await output.WriteLineAsync($"Result: {result}").ConfigureAwait(false);
            }

            return new(RunFailureExitCode, result);
        }

        var record = CreateRecord(snapshot, options);
        await PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.WriteAsync(
                output,
                record,
                cancellationToken)
            .ConfigureAwait(false);
        return new(CompletedExitCode, result);
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
