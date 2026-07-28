using System.Globalization;

namespace PeerCast.Nova.Cli;

public static class PlaybackControlledWindowsAudioRenderEvidenceChildCaptureCommand
{
    public const string Argument = "--capture-controlled-windows-audio-render-evidence";
    public const int EvidenceRecordProducedExitCode = 0;
    public const int TerminalRecordProducedExitCode = 1;
    public const int SanitizedFailureExitCode = 2;
    public const int InvalidConfigurationExitCode = 3;

    public static ValueTask<int> RunAsync(
        string[] arguments,
        string runnerAssemblyPath,
        TextWriter output,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            arguments,
            runnerAssemblyPath,
            Directory.GetCurrentDirectory(),
            output,
            new SystemPlaybackControlledWindowsAudioRenderEvidenceChildProcessExecutor(),
            cancellationToken);

    internal static async ValueTask<int> RunAsync(
        string[] arguments,
        string runnerAssemblyPath,
        string repositoryRoot,
        TextWriter output,
        IPlaybackControlledWindowsAudioRenderEvidenceChildProcessExecutor executor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(executor);
        if (!TryParse(arguments, out var options))
        {
            await output.WriteLineAsync("Result: InvalidConfiguration").ConfigureAwait(false);
            return InvalidConfigurationExitCode;
        }

        var composition =
            new PlaybackControlledWindowsAudioRenderEvidenceChildCaptureComposition(
                executor,
                runnerAssemblyPath,
                repositoryRoot);
        var exitCode = await composition.RunAsync(options, cancellationToken)
            .ConfigureAwait(false);
        await output.WriteLineAsync(
                exitCode switch
                {
                    EvidenceRecordProducedExitCode => "Result: RenderEvidenceRecordProduced",
                    TerminalRecordProducedExitCode => "Result: TerminalRenderRecordProduced",
                    SanitizedFailureExitCode => "Result: SanitizedFailureRecordProduced",
                    _ => "Result: CaptureIntegrationFailure",
                })
            .ConfigureAwait(false);
        return exitCode;
    }

    internal static bool TryParse(
        string[] arguments,
        out PlaybackControlledWindowsAudioRenderEvidenceChildCaptureOptions options)
    {
        options = default;
        if (Count(arguments, Argument) != 1 ||
            !TryGetOne(arguments, "--runtime-directory", out var runtime) ||
            !TryGetOne(arguments, "--controlled-fixture", out var fixture) ||
            !TryGetOne(arguments, "--fixture-sha256", out var fixtureSha256) ||
            !TryGetOne(
                arguments,
                "--render-evidence-record-path",
                out var evidenceRecord) ||
            !TryGetOptionalOne(
                arguments,
                "--terminal-render-record-path",
                out var terminalRecord) ||
            !TryGetOne(arguments, "--failure-record-path", out var failureRecord) ||
            !TryGetBounded(
                arguments,
                "--max-decode-operations",
                4,
                4096,
                out var maximumDecodeOperations) ||
            !TryGetBounded(
                arguments,
                "--max-delivery-operations",
                4,
                4096,
                out var maximumDeliveryOperations) ||
            !TryGetBounded(
                arguments,
                "--fixture-packet-bytes",
                1,
                1_048_576,
                out var fixturePacketBytes) ||
            !TryGetBounded(
                arguments,
                "--observation-hold-ms",
                0,
                30_000,
                out var observationHoldMilliseconds))
        {
            return false;
        }

        options = new(
            runtime,
            fixture,
            fixtureSha256,
            evidenceRecord,
            terminalRecord,
            failureRecord,
            maximumDecodeOperations,
            maximumDeliveryOperations,
            fixturePacketBytes,
            observationHoldMilliseconds);
        return true;
    }

    private static bool TryGetBounded(
        string[] arguments,
        string name,
        int minimum,
        int maximum,
        out int value)
    {
        value = 0;
        return TryGetOne(arguments, name, out var text) &&
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) &&
            value >= minimum && value <= maximum;
    }

    private static bool TryGetOptionalOne(
        string[] arguments,
        string name,
        out string? value)
    {
        value = null;
        var matches = GetMatches(arguments, name);
        if (matches.Count == 0)
        {
            return true;
        }
        if (matches.Count != 1 || string.IsNullOrWhiteSpace(matches[0]))
        {
            return false;
        }
        value = matches[0];
        return true;
    }

    private static bool TryGetOne(
        string[] arguments,
        string name,
        out string value)
    {
        value = string.Empty;
        var matches = GetMatches(arguments, name);
        if (matches.Count != 1 || string.IsNullOrWhiteSpace(matches[0]))
        {
            return false;
        }
        value = matches[0];
        return true;
    }

    private static List<string> GetMatches(string[] arguments, string name)
    {
        var matches = new List<string>();
        for (var index = 0; index < arguments.Length - 1; index++)
        {
            if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(arguments[index + 1]);
            }
        }
        return matches;
    }

    private static int Count(string[] arguments, string value) =>
        arguments.Count(argument => string.Equals(
            argument,
            value,
            StringComparison.OrdinalIgnoreCase));
}
