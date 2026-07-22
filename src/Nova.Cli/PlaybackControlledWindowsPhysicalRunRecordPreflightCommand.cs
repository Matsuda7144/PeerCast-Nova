using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli;

public readonly record struct PlaybackControlledWindowsPhysicalRunRecordPreflightCommandResult(
    int ExitCode,
    PlaybackControlledWindowsIndependentRunSetResult Result,
    bool Accepted);

/// <summary>
/// Verifies one repository-external sanitized physical-run record against the
/// same fail-closed admission boundary used by paired verification. It starts
/// no runner, runtime, fixture, or physical output.
/// </summary>
public static class PlaybackControlledWindowsPhysicalRunRecordPreflightCommand
{
    public const string Argument = "--verify-controlled-windows-run-record";

    public static async ValueTask<PlaybackControlledWindowsPhysicalRunRecordPreflightCommandResult>
        RunAsync(
            string[] arguments,
            TextWriter output,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        var recordPath = GetOption(arguments, "--record");
        var kindText = GetOption(arguments, "--expected-kind");
        var fixtureSha256 = GetOption(arguments, "--fixture-sha256");
        if (!IsValidPath(recordPath) ||
            !TryParseKind(kindText, out var expectedKind) ||
            !PlaybackControlledWindowsPhysicalRunRecordCodec.TryNormalizeSha256(
                fixtureSha256,
                out var normalizedDigest))
        {
            await output.WriteLineAsync("Result: InvalidConfiguration").ConfigureAwait(false);
            return new(
                2,
                PlaybackControlledWindowsIndependentRunSetResult.InvalidRecord,
                Accepted: false);
        }

        var text = await PlaybackControlledWindowsPhysicalRunRecordFile.ReadBoundedAsync(
                recordPath!,
                cancellationToken)
            .ConfigureAwait(false);
        if (!PlaybackControlledWindowsPhysicalRunRecordCodec.TryParse(text, out var record) ||
            record!.Kind != expectedKind)
        {
            await WriteReportAsync(
                output,
                PlaybackControlledWindowsIndependentRunSetResult.InvalidRecord,
                normalizedDigest,
                expectedKind,
                accepted: false).ConfigureAwait(false);
            return new(
                2,
                PlaybackControlledWindowsIndependentRunSetResult.InvalidRecord,
                Accepted: false);
        }

        using var boundary = new PlaybackControlledWindowsIndependentRunSetBoundary(
            normalizedDigest);
        var result = boundary.ValidateAndConsume(record);
        var accepted = expectedKind == PlaybackControlledWindowsOutputValidationKind.Video
            ? result == PlaybackControlledWindowsIndependentRunSetResult.VideoAccepted
            : result == PlaybackControlledWindowsIndependentRunSetResult.AudioAccepted;
        await WriteReportAsync(
            output,
            result,
            normalizedDigest,
            expectedKind,
            accepted).ConfigureAwait(false);
        return new(accepted ? 0 : 1, result, accepted);
    }

    private static async ValueTask WriteReportAsync(
        TextWriter output,
        PlaybackControlledWindowsIndependentRunSetResult result,
        string fixtureSha256,
        PlaybackControlledWindowsOutputValidationKind expectedKind,
        bool accepted)
    {
        await output.WriteLineAsync("Milestone: 0.0.10d Phase 5").ConfigureAwait(false);
        await output.WriteLineAsync($"Result: {result}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Runtime-Build: {PlaybackFfmpegBackendCandidate.BuildId}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Fixture-SHA256: {fixtureSha256}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Expected-Kind: {expectedKind}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Record-Accepted: {accepted}")
            .ConfigureAwait(false);
        await output.WriteLineAsync("Physical-Runs-Executed-By-This-Command: False")
            .ConfigureAwait(false);
    }

    private static bool TryParseKind(
        string? value,
        out PlaybackControlledWindowsOutputValidationKind kind)
    {
        if (string.Equals(value, "video", StringComparison.OrdinalIgnoreCase))
        {
            kind = PlaybackControlledWindowsOutputValidationKind.Video;
            return true;
        }

        if (string.Equals(value, "audio", StringComparison.OrdinalIgnoreCase))
        {
            kind = PlaybackControlledWindowsOutputValidationKind.Audio;
            return true;
        }

        kind = PlaybackControlledWindowsOutputValidationKind.None;
        return false;
    }

    private static bool IsValidPath(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 32_767 &&
        !value.Contains('\0');

    private static string? GetOption(string[] arguments, string name)
    {
        for (var index = 0; index < arguments.Length - 1; index++)
        {
            if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return arguments[index + 1];
            }
        }

        return null;
    }
}
