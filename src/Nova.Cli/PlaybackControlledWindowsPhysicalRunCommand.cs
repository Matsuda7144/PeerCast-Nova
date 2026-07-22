using System.Globalization;
using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli;

public readonly record struct PlaybackControlledWindowsPhysicalRunCommandResult(
    int ExitCode,
    PlaybackControlledWindowsPhysicalRunResult Result);

/// <summary>
/// Explicit sanitized command used only by the dedicated controlled Windows
/// physical-run entrypoint. Normal PeerCast Nova startup does not invoke it.
/// </summary>
public static class PlaybackControlledWindowsPhysicalRunCommand
{
    public const string Argument = "--controlled-windows-physical-run";

    public static async ValueTask<PlaybackControlledWindowsPhysicalRunCommandResult> RunAsync(
        string[] arguments,
        TextWriter output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        if (!TryParse(arguments, out var options))
        {
            await output.WriteLineAsync("Result: InvalidConfiguration").ConfigureAwait(false);
            return new(2, PlaybackControlledWindowsPhysicalRunResult.InvalidConfiguration);
        }

        await using var composition = new PlaybackControlledWindowsPhysicalRunComposition(options);
        var result = await composition.RunAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = composition.Snapshot;
        await output.WriteLineAsync("Milestone: 0.0.10d Phase 4").ConfigureAwait(false);
        await output.WriteLineAsync($"Result: {result}").ConfigureAwait(false);
        await output.WriteLineAsync($"Kind: {snapshot.Kind}").ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Runtime Build: {PlaybackFfmpegBackendCandidate.BuildId}").ConfigureAwait(false);
        await output.WriteLineAsync($"Fixture SHA-256: {snapshot.FixtureSha256}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Fixture Bytes: {snapshot.FixtureByteCount.ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Fixture Packets: {snapshot.FixturePacketCount.ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Decode Operations: {snapshot.DecodeOperationCount.ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Delivery Attempts: {snapshot.DeliveryAttemptCount.ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Delivered: {snapshot.DeliveredCount.ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Retained WouldBlock: {snapshot.RetainedWouldBlockCount.ToString(CultureInfo.InvariantCulture)}")
            .ConfigureAwait(false);
        await output.WriteLineAsync($"Evidence: {snapshot.EvidenceResult}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Borrowed Window Preserved: {snapshot.BorrowedWindowPreserved}")
            .ConfigureAwait(false);
        await output.WriteLineAsync(
            $"Borrowed Dispatcher Preserved: {snapshot.BorrowedDispatcherPreserved}")
            .ConfigureAwait(false);
        await output.WriteLineAsync("External Binary Or Payload Published: False")
            .ConfigureAwait(false);
        return new(result == PlaybackControlledWindowsPhysicalRunResult.Completed ? 0 : 1, result);
    }

    private static bool TryParse(
        string[] arguments,
        out PlaybackControlledWindowsPhysicalRunOptions options)
    {
        options = new();
        var runtime = GetOption(arguments, "--runtime-directory");
        var fixture = GetOption(arguments, "--controlled-fixture");
        var digest = GetOption(arguments, "--fixture-sha256");
        var kindText = GetOption(arguments, "--output-kind");
        if (runtime is null || fixture is null || digest is null ||
            !TryParseKind(kindText, out var kind) ||
            !TryParseBounded(arguments, "--max-decode-operations", 128, 1, 4096,
                out var maximumDecode) ||
            !TryParseBounded(arguments, "--max-delivery-operations", 128, 1, 4096,
                out var maximumDelivery) ||
            !TryParseBounded(arguments, "--fixture-packet-bytes", 64 * 1024, 1,
                1024 * 1024, out var packetBytes))
        {
            return false;
        }

        options = new()
        {
            Kind = kind,
            RuntimeDirectory = runtime,
            FixturePath = fixture,
            FixtureSha256 = digest,
            MaximumDecodeOperations = maximumDecode,
            MaximumDeliveryOperations = maximumDelivery,
            PacketPayloadBytes = packetBytes,
        };
        return true;
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

    private static bool TryParseBounded(
        string[] arguments,
        string name,
        int fallback,
        int minimum,
        int maximum,
        out int value)
    {
        var text = GetOption(arguments, name);
        if (text is null)
        {
            value = fallback;
            return true;
        }
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) &&
            value >= minimum && value <= maximum;
    }

    private static string? GetOption(string[] arguments, string name)
    {
        for (var index = 0; index < arguments.Length - 1; index++)
        {
            if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
                return arguments[index + 1];
        }
        return null;
    }
}
