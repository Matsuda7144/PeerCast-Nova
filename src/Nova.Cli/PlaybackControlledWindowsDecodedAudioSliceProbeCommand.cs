using System.Globalization;

namespace PeerCast.Nova.Cli;

public readonly record struct PlaybackControlledWindowsDecodedAudioSliceProbeCommandResult(
    int ExitCode,
    PlaybackControlledWindowsDecodedAudioSliceProbeResult Result);

/// <summary>
/// Diagnostic-only controlled command that decodes one retained external FLV
/// fixture and writes its chronological first 80 ms to the production Windows
/// audio buffer exactly once.
/// </summary>
public static class PlaybackControlledWindowsDecodedAudioSliceProbeCommand
{
    public const string Argument = "--controlled-windows-decoded-audio-slice-probe";
    private const int MaximumObservationHoldMilliseconds = 30_000;

    public static async ValueTask<
        PlaybackControlledWindowsDecodedAudioSliceProbeCommandResult> RunAsync(
            string[] arguments,
            TextWriter output,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        if (!TryParse(arguments, out var options))
        {
            await output.WriteLineAsync("Result: InvalidConfiguration")
                .ConfigureAwait(false);
            return new(
                2,
                PlaybackControlledWindowsDecodedAudioSliceProbeResult.InvalidConfiguration);
        }

        await using var probe =
            new PlaybackControlledWindowsDecodedAudioSliceProbe(options);
        var result = await probe.RunAsync(cancellationToken).ConfigureAwait(false);
        await PlaybackControlledWindowsDecodedAudioSliceProbeRecordCodec.WriteAsync(
            output,
            probe.Snapshot,
            cancellationToken).ConfigureAwait(false);
        return new(
            result == PlaybackControlledWindowsDecodedAudioSliceProbeResult.Completed
                ? 0
                : 1,
            result);
    }

    internal static bool TryParse(
        string[] arguments,
        out PlaybackControlledWindowsDecodedAudioSliceProbeOptions options)
    {
        options = new();
        var runtime = GetOption(arguments, "--runtime-directory");
        var fixture = GetOption(arguments, "--controlled-fixture");
        if (runtime is null || fixture is null ||
            !TryParseBounded(
                arguments,
                "--max-decode-operations",
                PlaybackControlledWindowsDecodedAudioSliceProbeOptions
                    .DefaultMaximumDecodeOperations,
                1,
                4_096,
                out var maximumDecodeOperations) ||
            !TryParseBounded(
                arguments,
                "--fixture-packet-bytes",
                PlaybackControlledWindowsDecodedAudioSliceProbeOptions
                    .DefaultFixturePacketBytes,
                1,
                PlaybackControlledWindowsDecodedAudioSliceProbeOptions.FixtureBytes,
                out var fixturePacketBytes) ||
            !TryParseBounded(
                arguments,
                "--observation-hold-ms",
                1_000,
                0,
                MaximumObservationHoldMilliseconds,
                out var observationHoldMilliseconds))
        {
            return false;
        }

        options = new()
        {
            RuntimeDirectory = runtime,
            FixturePath = fixture,
            MaximumDecodeOperations = maximumDecodeOperations,
            FixturePacketBytes = fixturePacketBytes,
            ObservationHoldDuration =
                TimeSpan.FromMilliseconds(observationHoldMilliseconds),
        };
        return true;
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
            return !HasOption(arguments, name);
        }

        return int.TryParse(
                text,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out value) &&
            value >= minimum &&
            value <= maximum;
    }

    private static bool HasOption(string[] arguments, string name) =>
        Array.Exists(
            arguments,
            argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));

    private static string? GetOption(string[] arguments, string name)
    {
        for (var index = 0; index < arguments.Length - 1; index++)
        {
            if (string.Equals(
                arguments[index],
                name,
                StringComparison.OrdinalIgnoreCase))
            {
                return arguments[index + 1];
            }
        }

        return null;
    }
}
