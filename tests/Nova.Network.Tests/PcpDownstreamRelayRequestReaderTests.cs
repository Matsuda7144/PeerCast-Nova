using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpDownstreamRelayRequestReaderTests
{
    private static readonly Guid ChannelId = Guid.ParseExact(
        "531dc8dfc7fb42928ac2c0a626517a87",
        "N");

    [Fact]
    public async Task ReadAsyncPreservesTrailingPcpBytesOnCallerOwnedReader()
    {
        var pipe = new Pipe();
        var header = Encoding.ASCII.GetBytes(string.Concat(
            "GET /channel/",
            ChannelId.ToString("N"),
            " HTTP/1.1\r\n",
            "x-peercast-pcp:1\r\n",
            "x-peercast-pos:17\r\n",
            "\r\n"));
        byte[] trailingPcpBytes = [0x70, 0x63, 0x70, 0x01, 0x02, 0x03];
        var input = new byte[header.Length + trailingPcpBytes.Length];
        header.CopyTo(input, 0);
        trailingPcpBytes.CopyTo(input, header.Length);

        await pipe.Writer.WriteAsync(input);
        await pipe.Writer.CompleteAsync();

        try
        {
            var validation = await PcpDownstreamRelayRequestReader.ReadAsync(
                pipe.Reader,
                maxHeaderBytes: 32 * 1024,
                expectedPcpVersion: 1);

            Assert.True(validation.IsAccepted);
            Assert.NotNull(validation.Request);
            Assert.Equal(ChannelId, validation.Request.ChannelId);
            Assert.Equal(17, validation.Request.StreamPosition);

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
        }
        finally
        {
            await pipe.Reader.CompleteAsync();
        }
    }
}
