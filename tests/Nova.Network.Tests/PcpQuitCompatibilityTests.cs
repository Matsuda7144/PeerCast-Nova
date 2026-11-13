using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpQuitCompatibilityTests
{
    [Theory]
    [InlineData(503, 1003U, true, PcpCompatibilityOutcome.RelayFull, PcpQuitReason.Unavailable)]
    [InlineData(200, 1003U, true, PcpCompatibilityOutcome.RelayFull, PcpQuitReason.Unavailable)]
    [InlineData(503, 0U, true, PcpCompatibilityOutcome.RelayFull, PcpQuitReason.Unspecified)]
    [InlineData(503, 0U, false, PcpCompatibilityOutcome.ProtocolRejected, PcpQuitReason.Unspecified)]
    [InlineData(200, 0U, true, PcpCompatibilityOutcome.ProtocolRejected, PcpQuitReason.Unspecified)]
    [InlineData(503, 1000U, true, PcpCompatibilityOutcome.ProtocolRejected, PcpQuitReason.Normal)]
    [InlineData(503, 1005U, true, PcpCompatibilityOutcome.ProtocolRejected, PcpQuitReason.NotIdentified)]
    [InlineData(503, 1007U, true, PcpCompatibilityOutcome.ProtocolRejected, PcpQuitReason.BadAgent)]
    [InlineData(503, 1007U, false, PcpCompatibilityOutcome.ProtocolRejected, PcpQuitReason.BadAgent)]
    [InlineData(503, 1008U, true, PcpCompatibilityOutcome.ProtocolRejected, PcpQuitReason.Other)]
    [InlineData(503, 1234U, true, PcpCompatibilityOutcome.ProtocolRejected, PcpQuitReason.Other)]
    public async Task Probe_ExplicitQuitReasonTakesPrecedenceOverHttp(
        int status, uint code, bool sendOleh, PcpCompatibilityOutcome outcome, PcpQuitReason reason)
    {
        var result = await ProbeReplyAsync(status, PcpQuitCodec.Create(code), sendOleh);
        Assert.Equal(outcome, result.Outcome);
        Assert.Equal(code, result.TerminalCode);
        Assert.Equal(new PcpQuitInfo(code), result.Quit);
        Assert.Equal(reason, result.Quit!.Value.Reason);
    }

    [Theory]
    [InlineData(200, 1)]
    [InlineData(200, 4096)]
    [InlineData(503, 1)]
    [InlineData(503, 4096)]
    public async Task Probe_CoalescedOrSplitHttpAndPcpContinueNegotiation(int status, int chunkSize)
    {
        var result = await ProbeReplyAsync(status,
            PcpAtom.Leaf(PcpKnownAtomNames.Okay, PcpWireValueCodec.EncodeUInt32(1)),
            chunkSize: chunkSize);
        Assert.Equal(PcpCompatibilityOutcome.Accepted, result.Outcome);
        Assert.Null(result.Quit);
        Assert.Equal(new[] { PcpKnownAtomNames.Oleh, PcpKnownAtomNames.Okay }, result.ReceivedAtomNames);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task Probe_HttpAuthorizationRejectionIsNotRelayFull(int status)
    {
        var result = await ProbeReplyAsync(status, PcpQuitCodec.Create(0));
        Assert.Equal(PcpCompatibilityOutcome.HttpRejected, result.Outcome);
        Assert.Null(result.Quit);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(8)]
    public async Task Probe_MalformedQuitIsNotRelayFull(int length)
    {
        var atom = length < 0 ? PcpAtom.Parent(PcpKnownAtomNames.Quit) :
            PcpAtom.Leaf(PcpKnownAtomNames.Quit, new byte[length]);
        await Assert.ThrowsAsync<InvalidDataException>(() => ProbeReplyAsync(503, atom));
    }

    [Fact]
    public async Task Probe_EmptyOlehIdentityDoesNotEnableLegacyCapacityFallback()
    {
        var result = await ProbeReplyAsync(503, PcpQuitCodec.Create(0), remoteSessionId: Guid.Empty);
        Assert.Equal(PcpCompatibilityOutcome.ProtocolRejected, result.Outcome);
        Assert.Equal(PcpQuitReason.Unspecified, result.Quit!.Value.Reason);
    }

    private static async Task<PcpCompatibilityProbeResult> ProbeReplyAsync(
        int status, PcpAtom terminal, bool sendOleh = true, int chunkSize = 4096,
        Guid? remoteSessionId = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var listener = new TcpTransportListener(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Start();
        var peer = ReplyAsync(listener, status, terminal, sendOleh, chunkSize,
            remoteSessionId ?? Guid.NewGuid(), timeout.Token);
        var client = new PcpCompatibilityClient(new TcpTransportConnector());
        try
        {
            return await client.ProbeAsync(listener.LocalEndPoint.Address.ToString(),
                listener.LocalEndPoint.Port, Guid.NewGuid(), cancellationToken: timeout.Token);
        }
        finally
        {
            await peer.WaitAsync(timeout.Token);
        }
    }

    private static async Task ReplyAsync(TcpTransportListener listener, int status,
        PcpAtom terminal, bool sendOleh, int chunkSize, Guid remoteSessionId, CancellationToken token)
    {
        await using var connection = await listener.AcceptAsync(token);
        var pipe = PipeReader.Create(connection.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
        try
        {
            var request = await PcpHttpHeaderBlockReader.ReadRequestAsync(pipe, 32 * 1024, token);
            Assert.Equal("GET", request.Method);
            var bytes = new ArrayBufferWriter<byte>();
            bytes.Write(Encoding.ASCII.GetBytes(
                $"HTTP/1.0 {status} Test\r\nContent-Type: application/x-peercast-pcp\r\n\r\n"));
            if (status is 200 or 503)
            {
                if (sendOleh)
                    PcpAtomTreeCodec.Write(bytes, PcpHeloCodec.CreateOleh("Synthetic peer", remoteSessionId));
                PcpAtomTreeCodec.Write(bytes, terminal);
            }

            // The HTTP boundary and PCP reply share a single write, or are
            // deliberately written one byte at a time, using the same reader.
            for (var offset = 0; offset < bytes.WrittenCount; offset += chunkSize)
                await connection.WriteAsync(bytes.WrittenMemory.Slice(offset,
                    Math.Min(chunkSize, bytes.WrittenCount - offset)), token);

            if (status is 200 or 503)
            {
                await using var atoms = new PcpAtomPipeMessageReader(pipe);
                _ = PcpHeloCodec.ParseHelo(await atoms.ReadNextAsync(token));
            }
        }
        finally
        {
            await pipe.CompleteAsync();
        }
    }
}
