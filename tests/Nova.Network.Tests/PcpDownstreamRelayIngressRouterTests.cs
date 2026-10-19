using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpDownstreamRelayIngressRouterTests
{
    private static readonly ChannelId ChannelId = new(
        Guid.ParseExact("531dc8dfc7fb42928ac2c0a626517a87", "N"));

    [Fact]
    public async Task ReadAndResolvePreservesTrailingBytesAndRouteLease()
    {
        var registry = new PcpDownstreamRelayRouteRegistry();
        await using var fanout = new PcpRelayFanout();
        var registration = registry.Register(ChannelId, fanout);
        var pipe = new Pipe();
        var header = Encoding.ASCII.GetBytes(string.Concat(
            "GET /channel/",
            ChannelId.Value.ToString("N"),
            " HTTP/1.1\r\n",
            "x-peercast-pcp:1\r\n",
            "x-peercast-pos:23\r\n",
            "\r\n"));
        byte[] trailingPcpBytes = [0x70, 0x63, 0x70, 0x04, 0x05, 0x06];
        var input = new byte[header.Length + trailingPcpBytes.Length];
        header.CopyTo(input, 0);
        trailingPcpBytes.CopyTo(input, header.Length);

        await pipe.Writer.WriteAsync(input);
        await pipe.Writer.CompleteAsync();

        try
        {
            var resolution =
                await PcpDownstreamRelayIngressRouter.ReadAndResolveAsync(
                    pipe.Reader,
                    registry,
                    maxHeaderBytes: 32 * 1024,
                    expectedPcpVersion: 1);

            Assert.True(resolution.IsRouted);
            Assert.Equal(200, resolution.StatusCode);
            Assert.NotNull(resolution.Request);
            Assert.Equal(ChannelId.Value, resolution.Request.ChannelId);
            Assert.Equal(23, resolution.Request.StreamPosition);
            Assert.Same(fanout, resolution.Fanout);

            var unregisterTask = registration.DisposeAsync().AsTask();
            Assert.False(unregisterTask.IsCompleted);

            var read = await pipe.Reader.ReadAsync();
            try
            {
                var remaining = new byte[checked((int)read.Buffer.Length)];
                read.Buffer.CopyTo(remaining);
                Assert.Equal(trailingPcpBytes, remaining);
            }
            finally
            {
                pipe.Reader.AdvanceTo(read.Buffer.End);
            }

            resolution.Dispose();
            resolution.Dispose();
            await unregisterTask;
        }
        finally
        {
            await registration.DisposeAsync();
            await pipe.Reader.CompleteAsync();
        }
    }

    [Fact]
    public async Task ReadAndResolveReturnsNotFoundWithoutLeasingUnknownChannel()
    {
        var registry = new PcpDownstreamRelayRouteRegistry();
        var pipe = new Pipe();
        var header = Encoding.ASCII.GetBytes(string.Concat(
            "GET /channel/",
            ChannelId.Value.ToString("N"),
            " HTTP/1.1\r\n",
            "x-peercast-pcp:1\r\n",
            "x-peercast-pos:0\r\n",
            "\r\n"));

        await pipe.Writer.WriteAsync(header);
        await pipe.Writer.CompleteAsync();

        try
        {
            using var resolution =
                await PcpDownstreamRelayIngressRouter.ReadAndResolveAsync(
                    pipe.Reader,
                    registry,
                    maxHeaderBytes: 32 * 1024,
                    expectedPcpVersion: 1);

            Assert.False(resolution.IsRouted);
            Assert.Equal(404, resolution.StatusCode);
            Assert.Null(resolution.Request);
            Assert.Null(resolution.Fanout);
        }
        finally
        {
            await pipe.Reader.CompleteAsync();
        }
    }
}
