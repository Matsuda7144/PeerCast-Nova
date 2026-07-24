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

    public static async ValueTask<PlaybackControlledWindowsAudioRenderEvidenceCommandResult>
        RunAsync(
            string[] arguments,
            TextWriter output,
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
        if (result != PlaybackControlledWindowsPhysicalRunResult.Completed)
        {
            await output.WriteLineAsync($"Result: {result}").ConfigureAwait(false);
            return new(RunFailureExitCode, result);
        }

        var record = CreateRecord(composition.Snapshot, options);
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
            capturePreStopPadding: true);

    internal static PlaybackControlledWindowsAudioRenderEvidenceRecord CreateRecord(
        PlaybackControlledWindowsPhysicalRunSnapshot snapshot,
        PlaybackControlledWindowsPhysicalRunOptions options) =>
        PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.Create(
            snapshot,
            options.FixtureSha256,
            options.ObservationHoldDuration,
            options.AudioBufferDuration,
            physicalRunsExecutedByThisCommand: true);

    private static int Count(string[] arguments, string value) =>
        arguments.Count(
            argument => string.Equals(argument, value, StringComparison.OrdinalIgnoreCase));
}
