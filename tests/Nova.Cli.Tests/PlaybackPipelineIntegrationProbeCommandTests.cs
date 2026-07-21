using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackPipelineIntegrationProbeCommandTests
{
    [Fact]
    public async Task RunAsync_ApprovedCandidateReachesRuntimeGateWithoutPathOutput()
    {
        var result = await PlaybackPipelineIntegrationProbeCommand.RunAsync(
        [
            PlaybackPipelineIntegrationProbeCommand.Argument,
            "--runtime-directory",
            "Z:\\private-runtime",
            "--diagnostic-media-file",
            "Z:\\private-media.bin",
        ]);

        Assert.Equal(PlaybackPipelineIntegrationProbeResult.RuntimeRejected, result);
    }
}
