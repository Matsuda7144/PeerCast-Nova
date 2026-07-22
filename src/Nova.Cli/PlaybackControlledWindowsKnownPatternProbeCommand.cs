using System.Globalization;

namespace PeerCast.Nova.Cli;

public readonly record struct PlaybackControlledWindowsKnownPatternProbeCommandResult(
    int ExitCode,
    PlaybackControlledWindowsKnownPatternProbeResult Result);

/// <summary>
/// Explicit diagnostic command that presents one generated non-black BGRA frame
/// through the real controlled Windows output boundaries without FFmpeg, media,
/// fixture, scheduler, retry, or a second presentation.
/// </summary>
public static class PlaybackControlledWindowsKnownPatternProbeCommand
{
    public const string Argument = "--controlled-windows-known-pattern-probe";
    private const string ObservationHoldArgument = "--observation-hold-ms";
    private const int DefaultObservationHoldMilliseconds = 3_000;
    private const int MaximumObservationHoldMilliseconds = 30_000;

    public static async ValueTask<PlaybackControlledWindowsKnownPatternProbeCommandResult> RunAsync(
        string[] arguments,
        TextWriter output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        if (!TryParse(arguments, out var options))
        {
            await output.WriteLineAsync("Result: InvalidConfiguration").ConfigureAwait(false);
            return new(2, PlaybackControlledWindowsKnownPatternProbeResult.InvalidConfiguration);
        }

        await using var probe = new PlaybackControlledWindowsKnownPatternProbe(options);
        var result = await probe.RunAsync(cancellationToken).ConfigureAwait(false);
        await PlaybackControlledWindowsKnownPatternProbeRecordCodec.WriteAsync(
            output,
            probe.Snapshot,
            cancellationToken).ConfigureAwait(false);
        return new(result == PlaybackControlledWindowsKnownPatternProbeResult.Completed ? 0 : 1,
            result);
    }

    internal static bool TryParse(
        string[] arguments,
        out PlaybackControlledWindowsKnownPatternProbeOptions options)
    {
        options = new();
        var hasHoldOption = Array.Exists(
            arguments,
            argument => string.Equals(
                argument,
                ObservationHoldArgument,
                StringComparison.OrdinalIgnoreCase));
        var text = GetOption(arguments, ObservationHoldArgument);
        var milliseconds = DefaultObservationHoldMilliseconds;
        if ((hasHoldOption && text is null) ||
            (text is not null &&
                (!int.TryParse(
                    text,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out milliseconds) ||
                    milliseconds < 0 || milliseconds > MaximumObservationHoldMilliseconds)))
        {
            return false;
        }

        options = new()
        {
            ObservationHoldDuration = TimeSpan.FromMilliseconds(milliseconds),
        };
        return true;
    }

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
