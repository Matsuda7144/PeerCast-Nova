using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpCompatibilityClientTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Probe_PerformsRelayRequestHeloOlehAndOk()
    {
        var transportOptions = new TcpTransportOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(2),
            MaxConcurrentConnections = 4,
        };

        await using var listener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0),
            transportOptions);

        listener.Start();

        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var remoteSessionId = Guid.ParseExact(
            "102132435465768798a9babbdcddedef",
            "N");

        var serverTask = RunAcceptedPeerAsync(
            listener,
            remoteSessionId,
            timeoutSource.Token);

        var client = new PcpCompatibilityClient(
            new TcpTransportConnector(transportOptions));

        var channelId = Guid.ParseExact(
            "531dc8dfc7fb42928ac2c0a626517a87",
            "N");

        var result = await client.ProbeAsync(
            listener.LocalEndPoint.Address.ToString(),
            listener.LocalEndPoint.Port,
            channelId,
            streamPosition: 22,
            cancellationToken: timeoutSource.Token);

        await serverTask;

        Assert.Equal(PcpCompatibilityOutcome.Accepted, result.Outcome);
        Assert.Equal(200, result.HttpStatusCode);
        Assert.Equal("application/x-peercast-pcp", result.ContentType);
        Assert.NotNull(result.RemotePeer);
        Assert.Equal(remoteSessionId, result.RemotePeer!.SessionId);
        Assert.Equal("PeerCastStation/Test", result.RemotePeer.Agent);
        Assert.Equal((uint)1218, result.RemotePeer.ServantVersion);
        Assert.Equal(PcpKnownAtomNames.Okay, result.TerminalAtom);
        Assert.Equal((uint)1, result.TerminalCode);
        Assert.Collection(
            result.ReceivedAtomNames,
            atomName => Assert.Equal(PcpKnownAtomNames.Oleh, atomName),
            atomName => Assert.Equal(PcpKnownAtomNames.Okay, atomName));
    }

    [Fact]
    public async Task ProbeAndObserve_ReportsContentFreeAtomShape()
    {
        var transportOptions = new TcpTransportOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(2),
            MaxConcurrentConnections = 4,
        };

        await using var listener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0),
            transportOptions);

        listener.Start();

        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var privateMetadata = "Sensitive synthetic metadata";
        var metadataBytes =
            PcpWireValueCodec.EncodeNullTerminatedUtf8(privateMetadata);

        PcpAtom[] postHandshakeAtoms =
        [
            PcpAtom.Parent(
                PcpKnownAtomNames.Channel,
                PcpAtom.Leaf(
                    PcpAtomName.FromAscii("meta"),
                    metadataBytes),
                PcpAtom.Leaf(
                    PcpAtomName.FromAscii("data"),
                    [0xFF, 0x00, 0x80, 0x01])),
        ];

        var serverTask = RunAcceptedPeerAsync(
            listener,
            Guid.NewGuid(),
            timeoutSource.Token,
            postHandshakeAtoms);

        var client = new PcpCompatibilityClient(
            new TcpTransportConnector(transportOptions));

        var result = await client.ProbeAndObserveAsync(
            listener.LocalEndPoint.Address.ToString(),
            listener.LocalEndPoint.Port,
            Guid.NewGuid(),
            new PcpPostHandshakeObservationOptions
            {
                Timeout = TimeSpan.FromSeconds(1),
                MaxTopLevelAtoms = 1,
                MaxAtomBytes = 4096,
            },
            streamPosition: 22,
            cancellationToken: timeoutSource.Token);

        await serverTask;

        var observation = Assert.IsType<PcpPostHandshakeObservation>(
            result.PostHandshakeObservation);

        Assert.Equal(
            PcpPostHandshakeStopReason.TopLevelAtomLimitReached,
            observation.StopReason);

        var channelShape = Assert.Single(observation.Atoms);
        Assert.Equal(PcpKnownAtomNames.Channel, channelShape.Name);
        Assert.True(channelShape.IsParent);
        Assert.Equal(2, channelShape.Value);

        Assert.Collection(
            channelShape.Children,
            metadataShape =>
            {
                Assert.Equal(
                    PcpAtomName.FromAscii("meta"),
                    metadataShape.Name);
                Assert.False(metadataShape.IsParent);
                Assert.Equal(metadataBytes.Length, metadataShape.Value);
                Assert.True(metadataShape.LooksLikeNullTerminatedUtf8);
            },
            dataShape =>
            {
                Assert.Equal(
                    PcpAtomName.FromAscii("data"),
                    dataShape.Name);
                Assert.False(dataShape.IsParent);
                Assert.Equal(4, dataShape.Value);
                Assert.False(dataShape.LooksLikeNullTerminatedUtf8);
            });

        Assert.DoesNotContain(
            typeof(PcpAtomShape).GetProperties(),
            property => property.PropertyType == typeof(byte[]) ||
                property.PropertyType == typeof(ReadOnlyMemory<byte>));
    }

    [Fact]
    public async Task ProbeAndObserve_NoPostHandshakeAtomReportsTimeout()
    {
        var transportOptions = new TcpTransportOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(2),
            MaxConcurrentConnections = 4,
        };

        await using var listener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0),
            transportOptions);

        listener.Start();

        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var serverTask = RunAcceptedPeerAsync(
            listener,
            Guid.NewGuid(),
            timeoutSource.Token,
            keepOpenAfterHandshake: TimeSpan.FromMilliseconds(200));

        var client = new PcpCompatibilityClient(
            new TcpTransportConnector(transportOptions));

        var result = await client.ProbeAndObserveAsync(
            listener.LocalEndPoint.Address.ToString(),
            listener.LocalEndPoint.Port,
            Guid.NewGuid(),
            new PcpPostHandshakeObservationOptions
            {
                Timeout = TimeSpan.FromMilliseconds(25),
                MaxTopLevelAtoms = 1,
                MaxAtomBytes = 4096,
            },
            streamPosition: 22,
            cancellationToken: timeoutSource.Token);

        await serverTask;

        var observation = Assert.IsType<PcpPostHandshakeObservation>(
            result.PostHandshakeObservation);

        Assert.Empty(observation.Atoms);
        Assert.Equal(
            PcpPostHandshakeStopReason.TimedOut,
            observation.StopReason);
    }

    [Fact]
    public async Task ProbeAndObserve_OversizedAtomReportsLimitWithoutShape()
    {
        var transportOptions = new TcpTransportOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(2),
            MaxConcurrentConnections = 4,
        };

        await using var listener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0),
            transportOptions);

        listener.Start();

        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        PcpAtom[] postHandshakeAtoms =
        [
            PcpAtom.Leaf(
                PcpAtomName.FromAscii("data"),
                new byte[128]),
        ];

        var serverTask = RunAcceptedPeerAsync(
            listener,
            Guid.NewGuid(),
            timeoutSource.Token,
            postHandshakeAtoms);

        var client = new PcpCompatibilityClient(
            new TcpTransportConnector(transportOptions));

        var result = await client.ProbeAndObserveAsync(
            listener.LocalEndPoint.Address.ToString(),
            listener.LocalEndPoint.Port,
            Guid.NewGuid(),
            new PcpPostHandshakeObservationOptions
            {
                Timeout = TimeSpan.FromSeconds(1),
                MaxTopLevelAtoms = 1,
                MaxAtomBytes = 64,
            },
            streamPosition: 22,
            cancellationToken: timeoutSource.Token);

        await serverTask;

        var observation = Assert.IsType<PcpPostHandshakeObservation>(
            result.PostHandshakeObservation);

        Assert.Empty(observation.Atoms);
        Assert.Equal(
            PcpPostHandshakeStopReason.AtomSizeLimitExceeded,
            observation.StopReason);
    }

    [Fact]
    public async Task Probe_Accepts503ThenReportsQuitAsRelayFull()
    {
        var transportOptions = new TcpTransportOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(2),
            MaxConcurrentConnections = 4,
        };

        await using var listener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0),
            transportOptions);

        listener.Start();

        using var timeoutSource = new CancellationTokenSource(TestTimeout);
        var remoteSessionId = Guid.NewGuid();

        var serverTask = RunRelayFullPeerAsync(
            listener,
            remoteSessionId,
            timeoutSource.Token);

        var client = new PcpCompatibilityClient(
            new TcpTransportConnector(transportOptions));

        var result = await client.ProbeAsync(
            listener.LocalEndPoint.Address.ToString(),
            listener.LocalEndPoint.Port,
            Guid.NewGuid(),
            cancellationToken: timeoutSource.Token);

        await serverTask;

        Assert.Equal(PcpCompatibilityOutcome.RelayFull, result.Outcome);
        Assert.Equal(503, result.HttpStatusCode);
        Assert.Equal(PcpKnownAtomNames.Quit, result.TerminalAtom);
        Assert.Equal((uint)1003, result.TerminalCode);
        Assert.Collection(
            result.ReceivedAtomNames,
            atomName => Assert.Equal(PcpKnownAtomNames.Oleh, atomName),
            atomName => Assert.Equal(PcpKnownAtomNames.Quit, atomName));
    }

    [Fact]
    public async Task Probe_Reports503QuitBeforeOlehAsRelayFull()
    {
        var transportOptions = new TcpTransportOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(2),
            MaxConcurrentConnections = 4,
        };

        await using var listener = new TcpTransportListener(
            new IPEndPoint(IPAddress.Loopback, 0),
            transportOptions);

        listener.Start();

        using var timeoutSource = new CancellationTokenSource(TestTimeout);

        var serverTask = RunRelayFullPeerAsync(
            listener,
            Guid.NewGuid(),
            timeoutSource.Token,
            sendOleh: false);

        var client = new PcpCompatibilityClient(
            new TcpTransportConnector(transportOptions));

        var result = await client.ProbeAsync(
            listener.LocalEndPoint.Address.ToString(),
            listener.LocalEndPoint.Port,
            Guid.NewGuid(),
            cancellationToken: timeoutSource.Token);

        await serverTask;

        Assert.Equal(PcpCompatibilityOutcome.RelayFull, result.Outcome);
        Assert.Equal(503, result.HttpStatusCode);
        Assert.Null(result.RemotePeer);
        Assert.Equal(PcpKnownAtomNames.Quit, result.TerminalAtom);
        Assert.Equal((uint)1003, result.TerminalCode);
        Assert.Collection(
            result.ReceivedAtomNames,
            atomName => Assert.Equal(PcpKnownAtomNames.Quit, atomName));
    }

    private static async Task RunAcceptedPeerAsync(
        TcpTransportListener listener,
        Guid remoteSessionId,
        CancellationToken cancellationToken,
        IReadOnlyList<PcpAtom>? postHandshakeAtoms = null,
        TimeSpan? keepOpenAfterHandshake = null)
    {
        await using var connection = await listener.AcceptAsync(cancellationToken);

        var pipeReader = PipeReader.Create(
            connection.TransportStream,
            new StreamPipeReaderOptions(leaveOpen: true));

        try
        {
            var request = await PcpHttpHeaderBlockReader.ReadRequestAsync(
                pipeReader,
                32 * 1024,
                cancellationToken);

            AssertRelayRequest(request);

            await WriteHttpResponseAsync(
                connection,
                statusCode: 200,
                reasonPhrase: "OK",
                cancellationToken);

            await using var atomReader = new PcpAtomPipeMessageReader(pipeReader);
            var helo = await atomReader.ReadNextAsync(cancellationToken);

            AssertHelo(helo);

            var oleh = PcpAtom.Parent(
                PcpKnownAtomNames.Oleh,
                PcpAtom.Leaf(
                    PcpKnownAtomNames.HeloRemoteIp,
                    PcpWireValueCodec.EncodeLegacyIpv4(IPAddress.Loopback)),
                PcpAtom.Leaf(
                    PcpKnownAtomNames.HeloAgent,
                    PcpWireValueCodec.EncodeNullTerminatedUtf8(
                        "PeerCastStation/Test")),
                PcpAtom.Leaf(
                    PcpKnownAtomNames.HeloSessionId,
                    PcpWireValueCodec.EncodeGuid(remoteSessionId)),
                PcpAtom.Leaf(
                    PcpKnownAtomNames.HeloPort,
                    PcpWireValueCodec.EncodeUInt16(7144)),
                PcpAtom.Leaf(
                    PcpKnownAtomNames.HeloVersion,
                    PcpWireValueCodec.EncodeUInt32(1218)));

            await WriteAtomAsync(
                connection,
                oleh,
                cancellationToken);

            await WriteAtomAsync(
                connection,
                PcpAtom.Leaf(
                    PcpKnownAtomNames.Okay,
                    PcpWireValueCodec.EncodeUInt32(1)),
                cancellationToken);

            if (postHandshakeAtoms is not null)
            {
                foreach (var atom in postHandshakeAtoms)
                {
                    await WriteAtomAsync(
                        connection,
                        atom,
                        cancellationToken);
                }
            }

            if (keepOpenAfterHandshake is not null)
            {
                await Task.Delay(
                    keepOpenAfterHandshake.Value,
                    cancellationToken);
            }
        }
        finally
        {
            await pipeReader.CompleteAsync();
        }
    }

    private static async Task RunRelayFullPeerAsync(
        TcpTransportListener listener,
        Guid remoteSessionId,
        CancellationToken cancellationToken,
        bool sendOleh = true)
    {
        await using var connection = await listener.AcceptAsync(cancellationToken);

        var pipeReader = PipeReader.Create(
            connection.TransportStream,
            new StreamPipeReaderOptions(leaveOpen: true));

        try
        {
            var request = await PcpHttpHeaderBlockReader.ReadRequestAsync(
                pipeReader,
                32 * 1024,
                cancellationToken);

            Assert.Equal("GET", request.Method);

            await WriteHttpResponseAsync(
                connection,
                statusCode: 503,
                reasonPhrase: "Service Unavailable",
                cancellationToken);

            await using var atomReader = new PcpAtomPipeMessageReader(pipeReader);
            var helo = await atomReader.ReadNextAsync(cancellationToken);

            AssertHelo(helo);

            if (sendOleh)
            {
                var oleh = PcpAtom.Parent(
                    PcpKnownAtomNames.Oleh,
                    PcpAtom.Leaf(
                        PcpKnownAtomNames.HeloAgent,
                        PcpWireValueCodec.EncodeNullTerminatedUtf8(
                            "PeerCastStation/Test")),
                    PcpAtom.Leaf(
                        PcpKnownAtomNames.HeloSessionId,
                        PcpWireValueCodec.EncodeGuid(remoteSessionId)),
                    PcpAtom.Leaf(
                        PcpKnownAtomNames.HeloVersion,
                        PcpWireValueCodec.EncodeUInt32(1218)));

                await WriteAtomAsync(
                    connection,
                    oleh,
                    cancellationToken);
            }

            await WriteAtomAsync(
                connection,
                PcpAtom.Leaf(
                    PcpKnownAtomNames.Quit,
                    PcpWireValueCodec.EncodeUInt32(1003)),
                cancellationToken);
        }
        finally
        {
            await pipeReader.CompleteAsync();
        }
    }

    private static void AssertRelayRequest(PcpHttpRequestHeader request)
    {
        Assert.Equal("GET", request.Method);
        Assert.Equal(new Version(1, 0), request.HttpVersion);
        Assert.StartsWith("/channel/", request.RequestTarget);
        Assert.True(request.Headers.ContainsKey("User-Agent"));
        Assert.Equal("1", request.Headers["x-peercast-pcp"]);
        Assert.Equal("22", request.Headers["x-peercast-pos"]);
    }

    private static void AssertHelo(PcpAtom atom)
    {
        Assert.True(atom.IsParent);
        Assert.Equal(PcpKnownAtomNames.Helo, atom.Name);
        Assert.NotNull(atom.FindFirst(PcpKnownAtomNames.HeloAgent));
        Assert.NotNull(atom.FindFirst(PcpKnownAtomNames.HeloSessionId));

        var versionAtom = atom.FindFirst(PcpKnownAtomNames.HeloVersion);
        Assert.NotNull(versionAtom);
        Assert.Equal(
            (uint)1218,
            PcpWireValueCodec.DecodeUInt32(versionAtom!.Content.Span));
    }

    private static async Task WriteHttpResponseAsync(
        TcpTransportConnection connection,
        int statusCode,
        string reasonPhrase,
        CancellationToken cancellationToken)
    {
        var response = Encoding.ASCII.GetBytes(
            $"HTTP/1.0 {statusCode} {reasonPhrase}\r\n" +
            "Server: PeerCastStation/Test\r\n" +
            "Content-Type:application/x-peercast-pcp\r\n" +
            "\r\n");

        await connection.WriteAsync(response, cancellationToken);
    }

    private static async Task WriteAtomAsync(
        TcpTransportConnection connection,
        PcpAtom atom,
        CancellationToken cancellationToken)
    {
        var writer = new ArrayBufferWriter<byte>();
        PcpAtomTreeCodec.Write(writer, atom);

        await connection.WriteAsync(
            writer.WrittenMemory,
            cancellationToken);
    }
}
