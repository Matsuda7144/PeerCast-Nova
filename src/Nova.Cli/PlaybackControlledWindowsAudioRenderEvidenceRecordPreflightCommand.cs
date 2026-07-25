using System.Globalization;
using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli;

public enum PlaybackControlledWindowsAudioRenderEvidenceRecordPreflightResult
{
    None,
    RenderEvidenceRecordValidated,
    InvalidConfiguration,
    InvalidRecord,
    IdentityMismatch,
}

public readonly record struct
    PlaybackControlledWindowsAudioRenderEvidenceRecordPreflightCommandResult(
        int ExitCode,
        PlaybackControlledWindowsAudioRenderEvidenceRecordPreflightResult Result,
        PlaybackControlledWindowsPhysicalRunResult RecordResult,
        PlaybackControlledWindowsOutputEvidenceResult EvidenceResult,
        bool Validated);

/// <summary>
/// Revalidates one retained completed controlled audio render-evidence record
/// without starting a runner, runtime, fixture replay, endpoint, or physical output.
/// </summary>
public static class PlaybackControlledWindowsAudioRenderEvidenceRecordPreflightCommand
{
    public const string Argument =
        "--verify-controlled-windows-audio-render-evidence-record";

    private const int InvalidConfigurationExitCode = 2;
    private const int ValidationFailureExitCode = 1;
    private const int ValidatedExitCode = 0;

    public static async ValueTask<
        PlaybackControlledWindowsAudioRenderEvidenceRecordPreflightCommandResult> RunAsync(
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
                PlaybackControlledWindowsAudioRenderEvidenceRecordPreflightResult
                    .InvalidConfiguration,
                PlaybackControlledWindowsPhysicalRunResult.None,
                PlaybackControlledWindowsOutputEvidenceResult.None,
                Validated: false);
        }

        var text = await PlaybackControlledWindowsPhysicalRunRecordFile.ReadBoundedAsync(
                options.RecordPath,
                cancellationToken)
            .ConfigureAwait(false);
        if (!PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.TryParse(
                text,
                out var record) ||
            record is null)
        {
            return await WriteReportAsync(
                    output,
                    options,
                    PlaybackControlledWindowsAudioRenderEvidenceRecordPreflightResult
                        .InvalidRecord,
                    PlaybackControlledWindowsPhysicalRunResult.None,
                    PlaybackControlledWindowsOutputEvidenceResult.None,
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
                    PlaybackControlledWindowsAudioRenderEvidenceRecordPreflightResult
                        .IdentityMismatch,
                    PlaybackControlledWindowsPhysicalRunResult.None,
                    PlaybackControlledWindowsOutputEvidenceResult.None,
                    validated: false)
                .ConfigureAwait(false);
        }

        return await WriteReportAsync(
                output,
                options,
                PlaybackControlledWindowsAudioRenderEvidenceRecordPreflightResult
                    .RenderEvidenceRecordValidated,
                record.Result,
                record.EvidenceResult,
                validated: true)
            .ConfigureAwait(false);
    }

    private static async ValueTask<
        PlaybackControlledWindowsAudioRenderEvidenceRecordPreflightCommandResult>
        WriteReportAsync(
            TextWriter output,
            Options options,
            PlaybackControlledWindowsAudioRenderEvidenceRecordPreflightResult result,
            PlaybackControlledWindowsPhysicalRunResult recordResult,
            PlaybackControlledWindowsOutputEvidenceResult evidenceResult,
            bool validated)
    {
        await output.WriteLineAsync(
                $"Milestone: {PlaybackControlledWindowsAudioRenderEvidenceRecordCodec.CurrentMilestone}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Result: {result}").ConfigureAwait(false);
        await output.WriteLineAsync($"Record-Result: {recordResult}").ConfigureAwait(false);
        await output.WriteLineAsync($"Evidence: {evidenceResult}").ConfigureAwait(false);
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
        await output.WriteLineAsync($"Render-Evidence-Record-Validated: {validated}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                $"Captured-Physical-Run-Completion-Established: {validated}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Completed-Render-Evidence-Established: {validated}")
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
            recordResult,
            evidenceResult,
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
