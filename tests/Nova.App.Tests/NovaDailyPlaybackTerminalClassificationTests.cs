using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.App.Tests;

public sealed class NovaDailyPlaybackTerminalClassificationTests
{
    [Theory]
    [InlineData(PcpChannelSessionEndReason.Stopped, false)]
    [InlineData(PcpChannelSessionEndReason.RelayFull, true)]
    [InlineData(PcpChannelSessionEndReason.HttpRejected, true)]
    [InlineData(PcpChannelSessionEndReason.ProtocolRejected, true)]
    [InlineData(PcpChannelSessionEndReason.QuitReceived, true)]
    [InlineData(PcpChannelSessionEndReason.ConnectionEnded, true)]
    public async Task SessionEndReasonProjectsToTruthfulFailure(
        PcpChannelSessionEndReason endReason,
        bool expectedFailure)
    {
        var result = new PcpChannelSessionResult(
            endReason,
            AttemptCount: 1,
            ReconnectCount: 0,
            HttpStatusCode: null,
            ChannelMessageCount: 0,
            PacketCount: 0,
            PayloadByteCount: 0);

        var failed = await NovaDailyHttpFlvPlaybackController
            .IsSessionTerminalFailureAsync(Task.FromResult(result));

        Assert.Equal(expectedFailure, failed);
    }

    [Fact]
    public async Task CallerCancellationIsNotClassifiedAsPlaybackFailure()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var failed = await NovaDailyHttpFlvPlaybackController.IsSessionTerminalFailureAsync(
            Task.FromCanceled<PcpChannelSessionResult>(cancellation.Token));

        Assert.False(failed);
    }

    [Fact]
    public async Task FaultedSessionTaskIsClassifiedAsPlaybackFailure()
    {
        var failed = await NovaDailyHttpFlvPlaybackController.IsSessionTerminalFailureAsync(
            Task.FromException<PcpChannelSessionResult>(
                new InvalidOperationException("private session failure")));

        Assert.True(failed);
    }
}
