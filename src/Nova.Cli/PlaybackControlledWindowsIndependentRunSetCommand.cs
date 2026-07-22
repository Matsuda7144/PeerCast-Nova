using System.Text;
using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli;

public readonly record struct PlaybackControlledWindowsIndependentRunSetCommandResult(
    int ExitCode,
    PlaybackControlledWindowsIndependentRunSetResult Result);

/// <summary>
/// Verifies two repository-external sanitized records. It never starts a
/// runner, opens a runtime or fixture, or claims that this command performed
/// physical output.
/// </summary>
public static class PlaybackControlledWindowsIndependentRunSetCommand
{
    public const string Argument = "--verify-controlled-windows-run-records";
    private const int MaximumRecordByteCount =
        PlaybackControlledWindowsPhysicalRunRecordCodec.MaximumRecordCharacterCount * 4;

    public static async ValueTask<PlaybackControlledWindowsIndependentRunSetCommandResult>
        RunAsync(
            string[] arguments,
            TextWriter output,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        var videoPath = GetOption(arguments, "--video-record");
        var audioPath = GetOption(arguments, "--audio-record");
        var fixtureSha256 = GetOption(arguments, "--fixture-sha256");
        if (!IsValidPath(videoPath) ||
            !IsValidPath(audioPath) ||
            string.Equals(videoPath, audioPath, StringComparison.OrdinalIgnoreCase) ||
            !PlaybackControlledWindowsPhysicalRunRecordCodec.TryNormalizeSha256(
                fixtureSha256,
                out var normalizedDigest))
        {
            await output.WriteLineAsync("Result: InvalidConfiguration").ConfigureAwait(false);
            return new(2, PlaybackControlledWindowsIndependentRunSetResult.InvalidRecord);
        }

        var videoText = await ReadBoundedAsync(videoPath!, cancellationToken)
            .ConfigureAwait(false);
        var audioText = await ReadBoundedAsync(audioPath!, cancellationToken)
            .ConfigureAwait(false);
        if (!PlaybackControlledWindowsPhysicalRunRecordCodec.TryParse(
                videoText,
                out var videoRecord) ||
            !PlaybackControlledWindowsPhysicalRunRecordCodec.TryParse(
                audioText,
                out var audioRecord) ||
            videoRecord!.Kind != PlaybackControlledWindowsOutputValidationKind.Video ||
            audioRecord!.Kind != PlaybackControlledWindowsOutputValidationKind.Audio)
        {
            await WriteReportAsync(
                output,
                PlaybackControlledWindowsIndependentRunSetResult.InvalidRecord,
                normalizedDigest,
                videoAccepted: false,
                audioAccepted: false).ConfigureAwait(false);
            return new(2, PlaybackControlledWindowsIndependentRunSetResult.InvalidRecord);
        }

        using var boundary = new PlaybackControlledWindowsIndependentRunSetBoundary(
            normalizedDigest);
        var video = boundary.ValidateAndConsume(videoRecord);
        if (video != PlaybackControlledWindowsIndependentRunSetResult.VideoAccepted)
        {
            await WriteReportAsync(
                output,
                video,
                normalizedDigest,
                videoAccepted: false,
                audioAccepted: false).ConfigureAwait(false);
            return new(1, video);
        }

        var audio = boundary.ValidateAndConsume(audioRecord);
        var completed = audio == PlaybackControlledWindowsIndependentRunSetResult.Completed;
        await WriteReportAsync(
            output,
            audio,
            normalizedDigest,
            videoAccepted: true,
            audioAccepted: completed).ConfigureAwait(false);
        return new(completed ? 0 : 1, audio);
    }

    private static async ValueTask<string?> ReadBoundedAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length <= 0 || stream.Length > MaximumRecordByteCount)
            {
                return null;
            }

            var bytes = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            var text = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true).GetString(bytes);
            return text.Length <=
                PlaybackControlledWindowsPhysicalRunRecordCodec.MaximumRecordCharacterCount
                ? text
                : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static async ValueTask WriteReportAsync(
        TextWriter output,
        PlaybackControlledWindowsIndependentRunSetResult result,
        string fixtureSha256,
        bool videoAccepted,
        bool audioAccepted)
    {
        await output.WriteLineAsync("Milestone: 0.0.10d Phase 5").ConfigureAwait(false);
        await output.WriteLineAsync($"Result: {result}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Runtime-Build: {PlaybackFfmpegBackendCandidate.BuildId}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Fixture-SHA256: {fixtureSha256}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Video-Record-Accepted: {videoAccepted}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Audio-Record-Accepted: {audioAccepted}")
            .ConfigureAwait(false);
        await output.WriteLineAsync("Physical-Runs-Executed-By-This-Command: False")
            .ConfigureAwait(false);
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
