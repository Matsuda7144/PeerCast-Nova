using System.Globalization;
using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli;

public enum PlaybackControlledWindowsAudioTerminalRenderRecordPreflightResult
{
    None,
    TerminalRecordValidated,
    InvalidConfiguration,
    InvalidRecord,
    IdentityMismatch,
}

public readonly record struct
    PlaybackControlledWindowsAudioTerminalRenderRecordPreflightCommandResult(
        int ExitCode,
        PlaybackControlledWindowsAudioTerminalRenderRecordPreflightResult Result,
        PlaybackControlledWindowsPhysicalRunResult TerminalResult,
        bool Validated);

/// <summary>
/// Revalidates one retained controlled audio terminal render record without
/// starting a runner, runtime, fixture replay, endpoint, or physical output.
/// </summary>
public static class PlaybackControlledWindowsAudioTerminalRenderRecordPreflightCommand
{
    public const string Argument =
        "--verify-controlled-windows-audio-terminal-render-record";

    private const int InvalidConfigurationExitCode = 2;
    private const int ValidationFailureExitCode = 1;
    private const int ValidatedExitCode = 0;

    public static async ValueTask<
        PlaybackControlledWindowsAudioTerminalRenderRecordPreflightCommandResult> RunAsync(
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
                PlaybackControlledWindowsAudioTerminalRenderRecordPreflightResult
                    .InvalidConfiguration,
                PlaybackControlledWindowsPhysicalRunResult.None,
                Validated: false);
        }

        var text = await PlaybackControlledWindowsPhysicalRunRecordFile.ReadBoundedAsync(
                options.RecordPath,
                cancellationToken)
            .ConfigureAwait(false);
        if (!PlaybackControlledWindowsAudioTerminalRenderRecordCodec.TryParse(
                text,
                out var record) ||
            record is null)
        {
            return await WriteReportAsync(
                    output,
                    options,
                    PlaybackControlledWindowsAudioTerminalRenderRecordPreflightResult
                        .InvalidRecord,
                    PlaybackControlledWindowsPhysicalRunResult.None,
                    validated: false)
                .ConfigureAwait(false);
        }

        if (!string.Equals(
                record.FixtureSha256,
                options.FixtureSha256,
                StringComparison.Ordinal) ||
            record.ObservationHoldMilliseconds != options.ObservationHoldMilliseconds ||
            record.AudioBufferMilliseconds != options.AudioBufferMilliseconds)
        {
            return await WriteReportAsync(
                    output,
                    options,
                    PlaybackControlledWindowsAudioTerminalRenderRecordPreflightResult
                        .IdentityMismatch,
                    PlaybackControlledWindowsPhysicalRunResult.None,
                    validated: false)
                .ConfigureAwait(false);
        }

        return await WriteReportAsync(
                output,
                options,
                PlaybackControlledWindowsAudioTerminalRenderRecordPreflightResult
                    .TerminalRecordValidated,
                record.Result,
                validated: true)
            .ConfigureAwait(false);
    }

    private static async ValueTask<
        PlaybackControlledWindowsAudioTerminalRenderRecordPreflightCommandResult>
        WriteReportAsync(
            TextWriter output,
            Options options,
            PlaybackControlledWindowsAudioTerminalRenderRecordPreflightResult result,
            PlaybackControlledWindowsPhysicalRunResult terminalResult,
            bool validated)
    {
        await output.WriteLineAsync(
                $"Milestone: {PlaybackControlledWindowsAudioTerminalRenderRecordCodec.CurrentMilestone}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Result: {result}").ConfigureAwait(false);
        await output.WriteLineAsync($"Terminal-Result: {terminalResult}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Runtime-Build: {PlaybackFfmpegBackendCandidate.BuildId}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Fixture-SHA256: {options.FixtureSha256}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                $"Observation-Hold-Milliseconds: {options.ObservationHoldMilliseconds}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                $"Audio-Buffer-Milliseconds: {options.AudioBufferMilliseconds}")
            .ConfigureAwait(false);
        await output.WriteLineAsync("Record-Kind: Audio").ConfigureAwait(false);
        await output.WriteLineAsync($"Terminal-Record-Validated: {validated}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Physical-Run-Started-Established: {validated}")
            .ConfigureAwait(false);
        await output.WriteLineAsync("Physical-Run-Completion-Established: False")
            .ConfigureAwait(false);
        await output.WriteLineAsync("Completed-Render-Evidence-Established: False")
            .ConfigureAwait(false);
        await output.WriteLineAsync("Audio-Acceptance-Claimed: False")
            .ConfigureAwait(false);
        await output.WriteLineAsync("External-Binary-Or-Payload-Published: False")
            .ConfigureAwait(false);
        await output.WriteLineAsync("Physical-Runs-Executed-By-This-Command: False")
            .ConfigureAwait(false);

        return new(
            validated ? ValidatedExitCode : ValidationFailureExitCode,
            result,
            terminalResult,
            validated);
    }

    private static bool TryParse(string[] arguments, out Options options)
    {
        options = default;
        if (arguments.Length != 9 ||
            Count(arguments, Argument) != 1 ||
            !TryGetOne(arguments, "--record", out var recordPath) ||
            !TryGetOne(arguments, "--fixture-sha256", out var fixtureSha256) ||
            !TryGetOne(arguments, "--observation-hold-ms", out var holdText) ||
            !TryGetOne(arguments, "--audio-buffer-ms", out var bufferText) ||
            !IsValidPath(recordPath) ||
            !PlaybackControlledWindowsPhysicalRunRecordCodec.TryNormalizeSha256(
                fixtureSha256,
                out var normalizedDigest) ||
            !int.TryParse(
                holdText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var observationHoldMilliseconds) ||
            observationHoldMilliseconds is < 0 or > 30_000 ||
            !int.TryParse(
                bufferText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var audioBufferMilliseconds) ||
            audioBufferMilliseconds < 1 ||
            audioBufferMilliseconds >
                PlaybackWindowsOutputStreamInitializationBoundary.MaximumAudioBufferDuration
                    .TotalMilliseconds)
        {
            return false;
        }

        options = new(
            recordPath,
            normalizedDigest,
            observationHoldMilliseconds,
            audioBufferMilliseconds);
        return true;
    }

    private static bool TryGetOne(
        string[] arguments,
        string name,
        out string value)
    {
        value = string.Empty;
        var matchCount = 0;
        for (var index = 0; index < arguments.Length - 1; index++)
        {
            if (!string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            matchCount++;
            value = arguments[index + 1];
        }

        return matchCount == 1 && !string.IsNullOrWhiteSpace(value);
    }

    private static int Count(string[] arguments, string value) =>
        arguments.Count(argument => string.Equals(
            argument,
            value,
            StringComparison.OrdinalIgnoreCase));

    private static bool IsValidPath(string value) =>
        value.Length <= 32_767 && !value.Contains('\0');

    private readonly record struct Options(
        string RecordPath,
        string FixtureSha256,
        int ObservationHoldMilliseconds,
        int AudioBufferMilliseconds);
}
