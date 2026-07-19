using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli;

internal static class PlaybackPipelineIntegrationProbeCommand
{
    public const string Argument = "--playback-pipeline-integration-probe";

    public static async ValueTask<PlaybackPipelineIntegrationProbeResult> RunAsync(
        string[] arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var runtimeDirectory = GetOption(arguments, "--runtime-directory");
        var mediaFile = GetOption(arguments, "--diagnostic-media-file");

        using var probe = new PlaybackPipelineIntegrationProbe();
        return await probe.RunAsync(new()
        {
            RuntimeDirectory = runtimeDirectory ?? string.Empty,
            DiagnosticMediaFile = mediaFile ?? string.Empty,
        }, cancellationToken).ConfigureAwait(false);
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
